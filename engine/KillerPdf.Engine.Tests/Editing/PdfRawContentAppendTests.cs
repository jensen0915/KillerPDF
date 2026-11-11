using System.Text;
using System.Text.RegularExpressions;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Editing;
using KillerPdf.Engine.Filters;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPdf.Engine.Tests.Editing;

public sealed class PdfRawContentAppendTests
{
    // Fork: upstream's version reads the page with PdfPageContentReader and renders it with
    // PdfPageRenderer, which this engine does not have. This one counts the fill operators in the
    // written content instead: a glued "f0" or a comment running into the next program loses one.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RawProgramsRemainSeparateAfterRepeatedAppendAndReopen(bool setFirst, bool trailingComment)
    {
        PdfDocument blank = PdfDocument.Open(new PdfDocumentBuilder().AddBlankPage(40, 20).Build());
        var editor = new PdfIncrementalPageEditor(blank);
        byte[] red = Encoding.ASCII.GetBytes("1 0 0 rg 0 0 20 20 re f"
            + (trailingComment ? "% first program" : ""));
        if (setFirst) editor.SetPageContent(0, red);
        else editor.AppendPageContent(0, red);
        editor.AppendPageContent(0, "0 0 1 rg 20 0 20 20 re f"u8.ToArray());
        PdfDocument reopened = PdfDocument.Open(editor.Build());

        string program = reopened.CrossReferences.Keys
            .Select(number => reopened.Resolve(number)).OfType<PdfStream>()
            .Select(stream => Encoding.Latin1.GetString(PdfStreamDecoder.Decode(stream, reopened.Resolve)))
            .Single(text => text.Contains("1 0 0 rg") && text.Contains("0 0 1 rg"));
        string code = Regex.Replace(program, "%[^\r\n]*", "");
        Assert.Equal(2, Regex.Matches(code, @"(?<!\S)f(?!\S)").Count);
    }
}
