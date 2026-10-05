using System;

public static class ScreenSchedule
{
    public static bool SuppressAutomaticRestore(DateTime utc, bool manual)
    {
        return !manual && ScreenConfig.Current.SuppressRestore(utc, false);
    }
}
