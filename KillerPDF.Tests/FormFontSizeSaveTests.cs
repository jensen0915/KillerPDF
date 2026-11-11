using System.IO;
using System.Linq;
using System.Text;
using KillerPDF.Services;
using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Filters;
using KillerPdf.Engine.Objects;
using Xunit;

namespace KillerPDF.Tests;

// Picked from upstream v1.9.1 (008a0544): a form field whose font size was changed with the size
// bar, but whose text was not edited, kept its old size on save. Upstream's test compares renders
// with PdfPageRenderer, which this engine does not have; this one looks for the rebuilt appearance.
public sealed class FormFontSizeSaveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FontSizeOnlyEditPreservesExistingValueAndChangesAppearance(bool editSibling)
    {
        string path = Path.Combine(Path.GetTempPath(), $"killerpdf-field-size-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, new PdfDocumentBuilder().AddBlankPage(612, 792)
                .AddTextField(0, "customer", 20, 20, 140, 24, "Original", defaultValue: "Default")
                .AddTextField(0, "sibling", 20, 70, 140, 24, "Keep")
                .Build());
            Assert.Equal(0, AppearancesAt20Points(PdfDocument.Open(File.ReadAllBytes(path))));

            PdfEngineIntegration.ApplyFormValues(path, new PdfEngineIntegration.FormEdits(
                editSibling ? new Dictionary<string, string> { ["sibling"] = "Changed" } : new(),
                new Dictionary<string, string>(), new Dictionary<string, IReadOnlyList<string>>(),
                new Dictionary<string, bool>(), new Dictionary<string, string>(),
                new Dictionary<string, double> { ["customer"] = 20 }));

            PdfDocument saved = PdfDocument.Open(File.ReadAllBytes(path));
            var fields = PdfFormWidgetReader.ReadPage(saved, 0).ToDictionary(field => field.FieldName);
            Assert.Equal("Original", fields["customer"].Value);
            Assert.Contains("20 Tf", fields["customer"].DefaultAppearance);
            Assert.Equal(editSibling ? "Changed" : "Keep", fields["sibling"].Value);
            Assert.True(AppearancesAt20Points(saved) > 0, "the field's appearance was not redrawn at 20 pt");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // Appearance streams drawing text at 20 pt (the /DA string is not a stream, so it does not count).
    private static int AppearancesAt20Points(PdfDocument document)
    {
        int count = 0;
        foreach (int number in document.CrossReferences.Keys)
            if (document.Resolve(number) is PdfStream stream)
                try
                {
                    if (Encoding.Latin1.GetString(PdfStreamDecoder.Decode(stream, document.Resolve)).Contains(" 20 Tf"))
                        count++;
                }
                catch { }
        return count;
    }
}
