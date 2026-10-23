using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KillerPDF.Services;

/// <summary>
/// Parses the print dialog's Pages box ("1-3,5") into sorted 0-based page indices. Fullwidth
/// digits and punctuation (１－３，５) and the ideographic comma (1-3、5) are accepted, so a
/// Chinese IME does not have to be switched off first. Blank means every page. Anything else
/// that is not an exact, in-range list - a reversed or out-of-range span, a missing end, a stray
/// comma - returns an empty list so the caller blocks printing instead of guessing.
/// </summary>
internal static class PrintPageRange
{
    internal static List<int> Parse(string? text, int count)
    {
        text = (text ?? "").Normalize(NormalizationForm.FormKC).Trim().Replace('、', ',');
        if (text.Length == 0) return [.. Enumerable.Range(0, Math.Max(0, count))];

        var pages = new SortedSet<int>();
        foreach (string part in text.Split(','))
        {
            string[] ends = part.Trim().Split('-');
            if (ends.Length > 2 || !TryPage(ends[0], out int first)) return [];
            int last = first;
            if (ends.Length == 2 && !TryPage(ends[1], out last)) return [];
            if (first < 1 || last < first || last > count) return [];
            for (int page = first - 1; page < last; page++) pages.Add(page);
        }
        return [.. pages];
    }

    private static bool TryPage(string text, out int page) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out page);
}
