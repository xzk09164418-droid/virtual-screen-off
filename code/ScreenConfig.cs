using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

// Deliberately restricted YAML: a flat mapping of known scalar keys. Reject
// unsupported YAML rather than silently interpreting a user's setting wrongly.
public sealed class ScreenConfig
{
    public static readonly string FilePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "config.yaml"));
    static ScreenConfig current;
    public static ScreenConfig Current { get { return current ?? (current = Load(FilePath)); } }
    public bool Autostart = true, AutoOff = true, AutoOn = true, NoAutoOn = true;
    public bool WakeOnInput = true;
    public bool IrEnabled = true, BatteryAwayEnabled = true;
    public bool TofEnabled = false;
    public int TofDistanceMm = 1500, TofSamples = 3, TofIntervalMs = 1000;
    public int TofDarkMaxIntervalMs = 30000, TofFaceTimeoutSeconds = 20;
    public int BatteryFaceConfirmSeconds = 5, BatteryTofChangeMm = 1000, BatteryTofMaxIntervalMs = 30000;
    public int BatteryTofInitialWaitSeconds = 30;
    public int BatteryTofStableSeconds = 180, BatteryFinalNoFaceSeconds = 5;
    public int IrAbsenceSeconds = 180, BatteryAwaySeconds = 180, IrStartupGraceSeconds = 20;
    public string MorningKey(DateTime utc) { return Local(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + Offset + "|" + OnTime; }
    public bool ScheduledDue(DateTime utc, string completedKey)
    { return OnDue(utc, DateTime.MinValue) && completedKey != MorningKey(utc); }
    public bool InputWakeDue(DateTime utc, bool on, long input, long observed)
    { return WakeOnInput && !on && input > observed && !SuppressRestore(utc, false); }
    public int Offset = 480, IdleSeconds = 180, PollSeconds = 5, GapSeconds = 30, RetrySeconds = 60, DebounceSeconds = 2;
    public TimeSpan OffStart = TimeSpan.Zero, OffEnd = TimeSpan.FromHours(9), OnTime = TimeSpan.FromHours(9), QuietStart = TimeSpan.FromHours(3), QuietEnd = TimeSpan.FromHours(7);
    public string ToggleHotkey = "Ctrl+Alt+F9", EmergencyHotkey = "Ctrl+Alt+F12";
    public DateTime Local(DateTime utc) { return utc.AddMinutes(Offset); }
    public static bool InWindow(TimeSpan time, TimeSpan start, TimeSpan end)
    { return start == end || (start < end ? time >= start && time < end : time >= start || time < end); }
    public bool OffDue(DateTime utc, double idle)
    { return AutoOff && idle >= IdleSeconds && InWindow(Local(utc).TimeOfDay, OffStart, OffEnd); }
    public bool PriorityOffDue(DateTime utc)
    { return NoAutoOn && InWindow(Local(utc).TimeOfDay, QuietStart, QuietEnd); }
    public bool SuppressRestore(DateTime utc, bool manual)
    { return !manual && PriorityOffDue(utc); }
    public bool OnDue(DateTime utc, DateTime completed)
    {
        DateTime local = Local(utc);
        return AutoOn && local.TimeOfDay >= OnTime && local.Date != completed.Date && !SuppressRestore(utc, false)
            && !(AutoOff && InWindow(local.TimeOfDay, OffStart, OffEnd));
    }
    static int Number(string value, int min, int max)
    { int n; if (!Int32.TryParse(value, out n) || n < min || n > max) throw new FormatException("须为 " + min + " 至 " + max + " 的整数"); return n; }
    static bool Boolean(string value)
    { if (value == "true") return true; if (value == "false") return false; throw new FormatException("须为 true 或 false"); }
    static TimeSpan Clock(string value)
    { DateTime d; if (!DateTime.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) throw new FormatException("须为 HH:mm，范围 00:00–23:59"); return d.TimeOfDay; }
    public static ScreenConfig Load(string path) { return Parse(File.ReadAllText(path)); }
    public static ScreenConfig Parse(string text)
    {
        var c = new ScreenConfig(); var seen = new HashSet<string>(); int line = 0;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            line++; string s = raw.Trim(); if (s.Length == 0 || s.StartsWith("#")) continue;
            try
            {
                if (raw.Length != raw.TrimStart().Length) throw new FormatException("仅支持无缩进的单层配置");
                int colon = s.IndexOf(':'); if (colon < 1) throw new FormatException("缺少 key: value");
                string key = s.Substring(0, colon).Trim(), value = s.Substring(colon + 1).Trim();
                if (!seen.Add(key)) throw new FormatException("重复配置项 " + key);
                if (value.StartsWith("\"") || value.StartsWith("'"))
                {
                    char quote = value[0]; int end = value.IndexOf(quote, 1);
                    if (end < 0) throw new FormatException("引号未闭合");
                    string tail = value.Substring(end + 1).Trim();
                    if (tail.Length > 0 && !tail.StartsWith("#")) throw new FormatException("引号后存在多余内容");
                    value = value.Substring(1, end - 1);
                    if (value.IndexOf('\\') >= 0) throw new FormatException("不支持转义字符");
                }
                else { int comment = value.IndexOf('#'); if (comment >= 0) value = value.Substring(0, comment).Trim(); }
                switch (key)
                {
                    case "battery_tof_initial_wait_seconds": c.BatteryTofInitialWaitSeconds = Number(value, 1, 600); break;
                    case "battery_tof_stable_seconds": c.BatteryTofStableSeconds = Number(value, 30, 3600); break;
                    case "battery_final_no_face_seconds": c.BatteryFinalNoFaceSeconds = Number(value, 1, 60); break;
                    case "battery_face_confirm_seconds": c.BatteryFaceConfirmSeconds = Number(value, 1, 60); break;
                    case "battery_tof_change_mm": c.BatteryTofChangeMm = Number(value, 100, 2000); break;
                    case "battery_tof_max_interval_ms": c.BatteryTofMaxIntervalMs = Number(value, 1000, 30000); break;
                    case "tof_dark_max_interval_ms": c.TofDarkMaxIntervalMs = Number(value, 1000, 30000); break;
                    case "tof_face_timeout_seconds": c.TofFaceTimeoutSeconds = Number(value, 5, 120); break;
                    case "tof_enabled": c.TofEnabled = Boolean(value); break;
                    case "tof_distance_mm": c.TofDistanceMm = Number(value, 100, 2000); break;
                    case "tof_confirm_samples": c.TofSamples = Number(value, 2, 20); break;
                    case "tof_interval_ms": c.TofIntervalMs = Number(value, 100, 1000); break;
                    case "ir_startup_grace_seconds": c.IrStartupGraceSeconds = Number(value, 0, 600); break;
                    case "ir_enabled": c.IrEnabled = Boolean(value); break;
                    case "ir_absence_seconds": c.IrAbsenceSeconds = Number(value, 1, 86400); break;
                    case "battery_away_enabled": c.BatteryAwayEnabled = Boolean(value); break;
                    case "battery_away_seconds": c.BatteryAwaySeconds = Number(value, 1, 86400); break;
                    case "autostart": c.Autostart = Boolean(value); break;
                    case "utc_offset_minutes": c.Offset = Number(value, -720, 840); break;
                    case "auto_off_enabled": c.AutoOff = Boolean(value); break;
                    case "auto_off_start": c.OffStart = Clock(value); break;
                    case "auto_off_end": c.OffEnd = Clock(value); break;
                    case "idle_seconds": c.IdleSeconds = Number(value, 1, 86400); break;
                    case "poll_seconds": c.PollSeconds = Number(value, 1, 30); break;
                    case "reset_idle_after_gap_seconds": c.GapSeconds = Number(value, 2, 3600); break;
                    case "auto_on_enabled": c.AutoOn = Boolean(value); break;
                    case "wake_on_real_input": c.WakeOnInput = Boolean(value); break;
                    case "auto_on_time": c.OnTime = Clock(value); break;
                    case "auto_on_retry_seconds": c.RetrySeconds = Number(value, 5, 3600); break;
                    case "no_auto_on_enabled": c.NoAutoOn = Boolean(value); break;
                    case "no_auto_on_start": c.QuietStart = Clock(value); break;
                    case "no_auto_on_end": c.QuietEnd = Clock(value); break;
                    case "toggle_hotkey": c.ToggleHotkey = value; break;
                    case "emergency_hotkey": c.EmergencyHotkey = value; break;
                    case "hotkey_debounce_seconds": c.DebounceSeconds = Number(value, 0, 60); break;
                    default: throw new FormatException("未知配置项 " + key);
                }
            }
            catch (Exception ex) { throw new FormatException("config.yaml 第 " + line + " 行：" + ex.Message); }
        }
        if (c.GapSeconds <= c.PollSeconds) throw new FormatException("reset_idle_after_gap_seconds 必须大于 poll_seconds");
        HotkeyBinding toggle = HotkeyBinding.Parse(c.ToggleHotkey, true), emergency = HotkeyBinding.Parse(c.EmergencyHotkey, false);
        if (toggle.Key == emergency.Key && toggle.Modifiers == emergency.Modifiers) throw new FormatException("切换键和应急恢复键不能相同");
        if (c.AutoOn && ((c.AutoOff && InWindow(c.OnTime, c.OffStart, c.OffEnd)) || (c.NoAutoOn && InWindow(c.OnTime, c.QuietStart, c.QuietEnd))))
            throw new FormatException("auto_on_time 不能位于自动熄屏或禁止自动亮屏时段内");
        return c;
    }
    public void ApplyAutostart()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        {
            if (Autostart) key.SetValue("ScreenOffHotkey", "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenHotkey.exe") + "\"", RegistryValueKind.String);
            else key.DeleteValue("ScreenOffHotkey", false);
        }
    }
}

