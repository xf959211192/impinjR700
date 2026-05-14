using System;
using System.Collections.Generic;
using System.Linq;

namespace ImpinjR700
{
    internal static class PlotSplitLayout
    {
        public const int SingleSubplotHeight = 180;

        public static string[] GetOrderedEpcs(IEnumerable<string> epcs)
        {
            return epcs
                .Where(epc => !string.IsNullOrWhiteSpace(epc))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(epc => epc, StringComparer.Ordinal)
                .ToArray();
        }

        public static int GetSubplotHeight(int epcCount)
        {
            return Math.Max(1, epcCount) * SingleSubplotHeight;
        }
    }
}
