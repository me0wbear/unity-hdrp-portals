using System;
using System.Globalization;

namespace Portals.Lab.Validation
{
    public static class PortalPerformanceMetrics
    {
        public static double? Percentile(double[] values, double fraction)
        {
            if (values == null || values.Length == 0 || !PortalCheckPolicy.Finite(fraction) || fraction < 0 || fraction > 1) return null;
            double[] sorted = (double[])values.Clone();
            foreach (double value in sorted) if (!PortalCheckPolicy.Finite(value) || value < 0) return null;
            Array.Sort(sorted);
            // Сохраняем статистику архива: upper order statistic, без интерполяции.
            return sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * fraction))];
        }

        public static string Format(double? value) => value.HasValue && PortalCheckPolicy.Finite(value.Value)
            ? value.Value.ToString("F4", CultureInfo.InvariantCulture) : "null";
        public static double NanosecondsToMilliseconds(long nanoseconds) => nanoseconds / 1000000.0;
        public static bool IsNewTimestamp(ulong timestamp, ulong previous) => timestamp != 0 && timestamp != previous;
        public static double? Ratio(double? numerator, double? denominator) => numerator.HasValue && denominator.HasValue
            && PortalCheckPolicy.Finite(numerator.Value) && PortalCheckPolicy.Finite(denominator.Value)
            && numerator.Value >= 0 && denominator.Value > 0 ? numerator.Value / denominator.Value : (double?)null;
    }
}
