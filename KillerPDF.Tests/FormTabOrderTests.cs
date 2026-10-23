using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

public sealed class FormTabOrderTests
{
    private static List<string> Order(params (string Name, double X, double Y, double W, double H)[] fields)
        => FormTabOrder.Order(fields, f => (f.X, f.Y, f.W, f.H)).Select(f => f.Name).ToList();

    [Fact]
    public void ReadsRowsTopToBottomAndLeftToRight()
        => Assert.Equal(["name", "date", "address"], Order(
            ("address", 50, 200, 300, 24),
            ("date", 300, 100, 120, 24),
            ("name", 50, 100, 200, 24)));

    [Fact]
    public void SmallCheckBoxBesideATallFieldStaysOnItsRow()
        // The check box's top is lower than the tall field's, but it sits on the same row to its left.
        => Assert.Equal(["agree", "notes", "next"], Order(
            ("notes", 100, 100, 300, 60),
            ("agree", 50, 120, 16, 16),
            ("next", 50, 200, 200, 24)));

    [Fact]
    public void SeparateRowsAreNotMerged()
        => Assert.Equal(["first", "second"], Order(
            ("second", 10, 140, 200, 24),
            ("first", 300, 100, 200, 24)));
}
