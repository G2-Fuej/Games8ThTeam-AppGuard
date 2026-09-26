using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;

namespace Games8thTeamBlocker
{
    internal static class EmbeddedDriverInstaller
    {
        private const string ServiceName = "Games8thGuard";
        private const string ResourceName = "Games8thTeamBlocker.Games8thGuard.sys";
        internal const string EmbeddedDriverSha256 =
            "CFCB98EC34428375E8374721DBBF9B588CB22E93B652F01B9BCF23A531297C97";

        private const uint ScManagerConnect = 0x0001;
        private const uint ScManagerCreateService = 0x0002;
        private const uint ServiceAllAccess = 0x000F01FF;
        private const uint ServiceKernelDriver = 0x00000001;
        private const uint ServiceDemandStart = 0x00000003;
        private const uint ServiceErrorNormal = 0x00000001;
        private const uint ServiceControlStop = 0x00000001;
        private const int ServiceStopped = 1;
        private const int ServiceRunning = 4;
        private const int ErrorServiceDoesNotExist = 1060;
        private const int ErrorServiceAlreadyRunning = 1056;

        private static string lastMessage = "";

        internal static string LastMessage { get { return lastMessage; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public int ServiceType;
            public int CurrentState;
            public int ControlsAccepted;
            public int Win32ExitCode;
            public int ServiceSpecificExitCode;
            public int CheckPoint;
            public int WaitHint;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            public IntPtr FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(
            string machineName, string databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateService(
            IntPtr scm, string serviceName, string displayName, uint desiredAccess,
            uint serviceType, uint startType, uint errorControl, string binaryPath,
            string loadOrderGroup, IntPtr tagId, string dependencies,
            string serviceStartName, string password);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(
            IntPtr scm, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool StartService(
            IntPtr service, int argumentCount, IntPtr arguments);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ControlService(
            IntPtr service, uint control, out ServiceStatus status);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceStatus(
            IntPtr service, out ServiceStatus status);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteService(IntPtr service);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int WinVerifyTrust(
            IntPtr windowHandle, [In] ref Guid actionId, IntPtr trustData);

        internal static bool EnsureLoaded()
        {
            KernelDriver.DriverStatus existing = KernelDriver.GetStatus();
            if (existing.IsLoaded)
            {
                lastMessage = existing.Summary;
                return true;
            }

            if (!IsAdministrator())
            {
                lastMessage = "需要管理员权限；应用清单应触发 UAC 提权";
                return false;
            }

            byte[] payload;
            if (!TryReadPayload(out payload)) return false;

            string driverPath = GetDriverPath();
            IntPtr scm = OpenSCManager(null, null,
                ScManagerConnect | ScManagerCreateService);
            if (scm == IntPtr.Zero)
            {
                SetWin32Error("无法打开服务控制管理器", Marshal.GetLastWin32Error());
                return false;
            }

            try
            {
                if (!RemoveExistingService(scm)) return false;
                if (!WaitForServiceDeletion(scm, 10000))
                {
                    lastMessage = "已有驱动服务未在 10 秒内完成删除";
                    return false;
                }
                if (!WriteAndVerifyDriver(driverPath, payload)) return false;

                IntPtr service = CreateService(
                    scm, ServiceName, ServiceName, ServiceAllAccess,
                    ServiceKernelDriver, ServiceDemandStart, ServiceErrorNormal,
                    driverPath, null, IntPtr.Zero, null, null, null);
                if (service == IntPtr.Zero)
                {
                    SetWin32Error("创建内核驱动服务失败", Marshal.GetLastWin32Error());
                    return false;
                }

                try
                {
                    if (!StartService(service, 0, IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != ErrorServiceAlreadyRunning)
                        {
                            SetWin32Error("启动内核驱动服务失败", error);
                            DeleteService(service);
                            return false;
                        }
                    }
                    if (!WaitForState(service, ServiceRunning, 10000))
                    {
                        lastMessage = "驱动服务未在 10 秒内进入 RUNNING 状态";
                        DeleteService(service);
                        return false;
                    }
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }

            KernelDriver.DriverStatus status = KernelDriver.GetStatus();
            if (!status.IsLoaded)
            {
                lastMessage = "服务已启动，但设备/QUERY_PATHS 验证失败：" + status.Summary;
                string ignored;
                UnloadAndCleanup(out ignored);
                return false;
            }

            lastMessage = status.Summary + "；驱动来自 EXE 内嵌签名资源";
            return true;
        }

        internal static bool UnloadAndCleanup(out string message)
        {
            if (!IsAdministrator())
            {
                message = "需要管理员权限";
                lastMessage = message;
                return false;
            }

            IntPtr scm = OpenSCManager(null, null,
                ScManagerConnect | ScManagerCreateService);
            if (scm == IntPtr.Zero)
            {
                SetWin32Error("无法打开服务控制管理器", Marshal.GetLastWin32Error());
                message = lastMessage;
                return false;
            }

            bool removed;
            try
            {
                removed = RemoveExistingService(scm) &&
                          WaitForServiceDeletion(scm, 10000);
                if (!removed && string.IsNullOrEmpty(lastMessage))
                    lastMessage = "驱动服务未在 10 秒内完成删除";
            }
            finally
            {
                CloseServiceHandle(scm);
            }
            if (!removed)
            {
                message = lastMessage;
                return false;
            }

            string path = GetDriverPath();
            try
            {
                if (File.Exists(path)) File.Delete(path);
                string directory = Path.GetDirectoryName(path);
                if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
                    Directory.Delete(directory);
            }
            catch (Exception ex)
            {
                lastMessage = "服务已删除，但驱动文件清理失败：" + ex.Message;
                message = lastMessage;
                return false;
            }

            lastMessage = "驱动服务和内嵌驱动落地文件均已清理";
            message = lastMessage;
            return true;
        }

        private static bool TryReadPayload(out byte[] payload)
        {
            payload = new byte[0];
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    lastMessage = "EXE 中缺少内嵌驱动资源 " + ResourceName;
                    return false;
                }
                using (MemoryStream memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    payload = memory.ToArray();
                }
            }

            string hash = ComputeSha256(payload);
            if (!string.Equals(hash, EmbeddedDriverSha256,
                               StringComparison.OrdinalIgnoreCase))
            {
                lastMessage = "内嵌驱动 SHA-256 不匹配：" + hash;
                payload = new byte[0];
                return false;
            }
            return true;
        }

        private static bool WriteAndVerifyDriver(string destination, byte[] payload)
        {
            string directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            string temporary = destination + ".new.sys";
            try
            {
                File.WriteAllBytes(temporary, payload);
                if (!string.Equals(ComputeSha256(temporary), EmbeddedDriverSha256,
                                   StringComparison.OrdinalIgnoreCase))
                {
                    lastMessage = "驱动临时文件 SHA-256 校验失败";
                    return false;
                }
                if (!VerifyAuthenticode(temporary))
                {
                    lastMessage = "内嵌驱动 Authenticode/信任链验证失败";
                    return false;
                }
                File.Copy(temporary, destination, true);
                if (!string.Equals(ComputeSha256(destination), EmbeddedDriverSha256,
                                   StringComparison.OrdinalIgnoreCase) ||
                    !VerifyAuthenticode(destination))
                {
                    lastMessage = "驱动落地后的 SHA-256 或 Authenticode 校验失败";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                lastMessage = "释放内嵌驱动失败：" + ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }

        private static bool RemoveExistingService(IntPtr scm)
        {
            IntPtr service = OpenService(scm, ServiceName, ServiceAllAccess);
            if (service == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorServiceDoesNotExist) return true;
                SetWin32Error("打开已有驱动服务失败", error);
                return false;
            }

            try
            {
                ServiceStatus status;
                if (QueryServiceStatus(service, out status) &&
                    status.CurrentState != ServiceStopped)
                {
                    ControlService(service, ServiceControlStop, out status);
                    if (!WaitForState(service, ServiceStopped, 10000))
                    {
                        lastMessage = "已有驱动服务未在 10 秒内停止";
                        return false;
                    }
                }
                if (!DeleteService(service))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != ErrorServiceDoesNotExist)
                    {
                        SetWin32Error("删除已有驱动服务失败", error);
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }

        private static bool WaitForState(IntPtr service, int expected, int timeoutMilliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            ServiceStatus status;
            while (watch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                if (!QueryServiceStatus(service, out status)) return false;
                if (status.CurrentState == expected) return true;
                Thread.Sleep(100);
            }
            return false;
        }

        private static bool WaitForServiceDeletion(IntPtr scm, int timeoutMilliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                IntPtr service = OpenService(scm, ServiceName, ServiceAllAccess);
                if (service == IntPtr.Zero)
                {
                    if (Marshal.GetLastWin32Error() == ErrorServiceDoesNotExist)
                        return true;
                }
                else
                {
                    CloseServiceHandle(service);
                }
                Thread.Sleep(100);
            }
            return false;
        }

        private static bool VerifyAuthenticode(string path)
        {
            IntPtr filePath = IntPtr.Zero;
            IntPtr fileInfoPointer = IntPtr.Zero;
            IntPtr trustDataPointer = IntPtr.Zero;
            try
            {
                filePath = Marshal.StringToCoTaskMemUni(path);
                WinTrustFileInfo fileInfo = new WinTrustFileInfo();
                fileInfo.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
                fileInfo.FilePath = filePath;
                fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

                WinTrustData trustData = new WinTrustData();
                trustData.StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData));
                trustData.UiChoice = 2;
                trustData.RevocationChecks = 0;
                trustData.UnionChoice = 1;
                trustData.FileInfo = fileInfoPointer;
                trustData.StateAction = 0;
                trustData.ProviderFlags = 0x00001000;
                trustDataPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData)));
                Marshal.StructureToPtr(trustData, trustDataPointer, false);

                Guid action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
                return WinVerifyTrust(new IntPtr(-1), ref action, trustDataPointer) == 0;
            }
            finally
            {
                if (trustDataPointer != IntPtr.Zero) Marshal.FreeHGlobal(trustDataPointer);
                if (fileInfoPointer != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPointer);
                if (filePath != IntPtr.Zero) Marshal.FreeCoTaskMem(filePath);
            }
        }

        private static string GetDriverPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Games8Th.Team", "FeilianBlocker", "Games8thGuard.sys");
        }

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static string ComputeSha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        private static void SetWin32Error(string prefix, int error)
        {
            lastMessage = prefix + "（" + error.ToString() + "）：" +
                          new Win32Exception(error).Message;
        }
    }
}
