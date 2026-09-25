using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Games8thTeamBlocker
{
    // ===================== THEME (AppGuard light theme) =====================
    public static class Theme
    {
        public static Color Hex(string h)
        {
            h = h.TrimStart('#');
            int r = int.Parse(h.Substring(0, 2), NumberStyles.HexNumber);
            int g = int.Parse(h.Substring(2, 2), NumberStyles.HexNumber);
            int b = int.Parse(h.Substring(4, 2), NumberStyles.HexNumber);
            return Color.FromArgb(r, g, b);
        }

        // Base
        public static readonly Color BG = Hex("#f5f7fa");
        public static readonly Color PANEL = Hex("#ffffff");
        public static readonly Color PANEL_ALT = Hex("#eef1f6");
        public static readonly Color BORDER = Hex("#d7dce5");
        public static readonly Color TEXT = Hex("#1f2530");
        public static readonly Color TEXT_DIM = Hex("#6b7383");
        public static readonly Color TEXT_MUTE = Hex("#9aa2b1");

        // Brand
        public static readonly Color BRAND = Hex("#e07820");
        public static readonly Color BRAND_DARK = Hex("#b85f12");
        public static readonly Color BRAND_SOFT = Hex("#fdf1e4");

        // Log colors
        public static readonly Color LOG_OK = Hex("#1f9d3d");
        public static readonly Color LOG_FAIL = Hex("#d63b30");
        public static readonly Color LOG_WARN = Hex("#c98a00");
        public static readonly Color LOG_INFO = Hex("#0f7f9c");
        public static readonly Color LOG_STEP = Hex("#8a4fbf");
        public static readonly Color LOG_DIM = Hex("#8b93a2");
        public static readonly Color LOG_BG = Hex("#0f1420");
        public static readonly Color LOG_FG = Hex("#d5dae4");

        public static readonly string FONT_UI = "Microsoft YaHei UI";
        public static readonly string FONT_MONO = "Consolas";

        public static Color LevelColor(string level)
        {
            switch (level)
            {
                case "OK": return LOG_OK;
                case "FAIL": return LOG_FAIL;
                case "WARN": return LOG_WARN;
                case "INFO": return LOG_INFO;
                case "STEP": return LOG_STEP;
                case "DIM": return LOG_DIM;
                case "HDR": return BRAND;
                default: return LOG_INFO;
            }
        }
    }

    // ===================== RUN HELPERS =====================
    public static class Cmd
    {
        public static readonly string Q = ((char)34).ToString();

        public class RunResult
        {
            public int ExitCode = -1;
            public string Output = "";
            public bool Succeeded { get { return ExitCode == 0; } }
        }

        // 中文 Windows 控制台工具(icacls/netsh/sc)输出为 GBK/OEM 936，
        // PowerShell 默认也按控制台代码页输出。统一用系统 OEM 代码页解码，
        // 避免 UTF8 解码把中文成功标记变乱码导致检测失败。
        private static Encoding OemEncoding
        {
            get
            {
                try
                {
                    return Encoding.GetEncoding(936);
                }
                catch
                {
                    return Encoding.Default;
                }
            }
        }

        public static string Run(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = OemEncoding;
                psi.StandardErrorEncoding = OemEncoding;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return "ERR: cannot start";
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit(60000);
                    return o + e;
                }
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        // Run a PowerShell script from a temp .ps1 file (avoids quoting issues)
        // PowerShell 输出也按控制台代码页(GBK)，与 Run 同一解码
        public static string RunPS(string script)
        {
            string dir = Path.GetTempPath();
            string file = Path.Combine(dir, "g8t_" + Guid.NewGuid().ToString("N") + ".ps1");
            try
            {
                File.WriteAllText(file, script, new UTF8Encoding(true));
                string cmd = "-NoProfile -ExecutionPolicy Bypass -File " + Q + file + Q;
                return Run("powershell.exe", cmd);
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }

        // 与 RunPS 相同，但返回退出码+输出，用于必须按真实结果判断的命令。
        public static RunResult RunPSEx(string script)
        {
            string dir = Path.GetTempPath();
            string file = Path.Combine(dir, "g8t_" + Guid.NewGuid().ToString("N") + ".ps1");
            try
            {
                File.WriteAllText(file, script, new UTF8Encoding(true));
                string cmd = "-NoProfile -ExecutionPolicy Bypass -File " + Q + file + Q;
                return RunEx("powershell.exe", cmd);
            }
            finally
            {
                try { File.Delete(file); } catch { }
            }
        }

        // 执行命令并返回退出码+输出，供调用方按真实结果判断成败。
        // 与 Run 相同的启动/编码设置，额外暴露 ExitCode；超时 60s 会终止进程。
        public static RunResult RunEx(string file, string args)
        {
            RunResult rr = new RunResult();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = OemEncoding;
                psi.StandardErrorEncoding = OemEncoding;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) { rr.Output = "ERR: cannot start"; return rr; }
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(60000))
                    {
                        try { p.Kill(); } catch { }
                        rr.ExitCode = -2;
                        rr.Output = o + e;
                        return rr;
                    }
                    rr.ExitCode = p.ExitCode;
                    rr.Output = o + e;
                    return rr;
                }
            }
            catch (Exception ex)
            {
                rr.Output = "ERR: " + ex.Message;
                return rr;
            }
        }
    }

    // ===================== FIREWALL ENGINE =====================
    public static class Firewall
    {
        public const string RulePrefix = "Games8th_Blocker";

        public static bool IsFirewallEnabled()
        {
            // ConvertTo-Csv 会把布尔序列化成 "value__"/"1"，且可能带 BOM/CRLF。
            // 用 -Foreach 直接输出 "True"/"False" 更可靠。
            string out1 = Cmd.RunPS(
                "Get-NetFirewallProfile | ForEach-Object { $_.Enabled.ToString() } | Where-Object { $_ -eq 'False' }");
            // 输出为空 = 没有任何 False = 全部开启
            string trimmed = out1.Trim();
            return trimmed.Length == 0;
        }

        public static string EnableFirewall()
        {
            return Cmd.RunPS("Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled True; 'DONE'");
        }

        public static string Hash8(string path)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(path);
                byte[] hash = SHA256.Create().ComputeHash(bytes);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
            catch { return "00000000"; }
        }

        public static string RuleName(string exePath, string direction)
        {
            return RulePrefix + "_" + Hash8(exePath) + "_" + direction;
        }

        // returns true on success
        public static bool AddBlockRule(string exePath, string direction)
        {
            string name = RuleName(exePath, direction);
            string norm = exePath.Replace('/', '\\');
            string script = "if (-not (Get-NetFirewallRule -DisplayName " + Cmd.Q + name + Cmd.Q + " -ErrorAction SilentlyContinue)) { " +
                "New-NetFirewallRule -DisplayName " + Cmd.Q + name + Cmd.Q +
                " -Direction " + (direction == "out" ? "Outbound" : "Inbound") +
                " -Action Block -Program " + Cmd.Q + norm + Cmd.Q +
                " -Enabled True -ErrorAction SilentlyContinue; } " +
                "if (Get-NetFirewallRule -DisplayName " + Cmd.Q + name + Cmd.Q + " -ErrorAction SilentlyContinue) { 'OK' } else { 'FAIL'; exit 1 }";
            Cmd.RunResult rr = Cmd.RunPSEx(script);
            return rr.Succeeded && rr.Output.Contains("OK");
        }

        public static bool RemoveRule(string ruleName)
        {
            string script = "Remove-NetFirewallRule -DisplayName " + Cmd.Q + ruleName + Cmd.Q +
                " -ErrorAction SilentlyContinue; " +
                "if (Get-NetFirewallRule -DisplayName " + Cmd.Q + ruleName + Cmd.Q + " -ErrorAction SilentlyContinue) { 'FAIL'; exit 1 } else { 'OK' }";
            Cmd.RunResult rr = Cmd.RunPSEx(script);
            return rr.Succeeded && rr.Output.Contains("OK");
        }

        public static List<string> ListRules()
        {
            string script = "Get-NetFirewallRule | Where-Object { $_.DisplayName -like 'Games8th_Blocker*' } | " +
                "Select-Object -ExpandProperty DisplayName";
            string output = Cmd.RunPS(script);
            List<string> list = new List<string>();
            string[] lines = output.Split('\n');
            foreach (string ln in lines)
            {
                string t = ln.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        public static int RemoveAllRules()
        {
            string script = "$before = @(Get-NetFirewallRule | Where-Object { $_.DisplayName -like 'Games8th_Blocker*' }).Count; " +
                "Get-NetFirewallRule | Where-Object { $_.DisplayName -like 'Games8th_Blocker*' } | " +
                "ForEach-Object { Remove-NetFirewallRule -DisplayName $_.DisplayName -ErrorAction SilentlyContinue }; $before";
            string output = Cmd.RunPS(script);
            string t = output.Trim();
            int n = 0;
            int.TryParse(t, out n);
            return n;
        }

        // Is an exe currently fully blocked (both rules exist)?
        public static bool IsBlocked(string exePath)
        {
            List<string> rules = ListRules();
            return rules.Exists(x => string.Equals(x, RuleName(exePath, "out"), StringComparison.OrdinalIgnoreCase)) &&
                   rules.Exists(x => string.Equals(x, RuleName(exePath, "in"), StringComparison.OrdinalIgnoreCase));
        }
    }

    // ===================== KASPERSKY ENGINE =====================
    public static class Kaspersky
    {
        private static string avpCom;

        public static string AvpPath
        {
            get
            {
                if (avpCom != null) return avpCom;
                avpCom = "";
                try
                {
                    string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    if (Directory.Exists(pf))
                    {
                        string kdir = Path.Combine(pf, "Kaspersky Lab");
                        if (Directory.Exists(kdir))
                        {
                            string[] sub = Directory.GetDirectories(kdir);
                            foreach (string d in sub)
                            {
                                string cand = Path.Combine(d, "avp.com");
                                if (File.Exists(cand)) { avpCom = cand; break; }
                            }
                        }
                    }
                }
                catch { }
                return avpCom;
            }
        }

        public static bool IsInstalled() { return AvpPath.Length > 0; }

        public static string Status()
        {
            if (!IsInstalled()) return "";
            return Cmd.Run(AvpPath, "STATUS");
        }

        public static string Scan(string target)
        {
            if (!IsInstalled()) return "";
            string report = Path.Combine(Path.GetTempPath(), "g8t_scan_" + Guid.NewGuid().ToString("N") + ".txt");
            string args = "SCAN " + Cmd.Q + target + Cmd.Q + " /i3 /R:" + Cmd.Q + report + Cmd.Q;
            string result = Cmd.Run(AvpPath, args);
            try { if (File.Exists(report)) File.Delete(report); } catch { }
            return result;
        }
    }

    // ===================== TARGET =====================
    public class Target
    {
        public string Path_;
        public string Name;
        public bool Enabled;
        public string Strategy; // "NET_BOTH" or "NET_OUT"
        public string State = "o";

        public Target(string path, bool enabled)
        {
            Path_ = path;
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (Name.Length == 0) Name = System.IO.Path.GetFileName(path);
            if (Name.Length == 0) Name = path;
            Enabled = enabled;
            Strategy = "NET_BOTH";
        }

        public bool IsDir { get { return Directory.Exists(Path_); } }

        public string StrategyLabel
        {
            get { return Strategy == "NET_OUT" ? "仅出站" : "双向封锁"; }
        }
    }

    // ===================== COMPONENT ENGINE (exe-callable files) =====================
    // 文件夹目标内的组件识别：
    //   .exe      -> 防火墙规则封锁网络（可独立运行的进程）
    //   .dll/.sys/.ocx/.node/.com/.bat/.cmd/.ps1/.scr 等
    //              -> icacls DENY 剥夺 Everyone 访问权，使任何 exe 都无法加载/调用
    public static class Components
    {
        public static readonly string[] ExeExts = { ".exe", ".com", ".scr" };
        public static readonly string[] DenyExts = { ".dll", ".sys", ".ocx", ".node", ".bat", ".cmd", ".ps1", ".js", ".vbs" };

        public static bool IsExe(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            return Array.Exists(ExeExts, x => x == e);
        }

        public static bool IsDenyComponent(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            return Array.Exists(DenyExts, x => x == e);
        }

        public static bool IsTargetable(string path)
        {
            return IsExe(path) || IsDenyComponent(path);
        }

        // Enumerate all targetable files under a folder (recursive)
        public static List<string> ScanFolder(string folder)
        {
            List<string> result = new List<string>();
            try
            {
                string[] all = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
                foreach (string f in all)
                {
                    if (IsTargetable(f)) result.Add(f);
                }
            }
            catch { }
            return result;
        }

        // Deny Everyone access to a component via icacls (works for DLL, SYS, etc.)
        public static bool DenyAccess(string path)
        {
            string norm = path.Replace('/', '\\');
            // icacls 退出码 0 表示成功；"已处理" 文本可能因系统语言差异不可靠，以退出码为准
            Cmd.RunResult rr = Cmd.RunEx("icacls", Cmd.Q + norm + Cmd.Q + " /deny Everyone:(RX)");
            return rr.Succeeded;
        }

        // Remove our DENY rule
        public static bool AllowAccess(string path)
        {
            string norm = path.Replace('/', '\\');
            Cmd.RunResult rr = Cmd.RunEx("icacls", Cmd.Q + norm + Cmd.Q + " /remove:d Everyone");
            return rr.Succeeded;
        }

        // Check if a component currently has an Everyone DENY
        public static bool IsDenied(string path)
        {
            string norm = path.Replace('/', '\\');
            // 输出示例: C:\x\y.dll: Everyone:(DENY)(RX)  需要同时存在 Everyone 和 deny/DENY
            string res = Cmd.Run("icacls", Cmd.Q + norm + Cmd.Q);
            return res.IndexOf("Everyone") >= 0 && res.ToLower().IndexOf("deny") >= 0;
        }
    }

    // ===================== PROCESS GUARD (watchdog) =====================
    // 持续监控所有进程：凡是被封锁目录内进程所直接/间接调用的子进程，一律强制结束。
    // 效果：受限软件无法「调用其他软件」（启动子进程会被立即杀掉）。
    public static class ProcessGuard
    {
        private static volatile bool running;
        private static Thread guardThread;
        private static List<string> blockedRoots = new List<string>();
        private static Action<string> logFn;

        private static readonly object Sync = new object();

        public static void Start(List<string> blockedDirectories, Action<string> log)
        {
            lock (Sync)
            {
                blockedRoots.Clear();
                foreach (string d in blockedDirectories)
                {
                    // 文件目标取其所在目录，否则拼上尾斜杠后永远匹配不到进程
                    string basePath = d;
                    if (File.Exists(basePath))
                        basePath = Path.GetDirectoryName(basePath);
                    if (string.IsNullOrEmpty(basePath))
                        continue;
                    string norm = basePath.Replace('/', '\\').TrimEnd('\\') + "\\";
                    // 排除本工具自身目录，避免守护线程把自己进程杀掉
                    if (IsSelfDirectory(norm))
                    {
                        if (log != null) log("已跳过自身目录: " + d);
                        continue;
                    }
                    blockedRoots.Add(norm.ToLowerInvariant());
                }
                logFn = log;
                if (running) return;
                running = true;
                guardThread = new Thread(GuardLoop);
                guardThread.IsBackground = true;
                guardThread.Start();
            }
        }

        private static bool IsSelfDirectory(string norm)
        {
            try
            {
                string self = Path.GetDirectoryName(Application.ExecutablePath);
                if (string.IsNullOrEmpty(self)) self = AppDomain.CurrentDomain.BaseDirectory;
                self = self.Replace('/', '\\').TrimEnd('\\') + "\\";
                return norm.ToLowerInvariant().StartsWith(self.ToLowerInvariant());
            }
            catch { return false; }
        }

        public static void Stop()
        {
            lock (Sync) { running = false; }
        }

        public static bool IsRunning
        {
            get { return running; }
        }


        private static void GuardLoop()
        {
            while (running)
            {
                try
                {
                    ScanOnce();
                }
                catch { }
                Thread.Sleep(800);
            }
        }

        private static void ScanOnce()
        {
            // Build PID -> ParentPID map via WMI (one query)
            Dictionary<int, int> parentMap = new Dictionary<int, int>();
            try
            {
                using (System.Management.ManagementObjectSearcher searcher =
                    new System.Management.ManagementObjectSearcher(
                        "SELECT ProcessId, ParentProcessId FROM Win32_Process"))
                {
                    foreach (System.Management.ManagementObject mo in searcher.Get())
                    {
                        try
                        {
                            int pid = Convert.ToInt32(mo["ProcessId"]);
                            int ppid = Convert.ToInt32(mo["ParentProcessId"]);
                            parentMap[pid] = ppid;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // Current process paths
            Dictionary<int, string> pathMap = new Dictionary<int, string>();
            Process[] procs = Process.GetProcesses();
            foreach (Process p in procs)
            {
                try
                {
                    string path = p.MainModule.FileName;
                    pathMap[p.Id] = path.ToLowerInvariant();
                }
                catch { }
            }

            int selfPid = Process.GetCurrentProcess().Id;
            foreach (Process p in procs)
            {
                if (p.Id == selfPid) continue;
                try
                {
                    // A process is "poisoned" if it, or any ancestor, lives under a blocked root.
                    if (IsPoisoned(p.Id, parentMap, pathMap, 0))
                    {
                        string name = p.ProcessName + " (PID " + p.Id + ")";
                        // 终止受限软件调用的进程（含受限软件自身）
                        p.Kill();
                        if (logFn != null)
                            logFn("已终止受限调用: " + name);
                    }
                }
                catch { }
            }
        }

        private static bool IsPoisoned(int pid, Dictionary<int, int> parentMap,
            Dictionary<int, string> pathMap, int depth)
        {
            if (depth > 12) return false;
            string path;
            if (pathMap.TryGetValue(pid, out path) && path != null)
            {
                foreach (string root in blockedRoots)
                {
                    if (path.StartsWith(root)) return true;
                }
            }
            int parent;
            if (!parentMap.TryGetValue(pid, out parent)) return false;
            if (parent == 0 || parent == pid) return false;
            return IsPoisoned(parent, parentMap, pathMap, depth + 1);
        }
    }

    // ===================== LOGGER =====================
    public class Logger
    {
        private RichTextBox rtb;
        private string logFile;

        public Logger(RichTextBox box, string file)
        {
            rtb = box;
            logFile = file;
            try
            {
                string d = Path.GetDirectoryName(logFile);
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            }
            catch { }
        }

        private void Write(string level, string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] [" + level + "] " + message;
            string full = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [" + level + "] " + message;

            // file
            try
            {
                File.AppendAllText(logFile, full + Environment.NewLine, Encoding.UTF8);
            }
            catch { }

            // GUI
            if (rtb != null && !rtb.IsDisposed)
            {
                if (rtb.InvokeRequired)
                {
                    try { rtb.BeginInvoke(new Action(delegate { AppendGui(line, level); })); }
                    catch { }
                }
                else AppendGui(line, level);
            }
        }

        private void AppendGui(string line, string level)
        {
            Color color = Theme.LevelColor(level);
            rtb.SelectionStart = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionFont = new Font("Consolas", 9f);
            rtb.SelectionColor = color;
            rtb.AppendText(line + Environment.NewLine);
            // 光标移到新行行首再滚动：保持视图左侧对齐。
            // WordWrap=false 时若把光标留在行尾，超长行会把左侧内容横向滚出视野。
            int end = rtb.TextLength;
            if (end > 0)
            {
                int lastLine = rtb.GetLineFromCharIndex(end - 1);
                int lineStart = rtb.GetFirstCharIndexFromLine(lastLine);
                if (lineStart < 0) lineStart = 0;
                rtb.SelectionStart = lineStart;
                rtb.SelectionLength = 0;
                rtb.ScrollToCaret();
            }
        }

        public void Ok(string m) { Write("OK", m); }
        public void Fail(string m) { Write("FAIL", m); }
        public void Warn(string m) { Write("WARN", m); }
        public void Info(string m) { Write("INFO", m); }
        public void Step(string m) { Write("STEP", m); }
        public void Dim(string m) { Write("DIM", m); }
        public void Hdr(string m) { Write("HDR", m); }
    }

    // ===================== MAIN FORM (AppGuard-style GUI) =====================
    public class MainForm : Form
    {
        private Logger log;
        private List<Target> targets = new List<Target>();
        private DataGridView grid;
        private Label lblCount;
        private Label lblMode;
        private Label lblAdmin;
        private Button btnApply;
        private Button btnClear;
        private Button btnVerify;
        private Button btnElevate;
        private Button btnToggleMode;
        private ProgressBar pb;
        private SplitContainer split;

        private bool dryRun = false;
        private bool admin;
        private string identity;
        private string configDir;
        private string configPath;
        private int vpnPort = 7890;

        public MainForm()
        {
            configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Games8ThTeam", "AppBlocker");
            configPath = Path.Combine(configDir, "config.txt");

            admin = IsAdmin();
            identity = Environment.UserName;

            Text = "Games8Th.Team  ·  软件权限限制工具";
            BackColor = Theme.BG;
            ForeColor = Theme.TEXT;
            MinimumSize = new Size(860, 560);

            FitScreen();
            LoadConfig();

            BuildUi();

            Shown += delegate(object s, EventArgs e2) { OnShownSetup(); };

            log = new Logger(rtbLog, Path.Combine(configDir, "appguard.log"));

            Banner();

            if (targets.Count == 0)
            {
                log.Warn("未配置目标。请使用「选择程序」或「选择目录」添加要限制的软件。");
                log.Warn("功能：封锁目标软件的全部网络请求（入站+出站）与组件访问，无需任何外部依赖。");
            }

            if (!admin)
            {
                log.Warn("未以管理员身份运行：对系统目录下的目标可能限制失败。");
                log.Warn("可点击右上角「以管理员重启」获取完整权限。");
            }
        }

        private bool IsAdmin()
        {
            try
            {
                using (System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                    return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        private void FitScreen()
        {
            int sw = Screen.PrimaryScreen.Bounds.Width;
            int sh = Screen.PrimaryScreen.Bounds.Height;
            int w = Math.Min(1180, Math.Max(860, sw - 120));
            int hgt = Math.Min(760, Math.Max(560, sh - 140));
            int x = Math.Max(0, (sw - w) / 2);
            int y = Math.Max(0, (sh - hgt) / 2 - 30);
            StartPosition = FormStartPosition.Manual;
            SetBounds(x, y, w, hgt);
        }

        // ---- Config ----
        private void LoadConfig()
        {
            try
            {
                if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
                if (File.Exists(configPath))
                {
                    string[] lines = File.ReadAllLines(configPath, Encoding.UTF8);
                    foreach (string raw in lines)
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        if (line.StartsWith("vpnPort=")) { int.TryParse(line.Substring(8), out vpnPort); continue; }
                        string[] parts = line.Split('|');
                        if (parts.Length >= 2)
                        {
                            Target t = new Target(parts[0], parts[1] == "1");
                            if (parts.Length >= 3 && parts[2] == "NET_OUT") t.Strategy = "NET_OUT";
                            targets.Add(t);
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                if (!Directory.Exists(configDir)) Directory.CreateDirectory(configDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Games8Th.Team AppBlocker config");
                sb.AppendLine("vpnPort=" + vpnPort.ToString());
                foreach (Target t in targets)
                {
                    sb.AppendLine(t.Path_ + "|" + (t.Enabled ? "1" : "0") + "|" + t.Strategy);
                }
                File.WriteAllText(configPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        // ---- UI ----
        private RichTextBox rtbLog;

        private void BuildUi()
        {
            // ===== Header (white) =====
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 96;
            header.BackColor = Theme.PANEL;
            header.Paint += Header_Paint;
            Controls.Add(header);

            // right side of header (labels + buttons)
            Panel rt = new Panel();
            rt.Dock = DockStyle.Right;
            rt.Width = 250;
            rt.BackColor = Theme.PANEL;
            header.Controls.Add(rt);

            // buttons row at top of right panel (Top, 30px)
            Panel btnRow = new Panel();
            btnRow.Dock = DockStyle.Top;
            btnRow.Height = 30;
            btnRow.BackColor = Theme.PANEL;

            btnToggleMode = new Button();
            btnToggleMode.Text = "切换模式";
            btnToggleMode.FlatStyle = FlatStyle.Flat;
            btnToggleMode.BackColor = Theme.PANEL;
            btnToggleMode.ForeColor = Theme.TEXT_DIM;
            btnToggleMode.FlatAppearance.BorderColor = Theme.BORDER;
            btnToggleMode.Font = new Font(Theme.FONT_UI, 8.5f);
            btnToggleMode.Location = new Point(0, 2);
            btnToggleMode.Size = new Size(80, 26);
            btnToggleMode.Click += delegate { ToggleMode(); };
            btnRow.Controls.Add(btnToggleMode);

            if (!admin)
            {
                btnElevate = new Button();
                btnElevate.Text = "以管理员重启";
                btnElevate.FlatStyle = FlatStyle.Flat;
                btnElevate.BackColor = Theme.PANEL;
                btnElevate.ForeColor = Theme.TEXT_DIM;
                btnElevate.FlatAppearance.BorderColor = Theme.BORDER;
                btnElevate.Font = new Font(Theme.FONT_UI, 8.5f);
                btnElevate.Location = new Point(84, 2);
                btnElevate.Size = new Size(90, 26);
                btnElevate.Click += delegate { Elevate(); };
                btnRow.Controls.Add(btnElevate);
            }
            rt.Controls.Add(btnRow);

            // status labels below buttons
            lblMode = new Label();
            lblMode.Text = "●  LIVE 真实生效";
            lblMode.ForeColor = Theme.LOG_OK;
            lblMode.Font = new Font(Theme.FONT_UI, 9f, FontStyle.Bold);
            lblMode.Location = new Point(16, 38);
            lblMode.AutoSize = true;
            rt.Controls.Add(lblMode);

            lblAdmin = new Label();
            lblAdmin.AutoSize = true;
            lblAdmin.Font = new Font(Theme.FONT_UI, 9f, FontStyle.Bold);
            lblAdmin.Location = new Point(16, 60);
            UpdateAdminLabel();
            rt.Controls.Add(lblAdmin);

            Label ident = new Label();
            ident.Text = "身份: " + identity;
            ident.ForeColor = Theme.TEXT_DIM;
            ident.Font = new Font(Theme.FONT_UI, 8f);
            ident.Location = new Point(16, 80);
            ident.AutoSize = true;
            rt.Controls.Add(ident);

            Label vpn = new Label();
            vpn.Text = "VPN端口: " + vpnPort.ToString();
            vpn.ForeColor = Theme.TEXT_DIM;
            vpn.Font = new Font(Theme.FONT_UI, 8f);
            vpn.Location = new Point(120, 80);
            vpn.AutoSize = true;
            rt.Controls.Add(vpn);

            // separator
            Panel sep = new Panel();
            sep.Dock = DockStyle.Top;
            sep.Height = 1;
            sep.BackColor = Theme.BORDER;
            Controls.Add(sep);

            // ===== Body: SplitContainer (left targets / right logs) =====
            // 注意：SplitterDistance/PanelMinSize 必须在控件布局完成后(Shown)再设置，
            // 否则控件 Width=150 时设置会抛 InvalidOperationException。
            split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.BackColor = Theme.BG;
            split.Orientation = Orientation.Vertical;
            split.SplitterWidth = 6;
            Controls.Add(split);

            // Left target panel
            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.BackColor = Theme.PANEL;
            split.Panel1.Controls.Add(left);
            BuildTargetPanel(left);

            // Right log panel
            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.BackColor = Theme.PANEL;
            split.Panel2.Controls.Add(right);
            BuildLogPanel(right);

            // Dock order in form: header top, sep under it, split fills
            Controls.SetChildIndex(split, 0);
            Controls.SetChildIndex(sep, 1);
            Controls.SetChildIndex(header, 2);
        }

        private void UpdateAdminLabel()
        {
            lblAdmin.Text = "●  " + (admin ? "管理员权限" : "普通用户");
            lblAdmin.ForeColor = admin ? Theme.LOG_OK : Theme.LOG_FAIL;
        }

        private void Header_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Panel p = (Panel)sender;

            // Orange badge
            int box = 56;
            int bx = 20, by = 18;
            using (GraphicsPath path = RoundRect(bx, by, bx + box, by + box, 12))
            {
                using (SolidBrush sb = new SolidBrush(Theme.BRAND))
                {
                    g.FillPath(sb, path);
                }
            }
            // inner border
            using (GraphicsPath path2 = RoundRect(bx + 3, by + 3, bx + box - 3, by + box - 3, 10))
            {
                using (Pen pen = new Pen(Theme.BRAND_DARK))
                {
                    g.DrawPath(pen, path2);
                }
            }

            // G8 mark (or logo image)
            Image mark = LoadMark(64);
            if (mark != null)
            {
                g.DrawImage(mark, bx + (box - 40) / 2, by + (box - 40) / 2 + 1, 40, 40);
            }
            else
            {
                using (Font f = new Font("Arial", 16f, FontStyle.Bold))
                {
                    g.DrawString("G8", f, Brushes.White, bx + 16, by + 14);
                }
            }

            // Wordmark
            int tx = bx + box + 16;
            using (Font fBrand = new Font(Theme.FONT_UI, 15f, FontStyle.Bold))
            {
                g.DrawString("Games8Th.Team", fBrand, new SolidBrush(Theme.BRAND_DARK), tx, by - 2);
                string ver = "v1.1.0";
                using (Font fVer = new Font(Theme.FONT_UI, 8f))
                {
                    float w = g.MeasureString("Games8Th.Team", fBrand).Width;
                    g.DrawString(ver, fVer, new SolidBrush(Theme.TEXT_MUTE), tx + w + 8, by + 10);
                }
            }

            // Product name + desc
            int y2 = by + 30;
            using (Font fProd = new Font(Theme.FONT_UI, 10f, FontStyle.Bold))
            {
                g.DrawString("软件权限限制工具", fProd, new SolidBrush(Theme.BRAND), tx, y2);
                float w = g.MeasureString("软件权限限制工具", fProd).Width;
                using (Font fNote = new Font(Theme.FONT_UI, 8f))
                {
                    g.DrawString("防火墙封锁 · 组件权限 · 进程守护", fNote, new SolidBrush(Theme.TEXT_DIM), tx + w + 10, y2 + 2);
                }
            }

            // note line
            int y3 = y2 + 18;
            using (Font fTiny = new Font(Theme.FONT_UI, 8f))
            {
                g.DrawString("日志颜色：绿=成功   红=失败   黄=警告", fTiny, new SolidBrush(Theme.TEXT_MUTE), tx, y3);
            }
        }

        private Image loadedMark;
        private Image LoadMark(int px)
        {
            if (loadedMark != null) return loadedMark;
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string cand = Path.Combine(baseDir, "assets", "mark_64.png");
                if (File.Exists(cand)) loadedMark = Image.FromFile(cand);
                else
                {
                    cand = Path.Combine(baseDir, "logo_mark.png");
                    if (File.Exists(cand)) loadedMark = Image.FromFile(cand);
                }
            }
            catch { }
            return loadedMark;
        }

        private GraphicsPath RoundRect(float x1, float y1, float x2, float y2, float r)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(x1, y1, r * 2, r * 2, 180, 90);
            path.AddArc(x2 - r * 2, y1, r * 2, r * 2, 270, 90);
            path.AddArc(x2 - r * 2, y2 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(x1, y2 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---- Left: target panel ----
        private void BuildTargetPanel(Panel parent)
        {
            parent.Padding = new Padding(14);

            // top bar: title + count + hint
            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 46;
            topBar.BackColor = Theme.PANEL;
            parent.Controls.Add(topBar);

            Label title = new Label();
            title.Text = "限制目标";
            title.Font = new Font(Theme.FONT_UI, 12f, FontStyle.Bold);
            title.ForeColor = Theme.TEXT;
            title.Location = new Point(0, 0);
            title.AutoSize = true;
            topBar.Controls.Add(title);

            lblCount = new Label();
            lblCount.AutoSize = true;
            lblCount.ForeColor = Theme.TEXT_DIM;
            lblCount.Font = new Font(Theme.FONT_UI, 8f);
            lblCount.Location = new Point(90, 4);
            lblCount.Text = "";
            topBar.Controls.Add(lblCount);

            Label hint = new Label();
            hint.Text = "勾选 = 参与限制；双击可切换 双向/仅出站";
            hint.ForeColor = Theme.TEXT_MUTE;
            hint.Font = new Font(Theme.FONT_UI, 7.5f);
            hint.Location = new Point(0, 26);
            hint.AutoSize = true;
            topBar.Controls.Add(hint);

            // bottom: action buttons + progress
            Panel ops = new Panel();
            ops.Dock = DockStyle.Bottom;
            ops.Height = 160;
            ops.BackColor = Theme.PANEL;
            parent.Controls.Add(ops);

            Button bAddText = NewBtn("输入路径", 0, 0, 82);
            bAddText.Click += delegate { AddTargetDialog(); };
            ops.Controls.Add(bAddText);

            Button bAddFile = NewBtn("选择程序", 88, 0, 82);
            bAddFile.Click += delegate { AddFile(); };
            ops.Controls.Add(bAddFile);

            Button bAddDir = NewBtn("选择目录", 176, 0, 82);
            bAddDir.Click += delegate { AddDir(); };
            ops.Controls.Add(bAddDir);

            Button bDel = NewBtn("删除", 264, 0, 82);
            bDel.Click += delegate { DelTarget(); };
            ops.Controls.Add(bDel);

            btnApply = AccentBtn("▶  实施限制", 0, 40, 150);
            btnApply.Click += delegate { ApplyAll(); };
            ops.Controls.Add(btnApply);

            btnClear = DangerBtn("■  解除全部限制", 156, 40, 150);
            btnClear.Click += delegate { ClearAll(); };
            ops.Controls.Add(btnClear);

            btnVerify = NewBtn("核验状态", 312, 40, 90);
            btnVerify.Click += delegate { VerifyAll(); };
            ops.Controls.Add(btnVerify);

            pb = new ProgressBar();
            pb.Location = new Point(0, 90);
            pb.Width = 400;
            pb.Height = 14;
            pb.Style = ProgressBarStyle.Continuous;
            pb.ForeColor = Theme.BRAND;
            ops.Controls.Add(pb);

            Label foot = new Label();
            foot.Text = "提示：限制的是软件的权限，不是限制用户。封锁后目标的所有网络请求将无法发出。";
            foot.ForeColor = Theme.TEXT_MUTE;
            foot.Font = new Font(Theme.FONT_UI, 7.5f);
            foot.Location = new Point(0, 120);
            foot.AutoSize = true;
            ops.Controls.Add(foot);

            // middle: grid (fills between topBar and ops)
            Panel gridWrap = new Panel();
            gridWrap.Dock = DockStyle.Fill;
            gridWrap.BackColor = Theme.PANEL;
            parent.Controls.Add(gridWrap);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = Theme.PANEL;
            grid.BorderStyle = BorderStyle.None;
            grid.DefaultCellStyle.BackColor = Theme.PANEL;
            grid.DefaultCellStyle.ForeColor = Theme.TEXT;
            grid.DefaultCellStyle.Font = new Font(Theme.FONT_UI, 9f);
            grid.DefaultCellStyle.SelectionBackColor = Theme.BRAND_SOFT;
            grid.DefaultCellStyle.SelectionForeColor = Theme.TEXT;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.PANEL_ALT;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TEXT;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(Theme.FONT_UI, 9f, FontStyle.Bold);
            grid.EnableHeadersVisualStyles = false;
            grid.RowHeadersVisible = false;
            grid.ColumnHeadersHeight = 28;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowTemplate.Height = 27;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.CellDoubleClick += Grid_CellDoubleClick;
            grid.CellValueChanged += Grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            gridWrap.Controls.Add(grid);

            // Columns
            DataGridViewCheckBoxColumn colOn = new DataGridViewCheckBoxColumn();
            colOn.HeaderText = "启用";
            colOn.Width = 48;
            colOn.FlatStyle = FlatStyle.Flat;
            grid.Columns.Add(colOn);

            DataGridViewTextBoxColumn colName = new DataGridViewTextBoxColumn();
            colName.HeaderText = "目标名称";
            colName.Width = 110;
            grid.Columns.Add(colName);

            DataGridViewTextBoxColumn colStrategy = new DataGridViewTextBoxColumn();
            colStrategy.HeaderText = "策略";
            colStrategy.Width = 70;
            grid.Columns.Add(colStrategy);

            DataGridViewTextBoxColumn colState = new DataGridViewTextBoxColumn();
            colState.HeaderText = "当前状态";
            colState.Width = 88;
            grid.Columns.Add(colState);

            DataGridViewTextBoxColumn colPath = new DataGridViewTextBoxColumn();
            colPath.HeaderText = "路径";
            colPath.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns.Add(colPath);

            // dock order: topBar top, ops bottom, grid fills
            parent.Controls.SetChildIndex(gridWrap, 0);
            parent.Controls.SetChildIndex(ops, 1);
            parent.Controls.SetChildIndex(topBar, 2);
        }

        private Button NewBtn(string txt, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = txt;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Theme.PANEL;
            b.ForeColor = Theme.TEXT;
            b.FlatAppearance.BorderColor = Theme.BORDER;
            b.Font = new Font(Theme.FONT_UI, 9f);
            b.Location = new Point(x, y);
            b.Size = new Size(w, 30);
            return b;
        }

        private Button AccentBtn(string txt, int x, int y, int w)
        {
            Button b = NewBtn(txt, x, y, w);
            b.BackColor = Theme.BRAND;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font(Theme.FONT_UI, 9f, FontStyle.Bold);
            return b;
        }

        private Button DangerBtn(string txt, int x, int y, int w)
        {
            Button b = NewBtn(txt, x, y, w);
            b.BackColor = Theme.Hex("#fdeaea");
            b.ForeColor = Theme.LOG_FAIL;
            b.FlatAppearance.BorderColor = Theme.Hex("#f5d3d3");
            b.Font = new Font(Theme.FONT_UI, 9f, FontStyle.Bold);
            return b;
        }

        // ---- Right: log panel ----
        private void BuildLogPanel(Panel parent)
        {
            parent.Padding = new Padding(14);

            // top bar: title + legend + hint
            Panel topBar = new Panel();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 46;
            topBar.BackColor = Theme.PANEL;
            parent.Controls.Add(topBar);

            Label title = new Label();
            title.Text = "运行日志";
            title.Font = new Font(Theme.FONT_UI, 12f, FontStyle.Bold);
            title.ForeColor = Theme.TEXT;
            title.Location = new Point(0, 0);
            title.AutoSize = true;
            topBar.Controls.Add(title);

            Label l1 = new Label();
            l1.Text = "● 成功";
            l1.ForeColor = Theme.LOG_OK;
            l1.Font = new Font(Theme.FONT_UI, 8f, FontStyle.Bold);
            l1.AutoSize = true;
            l1.Location = new Point(90, 4);
            topBar.Controls.Add(l1);
            Label l2 = new Label();
            l2.Text = "● 失败";
            l2.ForeColor = Theme.LOG_FAIL;
            l2.Font = new Font(Theme.FONT_UI, 8f, FontStyle.Bold);
            l2.AutoSize = true;
            l2.Location = new Point(150, 4);
            topBar.Controls.Add(l2);
            Label l3 = new Label();
            l3.Text = "● 警告";
            l3.ForeColor = Theme.LOG_WARN;
            l3.Font = new Font(Theme.FONT_UI, 8f, FontStyle.Bold);
            l3.AutoSize = true;
            l3.Location = new Point(210, 4);
            topBar.Controls.Add(l3);

            Label hint = new Label();
            hint.Text = "绿=限制成功 · 红=限制失败 · 黄=警告/进行中";
            hint.ForeColor = Theme.TEXT_MUTE;
            hint.Font = new Font(Theme.FONT_UI, 7.5f);
            hint.Location = new Point(0, 26);
            hint.AutoSize = true;
            topBar.Controls.Add(hint);

            // bottom toolbar
            Panel bar = new Panel();
            bar.Dock = DockStyle.Bottom;
            bar.Height = 36;
            bar.BackColor = Theme.PANEL;
            parent.Controls.Add(bar);

            Button bClear = NewBtn("清空日志", 0, 3, 82);
            bClear.Click += delegate { rtbLog.Clear(); };
            bar.Controls.Add(bClear);

            Button bOpen = NewBtn("打开日志文件", 88, 3, 100);
            bOpen.Click += delegate { OpenLogFile(); };
            bar.Controls.Add(bOpen);

            // middle: console (fills)
            Panel box = new Panel();
            box.Dock = DockStyle.Fill;
            box.BackColor = Theme.LOG_BG;
            parent.Controls.Add(box);

            rtbLog = new RichTextBox();
            rtbLog.Dock = DockStyle.Fill;
            rtbLog.BackColor = Theme.LOG_BG;
            rtbLog.ForeColor = Theme.LOG_FG;
            rtbLog.Font = new Font("Consolas", 9f);
            rtbLog.BorderStyle = BorderStyle.None;
            rtbLog.ReadOnly = true;
            rtbLog.WordWrap = false;
            rtbLog.DetectUrls = false;
            rtbLog.ScrollBars = RichTextBoxScrollBars.Both;
            box.Controls.Add(rtbLog);

            // dock order: topBar top, bar bottom, box fills
            parent.Controls.SetChildIndex(box, 0);
            parent.Controls.SetChildIndex(bar, 1);
            parent.Controls.SetChildIndex(topBar, 2);
        }

        private void OpenLogFile()
        {
            string p = Path.Combine(configDir, "appguard.log");
            try
            {
                if (!File.Exists(p)) File.WriteAllText(p, "", Encoding.UTF8);
                Process.Start(new ProcessStartInfo("notepad.exe", p));
            }
            catch (Exception ex)
            {
                log.Fail("无法打开日志文件: " + ex.Message);
            }
        }

        // ---- Banner ----
        private void Banner()
        {
            log.Hdr("======");
            log.Hdr("  Games8Th.Team  ·  软件权限限制工具  v1.1.0");
            log.Hdr("  防火墙封锁 · 组件权限 · 进程守护  |  日志三色: 绿=成功 红=失败 黄=警告");
            log.Hdr("======");
            log.Info("配置文件: " + configPath);
            log.Info("日志文件: " + configPath.Replace("config.txt", "appguard.log"));
            log.Info("VPN端口: " + vpnPort.ToString());
            log.Ok("Games8Th.Team 就绪，共 " + targets.Count.ToString() + " 个目标");
            RefreshGrid();
        }

        // ---- Grid ----
        private void RefreshGrid()
        {
            if (grid.InvokeRequired)
            {
                try { grid.BeginInvoke(new Action(RefreshGrid)); } catch { }
                return;
            }

            int selRow = -1;
            if (grid.CurrentRow != null) selRow = grid.CurrentRow.Index;

            grid.Rows.Clear();
            foreach (Target t in targets)
            {
                DataGridViewRow r = new DataGridViewRow();
                r.CreateCells(grid);
                r.Cells[0].Value = t.Enabled;
                r.Cells[0].ReadOnly = false;
                r.Cells[1].Value = t.Name;
                r.Cells[2].Value = t.StrategyLabel;
                r.Cells[3].Value = t.State;
                r.Cells[4].Value = t.Path_;
                r.Tag = t;
                grid.Rows.Add(r);
            }
            int nOn = 0;
            foreach (Target t in targets) if (t.Enabled) nOn++;
            lblCount.Text = "共 " + targets.Count.ToString() + " 项 · 启用 " + nOn.ToString() + " 项";
            if (selRow >= 0 && selRow < grid.Rows.Count) grid.CurrentCell = grid.Rows[selRow].Cells[0];
        }

        private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            DataGridViewRow r = grid.Rows[e.RowIndex];
            Target t = r.Tag as Target;
            if (t == null) return;
            t.Strategy = (t.Strategy == "NET_BOTH") ? "NET_OUT" : "NET_BOTH";
            log.Warn(t.Name + " 策略已切换为: " + t.StrategyLabel + "（重新「实施限制」生效）");
            SaveConfig();
            RefreshGrid();
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            DataGridViewRow r = grid.Rows[e.RowIndex];
            Target t = r.Tag as Target;
            if (t == null) return;
            if (e.ColumnIndex == 0)
            {
                try
                {
                    bool v = Convert.ToBoolean(r.Cells[0].Value);
                    if (v != t.Enabled)
                    {
                        t.Enabled = v;
                        if (v) log.Ok(t.Name + " 已加入限制目标");
                        else log.Warn(t.Name + " 已移出限制目标");
                        SaveConfig();
                    }
                }
                catch { }
            }
        }

        private Target SelectedTarget()
        {
            if (grid.CurrentRow == null) return null;
            return grid.CurrentRow.Tag as Target;
        }

        // ---- Target ops ----
        private void AddTargetDialog()
        {
            string p = Microsoft.VisualBasic.Interaction.InputBox(
                "输入要限制的程序或文件夹完整路径:", "Games8Th.Team - 添加目标", "");
            if (p != null && p.Trim().Length > 0) AddPath(p.Trim().Trim(QChar));
        }

        private static char[] QChar { get { return new char[] { '"' }; } }

        private void AddFile()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "选择要限制的程序";
            dlg.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
            if (dlg.ShowDialog() == DialogResult.OK) AddPath(dlg.FileName);
        }

        private void AddDir()
        {
            FolderBrowserDialog dlg = new FolderBrowserDialog();
            dlg.Description = "选择要限制的文件夹（其中的所有 .exe 都会被封锁）";
            if (dlg.ShowDialog() == DialogResult.OK) AddPath(dlg.SelectedPath);
        }

        private void AddPath(string path)
        {
            try { path = Path.GetFullPath(path); } catch { }
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                log.Fail("路径不存在: " + path);
                return;
            }
            foreach (Target t in targets)
            {
                if (String.Equals(t.Path_, path, StringComparison.OrdinalIgnoreCase))
                {
                    log.Warn("该目标已在列表中: " + path);
                    return;
                }
            }
            Target nt = new Target(path, true);
            targets.Add(nt);
            SaveConfig();
            RefreshGrid();
            log.Ok("已添加目标: " + nt.Name + "   [" + nt.Path_ + "]");
        }

        private void DelTarget()
        {
            Target t = SelectedTarget();
            if (t == null)
            {
                log.Warn("请先在列表中选择一个目标");
                return;
            }
            if (MessageBox.Show(
                "从列表移除「" + t.Name + "」?\n\n注意：只从配置移除，不会删除防火墙规则。\n如需解除限制请点击「解除全部限制」。",
                "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            targets.Remove(t);
            SaveConfig();
            RefreshGrid();
            log.Warn("已从列表移除: " + t.Name);
        }

        // ---- Mode / Elevate ----
        private void ToggleMode()
        {
            dryRun = !dryRun;
            string txt = dryRun ? "●  DRY-RUN 演示" : "●  LIVE 真实生效";
            lblMode.Text = txt;
            lblMode.ForeColor = dryRun ? Theme.LOG_WARN : Theme.LOG_OK;
            if (dryRun) log.Warn("已切换为 DRY-RUN 演示模式（仅显示动作，不写入系统）");
            else log.Ok("已切换为 LIVE 真实生效模式");
        }

        private void Elevate()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                log.Ok("正在以管理员身份重启 ...");
                Close();
            }
            catch
            {
                log.Fail("提权被取消或失败");
            }
        }

        // ---- Apply ----
        private void ApplyAll()
        {
            int n = 0;
            foreach (Target t in targets) if (t.Enabled) n++;
            if (n == 0)
            {
                log.Warn("没有启用中的目标，请先勾选");
                return;
            }
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                try { ApplyJob(); }
                catch (Exception ex) { log.Fail("任务异常: " + ex.Message); }
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                pb.Value = 0;
                btnApply.Enabled = true;
                btnClear.Enabled = true;
                RefreshGrid();
            };
            btnApply.Enabled = false;
            btnClear.Enabled = false;
            pb.Maximum = 100;
            log.Step("开始任务: 实施限制");
            bw.RunWorkerAsync();
        }

        private void ApplyJob()
        {
            if (dryRun)
            {
                log.Warn("当前为 DRY-RUN 模式，仅演示不写入系统");
                foreach (Target t in targets)
                {
                    if (!t.Enabled) continue;
                    log.Step("[DRY-RUN] 将对 " + t.Name + " 创建封锁规则" + (t.Strategy == "NET_OUT" ? "（仅出站）" : "（双向）"));
                }
                log.Ok("[DRY-RUN] 演示完成，未写入系统");
                return;
            }

            // 1. Firewall ensure
            log.Info("[防火墙] 检查 Windows 防火墙状态...");
            try
            {
                if (Firewall.IsFirewallEnabled())
                {
                    log.Ok("[防火墙] Windows 防火墙已开启");
                }
                else
                {
                    log.Warn("[防火墙] Windows 防火墙未开启，正在启用...");
                    string r = Firewall.EnableFirewall();
                    log.Ok("[防火墙] 已启用: " + r.Trim());
                }
            }
            catch (Exception ex)
            {
                log.Fail("[防火墙] 状态检查失败: " + ex.Message);
            }

            // 2. Kaspersky (optional enhancement — quiet if not installed)
            if (Kaspersky.IsInstalled())
            {
                log.Info("[Kaspersky] 检测到 Kaspersky（可选增强）：" + Kaspersky.AvpPath);
            }

            // 3. For each target
            int okCount = 0, failCount = 0, total = 0;
            foreach (Target t in targets)
            {
                if (!t.Enabled) continue;
                log.Step("处理目标: " + t.Name);

                // find all targetable components (exe + exe-callable like dll)
                List<string> files = new List<string>();
                if (Directory.Exists(t.Path_))
                {
                    try
                    {
                        files = Components.ScanFolder(t.Path_);
                    }
                    catch (Exception ex) { log.Fail("读取目录失败: " + ex.Message); }
                }
                else if (File.Exists(t.Path_))
                {
                    files.Add(t.Path_);
                }
                else
                {
                    log.Fail("目标不存在: " + t.Path_);
                    t.State = "✕ 不存在";
                    failCount++;
                    continue;
                }

                if (files.Count == 0)
                {
                    log.Warn(t.Name + " 未发现可限制组件（无 .exe / .dll 等）");
                    t.State = "○ 未限制";
                    continue;
                }

                // optional Kaspersky scan (quiet if not installed)
                if (Kaspersky.IsInstalled())
                {
                    log.Info("[Kaspersky] 扫描 " + t.Name + " ...");
                    string scan = Kaspersky.Scan(t.Path_);
                    if (scan.Length == 0 || scan.IndexOf("错误") < 0 && scan.IndexOf("ERR:") < 0)
                        log.Ok("[Kaspersky] " + t.Name + " 扫描完成");
                    else
                        log.Warn("[Kaspersky] " + t.Name + " 扫描结束（" + (scan.Length > 40 ? scan.Substring(0, 40) : scan) + "）");
                }

                bool targetOk = true;
                foreach (string file in files)
                {
                    total++;
                    string fname = Path.GetFileName(file);
                    bool isExe = Components.IsExe(file);

                    if (isExe)
                    {
                        // .exe -> firewall block (network layer)
                        bool full = t.Strategy != "NET_OUT";
                        bool ok = Firewall.AddBlockRule(file, "out");
                        if (full) { bool okIn = Firewall.AddBlockRule(file, "in"); ok = ok && okIn; }
                        if (ok)
                        {
                            okCount++;
                            log.Ok("已封锁: " + fname + "  " + (full ? "[入站+出站]" : "[出站]") + "  (" + file + ")");
                        }
                        else
                        {
                            failCount++;
                            targetOk = false;
                            log.Fail("封锁失败: " + fname + "  (" + file + ")");
                        }
                    }
                    else
                    {
                        // .dll etc -> icacls Deny Everyone access (no exe can load it)
                        bool ok = Components.DenyAccess(file);
                        if (ok)
                        {
                            okCount++;
                            log.Ok("已剥夺权限: " + fname + "  [ACL-DENY Everyone]  (" + file + ")");
                        }
                        else
                        {
                            failCount++;
                            targetOk = false;
                            log.Fail("权限剥夺失败: " + fname + "  (" + file + ")");
                        }
                    }
                }
                t.State = targetOk ? "● 已限制" : "? 部分失败";
            }

            SaveConfig();
            int nFail = failCount;
            int nOk = okCount;
            if (nFail == 0)
                log.Ok("★ 限制完成：全部成功（" + okCount.ToString() + "/" + total.ToString() + " 个组件）");
            else
                log.Fail("★ 限制完成：成功 " + okCount.ToString() + " / 失败 " + nFail.ToString());

            // Start the process guard (watchdog) to block spawning of other software
            List<string> dirs = new List<string>();
            foreach (Target t in targets)
            {
                if (t.Enabled) dirs.Add(t.Path_);
            }
            ProcessGuard.Start(dirs, delegate(string m) { log.Warn(m); });
            if (dirs.Count > 0)
                log.Step("实时守护已启动：受限软件启动的其他程序将被立即终止");

            // ALE 层网络封锁：优先内核驱动（Games8thGuard.sys），
            // 驱动不可用时自动回退到用户态 WFP（Fwpm* API）——
            // 两者都能拦走本地代理 127.0.0.1:7890 的流量（ALE 层带进程身份）。
            try
            {
                if (KernelDriver.IsLoaded())
                {
                    int pushed = 0;
                    foreach (Target t in targets)
                    {
                        if (!t.Enabled) continue;
                        if (KernelDriver.AddBlockedPath(t.Path_))
                            pushed++;
                    }
                    log.Ok("内核驱动已生效：已下发 " + pushed.ToString() + " 个受限路径（ALE层网络封锁）");
                }
                else if (WfpEngine.IsAvailable())
                {
                    int wfpOk = 0;
                    foreach (Target t in targets)
                    {
                        if (!t.Enabled) continue;
                        if (Directory.Exists(t.Path_))
                            wfpOk += WfpEngine.BlockDirectory(t.Path_, delegate(string m) { log.Warn(m); });
                        else if (File.Exists(t.Path_) && Components.IsExe(t.Path_))
                        {
                            if (WfpEngine.Block(t.Path_)) wfpOk++;
                        }
                    }
                    log.Ok("用户态 WFP 已生效：已封锁 " + wfpOk.ToString() + " 个可执行组件（免驱动/免签名，ALE层网络封锁）");
                }
                else
                {
                    log.Warn("内核驱动与用户态 WFP 均不可用（需要管理员权限）。已用防火墙/ACL 方案封锁；如需 ALE 层拦截请以管理员运行。");
                }
            }
            catch (Exception ex)
            {
                log.Fail("[网络封锁] 通信失败: " + ex.Message);
            }

            RefreshGrid();
        }

        // ---- Clear all ----
        private void ClearAll()
        {
            if (MessageBox.Show(
                "将删除本工具创建的全部防火墙规则（Games8th_Blocker 前缀），\n并恢复各目录内组件文件的访问权限（icacls /remove:d）。确认继续？",
                "确认解除限制", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                log.Step("开始任务: 解除全部限制");

                // 0. stop process guard
                ProcessGuard.Stop();

                // 0b. clear kernel driver blocked paths (if loaded)
                try
                {
                    if (KernelDriver.IsLoaded())
                    {
                        KernelDriver.ClearAll();
                        log.Ok("内核驱动限制已清除");
                    }
                }
                catch { }

                // 0c. clear user-mode WFP filters (driver-less fallback path)
                try
                {
                    if (WfpEngine.IsAvailable())
                    {
                        List<string> wfpPaths = new List<string>();
                        foreach (Target t in targets)
                        {
                            if (t.Enabled) wfpPaths.Add(t.Path_);
                        }
                        int removed = WfpEngine.ClearAll(wfpPaths);
                        if (removed > 0)
                            log.Ok("用户态 WFP 限制已清除（" + removed.ToString() + " 条过滤器）");
                    }
                }
                catch { }

                // 1. remove firewall rules
                Firewall.RemoveAllRules();
                log.Ok("★ 已删除全部 Games8th_Blocker 防火墙规则");

                // 2. restore ACL on dll components (per target directory)
                int restored = 0, restoreFail = 0;
                foreach (Target t in targets)
                {
                    if (!Directory.Exists(t.Path_)) continue;
                    List<string> comps = Components.ScanFolder(t.Path_);
                    foreach (string f in comps)
                    {
                        if (Components.IsExe(f)) continue;
                        if (!Components.IsDenied(f)) continue;
                        if (Components.AllowAccess(f))
                        {
                            restored++;
                            log.Ok("已恢复权限: " + Path.GetFileName(f) + "  (" + f + ")");
                        }
                        else
                        {
                            restoreFail++;
                            log.Fail("权限恢复失败: " + Path.GetFileName(f) + "  (" + f + ")");
                        }
                    }
                    t.State = "○ 未限制";
                }
                log.Ok("★ ACL 权限恢复：成功 " + restored.ToString() + " / 失败 " + restoreFail.ToString());
                SaveConfig();
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                pb.Value = 0;
                btnClear.Enabled = true;
                btnApply.Enabled = true;
                RefreshGrid();
            };
            btnClear.Enabled = false;
            btnApply.Enabled = false;
            pb.Maximum = 100;
            bw.RunWorkerAsync();
        }

        // ---- Verify ----
        private void VerifyAll()
        {
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                log.Step("开始任务: 核验状态");
                List<string> rules = Firewall.ListRules();
                int okC = 0, failC = 0, skip = 0;
                foreach (Target t in targets)
                {
                    if (!t.Enabled) { skip++; continue; }
                    List<string> files = new List<string>();
                    if (Directory.Exists(t.Path_))
                        files = Components.ScanFolder(t.Path_);
                    else if (File.Exists(t.Path_)) files.Add(t.Path_);

                    if (files.Count == 0)
                    {
                        t.State = "○ 未限制";
                        log.Warn(t.Name + " 无组件（无封锁目标）");
                        continue;
                    }

                    bool allBlocked = true;
                    int blockedN = 0;
                    foreach (string file in files)
                    {
                        if (Components.IsExe(file))
                        {
                            bool full = t.Strategy != "NET_OUT";
                            bool outOk = rules.Exists(x => string.Equals(x, Firewall.RuleName(file, "out"), StringComparison.OrdinalIgnoreCase));
                            bool inOk = !full || rules.Exists(x => string.Equals(x, Firewall.RuleName(file, "in"), StringComparison.OrdinalIgnoreCase));
                            if (outOk && inOk) blockedN++;
                            else allBlocked = false;
                        }
                        else
                        {
                            // dll -> check ACL deny
                            if (Components.IsDenied(file)) blockedN++;
                            else allBlocked = false;
                        }
                    }
                    if (allBlocked)
                    {
                        t.State = "● 已限制";
                        okC++;
                        log.Ok(t.Name + " 已限制（" + blockedN.ToString() + "/" + files.Count.ToString() + " 组件）");
                    }
                    else
                    {
                        t.State = "○ 未限制";
                        failC++;
                        log.Fail(t.Name + " 未完全限制（" + blockedN.ToString() + "/" + files.Count.ToString() + " 组件）");
                    }
                }
                log.Ok("核验完成：已限制 " + okC.ToString() + " 目标，未限制 " + failC.ToString() + " 目标，跳过 " + skip.ToString());
                SaveConfig();
                RefreshGrid();
            };
            btnVerify.Enabled = false;
            bw.RunWorkerCompleted += delegate { btnVerify.Enabled = true; };
            bw.RunWorkerAsync();
        }

        private void OnShownSetup()
        {
            // 布局完成后（split.Width 已正确），再设置分栏位置与最小宽度
            try
            {
                if (split != null)
                {
                    split.Panel1MinSize = 420;
                    split.Panel2MinSize = 320;
                    int w = split.Width;
                    if (w > 900) split.SplitterDistance = 540;
                    else if (w - split.SplitterWidth - split.Panel2MinSize > split.Panel1MinSize)
                        split.SplitterDistance = (w - split.SplitterWidth) * 52 / 100;
                }
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveConfig();
            base.OnFormClosing(e);
        }
    }

    // ===================== ENTRY =====================
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0].ToLower() == "cli")
            {
                ConsoleCli.Run(args);
                return;
            }
            Application.Run(new MainForm());
        }
    }

    public static class ConsoleCli
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        public static void Run(string[] args)
        {
            // winexe 程序默认无控制台：附加到父控制台(cmd)使输出正常显示
            try { AttachConsole(-1); } catch { }
            try
            {
                // 输出编码自适应：
                //   - 真实控制台(cmd.exe)   -> 系统代码页 GBK(936)，中文正常
                //   - 管道/重定向(Git Bash) -> UTF8，避免乱码
                if (Console.IsOutputRedirected)
                    Console.OutputEncoding = Encoding.UTF8;
                else
                    Console.OutputEncoding = Encoding.GetEncoding(936);
            }
            catch
            {
                try { Console.OutputEncoding = Encoding.GetEncoding(936); } catch { }
            }
            // AttachConsole 后需重新绑定标准输出，否则仍是无控制台状态
            try
            {
                StreamWriter outWriter = new StreamWriter(
                    Console.OpenStandardOutput(), Console.OutputEncoding);
                outWriter.AutoFlush = true;
                Console.SetOut(outWriter);
            }
            catch { }

            Console.WriteLine("Games8Th.Team 软件权限限制工具 - CLI");
            Console.WriteLine();
            if (args.Length < 2)
            {
                Console.WriteLine("用法: Games8thBlocker.exe cli list|block|clear|verify|watch|guard");
                return;
            }
            string action = args[1].ToLower();
            if (action == "block")
            {
                if (args.Length < 3) { Console.WriteLine("用法: cli block <path>"); return; }
                string p = args[2];
                if (Directory.Exists(p))
                {
                    // scan all components in folder
                    List<string> files = Components.ScanFolder(p);
                    Console.WriteLine("目录目标: " + p + "（" + files.Count.ToString() + " 个组件）");
                    foreach (string f in files)
                    {
                        if (Components.IsExe(f))
                        {
                            bool ok = Firewall.AddBlockRule(f, "out");
                            bool okIn = Firewall.AddBlockRule(f, "in");
                            Console.WriteLine((ok && okIn ? "  [OK] 封锁 " : "  [FAIL] 失败 ") + Path.GetFileName(f));
                        }
                        else
                        {
                            bool ok = Components.DenyAccess(f);
                            Console.WriteLine((ok ? "  [OK] 剥夺权限 " : "  [FAIL] 失败 ") + Path.GetFileName(f));
                        }
                    }
                }
                else
                {
                    Console.WriteLine("封锁: " + p);
                    if (Components.IsExe(p))
                    {
                        bool ok = Firewall.AddBlockRule(p, "out");
                        bool okIn = Firewall.AddBlockRule(p, "in");
                        Console.WriteLine((ok && okIn) ? "  [OK] 已封锁" : "  [FAIL] 失败");
                    }
                    else
                    {
                        bool ok = Components.DenyAccess(p);
                        Console.WriteLine(ok ? "  [OK] 已剥夺权限" : "  [FAIL] 失败");
                    }
                }
            }
            else if (action == "list")
            {
                List<string> rules = Firewall.ListRules();
                Console.WriteLine("当前规则 (" + rules.Count.ToString() + " 条):");
                foreach (string r in rules) Console.WriteLine("  " + r);
            }
            else if (action == "clear")
            {
                Firewall.RemoveAllRules();
                Console.WriteLine("  [OK] 已清除全部规则");
            }
            else if (action == "verify")
            {
                Console.WriteLine("防火墙状态: " + (Firewall.IsFirewallEnabled() ? "开启" : "关闭"));
                Console.WriteLine("规则数: " + Firewall.ListRules().Count.ToString());
                Console.WriteLine("守护运行中: " + (ProcessGuard.IsRunning ? "是" : "否"));
            }
            else if (action == "watch" || action == "guard")
            {
                // watch <path>: 实施限制 + 启动实时守护，持续阻止受限软件调用其他程序
                if (args.Length < 3) { Console.WriteLine("用法: cli watch <路径>"); return; }
                string p = args[2];

                List<string> watchDirs = new List<string>();
                if (Directory.Exists(p))
                {
                    watchDirs.Add(p);
                    if (action == "watch")
                    {
                        // also apply the restrictions first
                        List<string> files = Components.ScanFolder(p);
                        Console.WriteLine("目录目标: " + p + "（" + files.Count.ToString() + " 个组件）");
                        foreach (string f in files)
                        {
                            if (Components.IsExe(f))
                            {
                                bool ok = Firewall.AddBlockRule(f, "out");
                                bool okIn = Firewall.AddBlockRule(f, "in");
                                Console.WriteLine((ok && okIn ? "  [OK] 封锁 " : "  [FAIL] 失败 ") + Path.GetFileName(f));
                            }
                            else
                            {
                                bool ok = Components.DenyAccess(f);
                                Console.WriteLine((ok ? "  [OK] 剥夺权限 " : "  [FAIL] 失败 ") + Path.GetFileName(f));
                            }
                        }
                    }
                }
                else if (File.Exists(p))
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir)) watchDirs.Add(dir);
                }
                else
                {
                    Console.WriteLine("路径不存在: " + p);
                    return;
                }

                ProcessGuard.Start(watchDirs, delegate(string m) { Console.WriteLine("  [GUARD] " + m); });
                Console.WriteLine();
                Console.WriteLine("★ 实时守护已启动：受限目录内进程调用的其他程序将被立即终止");
                Console.WriteLine("  监控目录: " + string.Join(", ", watchDirs.ToArray()));
                Console.WriteLine("  按 Ctrl+C 退出守护");
                try
                {
                    System.Threading.ManualResetEvent stop = new System.Threading.ManualResetEvent(false);
                    Console.CancelKeyPress += delegate(object s, ConsoleCancelEventArgs ce)
                    {
                        ce.Cancel = true;
                        stop.Set();
                    };
                    stop.WaitOne();
                }
                catch { }
                ProcessGuard.Stop();
                Console.WriteLine("守护已停止");
            }
            else
            {
                Console.WriteLine("未知动作: " + action);
            }
        }
    }
}
