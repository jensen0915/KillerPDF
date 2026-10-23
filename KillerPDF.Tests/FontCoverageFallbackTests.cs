using System.Globalization;
using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

// Fork: symbol fallback (check marks) and the Traditional Chinese preference.
public sealed class FontCoverageFallbackTests
{
    [Fact]
    public void CheckMarkFallsBackToAFaceThatHasIt()
    {
        string family = FontCoverage.PickFamily("Segoe UI", "✓");
        // Every Windows install ships Segoe UI Symbol; the point is that the pick can carry U+2713.
        Assert.Equal("", FontCoverage.UncoveredChars(family, "✓"));
        Assert.False(FontCoverage.WillLoseGlyphs("Segoe UI", "✓"));
    }

    [Fact]
    public void OrdinaryLatinTextKeepsThePreferredFace()
        => Assert.Equal("Segoe UI", FontCoverage.PickFamily("Segoe UI", "115/10/03"));

    [Fact]
    public void TraditionalPreferencePicksJhengHeiForHanText()
    {
        bool saved = FontCoverage.PreferTraditionalChinese;
        try
        {
            FontCoverage.PreferTraditionalChinese = true;
            string family = FontCoverage.PickFamily("Segoe UI", "臺北市政府");
            if (FontCoverage.UncoveredChars("Microsoft JhengHei", "臺北市政府").Length == 0
                && family != "Segoe UI")
                Assert.Equal("Microsoft JhengHei", family);
        }
        finally { FontCoverage.PreferTraditionalChinese = saved; }
    }

    [Theory]
    [InlineData("zh-TW", true)]
    [InlineData("zh-HK", true)]
    [InlineData("zh-Hant", true)]
    [InlineData("zh-CN", false)]
    [InlineData("ja-JP", false)]
    [InlineData("en-US", false)]
    public void TraditionalChineseCultures(string name, bool expected)
        => Assert.Equal(expected, FontCoverage.IsTraditionalChineseCulture(new CultureInfo(name)));
}
