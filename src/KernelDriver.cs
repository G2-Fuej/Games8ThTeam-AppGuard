using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Games8thTeamBlocker
{
    /// <summary>
    /// Games8thGuard.sys 的唯一网络限制后端。
    /// 成功标准不是“规则/服务名称存在”，而是服务、设备和 QUERY_PATHS
    /// 三层都真实可用，并且每次下发后都能回读到完整路径。
    /// </summary>
    public static class KernelDriver
    {
        private const string ServiceName = "Games8thGuard";
        private const string DevicePath = @"\\.\G8TGuard";

        private const uint FileDeviceUnknown = 0x22;
        private const uint MethodBuffered = 0;
        private const uint FileAnyAccess = 0;
        private const uint IoctlBase = 0x8000;
        private const int MaxBlockedPaths = 64;
        private const int MaxPathLen = 520;
        private const int ErrorSuccess = 0;
        private const int ErrorServiceDoesNotExist = 1060;
        private const int ErrorServiceNotActive = 1062;
        private const int ServiceRunning = 4;
        private const uint ScManagerConnect = 0x0001;
        private const uint ServiceQueryStatus = 0x0004;

        private static readonly uint BlockPathCode =
            CtlCode(FileDeviceUnknown, IoctlBase + 1, MethodBuffered, FileAnyAccess);
        private static readonly uint UnblockPathCode =
            CtlCode(FileDeviceUnknown, IoctlBase + 2, MethodBuffered, FileAnyAccess);
        private static readonly uint QueryPathsCode =
            CtlCode(FileDeviceUnknown, IoctlBase + 3, MethodBuffered, FileAnyAccess);
        private static readonly uint ClearAllCode =
            CtlCode(FileDeviceUnknown, IoctlBase + 4, MethodBuffered, FileAnyAccess);

        private static int lastErrorCode;
        private static string lastErrorMessage = "";

        public sealed class DriverStatus
        {
            public bool ServicePresent;
            public bool ServiceRunning;
            public bool DeviceOpen;
            public bool IoctlResponsive;
            public bool IsLoaded;
            public int ErrorCode;
            public string ErrorMessage;
            public string Summary;

            public DriverStatus()
            {
                ErrorMessage = "";
                Summary = "";
            }
        }

        public static int LastErrorCode { get { return lastErrorCode; } }
        public static string LastErrorMessage { get { return lastErrorMessage; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatusProcess
        {
            public int ServiceType;
            public int CurrentState;
            public int ControlsAccepted;
            public int Win32ExitCode;
            public int ServiceSpecificExitCode;
            public int CheckPoint;
            public int WaitHint;
            public int ProcessId;
            public int ServiceFlags;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(
            string machineName,
            string databaseName,
            uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(
            IntPtr scm,
            string serviceName,
            uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatusEx(
            IntPtr service,
            int infoLevel,
            out ServiceStatusProcess status,
            int bufferSize,
            out int bytesNeeded);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr device,
            uint controlCode,
            IntPtr inputBuffer,
            uint inputBufferSize,
            IntPtr outputBuffer,
            uint outputBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;

        private static uint CtlCode(uint deviceType, uint function, uint method, uint access)
        {
            return (deviceType << 16) | (access << 14) | (function << 2) | method;
        }

        public static DriverStatus GetStatus()
        {
            DriverStatus status = new DriverStatus();
            int code;
            string message;

            if (!TryGetServiceState(out status.ServicePresent, out status.ServiceRunning,
                                    out code, out message))
            {
                status.ErrorCode = code;
                status.ErrorMessage = message;
                status.Summary = message;
                return status;
            }

            if (!status.ServiceRunning)
            {
                status.ErrorCode = ErrorServiceNotActive;
                status.ErrorMessage = "服务存在但未处于 RUNNING 状态";
                status.Summary = status.ErrorMessage;
                return status;
            }

            IntPtr device = OpenDevice(out code, out message);
            if (device == IntPtr.Zero)
            {
                status.ErrorCode = code;
                status.ErrorMessage = message;
                status.Summary = "服务 RUNNING，但设备句柄不可用：" + message;
                return status;
            }

            status.DeviceOpen = true;
            try
            {
                string[] paths;
                if (!TryQueryBlockedPaths(device, out paths, out code, out message))
                {
                    status.ErrorCode = code;
                    status.ErrorMessage = message;
                    status.Summary = "设备可打开，但 QUERY_PATHS 失败：" + message;
                    return status;
                }

                status.IoctlResponsive = true;
                status.IsLoaded = true;
                status.Summary = "服务 RUNNING、设备可打开、QUERY_PATHS 响应正常（当前 " +
                                 paths.Length.ToString() + " 条路径）";
                return status;
            }
            finally
            {
                CloseHandle(device);
            }
        }

        public static bool IsLoaded()
        {
            return GetStatus().IsLoaded;
        }

        public static bool AddBlockedPath(string path)
        {
            string normalized;
            if (!TryNormalizePath(path, out normalized))
            {
                SetError(87, "目标路径不存在或无法规范化");
                return false;
            }

            if (!SendString(BlockPathCode, normalized))
                return false;

            string[] paths;
            string error;
            if (!TryQueryBlockedPaths(out paths, out error))
            {
                SetError(lastErrorCode, error);
                return false;
            }
            if (!ContainsPath(paths, normalized))
            {
                SetError(1168, "驱动未回读到已下发的完整路径");
                return false;
            }
            return true;
        }

        public static bool RemoveBlockedPath(string path)
        {
            string normalized;
            if (!TryNormalizePath(path, out normalized))
            {
                SetError(87, "目标路径不存在或无法规范化");
                return false;
            }

            if (!SendString(UnblockPathCode, normalized))
                return false;

            string[] paths;
            string error;
            if (!TryQueryBlockedPaths(out paths, out error))
            {
                SetError(lastErrorCode, error);
                return false;
            }
            if (ContainsPath(paths, normalized))
            {
                SetError(1168, "驱动清理后仍回读到目标路径");
                return false;
            }
            return true;
        }

        public static bool ClearAll()
        {
            if (!SendString(ClearAllCode, ""))
                return false;

            string[] paths;
            string error;
            if (!TryQueryBlockedPaths(out paths, out error))
            {
                SetError(lastErrorCode, error);
                return false;
            }
            if (paths.Length != 0)
            {
                SetError(1168, "驱动清理后仍有 " + paths.Length.ToString() + " 条路径");
                return false;
            }
            return true;
        }

        public static string[] QueryBlockedPaths()
        {
            string[] paths;
            string error;
            if (TryQueryBlockedPaths(out paths, out error))
                return paths;
            return new string[0];
        }

        public static bool TryQueryBlockedPaths(out string[] paths, out string error)
        {
            IntPtr device = OpenDevice(out lastErrorCode, out error);
            if (device == IntPtr.Zero)
            {
                paths = new string[0];
                lastErrorMessage = error;
                return false;
            }

            try
            {
                bool result = TryQueryBlockedPaths(device, out paths, out lastErrorCode, out error);
                lastErrorMessage = error ?? "";
                return result;
            }
            finally
            {
                CloseHandle(device);
            }
        }

        public static bool ContainsBlockedPath(string path)
        {
            string normalized;
            if (!TryNormalizePath(path, out normalized))
                return false;

            string[] paths;
            string error;
            if (!TryQueryBlockedPaths(out paths, out error))
                return false;
            return ContainsPath(paths, normalized);
        }

        private static bool TryGetServiceState(
            out bool present,
            out bool running,
            out int errorCode,
            out string error)
        {
            present = false;
            running = false;
            errorCode = ErrorSuccess;
            error = "";

            IntPtr scm = OpenSCManager(null, null, ScManagerConnect);
            if (scm == IntPtr.Zero)
            {
                errorCode = Marshal.GetLastWin32Error();
                error = "无法打开 SCM：" + new Win32Exception(errorCode).Message;
                return false;
            }

            IntPtr service = IntPtr.Zero;
            try
            {
                service = OpenService(scm, ServiceName, ServiceQueryStatus);
                if (service == IntPtr.Zero)
                {
                    errorCode = Marshal.GetLastWin32Error();
                    if (errorCode == ErrorServiceDoesNotExist)
                        error = "服务 " + ServiceName + " 不存在";
                    else
                        error = "无法打开服务 " + ServiceName + "：" +
                                new Win32Exception(errorCode).Message;
                    return false;
                }

                present = true;
                ServiceStatusProcess serviceStatus;
                int bytesNeeded;
                if (!QueryServiceStatusEx(service, 0, out serviceStatus,
                                          Marshal.SizeOf(typeof(ServiceStatusProcess)),
                                          out bytesNeeded))
                {
                    errorCode = Marshal.GetLastWin32Error();
                    error = "QueryServiceStatusEx 失败：" +
                            new Win32Exception(errorCode).Message;
                    return false;
                }

                running = serviceStatus.CurrentState == ServiceRunning;
                return true;
            }
            finally
            {
                if (service != IntPtr.Zero) CloseServiceHandle(service);
                CloseServiceHandle(scm);
            }
        }

        private static IntPtr OpenDevice(out int errorCode, out string error)
        {
            IntPtr device = CreateFile(
                DevicePath,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (device.ToInt64() != -1 && device != IntPtr.Zero)
            {
                errorCode = ErrorSuccess;
                error = "";
                return device;
            }

            errorCode = Marshal.GetLastWin32Error();
            error = "CreateFile(" + DevicePath + ") 失败：" +
                    new Win32Exception(errorCode).Message;
            return IntPtr.Zero;
        }

        private static bool TryQueryBlockedPaths(
            IntPtr device,
            out string[] paths,
            out int errorCode,
            out string error)
        {
            paths = new string[0];
            errorCode = ErrorSuccess;
            error = "";

            int bufferSize = checked(4 + MaxBlockedPaths * MaxPathLen * 2);
            IntPtr output = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint bytesReturned;
                bool ok = DeviceIoControl(
                    device,
                    QueryPathsCode,
                    IntPtr.Zero,
                    0,
                    output,
                    (uint)bufferSize,
                    out bytesReturned,
                    IntPtr.Zero);
                if (!ok)
                {
                    errorCode = Marshal.GetLastWin32Error();
                    error = "QUERY_PATHS DeviceIoControl 失败：" +
                            new Win32Exception(errorCode).Message;
                    return false;
                }
                if (bytesReturned < 4)
                {
                    errorCode = 13;
                    error = "QUERY_PATHS 返回长度不足";
                    return false;
                }

                uint count = unchecked((uint)Marshal.ReadInt32(output));
                if (count > MaxBlockedPaths)
                {
                    errorCode = 13;
                    error = "QUERY_PATHS 返回非法路径数量：" + count.ToString();
                    return false;
                }

                List<string> result = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    IntPtr item = new IntPtr(output.ToInt64() + 4 +
                                             i * MaxPathLen * 2);
                    string value = Marshal.PtrToStringUni(item, MaxPathLen);
                    if (value == null) value = "";
                    int nul = value.IndexOf('\0');
                    if (nul >= 0) value = value.Substring(0, nul);
                    if (value.Length == 0)
                    {
                        errorCode = 13;
                        error = "QUERY_PATHS 返回空路径";
                        return false;
                    }
                    result.Add(value);
                }
                paths = result.ToArray();
                lastErrorCode = ErrorSuccess;
                lastErrorMessage = "";
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(output);
            }
        }

        private static bool SendString(uint ioctl, string value)
        {
            if (value == null) value = "";
            if (value.Length >= MaxPathLen)
            {
                SetError(206, "路径长度超过驱动限制");
                return false;
            }

            IntPtr device = OpenDevice(out lastErrorCode, out lastErrorMessage);
            if (device == IntPtr.Zero)
                return false;

            byte[] data = Encoding.Unicode.GetBytes(value + "\0");
            IntPtr input = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, input, data.Length);
                uint bytesReturned;
                bool ok = DeviceIoControl(
                    device,
                    ioctl,
                    input,
                    (uint)data.Length,
                    IntPtr.Zero,
                    0,
                    out bytesReturned,
                    IntPtr.Zero);
                if (!ok)
                {
                    lastErrorCode = Marshal.GetLastWin32Error();
                    lastErrorMessage = "DeviceIoControl 失败：" +
                                       new Win32Exception(lastErrorCode).Message;
                    return false;
                }
                lastErrorCode = ErrorSuccess;
                lastErrorMessage = "";
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(input);
                CloseHandle(device);
            }
        }

        private static bool TryNormalizePath(string path, out string normalized)
        {
            normalized = "";
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                string candidate = path.Trim().Trim('"');
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                    return false;
                normalized = Path.GetFullPath(candidate).Replace('/', '\\');
                return normalized.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsPath(string[] paths, string expected)
        {
            foreach (string path in paths)
            {
                if (string.Equals(path, expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void SetError(int code, string message)
        {
            lastErrorCode = code;
            lastErrorMessage = message ?? "";
        }
    }
}
