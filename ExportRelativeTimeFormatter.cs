using System;
using System.Globalization;

namespace ImpinjR700
{
    public static class ExportRelativeTimeFormatter
    {
        public static string FormatSeconds(DateTime timestamp, DateTime baseline)
        {
            var seconds = Math.Max(0, (timestamp - baseline).TotalSeconds);
            return seconds.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
