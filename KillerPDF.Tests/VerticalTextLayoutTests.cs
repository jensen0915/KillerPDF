using System.Linq;
using System.Windows;
using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

// Fork: vertical text (直書) - the cells shared by the overlay, the edit preview and the saved PDF.
public sealed class VerticalTextLayoutTests
{
    private static readonly System.Func<string, bool> AllFaces = _ => true;
    private static readonly System.Func<string, bool> NoForms = _ => false;

    [Fact]
    public void CharactersRunDownAndEnterStartsTheNextColumnToTheLeft()
    {
        var layout = VerticalTextLayout.Layout("臺北市\n信義區", 10, AllFaces);

        Assert.Equal(2, layout.Columns);
        Assert.Equal(3, layout.Rows);
        Assert.Equal(["臺", "北", "市", "信", "義", "區"], layout.Cells.Select(c => c.Text));
        Assert.Equal([(0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2)], layout.Cells.Select(c => (c.Column, c.Row)));
    }

    [Fact]
    public void AFullColumnContinuesInTheNextOne()
    {
        var layout = VerticalTextLayout.Layout("一二三四五", 2, AllFaces);

        Assert.Equal(3, layout.Columns);
        Assert.Equal(2, layout.Rows);
        Assert.Equal((2, 0), (layout.Cells[4].Column, layout.Cells[4].Row));
    }

    [Fact]
    public void BracketsUseVerticalFormsWhenTheFaceHasThem()
    {
        var cells = VerticalTextLayout.Layout("「好」", 10, AllFaces).Cells;

        Assert.Equal(["﹁", "好", "﹂"], cells.Select(c => c.Text));
        Assert.All(cells, c => Assert.False(c.Rotated));
    }

    [Fact]
    public void BracketsTurnWhenTheFaceHasNoVerticalForm_PunctuationStaysUpright()
    {
        var cells = VerticalTextLayout.Layout("「好，」", 10, NoForms).Cells;

        Assert.Equal(["「", "好", "，", "」"], cells.Select(c => c.Text));
        Assert.Equal([true, false, false, true], cells.Select(c => c.Rotated));
    }

    [Fact]
    public void TheCaretSlotFollowsWrapsAndLineBreaks()
    {
        var full = VerticalTextLayout.Layout("AB", 2, AllFaces, caretIndex: 2);
        Assert.Equal((1, 0), (full.CaretColumn, full.CaretRow));

        var afterEnter = VerticalTextLayout.Layout("A\n", 5, AllFaces, caretIndex: 2);
        Assert.Equal((1, 0), (afterEnter.CaretColumn, afterEnter.CaretRow));
        Assert.Equal(2, afterEnter.Columns);

        var middle = VerticalTextLayout.Layout("ABC", 5, AllFaces, caretIndex: 1);
        Assert.Equal((0, 1), (middle.CaretColumn, middle.CaretRow));
    }

    [Theory]
    [InlineData(12.0, 0.0)]
    [InlineData(31.9, 0.0)]
    [InlineData(17.3, 2.7)]
    [InlineData(9.6, -1.1)]
    public void ABoxSizedForSomeRowsHoldsExactlyThoseRows(double size, double spacing)
    {
        var m = new VerticalTextLayout.Metrics(size, size + spacing, size * 1.2, size * .38);
        for (int rows = 1; rows <= 40; rows++)
            Assert.Equal(rows, VerticalTextLayout.MaxRowsFor(VerticalTextLayout.BoxSize(1, rows, m).Height, m));
    }

    [Fact]
    public void GlyphsAreCentredInTheirCells()
    {
        var m = new VerticalTextLayout.Metrics(20, 20, 24, 7.6);
        var upright = new VerticalTextLayout.Cell(0, 1, "國", 1, 2, false);
        var turned = upright with { Rotated = true };
        // Column 1 is centred at 100 - 2 - 10 - 24 = 64; row 2's cell is centred at 2 + 40 + 10 = 52.
        Assert.Equal(new Point(64 - 9, 52 + 7.6), VerticalTextLayout.GlyphOrigin(upright, 100, 0, m, 18));
        Assert.Equal(new Point(64 - 7.6, 52 - 9), VerticalTextLayout.GlyphOrigin(turned, 100, 0, m, 18));
    }

    [Fact]
    public void AColumnPastANarrowedBoxIsHidden()
    {
        var m = new VerticalTextLayout.Metrics(20, 20, 24, 7.6);
        double width = VerticalTextLayout.BoxSize(2, 1, m).Width;
        Assert.True(VerticalTextLayout.ColumnFits(1, 0, width, m));
        Assert.False(VerticalTextLayout.ColumnFits(2, 0, width, m));
    }

    [Fact]
    public void AnIdeographSitsAroundTheMiddleOfItsEmSquare()
    {
        string family = FontCoverage.PickFamily("Segoe UI", "國");
        if (!FontCoverage.FamilyCovers(family, "國")) return;   // no Han face on this machine
        double ratio = VerticalTextLayout.EmCenterRatio(family, false, false);
        Assert.InRange(ratio, .25, .5);
    }
}
