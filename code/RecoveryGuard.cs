using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class RecoveryGuard : ApplicationContext
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    readonly string dir; readonly int limit; readonly Process parent;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Timer timer = new Timer(); readonly HotkeyWindow window;
    bool restoring;
    sealed class HotkeyWindow : NativeWindow
    {
        public Action Restore;
        public HotkeyWindow() { CreateHandle(new CreateParams { Caption = "ScreenOff restore watchdog", Parent = new IntPtr(-3) }); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x312 && Restore != null) Restore(); base.WndProc(ref m); }
    }
    public RecoveryGuard(string directory, int seconds, int parentId)
    {
        dir = directory; limit = seconds; parent = Process.GetProcessById(parentId);
        window = new HotkeyWindow(); window.Restore = delegate { Recover("emergency-hotkey"); };
        ScreenConfig config = ScreenConfig.Current;
        HotkeyBinding hotkey = HotkeyBinding.Parse(config.EmergencyHotkey, false);
        if (!RegisterHotKey(window.Handle, 1, hotkey.Modifiers | 0x4000, hotkey.Key)) throw new Exception(config.EmergencyHotkey + " 已被占用，测试未开始。");
        if (DisplayNative.SetThreadExecutionState(0x80000003) == 0) throw new Exception("恢复进程无法保持唤醒。");
        timer.Interval = 250;
        timer.Tick += delegate
        {
            string reason = null;
            if (File.Exists(Path.Combine(dir, "restored.ok"))) { Finish(); return; }
            if (File.Exists(Path.Combine(dir, "restore.request")))
                reason = File.ReadAllText(Path.Combine(dir, "restore.request")).Trim() == "manual" ? "manual" : "requested";
            else if (parent.HasExited) reason = "parent-exited";
            else if (DateTime.UtcNow - File.GetLastWriteTimeUtc(Path.Combine(dir, "parent-heartbeat")) > TimeSpan.FromSeconds(90)) reason = "parent-heartbeat-timeout";
            else if (limit > 0 && clock.Elapsed.TotalSeconds >= limit) reason = "safety-timeout";
            if (reason != null) Recover(reason);
        };
        timer.Start(); File.WriteAllText(Path.Combine(dir, "watchdog.ready"), "pid=" + Process.GetCurrentProcess().Id + "; timeout=" + seconds + "; " + config.EmergencyHotkey + " registered");
    }
    void Recover(string reason)
    {
        if (ScreenSchedule.SuppressAutomaticRestore(DateTime.UtcNow, reason == "manual" || reason == "emergency-hotkey")) return;
        if (restoring) return; restoring = true; timer.Stop();
        bool finished = false;
        try { finished = SessionRunner.Restore(dir, reason); }
        catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "restore.failed"), ex.ToString()); }
        finally
        {
            if (finished) Finish();
            else { restoring = false; timer.Interval = 5000; timer.Start(); }
        }
    }
    void Finish()
    {
        timer.Stop(); timer.Dispose();
        UnregisterHotKey(window.Handle, 1); window.DestroyHandle(); parent.Dispose();
        DisplayNative.SetThreadExecutionState(0x80000000); ExitThread();
    }
}
