using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Preserve only windows that were minimized immediately before this transition.
// Never show/activate a window, restore an old global layout, or enforce state
// for the duration of a long-running script.
public static class WindowStates
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    public sealed class Item { public IntPtr Handle; public uint Pid; public long Started; public string ClassName; }
    static string ClassOf(IntPtr hwnd) { StringBuilder b = new StringBuilder(256); GetClassName(hwnd, b, b.Capacity); return b.ToString(); }
    public static List<Item> Capture()
    {
        List<Item> result = new List<Item>();
        EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
        {
            if (!IsWindowVisible(hwnd) || !IsIconic(hwnd)) return true;
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                    result.Add(new Item { Handle = hwnd, Pid = pid, Started = p.StartTime.ToUniversalTime().Ticks, ClassName = ClassOf(hwnd) });
            }
            catch { /* A closing or inaccessible process cannot safely be restored. */ }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    static bool SameWindow(Item item)
    {
        if (!IsWindow(item.Handle)) return false;
        uint pid; GetWindowThreadProcessId(item.Handle, out pid);
        if (pid != item.Pid || ClassOf(item.Handle) != item.ClassName) return false;
        try { using (Process p = Process.GetProcessById((int)pid)) return p.StartTime.ToUniversalTime().Ticks == item.Started; }
        catch { return false; }
    }
    public static void Preserve(List<Item> snapshot, string directory, string phase)
    {
        int requests = 0;
        // Windows can restore windows asynchronously after a display reconnect.
        // Reconcile for a bounded 2.4 seconds, without activating any window.
        for (int pass = 0; pass < 8; pass++)
        {
            foreach (Item item in snapshot)
                if (SameWindow(item) && !IsIconic(item.Handle)) { ShowWindowAsync(item.Handle, 7); requests++; }
            Thread.Sleep(300);
        }
        int failures = 0;
        foreach (Item item in snapshot) if (SameWindow(item) && !IsIconic(item.Handle)) failures++;
        if (directory != null) File.AppendAllText(Path.Combine(directory, "windows.log"), DateTime.Now.ToString("O") + " " + phase + " captured_minimized=" + snapshot.Count + " minimize_requests=" + requests + " unresolved=" + failures + Environment.NewLine);
    }
    // Exercises the state repair on this process's own windows only; no display
    // changes or changes to other applications are involved.
    public static void SelfTest()
    {
        using (Form form = new Form { Text = "ScreenOff state regression probe", ShowInTaskbar = false, Width = 120, Height = 80 })
        {
            IntPtr hwnd = form.Handle;
            ShowWindowAsync(hwnd, 7); Application.DoEvents(); Thread.Sleep(100); Application.DoEvents();
            if (!IsIconic(hwnd)) throw new Exception("Probe could not be minimized.");
            List<Item> all = Capture(); List<Item> own = all.FindAll(x => x.Handle == hwnd);
            if (own.Count != 1) throw new Exception("Minimized probe was not captured.");
            ShowWindowAsync(hwnd, 4); Application.DoEvents();
            if (IsIconic(hwnd)) throw new Exception("Probe could not simulate an unwanted restore.");
            // A helper thread repairs state while the owning UI thread pumps.
            Exception failure = null;
            Thread worker = new Thread(delegate() { try { Preserve(own, null, "self-test"); } catch (Exception ex) { failure = ex; } });
            worker.Start(); while (worker.IsAlive) { Application.DoEvents(); Thread.Sleep(10); }
            if (failure != null) throw failure;
            if (!IsIconic(hwnd)) throw new Exception("Minimized state repair failed.");
            form.Close();
        }
    }
}
