using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

public sealed class PrintPageRangeTests
{
    [Theory]
    [InlineData("1-3,5")]
    [InlineData("１－３，５")]
    [InlineData("1-3、5")]
    [InlineData(" 1 - 3, 5, 3 ")]
    public void AcceptsHalfwidthFullwidthAndIdeographicCommaInput(string value)
        => Assert.Equal([0, 1, 2, 4], PrintPageRange.Parse(value, 5));

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("1,abc")]
    [InlineData("3-1")]
    [InlineData("1-999999999")]
    [InlineData("99999999999")]
    [InlineData("1,")]
    [InlineData("1--3")]
    [InlineData("-1")]
    public void InvalidRangeNeverFallsBackToAllPages(string value)
        => Assert.Empty(PrintPageRange.Parse(value, 5));

    [Fact]
    public void BlankRangeMeansAllPages()
        => Assert.Equal(Enumerable.Range(0, 5), PrintPageRange.Parse(" ", 5));

    [Fact]
    public void FullwidthBlankRangeMeansAllPages()
        => Assert.Equal(Enumerable.Range(0, 3), PrintPageRange.Parse("　", 3));
}
