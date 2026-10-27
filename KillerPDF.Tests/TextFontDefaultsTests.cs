using System.Linq;
using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

// Fork: the typeface new text starts in, and the font list's order and names.
public sealed class TextFontDefaultsTests
{
    [Fact]
    public void TraditionalChineseStartsInPMingLiU()
        => Assert.Equal("PMingLiU", TextFontDefaults.For(true, _ => true));

    [Fact]
    public void TraditionalChineseWithoutMingFacesFallsBackToJhengHei()
        => Assert.Equal("Microsoft JhengHei", TextFontDefaults.For(true, f => f == "Microsoft JhengHei"));

    [Fact]
    public void OtherLanguagesKeepSegoeUi()
        => Assert.Equal("Segoe UI", TextFontDefaults.For(false, _ => true));

    [Fact]
    public void ASavedChoiceWinsWhileItIsInstalled()
    {
        Assert.Equal("DFKai-SB", TextFontDefaults.Initial("DFKai-SB", true, _ => true));
        Assert.Equal("PMingLiU", TextFontDefaults.Initial("Removed Font", true, f => f != "Removed Font"));
        Assert.Equal("PMingLiU", TextFontDefaults.Initial(null, true, _ => true));
    }

    [Fact]
    public void TraditionalChinesePinsTheTaiwanFacesFirst()
    {
        var choices = TextFontDefaults.Choices(["Arial", "Times New Roman", "DFKai-SB", "PMingLiU", "Calibri"], null, true);
        Assert.Equal(["PMingLiU", "DFKai-SB", "Times New Roman", "Arial", "Calibri"], choices.Select(c => c.Family));

        var plain = TextFontDefaults.Choices(["Times New Roman", "Arial", "PMingLiU"], null, false);
        Assert.Equal(["Arial", "PMingLiU", "Times New Roman"], plain.Select(c => c.Family));
    }

    [Fact]
    public void FontsAreListedByTheirChineseNames()
    {
        if (!TextFontDefaults.IsInstalled("PMingLiU")) return;
        var choice = TextFontDefaults.Choices(["PMingLiU"], "zh-tw", true).Single();
        Assert.Equal("PMingLiU", choice.Family);
        Assert.Equal("新細明體", choice.Label);
        Assert.Equal("新細明體", choice.ToString());
    }
}
