using System;
using System.IO;
using System.Text;

namespace KillerPDF
{
    internal static class PdfSourceCompatibility
    {
        private static readonly string[] BrowserFragileSourceMarkers =
        [
            "Microsoft Print To PDF",
            "Microsoft: Print To PDF",
            "Microsoft Print to PDF",
            "Skia/PDF",
            "HeadlessChrome",
            "StreamServe",
            "iText",
            "GTS_PDFXVersion",
            "PDF/X"
        ];

        public static bool ShouldNormalizeBeforeEditing(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                int headSize = (int)Math.Min(64 * 1024, fs.Length);
                byte[] head = new byte[headSize];
                _ = fs.Read(head, 0, head.Length);

                int tailSize = (int)Math.Min(64 * 1024, fs.Length);
                byte[] tail = new byte[tailSize];
                fs.Seek(-tailSize, SeekOrigin.End);
                _ = fs.Read(tail, 0, tail.Length);

                return ContainsBrowserFragileSourceMarker(head) || ContainsBrowserFragileSourceMarker(tail);
            }
            catch { return false; }
        }

        internal static bool ContainsBrowserFragileSourceMarker(byte[] bytes)
        {
            string text = Encoding.GetEncoding(1252).GetString(bytes);
            foreach (var marker in BrowserFragileSourceMarkers)
                if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }
}
