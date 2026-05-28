using System;

namespace AutoExporter.Background
{
    /// <summary>
    /// Pure time-range math, isolated from MIP types so it can be unit-tested
    /// without VideoOS.Platform being loaded in the test process.
    /// </summary>
    internal static class TimeRange
    {
        public static DateTime Subtract(DateTime end, int value, string unit)
        {
            if (value <= 0) value = 1;
            switch ((unit ?? "Days").ToLowerInvariant())
            {
                case "minutes": return end.AddMinutes(-value);
                case "hours":   return end.AddHours(-value);
                case "months":  return end.AddDays(-value * 30);
                default:        return end.AddDays(-value);
            }
        }
    }
}
