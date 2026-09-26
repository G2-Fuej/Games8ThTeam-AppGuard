using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Games8thTeamBlocker
{
    /// <summary>
    /// 用户态 WFP 引擎：在 ALE_AUTH_CONNECT V4/V6 层按进程可执行文件路径
    /// 添加出站 BLOCK 过滤器。管理员权限即可运行，无需内核驱动、无需签名、
    /// 无需测试模式/Secure Boot/HVCI 改动。
    /// 结构布局严格对照 Windows SDK fwpmtypes.h / fwpstypes.h（x64）。
    /// </summary>
    public static class WfpEngine
    {
        // ---- constants from fwpmu.h / fwpstypes.h ----
        private const uint RPC_C_AUTHN_DEFAULT = 0xFFFFFFFF;
        private const uint ERROR_SUCCESS = 0;
        private const uint FWP_E_ALREADY_EXISTS = 0x80320009;
        private const uint FWP_E_FILTER_NOT_FOUND = 0x80320003;
        private const uint FWPM_FILTER_FLAG_PERSISTENT = 0x1;
        private const uint FWPM_PROVIDER_FLAG_PERSISTENT = 0x1;

        private const uint FWP_UINT64 = 4;
        private const uint FWP_BYTE_BLOB_TYPE = 12;
        private const uint FWP_MATCH_EQUAL = 0;
        // FWP_ACTION_BLOCK = 0x1 | FWP_ACTION_FLAG_TERMINATING(0x1000) = 0x1001
        // 缺少 TERMINATING 标志会导致 FwpmFilterAdd0 返回 FWP_E_INVALID_ACTION_TYPE
        private const uint FWP_ACTION_BLOCK = 0x1001;

        // FWPM_LAYER_ALE_AUTH_CONNECT_V4/V6（Windows SDK 定义）
        private static readonly Guid LAYER_ALE_AUTH_CONNECT_V4 =
            new Guid("c38d57d1-05a7-4c33-904f-7fbceee60e82");
        private static readonly Guid LAYER_ALE_AUTH_CONNECT_V6 =
            new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");

        // Windows SDK 真实 GUID
        private static readonly Guid FWPM_CONDITION_ALE_APP_ID =
            new Guid("d78e1e87-8644-4ea5-9437-d809ecefc971");
        private static readonly Guid FWPM_SUBLAYER_UNIVERSAL =
            new Guid("eebecc03-ced4-4380-819a-2734397b2b74");

        internal static readonly Guid ProviderGuid =
            new Guid("6F8C0C2A-9C5A-4E7B-9D3E-1A2B3C4D5E6F");

        [DllImport("fwpuclnt.dll", SetLastError = true)]
        private static extern uint FwpmEngineOpen0(
            IntPtr serverName,
            uint authnService,
            IntPtr authIdentity,
            IntPtr session,
            out IntPtr engineHandle);

        [DllImport("fwpuclnt.dll", SetLastError = true)]
        private static extern uint FwpmEngineClose0(IntPtr engineHandle);

        [DllImport("fwpuclnt.dll", SetLastError = true)]
        private static extern uint FwpmFreeMemory0(ref IntPtr ptr);

        [DllImport("fwpuclnt.dll", SetLastError = true)]
        private static extern uint FwpmGetAppIdFromFileName0(
            [MarshalAs(UnmanagedType.LPWStr)] string fileName,
            out IntPtr appId);

        [DllImport("fwpuclnt.dll", SetLastError = true)]
        private static extern uint FwpmProviderAdd0(
            IntPtr engineHandle,
            ref FWPM_PROVIDER0 provider,
            IntPtr sd);

        [DllImport("fwpuclnt.dll", SetLastError = true)]
        private static extern uint FwpmFilterAdd0(
            IntPtr engineHandle,
            ref FWPM_FILTER0 filter,
            IntPtr sd,
            out ulong id);

        [DllImport("fwpuclnt.dll", SetLastError = true)]
        private static extern uint FwpmFilterDeleteByKey0(
            IntPtr engineHandle,
            ref Guid key);

        [DllImport("fwpuclnt.dll", SetLastError = true)]
        private static extern uint FwpmFilterGetByKey0(
            IntPtr engineHandle,
            ref Guid key,
            out IntPtr filter);

        // ---- SeDebugPrivilege helpers (persistent WFP filter add requires it) ----
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess,
            out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValueW(
            string lpSystemName,
            string lpName,
            out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr tokenHandle,
            bool disableAllPrivileges,
            ref TOKEN_PRIVILEGES newState,
            uint bufferLength,
            IntPtr previousState,
            IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private const uint TOKEN_QUERY = 0x0008;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint SE_PRIVILEGE_ENABLED = 0x2;

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        private static void EnableSeDebugPrivilege()
        {
            try
            {
                IntPtr hToken;
                if (!OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle,
                    TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, out hToken)) return;
                try
                {
                    long luid;
                    if (!LookupPrivilegeValueW(null, "SeDebugPrivilege", out luid)) return;
                    TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES();
                    tp.PrivilegeCount = 1;
                    tp.Luid = luid;
                    tp.Attributes = SE_PRIVILEGE_ENABLED;
                    AdjustTokenPrivileges(hToken, false, ref tp, (uint)Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), IntPtr.Zero, IntPtr.Zero);
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }
            catch { }
        }

        // ---- structures (x64 layout, per Windows SDK) ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FWPM_DISPLAY_DATA0
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string Name;
            [MarshalAs(UnmanagedType.LPWStr)] public string Description;
        }

        // FWP_BYTE_BLOB: UINT32 size + UINT8* data = 16 bytes (value type)
        [StructLayout(LayoutKind.Sequential)]
        private struct FWP_BYTE_BLOB
        {
            public uint Size;
            public IntPtr Data;
        }

        // FWP_VALUE0 / FWP_CONDITION_VALUE0: FWP_DATA_TYPE type + union (pointer) = 16 bytes
        [StructLayout(LayoutKind.Explicit)]
        private struct FWP_VALUE0
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public IntPtr ValuePtr;
        }

        // FWPM_FILTER_CONDITION0: GUID + FWP_MATCH_TYPE + FWP_CONDITION_VALUE0 = 40 bytes
        [StructLayout(LayoutKind.Sequential)]
        private struct FWPM_FILTER_CONDITION0
        {
            public Guid FieldKey;
            public uint MatchType;
            public FWP_VALUE0 ConditionValue;
        }

        // FWPM_ACTION0: FWP_ACTION_TYPE + union(GUID) = 24 bytes
        [StructLayout(LayoutKind.Sequential)]
        private struct FWPM_ACTION0
        {
            public uint Type;
            private uint _pad;
            public Guid CalloutKey;
        }

        // FWPM_FILTER0 union: UINT64 rawContext / GUID providerContextKey = 16 bytes
        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct FWPM_FILTER_UNION0
        {
            [FieldOffset(0)] public ulong RawContext;
            [FieldOffset(0)] public Guid ProviderContextKey;
        }

        // FWPM_FILTER0 = 200 bytes on x64
        [StructLayout(LayoutKind.Sequential)]
        private struct FWPM_FILTER0
        {
            public Guid FilterKey;                       // 16
            public FWPM_DISPLAY_DATA0 DisplayData;       // 16
            public uint Flags;                           // 4
            public IntPtr ProviderKey;                   // 8
            public FWP_BYTE_BLOB ProviderData;           // 16 (value type)
            public Guid LayerKey;                        // 16
            public Guid SubLayerKey;                     // 16
            public FWP_VALUE0 Weight;                    // 16
            public uint NumFilterConditions;             // 4
            public IntPtr FilterCondition;               // 8
            public FWPM_ACTION0 Action;                  // 24
            public FWPM_FILTER_UNION0 FilterUnion;       // 16
            public IntPtr Reserved;                      // 8
            public ulong FilterId;                       // 8
            public FWP_VALUE0 EffectiveWeight;           // 16
        }

        // FWPM_PROVIDER0 = 64 bytes on x64
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FWPM_PROVIDER0
        {
            public Guid ProviderKey;                     // 16
            public FWPM_DISPLAY_DATA0 DisplayData;       // 16
            public uint Flags;                           // 4
            public FWP_BYTE_BLOB ProviderData;           // 16 (value type)
            [MarshalAs(UnmanagedType.LPWStr)] public string ServiceName; // 8
        }

        private static string SelfPath = "";

        static WfpEngine()
        {
            try { SelfPath = System.Reflection.Assembly.GetExecutingAssembly().Location; }
            catch { }
        }

        /// <summary>检查本机 WFP 引擎是否可正常打开（管理员权限）</summary>
        public static bool IsAvailable()
        {
            EnableSeDebugPrivilege();
            IntPtr h = IntPtr.Zero;
            uint st = FwpmEngineOpen0(IntPtr.Zero, RPC_C_AUTHN_DEFAULT, IntPtr.Zero, IntPtr.Zero, out h);
            if (st == ERROR_SUCCESS && h != IntPtr.Zero) FwpmEngineClose0(h);
            return st == ERROR_SUCCESS;
        }

        /// <summary>对单个 exe 路径添加 V4+V6 出站 BLOCK 过滤器。返回是否成功。</summary>
        public static bool Block(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            if (!File.Exists(exePath)) return false;
            if (!string.IsNullOrEmpty(SelfPath) &&
                string.Equals(exePath, SelfPath, StringComparison.OrdinalIgnoreCase)) return false;

            IntPtr appId = IntPtr.Zero;
            uint st = FwpmGetAppIdFromFileName0(exePath, out appId);
            if (st != ERROR_SUCCESS || appId == IntPtr.Zero) return false;
            try
            {
                EnableSeDebugPrivilege();
                IntPtr h = IntPtr.Zero;
                st = FwpmEngineOpen0(IntPtr.Zero, RPC_C_AUTHN_DEFAULT, IntPtr.Zero, IntPtr.Zero, out h);
                if (st != ERROR_SUCCESS || h == IntPtr.Zero) return false;
                try
                {
                    Guid v4Key = MakeFilterKey(exePath, "v4");
                    Guid v6Key = MakeFilterKey(exePath, "v6");
                    bool ok4 = AddFilter(h, LAYER_ALE_AUTH_CONNECT_V4, v4Key, appId);
                    bool ok6 = AddFilter(h, LAYER_ALE_AUTH_CONNECT_V6, v6Key, appId);
                    if (!ok4 || !ok6)
                    {
                        DeleteFilter(h, v4Key);
                        DeleteFilter(h, v6Key);
                        return false;
                    }
                    return FilterMatchesAppId(h, v4Key, LAYER_ALE_AUTH_CONNECT_V4, appId) &&
                           FilterMatchesAppId(h, v6Key, LAYER_ALE_AUTH_CONNECT_V6, appId);
                }
                finally
                {
                    FwpmEngineClose0(h);
                }
            }
            finally
            {
                FwpmFreeMemory0(ref appId);
            }
        }

        /// <summary>对目录递归枚举 .exe 并逐个添加。返回成功数量。</summary>
        public static int BlockDirectory(string directory, Action<string> log)
        {
            int ok = 0;
            try
            {
                string[] all = Directory.GetFiles(directory, "*.exe", SearchOption.AllDirectories);
                foreach (string f in all)
                {
                    if (Block(f)) ok++;
                    else if (log != null) log("WFP 添加失败: " + f);
                }
            }
            catch { }
            return ok;
        }

        /// <summary>按 exe 路径列表解除全部本工具 WFP 过滤器</summary>
        public static int ClearAll(IEnumerable<string> exePaths)
        {
            int removed = 0;
            if (exePaths == null) return removed;
            foreach (string p in exePaths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (Directory.Exists(p))
                {
                    try
                    {
                        foreach (string f in Directory.GetFiles(p, "*.exe", SearchOption.AllDirectories))
                            if (Unblock(f)) removed++;
                    }
                    catch { }
                }
                else if (File.Exists(p))
                {
                    if (Unblock(p)) removed++;
                }
            }
            return removed;
        }

        /// <summary>解除单个路径的 V4/V6 过滤器</summary>
        public static bool Unblock(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            IntPtr h = IntPtr.Zero;
            uint st = FwpmEngineOpen0(IntPtr.Zero, RPC_C_AUTHN_DEFAULT, IntPtr.Zero, IntPtr.Zero, out h);
            if (st != ERROR_SUCCESS || h == IntPtr.Zero) return false;
            try
            {
                Guid v4Key = MakeFilterKey(exePath, "v4");
                Guid v6Key = MakeFilterKey(exePath, "v6");
                bool ok1 = DeleteFilter(h, v4Key);
                bool ok2 = DeleteFilter(h, v6Key);
                return ok1 && ok2 &&
                       FilterIsAbsent(h, v4Key) &&
                       FilterIsAbsent(h, v6Key);
            }
            finally
            {
                FwpmEngineClose0(h);
            }
        }

        /// <summary>查询某个路径是否同时拥有绑定正确 AppId 的 V4/V6 过滤器</summary>
        public static bool IsBlockedPath(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            if (!File.Exists(exePath)) return false;

            IntPtr appId = IntPtr.Zero;
            uint appStatus = FwpmGetAppIdFromFileName0(exePath, out appId);
            if (appStatus != ERROR_SUCCESS || appId == IntPtr.Zero) return false;

            IntPtr h = IntPtr.Zero;
            try
            {
                uint st = FwpmEngineOpen0(IntPtr.Zero, RPC_C_AUTHN_DEFAULT, IntPtr.Zero, IntPtr.Zero, out h);
                if (st != ERROR_SUCCESS || h == IntPtr.Zero) return false;
                Guid v4Key = MakeFilterKey(exePath, "v4");
                Guid v6Key = MakeFilterKey(exePath, "v6");
                return FilterMatchesAppId(h, v4Key, LAYER_ALE_AUTH_CONNECT_V4, appId) &&
                       FilterMatchesAppId(h, v6Key, LAYER_ALE_AUTH_CONNECT_V6, appId);
            }
            finally
            {
                if (h != IntPtr.Zero) FwpmEngineClose0(h);
                FwpmFreeMemory0(ref appId);
            }
        }

        private static bool AddFilter(IntPtr h, Guid layer, Guid key, IntPtr appId)
        {
            FWPM_FILTER0 filter = new FWPM_FILTER0();
            filter.FilterKey = key;
            filter.DisplayData.Name = "Games8th_Blocker_WFP";
            filter.DisplayData.Description = "Games8Th.Team AppBlocker outbound block";
            filter.Flags = FWPM_FILTER_FLAG_PERSISTENT;
            filter.LayerKey = layer;
            filter.SubLayerKey = FWPM_SUBLAYER_UNIVERSAL;
            filter.Weight.Type = FWP_UINT64;
            // FWP_VALUE0.uint64 是指针，指向 UINT64 值
            IntPtr weightPtr = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.WriteInt64(weightPtr, 0, unchecked((long)0xFFFFFFFFFFFFFFFFUL));
                filter.Weight.ValuePtr = weightPtr;

                FWPM_FILTER_CONDITION0 cond = new FWPM_FILTER_CONDITION0();
                cond.FieldKey = FWPM_CONDITION_ALE_APP_ID;
                cond.MatchType = FWP_MATCH_EQUAL;
                cond.ConditionValue.Type = FWP_BYTE_BLOB_TYPE;
                // FwpmGetAppIdFromFileName0 返回的 appId 即 FWP_BYTE_BLOB*
                cond.ConditionValue.ValuePtr = appId;

                IntPtr condPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FWPM_FILTER_CONDITION0)));
                try
                {
                    Marshal.StructureToPtr(cond, condPtr, false);
                    filter.NumFilterConditions = 1;
                    filter.FilterCondition = condPtr;
                    filter.Action.Type = FWP_ACTION_BLOCK;

                    ulong id = 0;
                    uint st = FwpmFilterAdd0(h, ref filter, IntPtr.Zero, out id);
                    if (st == FWP_E_ALREADY_EXISTS)
                    {
                        if (FilterMatchesAppId(h, key, layer, appId)) return true;
                        DeleteFilter(h, key);
                        st = FwpmFilterAdd0(h, ref filter, IntPtr.Zero, out id);
                    }
                    return st == ERROR_SUCCESS &&
                           FilterMatchesAppId(h, key, layer, appId);
                }
                finally
                {
                    Marshal.FreeHGlobal(condPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(weightPtr);
            }
        }

        private static bool DeleteFilter(IntPtr h, Guid key)
        {
            uint st = FwpmFilterDeleteByKey0(h, ref key);
            return st == ERROR_SUCCESS || st == FWP_E_FILTER_NOT_FOUND;
        }

        private static bool FilterIsAbsent(IntPtr h, Guid key)
        {
            IntPtr filter = IntPtr.Zero;
            uint st = FwpmFilterGetByKey0(h, ref key, out filter);
            if (st == ERROR_SUCCESS)
            {
                if (filter != IntPtr.Zero) FwpmFreeMemory0(ref filter);
                return false;
            }
            return st == FWP_E_FILTER_NOT_FOUND;
        }

        private static bool FilterMatchesAppId(
            IntPtr h,
            Guid key,
            Guid expectedLayer,
            IntPtr expectedAppId)
        {
            IntPtr filterPtr = IntPtr.Zero;
            uint st = FwpmFilterGetByKey0(h, ref key, out filterPtr);
            if (st != ERROR_SUCCESS || filterPtr == IntPtr.Zero) return false;

            try
            {
                FWPM_FILTER0 filter =
                    (FWPM_FILTER0)Marshal.PtrToStructure(filterPtr, typeof(FWPM_FILTER0));
                if (filter.LayerKey != expectedLayer ||
                    filter.SubLayerKey != FWPM_SUBLAYER_UNIVERSAL ||
                    filter.Action.Type != FWP_ACTION_BLOCK ||
                    filter.NumFilterConditions != 1 ||
                    filter.FilterCondition == IntPtr.Zero)
                    return false;

                FWPM_FILTER_CONDITION0 condition =
                    (FWPM_FILTER_CONDITION0)Marshal.PtrToStructure(
                        filter.FilterCondition, typeof(FWPM_FILTER_CONDITION0));
                if (condition.FieldKey != FWPM_CONDITION_ALE_APP_ID ||
                    condition.MatchType != FWP_MATCH_EQUAL ||
                    condition.ConditionValue.Type != FWP_BYTE_BLOB_TYPE ||
                    condition.ConditionValue.ValuePtr == IntPtr.Zero)
                    return false;

                return AppIdEquals(expectedAppId, condition.ConditionValue.ValuePtr);
            }
            catch
            {
                return false;
            }
            finally
            {
                FwpmFreeMemory0(ref filterPtr);
            }
        }

        private static bool AppIdEquals(IntPtr leftPtr, IntPtr rightPtr)
        {
            if (leftPtr == IntPtr.Zero || rightPtr == IntPtr.Zero) return false;
            try
            {
                FWP_BYTE_BLOB left =
                    (FWP_BYTE_BLOB)Marshal.PtrToStructure(leftPtr, typeof(FWP_BYTE_BLOB));
                FWP_BYTE_BLOB right =
                    (FWP_BYTE_BLOB)Marshal.PtrToStructure(rightPtr, typeof(FWP_BYTE_BLOB));
                if (left.Size == 0 || left.Size != right.Size ||
                    left.Data == IntPtr.Zero || right.Data == IntPtr.Zero ||
                    left.Size > 1024 * 1024)
                    return false;

                byte[] a = new byte[left.Size];
                byte[] b = new byte[right.Size];
                Marshal.Copy(left.Data, a, 0, a.Length);
                Marshal.Copy(right.Data, b, 0, b.Length);
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Guid MakeFilterKey(string exePath, string suffix)
        {
            byte[] pathBytes = Encoding.UTF8.GetBytes(exePath.ToLowerInvariant() + "|" + suffix);
            byte[] hash = System.Security.Cryptography.SHA256.Create().ComputeHash(pathBytes);
            byte[] g = new byte[16];
            Array.Copy(hash, g, 16);
            g[7] = (byte)((g[7] & 0x0F) | 0x50); // version 5
            g[8] = (byte)((g[8] & 0x3F) | 0x80); // variant
            return new Guid(g);
        }
    }
}
