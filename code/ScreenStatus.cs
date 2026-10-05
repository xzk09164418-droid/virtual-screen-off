using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public static class ScreenStatus
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var json = new JavaScriptSerializer();
        try
        {
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--json"))
                throw new ArgumentException("Usage: ScreenStatus.exe [--json]");
            string target = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-target.txt")).Trim();
            if (target.Length == 0) throw new InvalidDataException("Virtual display identity is empty.");
            var config = DisplayNative.Query(false);
            var displays = config.Paths.Select(p => new {
                internal_display = DisplayNative.Internal(p),
                device_path = DisplayNative.Name(p).DevicePath,
                width = DisplayNative.GetSource(config, p).Width,
                height = DisplayNative.GetSource(config, p).Height
            }).ToArray();
            bool internalActive = displays.Any(p => p.internal_display);
            bool virtualActive = displays.Any(p => String.Equals(p.device_path, target, StringComparison.OrdinalIgnoreCase));
            Mutex session;
            bool running = Mutex.TryOpenExisting("Local\\ScreenOffHotkey.Test", out session);
            if (session != null) session.Dispose();
            Console.WriteLine(json.Serialize(new {
                schema_version = 1, ok = true, timestamp_utc = DateTime.UtcNow.ToString("O"),
                state = internalActive ? "internal_on" : virtualActive ? "virtual_only" : "internal_off_other",
                internal_display_active = internalActive, target_virtual_display_active = virtualActive,
                screen_off_session_running = running, active_displays = displays,
                physical_panel_power = "unknown"
            }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(json.Serialize(new { schema_version = 1, ok = false,
                timestamp_utc = DateTime.UtcNow.ToString("O"), state = "unknown", error = ex.Message }));
            return 1;
        }
    }
}
