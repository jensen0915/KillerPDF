using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KillerPDF.Services
{
    internal static class PrintPageRange
    {
        public static List<int> Parse(string? text, int count)
        {
            text = (text ?? "").Normalize(NormalizationForm.FormKC).Trim().Replace('、', ',');
            if (text.Length == 0) return Enumerable.Range(0, Math.Max(0, count)).ToList();
            var pages = new SortedSet<int>();
            foreach (string part in text.Split(','))
            {
                var ends = part.Trim().Split('-');
                if (ends.Length > 2 || !int.TryParse(ends[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int first)) return new();
                int last = first;
                if (ends.Length == 2 && !int.TryParse(ends[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out last)) return new();
                if (first < 1 || last < first || last > count) return new();
                for (int page = first - 1; page < last; page++) pages.Add(page);
            }
            return pages.ToList();
        }
    }
}
