using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Games8thTeamBlocker
{
    public static class Program
    {
        private const string FeilianInstallRoot = @"C:\Program Files\CorpLink";

        [STAThread]
        public static void Main(string[] args)
        {
            ConfigureConsole();
            ShowLogo();

            string action = ParseAction(args);
            if (action == "auto" || action == "feilian")
                RunAutomaticBlock();
            else if (action == "driver-status")
                PrintDriverStatus();
            else if (action == "list")
                ListBlockedPaths();
            else if (action == "clear")
                ClearBlockedPaths();
            else if (action == "unload")
                UnloadDriver();
            else if (action == "verify")
                VerifyFeilianTargets();
            else if (action == "test")
                RunDirectTest(args);
            else
            {
                PrintUsage();
                Environment.ExitCode = 64;
            }
        }

        private static void ConfigureConsole()
        {
            try
            {
                Console.Title = "Games8Th.Team - 飞连专用 CLI";
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch { }
        }

        private static string ParseAction(string[] args)
        {
            if (args == null || args.Length == 0) return "auto";
            if (string.Equals(args[0], "cli", StringComparison.OrdinalIgnoreCase))
                return args.Length > 1 ? args[1].ToLowerInvariant() : "auto";
            return args[0].ToLowerInvariant();
        }

        private static string GetActionArgument(string[] args)
        {
            if (args == null || args.Length == 0) return "";
            int start = string.Equals(args[0], "cli", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            if (args.Length <= start) return "";
            return string.Join(" ", args, start, args.Length - start).Trim().Trim('\"');
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法: Games8thBlocker.exe [cli] [auto|driver-status|list|clear|unload|verify|test]");
            Console.WriteLine("测试: Games8thBlocker.exe [cli] test \"C:\\Path\\Target.exe\"");
        }

        private static void ShowLogo()
        {
            try
            {
                Console.Clear();
            }
            catch { }

            Console.WriteLine();
            SetConsoleColor(ConsoleColor.DarkGray);
            WriteCentered("+--------------------------------------------------+");
            SetConsoleColor(ConsoleColor.DarkYellow);
            WriteCentered("   GGGGGG      88888     TTTTTTTTT   ");
            WriteCentered("  GG          88   88       TTT      ");
            WriteCentered("  GG  GGG      88888        TTT      ");
            WriteCentered("  GG   GG     88   88       TTT      ");
            WriteCentered("   GGGGGG      88888        TTT      ");
            SetConsoleColor(ConsoleColor.DarkGray);
            WriteCentered("+--------------------------------------------------+");
            SetConsoleColor(ConsoleColor.White);
            WriteCentered("G A M E S 8 T H . T E A M");
            SetConsoleColor(ConsoleColor.Cyan);
            WriteCentered("FEILIAN  KERNEL  NETWORK  GUARD");
            SetConsoleColor(ConsoleColor.DarkGray);
            WriteCentered("DRIVER MODE  |  CLI ONLY  |  X64");
            WriteCentered("+--------------------------------------------------+");
            Console.WriteLine();
            try { Console.ResetColor(); } catch { }
            Thread.Sleep(2000);
        }

        private static void SetConsoleColor(ConsoleColor color)
        {
            try { Console.ForegroundColor = color; }
            catch { }
        }

        private static void WriteCentered(string text)
        {
            int width = 80;
            try
            {
                if (!Console.IsOutputRedirected && Console.WindowWidth > 0)
                    width = Console.WindowWidth;
            }
            catch { }

            int padding = Math.Max(0, (width - text.Length) / 2);
            Console.WriteLine(new string(' ', padding) + text);
        }

        private static void RunAutomaticBlock()
        {
            Console.WriteLine("[1/3] 正在检测飞连进程、服务和安装路径...");
            List<string> targets = DiscoverFeilianTargets();
            PrintTargets(targets);
            if (targets.Count == 0)
            {
                Console.WriteLine("[UNVERIFIED] 当前机器未发现飞连目标，未执行任何屏蔽。");
                Environment.ExitCode = 2;
                return;
            }

            Console.WriteLine("[2/3] 正在验证内核驱动...");
            KernelDriver.DriverStatus status = EnsureDriverReady();
            if (!status.IsLoaded)
            {
                Console.WriteLine("[UNVERIFIED] 驱动不可用：" + status.Summary);
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine("[OK] " + status.Summary);

            Console.WriteLine("[3/3] 正在按真实飞连路径实施屏蔽...");
            bool allOk = true;
            foreach (string target in targets)
            {
                bool ok = KernelDriver.AddBlockedPath(target) &&
                          KernelDriver.ContainsBlockedPath(target);
                Console.WriteLine((ok ? "  [OK] " : "  [UNVERIFIED] ") + target);
                if (!ok)
                {
                    allOk = false;
                    if (!string.IsNullOrEmpty(KernelDriver.LastErrorMessage))
                        Console.WriteLine("       " + KernelDriver.LastErrorMessage);
                }
            }

            if (allOk)
                Console.WriteLine("[OK] 飞连针对性屏蔽已完成，全部目标均已通过 QUERY_PATHS 回读核验。");
            else
            {
                Console.WriteLine("[UNVERIFIED] 部分飞连目标未通过驱动下发/回读核验。");
                Environment.ExitCode = 1;
            }
        }

        private static void RunDirectTest(string[] args)
        {
            Console.WriteLine("[TEST] 跳过飞连检测，直接测试指定软件的驱动屏蔽链。");
            string rawTarget = GetActionArgument(args);
            if (string.IsNullOrWhiteSpace(rawTarget))
            {
                Console.WriteLine("[UNVERIFIED] 未提供测试软件 EXE 路径。");
                PrintUsage();
                Environment.ExitCode = 64;
                return;
            }

            string target;
            try { target = Path.GetFullPath(rawTarget); }
            catch (Exception ex)
            {
                Console.WriteLine("[UNVERIFIED] 测试路径无效：" + ex.Message);
                Environment.ExitCode = 2;
                return;
            }

            if (!File.Exists(target) ||
                !string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[UNVERIFIED] 测试目标必须是当前存在的 EXE：" + target);
                Environment.ExitCode = 2;
                return;
            }
            if (IsSelfPath(target))
            {
                Console.WriteLine("[UNVERIFIED] 拒绝将屏蔽器自身作为测试目标。");
                Environment.ExitCode = 2;
                return;
            }

            Console.WriteLine("  [TARGET] " + target);
            Console.WriteLine("[1/2] 正在验证内核驱动...");
            KernelDriver.DriverStatus status = EnsureDriverReady();
            if (!status.IsLoaded)
            {
                Console.WriteLine("[UNVERIFIED] 驱动不可用：" + status.Summary);
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine("[OK] " + status.Summary);

            Console.WriteLine("[2/2] 正在下发并回读测试软件完整路径...");
            bool ok = KernelDriver.AddBlockedPath(target) &&
                      KernelDriver.ContainsBlockedPath(target);
            if (ok)
            {
                Console.WriteLine("[OK] 测试目标已通过 QUERY_PATHS 完整路径回读核验：" + target);
                return;
            }

            Console.WriteLine("[UNVERIFIED] 测试目标未通过驱动下发/回读核验：" + target);
            if (!string.IsNullOrEmpty(KernelDriver.LastErrorMessage))
                Console.WriteLine("  " + KernelDriver.LastErrorMessage);
            Environment.ExitCode = 1;
        }

        private static KernelDriver.DriverStatus EnsureDriverReady()
        {
            KernelDriver.DriverStatus status = KernelDriver.GetStatus();
            if (status.IsLoaded) return status;

            Console.WriteLine("  驱动尚未就绪，正在释放并加载 EXE 内嵌签名驱动...");
            if (!EmbeddedDriverInstaller.EnsureLoaded())
                Console.WriteLine("  [UNVERIFIED] " + EmbeddedDriverInstaller.LastMessage);
            return KernelDriver.GetStatus();
        }

        private static void UnloadDriver()
        {
            if (KernelDriver.GetStatus().IsLoaded && !KernelDriver.ClearAll())
                Console.WriteLine("[UNVERIFIED] 卸载前清空路径失败：" + KernelDriver.LastErrorMessage);

            string message;
            if (EmbeddedDriverInstaller.UnloadAndCleanup(out message))
                Console.WriteLine("[OK] " + message);
            else
            {
                Console.WriteLine("[UNVERIFIED] " + message);
                Environment.ExitCode = 1;
            }
        }

        private static void PrintDriverStatus()
        {
            KernelDriver.DriverStatus status = KernelDriver.GetStatus();
            Console.WriteLine("服务存在: " + (status.ServicePresent ? "是" : "否"));
            Console.WriteLine("服务状态: " + (status.ServiceRunning ? "RUNNING" : "非 RUNNING"));
            Console.WriteLine("设备句柄: " + (status.DeviceOpen ? "可用" : "不可用"));
            Console.WriteLine("QUERY_PATHS: " + (status.IoctlResponsive ? "响应正常" : "失败"));
            Console.WriteLine("驱动状态: " + (status.IsLoaded ? "VERIFIED" : "UNVERIFIED"));
            Console.WriteLine("说明: " + status.Summary);
            if (!status.IsLoaded) Environment.ExitCode = 1;
        }

        private static void ListBlockedPaths()
        {
            string[] paths;
            string error;
            if (!KernelDriver.TryQueryBlockedPaths(out paths, out error))
            {
                Console.WriteLine("[UNVERIFIED] 无法查询驱动路径：" + error);
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine("驱动当前屏蔽路径（" + paths.Length.ToString() + " 条）：");
            foreach (string path in paths) Console.WriteLine("  " + path);
        }

        private static void ClearBlockedPaths()
        {
            KernelDriver.DriverStatus status = KernelDriver.GetStatus();
            if (!status.IsLoaded)
            {
                Console.WriteLine("[UNVERIFIED] 驱动不可用：" + status.Summary);
                Environment.ExitCode = 1;
                return;
            }
            if (KernelDriver.ClearAll())
                Console.WriteLine("[OK] 驱动屏蔽路径已清空，并已回读确认为空。");
            else
            {
                Console.WriteLine("[UNVERIFIED] 清理失败：" + KernelDriver.LastErrorMessage);
                Environment.ExitCode = 1;
            }
        }

        private static void VerifyFeilianTargets()
        {
            List<string> targets = DiscoverFeilianTargets();
            PrintTargets(targets);
            if (targets.Count == 0)
            {
                Console.WriteLine("[UNVERIFIED] 当前机器未发现飞连目标。");
                Environment.ExitCode = 2;
                return;
            }
            KernelDriver.DriverStatus status = KernelDriver.GetStatus();
            if (!status.IsLoaded)
            {
                Console.WriteLine("[UNVERIFIED] 驱动不可用：" + status.Summary);
                Environment.ExitCode = 1;
                return;
            }
            bool allPresent = true;
            foreach (string target in targets)
            {
                bool present = KernelDriver.ContainsBlockedPath(target);
                Console.WriteLine((present ? "  [OK] " : "  [MISSING] ") + target);
                if (!present) allPresent = false;
            }
            if (!allPresent) Environment.ExitCode = 1;
        }

        private static void PrintTargets(List<string> targets)
        {
            Console.WriteLine("发现飞连目标: " + targets.Count.ToString());
            foreach (string target in targets) Console.WriteLine("  [TARGET] " + target);
        }

        private static List<string> DiscoverFeilianTargets()
        {
            HashSet<string> found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddExecutablesUnder(FeilianInstallRoot, found);
            ScanProcesses(found);
            ScanServices(found);
            ScanUninstallRegistry(found);
            return CollapseTargets(found);
        }

        private static bool IsUnderFeilianInstallRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string root = Path.GetFullPath(FeilianInstallRoot).TrimEnd('\\');
                string candidate = Path.GetFullPath(path.Trim().Trim('"')).TrimEnd('\\');
                return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) ||
                       candidate.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static void AddExecutableTarget(string path, HashSet<string> found)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string candidate = Path.GetFullPath(path.Trim().Trim('"'));
                if (!IsUnderFeilianInstallRoot(candidate) || IsSelfPath(candidate)) return;
                if (!File.Exists(candidate)) return;
                if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) return;
                if (!string.Equals(Path.GetExtension(candidate), ".exe",
                                   StringComparison.OrdinalIgnoreCase)) return;
                found.Add(candidate);
            }
            catch { }
        }

        private static void AddExecutablesUnder(string root, HashSet<string> found)
        {
            if (!IsUnderFeilianInstallRoot(root) || !Directory.Exists(root)) return;
            try
            {
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
            }
            catch { return; }

            Stack<string> pending = new Stack<string>();
            pending.Push(Path.GetFullPath(root));
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                string[] files;
                try { files = Directory.GetFiles(current, "*.exe", SearchOption.TopDirectoryOnly); }
                catch { files = new string[0]; }
                foreach (string file in files) AddExecutableTarget(file, found);

                string[] directories;
                try { directories = Directory.GetDirectories(current); }
                catch { directories = new string[0]; }
                foreach (string directory in directories)
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(directory);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (IsUnderFeilianInstallRoot(directory)) pending.Push(directory);
                    }
                    catch { }
                }
            }
        }

        private static bool IsSelfPath(string candidate)
        {
            string self = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            string baseDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                .TrimEnd('\\');
            if (string.Equals(candidate, self, StringComparison.OrdinalIgnoreCase)) return true;
            if (Directory.Exists(candidate) &&
                string.Equals(candidate.TrimEnd('\\'), baseDirectory,
                              StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void ScanProcesses(HashSet<string> found)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Name, ExecutablePath FROM Win32_Process WHERE ExecutablePath IS NOT NULL"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        string path = item["ExecutablePath"] as string;
                        AddExecutableTarget(path, found);
                    }
                }
            }
            catch { }
        }

        private static void ScanServices(HashSet<string> found)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Name, DisplayName, PathName FROM Win32_Service"))
                {
                    foreach (ManagementObject item in searcher.Get())
                    {
                        string rawPath = item["PathName"] as string;
                        string executable = ExtractExecutablePath(rawPath);
                        AddExecutableTarget(executable, found);
                    }
                }
            }
            catch { }
        }

        private static void ScanUninstallRegistry(HashSet<string> found)
        {
            RegistryKey[] roots = { Registry.LocalMachine, Registry.CurrentUser };
            string[] keys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (RegistryKey root in roots)
            {
                foreach (string keyPath in keys)
                {
                    try
                    {
                        using (RegistryKey parent = root.OpenSubKey(keyPath))
                        {
                            if (parent == null) continue;
                            foreach (string childName in parent.GetSubKeyNames())
                            {
                                using (RegistryKey child = parent.OpenSubKey(childName))
                                {
                                    if (child == null) continue;
                                    string installLocation =
                                        child.GetValue("InstallLocation") as string;
                                    if (IsUnderFeilianInstallRoot(installLocation))
                                        AddExecutablesUnder(installLocation, found);
                                    AddExecutableTarget(
                                        ExtractExecutablePath(
                                            child.GetValue("DisplayIcon") as string),
                                        found);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private static string ExtractExecutablePath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string value = raw.Trim();
            int comma = value.LastIndexOf(',');
            if (comma > 1) value = value.Substring(0, comma);
            if (value.StartsWith("\""))
            {
                int end = value.IndexOf('"', 1);
                return end > 1 ? value.Substring(1, end - 1) : value.Trim('"');
            }
            int executableEnd = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (executableEnd >= 0) return value.Substring(0, executableEnd + 4);
            return value;
        }

        private static List<string> CollapseTargets(HashSet<string> found)
        {
            List<string> sorted = new List<string>(found);
            sorted.Sort(delegate(string left, string right)
            {
                int byLength = left.Length.CompareTo(right.Length);
                return byLength != 0 ? byLength : StringComparer.OrdinalIgnoreCase.Compare(left, right);
            });

            List<string> result = new List<string>();
            foreach (string candidate in sorted)
            {
                bool covered = false;
                foreach (string parent in result)
                {
                    if (Directory.Exists(parent) &&
                        candidate.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        covered = true;
                        break;
                    }
                }
                if (!covered) result.Add(candidate);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }
}
