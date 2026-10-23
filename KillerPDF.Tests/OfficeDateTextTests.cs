using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

public sealed class OfficeDateTextTests
{
    [Fact]
    public void GregorianDateUsesSlashes()
    {
        Assert.True(OfficeDateText.TryFormat(new DateTime(2026, 10, 3), rocYear: false, out string text));
        Assert.Equal("2026/10/03", text);
    }

    [Theory]
    [InlineData(2026, 10, 3, "115/10/03")]
    [InlineData(2011, 1, 1, "100/01/01")]
    [InlineData(1912, 1, 1, "001/01/01")]
    public void RocYearIsGregorianMinus1911PaddedToThreeDigits(int year, int month, int day, string expected)
    {
        Assert.True(OfficeDateText.TryFormat(new DateTime(year, month, day), rocYear: true, out string text));
        Assert.Equal(expected, text);
    }

    [Fact]
    public void DatesBeforeTheRocCalendarAreRejected()
    {
        Assert.False(OfficeDateText.TryFormat(new DateTime(1911, 12, 31), rocYear: true, out string text));
        Assert.Equal("", text);
    }
}
