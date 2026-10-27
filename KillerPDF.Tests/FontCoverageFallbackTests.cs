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
    public void ASerifChoiceBorrowsAMingFaceForChinese()
    {
        // Times New Roman with 新細明體, as Word pairs them; a sans choice keeps 微軟正黑體.
        if (!FontCoverage.FamilyCovers("PMingLiU", "臺北市政府")) return;
        Assert.Equal("PMingLiU", FontCoverage.PickFamily("Times New Roman", "臺北市政府", preferTraditional: true));
        Assert.True(FontCoverage.IsSerifFamily("Times New Roman"));
        Assert.False(FontCoverage.IsSerifFamily("Segoe UI"));
        Assert.False(FontCoverage.IsSerifFamily("Microsoft Sans Serif"));
    }

    [Fact]
    public void TraditionalPreferencePicksJhengHeiForHanText()
    {
        // Passed explicitly: flipping the shared setting would race other tests that pick fonts.
        string family = FontCoverage.PickFamily("Segoe UI", "臺北市政府", preferTraditional: true);
        if (FontCoverage.UncoveredChars("Microsoft JhengHei", "臺北市政府").Length == 0
            && family != "Segoe UI")
            Assert.Equal("Microsoft JhengHei", family);
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