public sealed class HotkeyBinding
{
    public uint Modifiers, Key;
    public static HotkeyBinding Parse(string text, bool allowEmpty)
    {
        var h = new HotkeyBinding();
        if (String.IsNullOrWhiteSpace(text)) { if (allowEmpty) return h; throw new FormatException("应急恢复热键不能为空"); }
        foreach (string raw in text.Split('+'))
        {
            string p = raw.Trim().ToUpperInvariant(); uint mod = p == "CTRL" ? 2u : p == "ALT" ? 1u : p == "SHIFT" ? 4u : p == "WIN" ? 8u : 0u;
            if (mod != 0) { if ((h.Modifiers & mod) != 0) throw new FormatException("重复热键修饰符：" + text); h.Modifiers |= mod; continue; }
            if (h.Key != 0) throw new FormatException("热键只能包含一个主键：" + text);
            int fn;
            if (p.StartsWith("F") && Int32.TryParse(p.Substring(1), out fn) && fn >= 1 && fn <= 24) h.Key = (uint)(0x70 + fn - 1);
            else if (p.Length == 1 && ((p[0] >= 'A' && p[0] <= 'Z') || (p[0] >= '0' && p[0] <= '9'))) h.Key = p[0];
            else throw new FormatException("热键主键支持 F1–F24、A–Z、0–9：" + text);
        }
        if (h.Key == 0 || h.Modifiers == 0) throw new FormatException("热键必须包含修饰符和主键：" + text);
        return h;
    }
}
