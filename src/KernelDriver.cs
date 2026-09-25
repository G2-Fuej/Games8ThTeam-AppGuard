using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Games8thTeamBlocker
{
    /// <summary>
    /// 与 Games8thGuard.sys 内核驱动通信的封装。
    /// 通过 DeviceIoControl 下发/查询被限制进程路径列表。
    /// 驱动在 ALE 授权连接层按进程路径拦截所有网络连接（含本地代理流量）。
    /// </summary>
    public static class KernelDriver
    {
        // 设备符号链接
        private const string G8T_DEVICE_PATH = @"\\.\G8TGuard";

        // IOCTL 定义（与驱动 g8tguard.c 保持一致）
        private const uint FILE_DEVICE_UNKNOWN = 0x22;
        private const uint METHOD_BUFFERED = 0;
        private const uint FILE_ANY_ACCESS = 0;
        private const uint G8T_IOCTL_BASE = 0x8000;

        private static readonly uint IOCTL_G8T_BLOCK_PATH =
            CTL_CODE(FILE_DEVICE_UNKNOWN, G8T_IOCTL_BASE + 1, METHOD_BUFFERED, FILE_ANY_ACCESS);
        private static readonly uint IOCTL_G8T_UNBLOCK_PATH =
            CTL_CODE(FILE_DEVICE_UNKNOWN, G8T_IOCTL_BASE + 2, METHOD_BUFFERED, FILE_ANY_ACCESS);
        private static readonly uint IOCTL_G8T_QUERY_PATHS =
            CTL_CODE(FILE_DEVICE_UNKNOWN, G8T_IOCTL_BASE + 3, METHOD_BUFFERED, FILE_ANY_ACCESS);
        private static readonly uint IOCTL_G8T_CLEAR_ALL =
            CTL_CODE(FILE_DEVICE_UNKNOWN, G8T_IOCTL_BASE + 4, METHOD_BUFFERED, FILE_ANY_ACCESS);

        private const int MAX_BLOCKED_PATHS = 64;
        private const int MAX_PATH_LEN = 520;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct G8T_BLOCKED_PATHS
        {
            public uint Count;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_BLOCKED_PATHS * MAX_PATH_LEN)]
            public char[] Raw;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;

        private static uint CTL_CODE(uint deviceType, uint function, uint method, uint access)
        {
            return (deviceType << 16) | (access << 14) | (function << 2) | method;
        }

        /// <summary>检查内核驱动是否已加载</summary>
        public static bool IsLoaded()
        {
            IntPtr h = CreateFileW(G8T_DEVICE_PATH, GENERIC_READ | GENERIC_WRITE,
                                   FILE_SHARE_READ | FILE_SHARE_WRITE,
                                   IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.ToInt64() == -1)
                return false;
            CloseHandle(h);
            return true;
        }

        /// <summary>向驱动下发一个被限制路径</summary>
        public static bool AddBlockedPath(string path)
        {
            return SendString(IOCTL_G8T_BLOCK_PATH, path);
        }

        /// <summary>从驱动移除一个被限制路径</summary>
        public static bool RemoveBlockedPath(string path)
        {
            return SendString(IOCTL_G8T_UNBLOCK_PATH, path);
        }

        /// <summary>清空驱动中的全部被限制路径</summary>
        public static bool ClearAll()
        {
            return SendString(IOCTL_G8T_CLEAR_ALL, "");
        }

        /// <summary>查询驱动中当前被限制的路径列表</summary>
        public static string[] QueryBlockedPaths()
        {
            string[] result = new string[0];
            IntPtr h = CreateFileW(G8T_DEVICE_PATH, GENERIC_READ | GENERIC_WRITE,
                                   FILE_SHARE_READ | FILE_SHARE_WRITE,
                                   IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.ToInt64() == -1)
                return result;

            try
            {
                int bufSize = MAX_BLOCKED_PATHS * MAX_PATH_LEN * 2 + 8;
                IntPtr outBuf = Marshal.AllocHGlobal(bufSize);
                uint bytesReturned = 0;
                try
                {
                    bool ok = DeviceIoControl(h, IOCTL_G8T_QUERY_PATHS, IntPtr.Zero, 0,
                                              outBuf, (uint)bufSize, out bytesReturned, IntPtr.Zero);
                    if (ok && bytesReturned >= 4)
                    {
                        uint count = (uint)Marshal.ReadInt32(outBuf);
                        // 数据区为 UTF-16 字符串数组（每项 MAX_PATH_LEN 字符）
                        System.Collections.Generic.List<string> paths = new System.Collections.Generic.List<string>();
                        for (uint i = 0; i < count && i < MAX_BLOCKED_PATHS; i++)
                        {
                            IntPtr p = new IntPtr(outBuf.ToInt64() + 4 + i * MAX_PATH_LEN * 2);
                            string s = Marshal.PtrToStringUni(p, MAX_PATH_LEN);
                            if (s != null)
                            {
                                int nul = s.IndexOf('\0');
                                if (nul >= 0) s = s.Substring(0, nul);
                                if (s.Length > 0) paths.Add(s);
                            }
                        }
                        result = paths.ToArray();
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(outBuf);
                }
            }
            finally
            {
                CloseHandle(h);
            }
            return result;
        }

        private static bool SendString(uint ioctl, string value)
        {
            IntPtr h = CreateFileW(G8T_DEVICE_PATH, GENERIC_READ | GENERIC_WRITE,
                                   FILE_SHARE_READ | FILE_SHARE_WRITE,
                                   IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.ToInt64() == -1)
                return false;

            try
            {
                byte[] data = Encoding.Unicode.GetBytes(value + "\0");
                IntPtr inBuf = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, inBuf, data.Length);
                uint bytesReturned = 0;
                try
                {
                    bool ok = DeviceIoControl(h, ioctl, inBuf, (uint)data.Length,
                                              IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
                    return ok;
                }
                finally
                {
                    Marshal.FreeHGlobal(inBuf);
                }
            }
            finally
            {
                CloseHandle(h);
            }
        }
    }
}
