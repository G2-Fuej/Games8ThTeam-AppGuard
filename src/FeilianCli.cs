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
        private static readonly string[] FeilianHints = { "feilian", "飞连" };

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
            else if (action == "verify")
                VerifyFeilianTargets();
            else
            {
                Console.WriteLine("用法: Games8thBlocker.exe [cli] [auto|driver-status|list|clear|verify]");
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

        private static void ShowLogo()
        {
            try
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.DarkYellow;
            }
            catch { }

            Console.WriteLine();
            Console.WriteLine("        GGGG   8888  TTTTT");
            Console.WriteLine("       G       88     T");
            Console.WriteLine("       G  GG   8888   T");
            Console.WriteLine("       G   G   88     T");
            Console.WriteLine("        GGGG   8888   T");
            Console.WriteLine();
            Console.WriteLine("                 Games8Th.Team");
            Console.WriteLine("              飞连专用网络屏蔽器");
            Console.WriteLine();
            try { Console.ResetColor(); } catch { }
            Thread.Sleep(2000);
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

        private static KernelDriver.DriverStatus EnsureDriverReady()
        {
            KernelDriver.DriverStatus status = KernelDriver.GetStatus();
            if (status.IsLoaded) return status;

            string loader = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "driver", "load.bat");
            if (!File.Exists(loader)) return status;

            Console.WriteLine("  驱动尚未就绪，正在调用 driver\\load.bat...");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/d /c \"\"" + loader + "\"\"";
                psi.WorkingDirectory = Path.GetDirectoryName(loader);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output.TrimEnd());
                        if (!string.IsNullOrWhiteSpace(error)) Console.WriteLine(error.TrimEnd());
                        Console.WriteLine("  驱动加载脚本退出码: " + process.ExitCode.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [UNVERIFIED] 无法运行驱动加载脚本：" + ex.Message);
            }
            return KernelDriver.GetStatus();
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
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            };

            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                AddKnownLocations(root, found);
                ScanDirectories(root, 3, found);
            }
            ScanProcesses(found);
            ScanServices(found);
            ScanUninstallRegistry(found);
            return CollapseTargets(found);
        }

        private static void AddKnownLocations(string root, HashSet<string> found)
        {
            string[] relatives =
            {
                "Feilian", "飞连", "ByteDance\\Feilian", "Bytedance\\Feilian",
                "Lark\\Feilian", "FeilianClient"
            };
            foreach (string relative in relatives) AddExisting(Path.Combine(root, relative), found);
        }

        private static bool HasHint(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string lower = value.ToLowerInvariant();
            foreach (string hint in FeilianHints)
                if (lower.Contains(hint)) return true;
            return false;
        }

        private static void AddExisting(string path, HashSet<string> found)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string candidate = Path.GetFullPath(path.Trim().Trim('"'));
                if (IsSelfPath(candidate)) return;
                if (File.Exists(candidate) || Directory.Exists(candidate)) found.Add(candidate);
            }
            catch { }
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

        private static void ScanDirectories(string root, int depth, HashSet<string> found)
        {
            if (depth < 0 || !Directory.Exists(root)) return;
            string[] directories;
            try { directories = Directory.GetDirectories(root); }
            catch { return; }
            foreach (string directory in directories)
            {
                if (HasHint(Path.GetFileName(directory))) AddExisting(directory, found);
                if (depth > 0) ScanDirectories(directory, depth - 1, found);
            }
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
                        string name = item["Name"] as string;
                        string path = item["ExecutablePath"] as string;
                        if (HasHint(name) || HasHint(Path.GetFileName(path)))
                            AddExisting(path, found);
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
                        string name = item["Name"] as string;
                        string display = item["DisplayName"] as string;
                        string rawPath = item["PathName"] as string;
                        string executable = ExtractExecutablePath(rawPath);
                        if (HasHint(name) || HasHint(display) ||
                            HasHint(Path.GetFileName(executable)))
                            AddExisting(executable, found);
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
                                    string display = child.GetValue("DisplayName") as string;
                                    if (!HasHint(display)) continue;
                                    AddExisting(child.GetValue("InstallLocation") as string, found);
                                    AddExisting(ExtractExecutablePath(child.GetValue("DisplayIcon") as string), found);
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
            int split = value.IndexOf(' ');
            return split > 0 ? value.Substring(0, split) : value;
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
