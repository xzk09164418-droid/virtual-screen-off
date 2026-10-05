using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using D = DisplayNative;

public sealed class TestOptions
{
    public int Seconds = 14400;
    public bool UntilRestore;
    public string LaunchFile = "", Arguments = "", StartDirectory = "";
    public int LaunchDelaySeconds = 10;
    public void Validate()
    {
        if (Seconds < 8 || Seconds > 43200) throw new Exception("测试时长须在 8 秒至 12 小时之间。");
        if (LaunchDelaySeconds < 0 || LaunchDelaySeconds > 600) throw new Exception("启动延时须在 0 至 600 秒之间。");
        if (String.IsNullOrWhiteSpace(LaunchFile)) return;
        if (!File.Exists(LaunchFile)) throw new Exception("启动文件不存在。");
        string ext = Path.GetExtension(LaunchFile).ToLowerInvariant();
        if (!new[] { ".exe", ".lnk", ".cmd", ".bat", ".ps1" }.Contains(ext)) throw new Exception("请选择程序、快捷方式，或 cmd/bat/ps1 启动脚本。");
        if (!String.IsNullOrEmpty(StartDirectory) && !Directory.Exists(StartDirectory)) throw new Exception("工作目录不存在。");
    }
}

public static class SessionRunner
{
    public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    public static string LastFile { get { return Path.Combine(Root, "last-test.txt"); } }
    public static void Log(string dir, string text)
    {
        // Recovery must not fail merely because the parent is writing a log.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { File.AppendAllText(Path.Combine(dir, "test.log"), DateTime.Now.ToString("O") + " " + text + Environment.NewLine); return; }
            catch (IOException) { System.Threading.Thread.Sleep(30); }
            catch (UnauthorizedAccessException) { return; }
        }
    }
    static string Target { get { return File.ReadAllText(Path.Combine(Root, "test-target.txt")).Trim(); } }
    public static bool InternalActive() { return D.Query(false).Paths.Any(p => D.Internal(p)); }
    static bool Stopping(string dir) { return File.Exists(Path.Combine(dir, "restore.request")) || File.Exists(Path.Combine(dir, "restored.ok")) || File.Exists(Path.Combine(dir, "restore.failed")); }
    static void Pulse(string dir) { File.WriteAllText(Path.Combine(dir, "parent-heartbeat"), DateTime.Now.ToString("O")); }
    public static D.Config Prepare(out D.Config before)
    {
        before = D.Query(false);
        var internalPaths = before.Paths.Where(p => D.Internal(p)).ToArray();
        if (internalPaths.Length != 1) throw new Exception("请先恢复内屏，再开始下一次测试。");
        D.SourceMode size = D.GetSource(before, internalPaths[0]);
        var existing = before.Paths.Where(p => !D.Internal(p) && String.Equals(D.Name(p).DevicePath, Target, StringComparison.OrdinalIgnoreCase)).ToArray();
        D.Config next = existing.Length == 1 ? new D.Config { Paths = existing, Modes = before.Modes } : D.Single(Target, size.Width, size.Height);
        string adapter = File.ReadAllText(Path.Combine(Root, "test-adapter.txt")).Trim();
        if (D.Internal(next.Paths[0]) || !String.Equals(D.AdapterPath(next.Paths[0]), adapter, StringComparison.OrdinalIgnoreCase)) throw new Exception("虚拟显示设备身份已改变，停止测试。");
        D.SourceMode virtualSize = D.GetSource(next, next.Paths[0]);
        if (virtualSize.Width != size.Width || virtualSize.Height != size.Height) throw new Exception("虚拟屏分辨率不匹配。");
        D.Apply(before, true); D.Apply(next, true, true); return next;
    }
    static D.Config CheckVirtual(D.Config before)
    {
        D.Config current = D.Query(false);
        if (current.Paths.Length != 1 || D.Internal(current.Paths[0]) || !String.Equals(D.Name(current.Paths[0]).DevicePath, Target, StringComparison.OrdinalIgnoreCase)) throw new Exception("显示布局已经改变，结束测试并恢复内屏。");
        D.SourceMode original = D.GetSource(before, before.Paths.First(p => D.Internal(p)));
        D.SourceMode actual = D.GetSource(current, current.Paths[0]);
        // The independent force tray owns resolution while active; it corrects drift.
        // Continue enforcing display identity/topology above, without fighting its correction.
        if (!ForceDisplayRunning() && (actual.Width != original.Width || actual.Height != original.Height)) throw new Exception("桌面分辨率发生变化，结束测试。");
        return current;
    }
    static bool ForceDisplayRunning()
    {
        Mutex owner;
        if (!Mutex.TryOpenExisting(@"Local\ScreenOffHotkey.ForceDisplay", out owner)) return false;
        owner.Dispose(); return true;
    }
    static void Capture(string dir, string name)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            if (bounds.Width < 1 || bounds.Height < 1 || bounds.Width > 16000 || bounds.Height > 16000) throw new Exception("Invalid desktop bounds.");
            using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap)) g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                int samples = 0, nonblack = 0;
                for (int y = 0; y < bitmap.Height; y += 40)
                    for (int x = 0; x < bitmap.Width; x += 40) { samples++; Color c = bitmap.GetPixel(x, y); if (c.R + c.G + c.B > 30) nonblack++; }
                if (nonblack > 0 || attempt == 11)
                {
                    bitmap.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
                    Log(dir, String.Format("Capture {0}: {1}x{2}, sampled_nonblack={3}/{4}, settle_retries={5}", name, bounds.Width, bounds.Height, nonblack, samples, attempt));
                    if (nonblack == 0) Log(dir, "WARNING: screenshot is black; test continues for script-log verification.");
                    return;
                }
            }
            Thread.Sleep(400);
        }
    }
    static void TryCapture(string dir, string name)
    {
        try { Capture(dir, name); } catch (Exception ex) { Log(dir, "WARNING: capture failed: " + ex.Message); }
    }
    public static void LaunchOnce(TestOptions options, string dir, D.Config before)
    {
        if (String.IsNullOrWhiteSpace(options.LaunchFile)) return;
        if (!File.Exists(Path.Combine(dir, "virtual.ready")) || Stopping(dir)) throw new Exception("虚拟屏尚未稳定或测试已结束，不启动脚本。");
        CheckVirtual(before);
        string claim = Path.Combine(dir, "launch.claimed");
        using (FileStream mark = new FileStream(claim, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
        ProcessStartInfo p = new ProcessStartInfo();
        p.FileName = options.LaunchFile; p.Arguments = options.Arguments;
        p.WorkingDirectory = String.IsNullOrWhiteSpace(options.StartDirectory) ? Path.GetDirectoryName(options.LaunchFile) : options.StartDirectory;
        p.UseShellExecute = true;
        // Shell scripts run without an extra console stealing the game's focus.
        string ext = Path.GetExtension(options.LaunchFile).ToLowerInvariant();
        if (ext == ".cmd" || ext == ".bat") p.WindowStyle = ProcessWindowStyle.Hidden;
        if (ext == ".ps1")
        {
            p.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            p.Arguments = "-NoProfile -File \"" + options.LaunchFile + "\" " + options.Arguments;
            p.WindowStyle = ProcessWindowStyle.Hidden;
        }
        Log(dir, "Launch requested after virtual-ready: " + options.LaunchFile);
        using (Process launched = Process.Start(p)) Log(dir, "Launch dispatched once; process_id=" + (launched == null ? "shell-managed" : launched.Id.ToString()));
        File.WriteAllText(Path.Combine(dir, "launch.dispatched"), DateTime.Now.ToString("O"));
    }
    public static string Run(TestOptions options, Action prepared = null)
    {
        options.Validate(); bool created;
        using (Mutex session = new Mutex(true, "Local\\ScreenOffHotkey.Test", out created))
        {
            if (!created) throw new Exception("已有测试正在运行。请先恢复内屏。");
            D.Config before; D.Config next = Prepare(out before);
            int scale = D.GetScale(before.Paths.First(p => D.Internal(p)));
            string dir = Path.Combine(Root, "TestResults", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")); Directory.CreateDirectory(dir);
            D.Save(before, Path.Combine(dir, "restore.bin"));
            File.WriteAllText(Path.Combine(dir, "before.txt"), D.Describe(before)); File.WriteAllText(LastFile, dir);
            Pulse(dir); Log(dir, "Prepared v2; duration_seconds=" + options.Seconds + "; scale=" + scale + "; delayed_launch=" + !String.IsNullOrWhiteSpace(options.LaunchFile));
            if (prepared != null) prepared();
            int safetyLimit = options.UntilRestore ? 0 : options.Seconds + (String.IsNullOrWhiteSpace(options.LaunchFile) ? 0 : options.LaunchDelaySeconds) + 90;
            ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, "--watchdog \"" + dir + "\" " + safetyLimit + " " + Process.GetCurrentProcess().Id);
            start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process guard = Process.Start(start))
            {
                try
                {
                    Stopwatch ready = Stopwatch.StartNew();
                    while (!File.Exists(Path.Combine(dir, "watchdog.ready")) && ready.ElapsedMilliseconds < 8000 && !guard.HasExited) Thread.Sleep(100);
                    if (!File.Exists(Path.Combine(dir, "watchdog.ready"))) throw new Exception("恢复进程未就绪，未关闭内屏。");
                    if (D.SetThreadExecutionState(0x80000003) == 0) throw new Exception("无法保持系统唤醒。");
                    TryCapture(dir, "before");
                    if (guard.HasExited || Stopping(dir)) throw new Exception("测试已取消，未切屏。");
                    var minimized = WindowStates.Capture();
                    D.Apply(next, false, true); Thread.Sleep(1800);
                    D.Config active = CheckVirtual(before); if (!ForceDisplayRunning()) D.SetScale(active.Paths[0], scale);
                    Thread.Sleep(1000); WindowStates.Preserve(minimized, dir, "switch-off");
                    File.WriteAllText(Path.Combine(dir, "during.txt"), D.Describe(CheckVirtual(before)));
                    File.WriteAllText(Path.Combine(dir, "virtual.ready"), DateTime.Now.ToString("O"));
                    Pulse(dir); Log(dir, "Virtual-only active and stable; scale=" + D.GetScale(active.Paths[0]));
                    if (!String.IsNullOrWhiteSpace(options.LaunchFile))
                    {
                        Stopwatch delay = Stopwatch.StartNew();
                        while (delay.Elapsed.TotalSeconds < options.LaunchDelaySeconds)
                        { if (Stopping(dir) || guard.HasExited) throw new Exception("已恢复内屏，取消延时启动。"); Pulse(dir); Thread.Sleep(200); }
                        LaunchOnce(options, dir, before);
                    }
                    // For delayed launch, the selected duration begins at dispatch,
                    // not while waiting for the display or launcher delay.
                    Stopwatch elapsed = Stopwatch.StartNew(); int nextLog = 0, nextCapture = 0, nextCheck = 0;
                    int captureInterval = options.Seconds <= 180 ? Math.Max(2, options.Seconds / 3) : 600;
                    File.WriteAllText(Path.Combine(dir, "test.started"), DateTime.Now.ToString("O"));
                    while (options.UntilRestore || elapsed.Elapsed.TotalSeconds < options.Seconds)
                    {
                        if (Stopping(dir)) break;
                        if (guard.HasExited) throw new Exception("恢复进程意外退出，结束测试。");
                        int second = (int)elapsed.Elapsed.TotalSeconds; Pulse(dir);
                        if (second >= nextCheck) { CheckVirtual(before); nextCheck = second + 30; }
                        if (!options.UntilRestore && second >= nextCapture) { TryCapture(dir, "during-" + second.ToString("D6")); nextCapture = second + captureInterval; }
                        if (second >= nextLog) { Log(dir, "Heartbeat elapsed=" + second + "; remaining=" + (options.UntilRestore ? "until-restore" : Math.Max(0, options.Seconds - second).ToString())); nextLog = second + (options.Seconds <= 180 ? 1 : 30); }
                        Thread.Sleep(500);
                    }
                    Log(dir, "Test interval ended; elapsed_seconds=" + (int)elapsed.Elapsed.TotalSeconds);
                }
                catch (Exception ex) { Log(dir, "ERROR " + ex); throw; }
                finally
                {
                    try
                    {
                        if (!File.Exists(Path.Combine(dir, "restore.request")))
                            File.WriteAllText(Path.Combine(dir, "restore.request"), "runner-finished");
                        Stopwatch wait = Stopwatch.StartNew();
                        while (!File.Exists(Path.Combine(dir, "restored.ok")) && wait.ElapsedMilliseconds < 15000 && !guard.HasExited) Thread.Sleep(100);
                        if (!File.Exists(Path.Combine(dir, "restored.ok")) || !InternalActive()) Restore(dir, "parent-fallback");
                    }
                    finally { D.SetThreadExecutionState(0x80000000); }
                }
            }
            File.WriteAllText(Path.Combine(dir, "after.txt"), D.Describe(D.Query(false)));
            if (!InternalActive()) throw new Exception("未确认内屏恢复，请按 Win+P 选择仅电脑屏幕。");
            TryCapture(dir, "after"); Log(dir, "Completed; internal display active; test window remains minimized."); return dir;
        }
    }
    public static bool Restore(string dir, string reason)
    {
        using (Mutex restoreLock = new Mutex(false, "Local\\ScreenOffHotkey.Restore." + Path.GetFileName(dir)))
        {
            try { if (!restoreLock.WaitOne(15000)) throw new Exception("恢复进程仍在处理，请稍候。"); } catch (AbandonedMutexException) { }
            try
            {
                bool manual = reason == "manual" || reason == "emergency-hotkey";
                if (ScreenSchedule.SuppressAutomaticRestore(DateTime.UtcNow, manual)) return false;
                if (File.Exists(Path.Combine(dir, "restored.ok")) && InternalActive()) return true;
                Log(dir, "Restore reason=" + reason); var minimized = WindowStates.Capture(); bool success = false;
                for (int attempt = 0; attempt < 3 && !success; attempt++)
                {
                    if (ScreenSchedule.SuppressAutomaticRestore(DateTime.UtcNow, manual)) return false;
                    try { D.Apply(D.Load(Path.Combine(dir, "restore.bin")), false); success = InternalActive(); }
                    catch (Exception ex) { Log(dir, "Restore retry: " + ex.Message); }
                    if (!success) Thread.Sleep(500);
                }
                if (!success) {
                    if (ScreenSchedule.SuppressAutomaticRestore(DateTime.UtcNow, manual)) return false;
                    D.RestoreInternal(); success = InternalActive();
                }
                Thread.Sleep(1000); WindowStates.Preserve(minimized, dir, "switch-on");
                File.WriteAllText(Path.Combine(dir, success ? "restored.ok" : "restore.failed"), DateTime.Now.ToString("O"));
                if (!success) throw new Exception("内屏恢复未通过核验。");
                return true;
            }
            finally { restoreLock.ReleaseMutex(); }
        }
    }
    public static void RestoreLast(bool manual = true)
    {
        if (ScreenSchedule.SuppressAutomaticRestore(DateTime.UtcNow, manual)) return;
        if (!File.Exists(LastFile))
        {
            var minimized = WindowStates.Capture(); D.RestoreInternal(); Thread.Sleep(1000); WindowStates.Preserve(minimized, null, "manual"); return;
        }
        string reason = manual ? "manual" : "scheduled";
        string dir = File.ReadAllText(LastFile).Trim(); File.WriteAllText(Path.Combine(dir, "restore.request"), reason);
        // The same lock also serializes a manual recovery with the watchdog.
        Restore(dir, reason);
    }
    public static void HotkeyToggle(bool offOnly = false)
    {
        using (var gate = new Mutex(false, "Local\\ScreenOffHotkey.Toggle"))
        {
            bool owned = false;
            try
            {
                try { owned = gate.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) return;
                Mutex active;
                bool running = Mutex.TryOpenExisting("Local\\ScreenOffHotkey.Test", out active);
                if (active != null) active.Dispose();
                if (running || !InternalActive()) { if (!offOnly) RestoreLast(); return; }
                // Release only after the new session's recovery file is published.
                Run(new TestOptions { UntilRestore = true }, delegate { gate.ReleaseMutex(); owned = false; });
            }
            finally { if (owned) gate.ReleaseMutex(); }
        }
    }
}
