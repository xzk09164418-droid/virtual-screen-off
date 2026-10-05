using System;
using System.Diagnostics;

// Pure rules shared by runtime and regression tests. Unknown frames never mean absence.
public sealed class PresenceHistory
{
    readonly object gate = new object();
    double lastFrame = -1, absentSince = -1, presentSince = -1;
    public void Reset() { lock(gate) { lastFrame = absentSince = presentSince = -1; } }
    public void Observe(double now, bool face)
    {
        lock(gate)
        {
            if (lastFrame < 0 || now - lastFrame > 3) absentSince = presentSince = -1;
            lastFrame = now;
            if(face) { absentSince = -1; if(presentSince < 0) presentSince = now; }
            else { presentSince = -1; if(absentSince < 0) absentSince = now; }
        }
    }
    public double Absent(double now) { lock(gate) return lastFrame >= 0 && now-lastFrame <= 3 && absentSince >= 0 ? now-absentSince : -1; }
    public double Present(double now) { lock(gate) return lastFrame >= 0 && now-lastFrame <= 3 && presentSince >= 0 ? now-presentSince : -1; }
    public static double Now { get { return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency; } }
}
public static class PresencePolicy
{
    public static bool NightOff(ScreenConfig c, DateTime utc, bool battery, bool inputHealthy, double idle, double absent)
    { return !battery && inputHealthy && c.OffDue(utc,idle) && (!c.IrEnabled || absent >= c.IrAbsenceSeconds); }
    public static bool BatteryOff(ScreenConfig c, bool battery, bool keyboardHealthy, double absent)
    { return battery && keyboardHealthy && c.BatteryAwayEnabled && c.IrEnabled && absent >= c.BatteryAwaySeconds; }
    public static bool BatterySensorOff(ScreenConfig c, bool? battery, bool inputHealthy, bool dark, bool timedOut)
    { return battery==true && inputHealthy && !dark && timedOut && c.BatteryAwayEnabled && c.IrEnabled && c.TofEnabled; }
    public static bool NeedCamera(ScreenConfig c, DateTime utc, bool battery, bool away)
    { return !c.PriorityOffDue(utc) && c.IrEnabled && !away && (battery ? c.BatteryAwayEnabled : c.AutoOff && ScreenConfig.InWindow(c.Local(utc).TimeOfDay,c.OffStart,c.OffEnd)); }
    public static bool NeedDetection(ScreenConfig c, DateTime utc, bool? battery, bool dark)
    {
        if(c.PriorityOffDue(utc))return false;
        if(!battery.HasValue || (dark && battery.Value))return false;
        if(dark && c.TofEnabled)return c.IrEnabled && !c.SuppressRestore(utc,false);
        return NeedCamera(c,utc,battery.Value,false);
    }
    public static bool PowerCommandAllowed(int token, int current, bool on, bool away, bool keyboardPending)
    { return token == current && (on || away && !keyboardPending); }
    public static bool SystemDisplayWake(bool away, bool offIssued, int state)
    { return away && offIssued && state == 1; }
    public static bool IrWarmupReady(double now, double readyAt, int seconds)
    { return readyAt >= 0 && now - readyAt >= seconds; }
    public static bool BatteryWake(bool away, bool real, bool keyDown) { return away && real && keyDown; }
}
