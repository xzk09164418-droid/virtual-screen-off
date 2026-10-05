using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class ScreenTestApp
{
    [STAThread] public static int Main(string[] args)
    {
        if (!DisplayNative.SetProcessDpiAwarenessContext(new IntPtr(-4))) DisplayNative.SetProcessDPIAware();
        Application.EnableVisualStyles();
        try
        {
            if (args.Length == 4 && args[0] == "--watchdog") { Application.Run(new RecoveryGuard(args[1], Int32.Parse(args[2]), Int32.Parse(args[3]))); return 0; }
            if (args.Length == 1 && args[0] == "--check")
            {
                DisplayNative.Config before; var next = SessionRunner.Prepare(out before);
                File.WriteAllText(Path.Combine(SessionRunner.Root, "preflight.txt"), "V2 validation only; no display change.\r\n" + DisplayNative.Describe(before) + "\r\nRequested:\r\n" + DisplayNative.Describe(next)); return 0;
            }
            if (args.Length == 1 && args[0] == "--self-test")
            {
                new TestOptions { Seconds = 14400 }.Validate(); new TestOptions { Seconds = 28800 }.Validate();
                bool rejected = false; try { new TestOptions { Seconds = 43201 }.Validate(); } catch { rejected = true; }
                if (!rejected) throw new Exception("Duration bounds regression.");
                WindowStates.SelfTest();
                // Test the cold-launch safety gate using this executable; the gate
                // must reject it without starting any process or changing displays.
                string testDir = Path.Combine(SessionRunner.Root, "SelfTest"); Directory.CreateDirectory(testDir);
                rejected = false;
                try { SessionRunner.LaunchOnce(new TestOptions { LaunchFile = Application.ExecutablePath, Arguments = "--check" }, testDir, null); }
                catch { rejected = true; }
                if (!rejected || File.Exists(Path.Combine(testDir, "launch.claimed"))) throw new Exception("Cold launch gate regression.");
                File.WriteAllText(Path.Combine(SessionRunner.Root, "v2-self-test.txt"), DateTime.Now.ToString("O") + " PASS: 4h/8h validation; invalid duration rejected; minimized-window repair on owned window; launch blocked before virtual-ready. No display switch; no long-run compatibility claim.\r\n"); return 0;
            }
            if (args.Length == 2 && args[0] == "--test") { SessionRunner.Run(new TestOptions { Seconds = Int32.Parse(args[1]) }); return 0; }
            if (args.Length == 1 && args[0] == "--restore") { SessionRunner.RestoreLast(); return 0; }
            if (args.Length == 1 && args[0] == "--auto-restore") { SessionRunner.RestoreLast(false); return 0; }
            if (args.Length == 1 && args[0] == "--off") { SessionRunner.HotkeyToggle(true); return 0; }
            if (args.Length == 1 && args[0] == "--hotkey-toggle") { SessionRunner.HotkeyToggle(); return 0; }
            if (args.Length != 0) throw new Exception("Unknown command line arguments.");
            Application.Run(new LongTestForm()); return 0;
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(SessionRunner.Root, "test-errors.log"), DateTime.Now.ToString("O") + " " + ex + Environment.NewLine);
            if (args.Length == 0) MessageBox.Show(ex.Message, "熄屏测试", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

public sealed class LongTestForm : Form
{
    readonly ComboBox duration = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    readonly CheckBox cold = new CheckBox { Text = "内屏关闭并稳定后，再启动指定程序（只启动一次）", AutoSize = true };
    readonly TextBox launchFile = new TextBox { Width = 475 };
    readonly TextBox arguments = new TextBox { Width = 550 };
    readonly TextBox workingDir = new TextBox { Width = 550 };
    readonly NumericUpDown delay = new NumericUpDown { Minimum = 0, Maximum = 600, Value = 10, Width = 90 };
    readonly Button browse = new Button { Text = "选择…", Width = 70, Height = 28 };
    readonly Button start = new Button { Text = "开始测试", Width = 170, Height = 40 };
    readonly Button recover = new Button { Text = "立即恢复内屏", Width = 170, Height = 40 };
    readonly Label state = new Label { AutoSize = true, MaximumSize = new Size(630, 0), Text = "请选择时长和启动顺序。长测需要你手动开始。" };
    readonly NotifyIcon tray = new NotifyIcon();
    bool running, sessionStarted, cancelCountdown;
    public LongTestForm()
    {
        SuspendLayout();
        Text = "内屏熄灭 · 长时间兼容性测试 v2";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Microsoft YaHei UI", 10); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowLayoutPanel layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20), Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(640, 0), Text = "开始后会最小化，留出 8 秒切回游戏。测试结束后保持最小化，不抢焦点。\r\n提前恢复：" + ScreenConfig.Current.EmergencyHotkey + "，或托盘菜单。移动鼠标不结束测试。" });
        duration.Items.AddRange(new object[] { "30 秒（先验证窗口状态）", "2 分钟", "4 小时", "6 小时", "8 小时" }); duration.SelectedIndex = 2; duration.Width = 245;
        layout.Controls.Add(Row(new Label { Text = "测试时长", AutoSize = true }, duration));
        cold.Margin = new Padding(3, 12, 3, 5); layout.Controls.Add(cold);
        layout.Controls.Add(Row(new Label { Text = "启动文件", AutoSize = true }, launchFile, browse));
        layout.Controls.Add(Row(new Label { Text = "启动参数", AutoSize = true }, arguments));
        layout.Controls.Add(Row(new Label { Text = "工作目录", AutoSize = true }, workingDir));
        layout.Controls.Add(Row(new Label { Text = "屏幕稳定后等待", AutoSize = true }, delay, new Label { Text = "秒再启动；所选测试时长从启动请求发出后计时。", AutoSize = true }));
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(640, 0), Text = "未勾选：先运行脚本，再熄屏。\r\n已勾选：选择能自动开始任务的启动项；仅打开软件界面不会自动点击“开始”。\r\n不需要延时启动时，启动文件、参数和工作目录可以留空。" });
        Button logs = new Button { Text = "打开测试记录", Width = 160, Height = 40 };
        layout.Controls.Add(Row(start, recover, logs)); layout.Controls.Add(state); Controls.Add(layout);
        browse.Click += delegate
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "选择平时使用的自动运行启动项", Filter = "程序、快捷方式、启动脚本|*.exe;*.lnk;*.cmd;*.bat;*.ps1", CheckFileExists = true, DereferenceLinks = false })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) { launchFile.Text = dialog.FileName; workingDir.Text = Path.GetDirectoryName(dialog.FileName); }
            }
        };
        cold.CheckedChanged += delegate { EnableInputs(); };
        start.Click += async delegate { await StartTest(); };
        recover.Click += async delegate { await Restore(); };
        logs.Click += delegate { string folder = Path.Combine(SessionRunner.Root, "TestResults"); Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true }); };
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("打开测试面板", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("立即恢复内屏", null, async delegate { await Restore(); });
        menu.Items.Add("退出", null, delegate { Close(); });
        tray.Icon = Icon; tray.Text = "虚拟屏测试：未运行"; tray.Visible = true; tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (running) { e.Cancel = true; WindowState = FormWindowState.Minimized; } };
        FormClosed += delegate { tray.Visible = false; tray.Dispose(); };
        AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
        EnableInputs(); ResumeLayout(false); PerformLayout();
    }
    static FlowLayoutPanel Row(params Control[] controls)
    {
        FlowLayoutPanel row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 6, 0, 6) };
        foreach (Control c in controls) row.Controls.Add(c); return row;
    }
    void EnableInputs()
    {
        start.Enabled = duration.Enabled = cold.Enabled = !running;
        launchFile.Enabled = arguments.Enabled = workingDir.Enabled = delay.Enabled = browse.Enabled = !running && cold.Checked;
    }
    async Task StartTest()
    {
        if (running) return;
        int[] times = { 30, 120, 14400, 21600, 28800 };
        TestOptions options = new TestOptions { Seconds = times[duration.SelectedIndex], LaunchDelaySeconds = (int)delay.Value };
        if (cold.Checked)
        {
            options.LaunchFile = launchFile.Text.Trim(); options.Arguments = arguments.Text; options.StartDirectory = workingDir.Text.Trim();
            if (String.IsNullOrWhiteSpace(options.LaunchFile)) { state.Text = "请选择熄屏后需要启动的文件。"; return; }
        }
        try { options.Validate(); } catch (Exception ex) { state.Text = ex.Message; return; }
        running = true; sessionStarted = false; cancelCountdown = false; EnableInputs(); state.Text = "测试正在运行。到时自动恢复（受禁止自动亮屏时段限制），或按 " + ScreenConfig.Current.EmergencyHotkey + " 提前恢复。";
        tray.Text = "虚拟屏测试进行中 · " + duration.Text;
        WindowState = FormWindowState.Minimized;
        try
        {
            await Task.Delay(8000);
            if (cancelCountdown) throw new OperationCanceledException("已取消开始前的倒计时。");
            sessionStarted = true;
            string dir = await Task.Run(() => SessionRunner.Run(options));
            state.Text = "测试已结束，内屏已恢复。请核对脚本日志。记录：" + Path.GetFileName(dir);
            tray.Text = "虚拟屏测试已结束 · 双击查看面板";
        }
        catch (Exception ex) { state.Text = "测试结束／未完成：" + ex.Message; tray.Text = "虚拟屏测试需要检查 · 双击查看面板"; }
        finally
        {
            running = false; EnableInputs();
            // Do not restore or activate this window when the test finishes.
        }
    }
    async Task Restore()
    {
        if (running && !sessionStarted) { cancelCountdown = true; state.Text = "已取消开始前的倒计时。"; return; }
        try { await Task.Run(() => SessionRunner.RestoreLast()); state.Text = "已请求恢复内屏。"; }
        catch (Exception ex) { state.Text = "恢复失败：" + ex.Message; }
    }
}
