using System.Text;
using Xunit;

namespace KillerPDF.Tests
{
    public sealed class PdfSourceCompatibilityTests
    {
        [Theory]
        [InlineData("/Producer (Microsoft: Print To PDF)")]
        [InlineData("/Producer (Skia/PDF m137)")]
        [InlineData("/Producer (StreamServe Communication Server 11.0.0)")]
        [InlineData("/Producer (modified using iText 7.1.4)")]
        [InlineData("/GTS_PDFXVersion (PDF/X-3:2003)")]
        public void ContainsBrowserFragileSourceMarkerDetectsKnownSources(string text)
        {
            Assert.True(PdfSourceCompatibility.ContainsBrowserFragileSourceMarker(Encoding.ASCII.GetBytes(text)));
        }

        [Fact]
        public void ContainsBrowserFragileSourceMarkerIgnoresOrdinaryPdfMetadata()
        {
            Assert.False(PdfSourceCompatibility.ContainsBrowserFragileSourceMarker(Encoding.ASCII.GetBytes("/Producer (Acrobat Distiller)")));
        }
    }
}
