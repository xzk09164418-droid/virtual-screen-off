using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public static class ScreenHotkey
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string caption);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint msg,IntPtr w,IntPtr l);
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    sealed class Receiver : NativeWindow
    {
        public Action Toggle; public Action Stop;
        public Receiver() { CreateHandle(new CreateParams { Parent = new IntPtr(-3), Caption = "Screen hotkey receiver" }); }
        protected override void WndProc(ref Message m) { if(m.Msg == 0x10 && Stop != null) { Stop(); return; } if (m.Msg == 0x312 && m.WParam.ToInt32() == 1 && Toggle != null) Toggle(); base.WndProc(ref m); }
        public void Close() { DestroyHandle(); }
    }
    sealed class Context : ApplicationContext
    {
        readonly Receiver receiver = new Receiver();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly NightScreenGuard night;
        DateTime last = DateTime.MinValue;
        public Context()
        {
            ScreenConfig config = ScreenConfig.Current;
            HotkeyBinding hotkey = HotkeyBinding.Parse(config.ToggleHotkey, true);
            if (hotkey.Key != 0 && !RegisterHotKey(receiver.Handle, 1, hotkey.Modifiers | 0x4000, hotkey.Key))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), config.ToggleHotkey + " 注册失败，可能已被其他程序占用。");
            config.ApplyAutostart();
            receiver.Toggle = delegate
            {
                if (DateTime.UtcNow - last < TimeSpan.FromSeconds(config.DebounceSeconds)) return;
                last = DateTime.UtcNow; if (night != null && night.ManualAction()) return; Launch("--hotkey-toggle");
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("熄屏 / 恢复（" + (hotkey.Key == 0 ? "热键禁用" : config.ToggleHotkey) + "）", null, delegate { receiver.Toggle(); });
            menu.Items.Add("恢复内屏", null, delegate { if (night != null && night.ManualAction()) return; Launch("--restore"); });
            menu.Items.Add("自动熄屏：" + (config.AutoOff ? config.OffStart + "–" + config.OffEnd + " / " + config.IdleSeconds + " 秒" : "禁用")).Enabled = false;
            menu.Items.Add("电池离开省电：" + (config.BatteryAwayEnabled ? config.BatteryAwaySeconds + " 秒无人脸 / 系统亮屏后恢复检测" : "禁用")).Enabled = false;
            menu.Items.Add("编辑 config.yaml（修改后重启程序）", null, delegate {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + ScreenConfig.FilePath + "\"") { UseShellExecute = true });
            });
            menu.Items.Add("退出热键程序", null, delegate { ExitThread(); });
            tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); tray.Text = "屏幕控制（config.yaml）";
            tray.ContextMenuStrip = menu; tray.Visible = true;
            Log("READY toggle=" + config.ToggleHotkey + "; emergency=" + config.EmergencyHotkey + "; autostart=" + config.Autostart);
            night = new NightScreenGuard(Log);
            receiver.Stop = delegate { ExitThread(); };
        }
        void Launch(string argument)
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo(Path.Combine(Root, "VirtualScreenTest.exe"), argument)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                    Log("Requested " + argument + "; pid=" + p.Id);
            }
            catch (Exception ex) { Log("ERROR " + ex.Message); tray.ShowBalloonTip(5000, "熄屏热键", ex.Message, ToolTipIcon.Error); }
        }
        protected override void ExitThreadCore()
        {
            if (night != null) night.Dispose();
            UnregisterHotKey(receiver.Handle, 1); receiver.Close(); tray.Visible = false; tray.Dispose(); Log("STOPPED"); base.ExitThreadCore();
        }
    }
    static void Log(string value) { try { File.AppendAllText(Path.Combine(Root, "screen-hotkey.log"), DateTime.Now.ToString("O") + " " + value + Environment.NewLine); } catch (IOException) { } }
    [STAThread] public static int Main(string[] args)
    {
        if(args.Length == 1 && args[0] == "--stop") {
            IntPtr window=FindWindowEx(new IntPtr(-3),IntPtr.Zero,null,"Screen hotkey receiver");
            return window==IntPtr.Zero || PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero) ? 0 : 1;
        }
        if (args.Length == 1 && (args[0] == "--check-config" || args[0] == "--apply-config"))
        {
            try {
                ScreenConfig config = ScreenConfig.Current;
                if (args[0] == "--apply-config") config.ApplyAutostart();
                Log("CONFIG OK " + ScreenConfig.FilePath); return 0;
            }
            catch (Exception ex) { Log("CONFIG ERROR " + ex.Message); return 1; }
        }
        if (args.Length != 0) return 1;
        bool created;
        using (var mutex = new Mutex(true, "Local\\ScreenOffHotkey.CtrlAltF9", out created))
        {
            if (!created) return 0;
            try { Application.Run(new Context()); return 0; }
            catch (Exception ex) { Log("ERROR " + ex); MessageBox.Show(ex.Message, "熄屏热键"); return 1; }
        }
    }
}
