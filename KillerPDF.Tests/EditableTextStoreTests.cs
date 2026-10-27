using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using KillerPDF.Services;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Editing;
using KillerPdf.Engine.Filters;
using KillerPdf.Engine.Objects;
using EngineDocument = KillerPdf.Engine.Documents.PdfDocument;
using Xunit;

namespace KillerPDF.Tests;

// Fork: text placed in KillerPDF stays editable after the file is saved, closed and reopened.
public sealed class EditableTextStoreTests
{
    private static readonly Dictionary<int, (int w, int h)> Dims = new() { [0] = (612, 792) };   // 1 canvas unit = 1 pt

    private static List<PageAnnotation> Placed()
    {
        var plain = new TextAnnotation
        {
            PageIndex = 0, Position = new Point(40, 60), Content = "Hello 123", FontName = "Segoe UI",
            FontSize = 14, LetterSpacing = 1.5, Bold = true, Width = 120, Height = 22
        };
        plain.SetColor(Color.FromArgb(255, 200, 30, 40));
        var vertical = new TextAnnotation
        {
            PageIndex = 0, Position = new Point(400, 100), Content = "AB\nC", FontName = "Segoe UI",
            FontSize = 18, Vertical = true, Width = 50, Height = 50
        };
        var check = new TextAnnotation
        {
            PageIndex = 0, Position = new Point(80, 200), Content = "✓", FontName = "Segoe UI",
            FontSize = 16, Width = 20, Height = 24
        };
        var ink = new InkAnnotation { PageIndex = 0, Points = [new Point(10, 10), new Point(100, 30)] };
        return [ink, plain, vertical, check];
    }

    private static string Burned(List<PageAnnotation> annotations, bool forRasterization = false, byte[]? source = null)
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-editable-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, source ?? new PdfDocumentBuilder().AddBlankPage(612, 792).Build());
        PdfEngineBurn.Burn(path, new Dictionary<int, List<PageAnnotation>> { [0] = annotations }, Dims,
            forRasterization: forRasterization);
        return path;
    }

    // Text-showing operators in every object the latest revision reaches.
    private static int TextShows(byte[] pdf)
    {
        var document = EngineDocument.Open(pdf);
        int count = 0;
        foreach (int number in document.CrossReferences.Keys)
            if (document.Resolve(number) is PdfStream stream)
                try
                {
                    string text = Encoding.Latin1.GetString(PdfStreamDecoder.Decode(stream, document.Resolve));
                    count += Regex.Matches(text, @"\bT[jJ]\b").Count;
                }
                catch { }
        return count;
    }

    private static List<PageAnnotation> AsAnnotations(IEnumerable<EditableTextStore.Box> boxes)
        => [.. boxes.Select(b =>
        {
            var t = new TextAnnotation
            {
                PageIndex = 0, Position = new Point(b.X, b.Y), Content = b.Text, FontName = b.Font,
                FontSize = b.Size, LetterSpacing = b.LetterSpacing, Bold = b.Bold, Italic = b.Italic,
                Strike = b.Strike, Underline = b.Underline, Vertical = b.Vertical, Width = b.Width, Height = b.Height
            };
            t.SetColor(Color.FromArgb((byte)(b.Color >> 24), (byte)(b.Color >> 16), (byte)(b.Color >> 8), (byte)b.Color));
            return (PageAnnotation)t;
        })];

    [Fact]
    public void SavedTextComesBackAsTheSameBoxes()
    {
        string path = Burned(Placed());
        try
        {
            byte[] saved = File.ReadAllBytes(path);
            Assert.True(EditableTextStore.MayContain(saved));
            var restored = EditableTextStore.Extract(saved);

            Assert.NotNull(restored);
            Assert.Equal(0, restored!.KeptFixedPages);
            var boxes = restored.Pages[0];
            Assert.Equal(["Hello 123", "AB\nC", "✓"], boxes.Select(b => b.Text));
            var plain = boxes[0];
            Assert.Equal((40.0, 60.0, 120.0, 22.0), (plain.X, plain.Y, plain.Width, plain.Height));
            Assert.Equal((14.0, 1.5, true, false), (plain.Size, plain.LetterSpacing, plain.Bold, plain.Vertical));
            Assert.Equal(0xFFC81E28u, plain.Color);
            Assert.True(boxes[1].Vertical);

            // The working copy draws the ink but no longer the text, which the editor draws instead.
            Assert.True(TextShows(saved) > 0);
            Assert.Equal(0, TextShows(restored.CleanPdf));
            Assert.Null(EditableTextStore.Extract(restored.CleanPdf));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void APageChangedElsewhereKeepsItsTextAsPageContent()
    {
        string path = Burned(Placed());
        try
        {
            var document = EngineDocument.Open(File.ReadAllBytes(path));
            byte[] edited = new PdfIncrementalPageEditor(document).AppendPageContent(0, "q Q\n"u8.ToArray()).Build();

            var restored = EditableTextStore.Extract(edited);

            Assert.NotNull(restored);
            Assert.Empty(restored!.Pages);
            Assert.Equal(1, restored.KeptFixedPages);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SavingAgainAfterReopeningDoesNotDuplicateTheText()
    {
        string first = Burned(Placed());
        string? second = null;
        try
        {
            byte[] once = File.ReadAllBytes(first);
            var restored = EditableTextStore.Extract(once)!;
            var annotations = AsAnnotations(restored.Pages[0]);
            second = Burned(annotations, source: restored.CleanPdf);
            byte[] twice = File.ReadAllBytes(second);

            int textOnce = TextShows(once) - TextShows(restored.CleanPdf);
            Assert.Equal(textOnce, TextShows(twice));
            var again = EditableTextStore.Extract(twice);
            Assert.NotNull(again);
            Assert.Equal(restored.Pages[0], again!.Pages[0]);
        }
        finally
        {
            File.Delete(first);
            if (second is not null) File.Delete(second);
        }
    }

    [Fact]
    public void EachEmbeddedFontsEncodingCMapHasItsOwnName()
    {
        // Adobe Reader keeps embedded CMaps by name: when the check mark's map and the text's map
        // shared one name, whichever came first served both and the other font's text became boxes.
        string path = Burned(Placed());
        try
        {
            var document = EngineDocument.Open(File.ReadAllBytes(path));
            var maps = new Dictionary<string, string>();
            foreach (int number in document.CrossReferences.Keys)
            {
                if (document.Resolve(number) is not PdfStream stream) continue;
                string text;
                try { text = Encoding.Latin1.GetString(PdfStreamDecoder.Decode(stream, document.Resolve)); }
                catch { continue; }
                var name = Regex.Match(text, @"/CMapName /(KillerPDF-Identity\S*) def");
                if (!name.Success) continue;
                string body = text[text.IndexOf("endcodespacerange", StringComparison.Ordinal)..];
                if (maps.TryGetValue(name.Groups[1].Value, out string? seen)) Assert.Equal(seen, body);
                else maps[name.Groups[1].Value] = body;
            }
            // Segoe UI for the Latin text and another face for the check mark: two different maps.
            Assert.True(maps.Count >= 2, $"expected at least two encoding CMaps, found {maps.Count}");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnImageCopyCarriesNoEditableText()
    {
        string path = Burned(Placed(), forRasterization: true);
        try { Assert.False(EditableTextStore.MayContain(File.ReadAllBytes(path))); }
        finally { File.Delete(path); }
    }
}
