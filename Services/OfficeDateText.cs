using System;
using System.Globalization;

namespace KillerPDF.Services;

/// <summary>
/// Date text for filling Taiwanese office forms: Gregorian yyyy/MM/dd, or the Republic of China
/// (Minguo) year that government and school forms ask for - 2026/10/03 is 115/10/03. The ROC
/// year is zero-padded to three digits, the width those forms print.
/// </summary>
internal static class OfficeDateText
{
    internal const int RocEpochYear = 1911;

    internal static bool TryFormat(DateTime date, bool rocYear, out string text)
    {
        if (!rocYear)
        {
            text = date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            return true;
        }
        if (date.Year <= RocEpochYear)
        {
            // The ROC calendar starts in 1912; earlier dates have no positive ROC year.
            text = "";
            return false;
        }
        text = string.Format(CultureInfo.InvariantCulture, "{0:000}/{1:00}/{2:00}",
            date.Year - RocEpochYear, date.Month, date.Day);
        return true;
    }
}
