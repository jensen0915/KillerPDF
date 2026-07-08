using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerPDF
{
    internal static class TextAnnotationRasterizer
    {
        private const double Scale = 3.0;

        public static bool ContainsCjk(string? text)
        {
            if (text is null || text.Length == 0) return false;
            string s = text;

            for (int i = 0; i < s.Length; i++)
            {
                int value = char.ConvertToUtf32(s, i);
                if (char.IsHighSurrogate(s[i])) i++;

                if ((value >= 0x4E00 && value <= 0x9FFF) ||   // CJK Unified Ideographs
                    (value >= 0x3400 && value <= 0x4DBF) ||   // CJK Extension A
                    (value >= 0x20000 && value <= 0x2EBEF) || // CJK Extensions B-F
                    (value >= 0xF900 && value <= 0xFAFF) ||   // CJK Compatibility Ideographs
                    (value >= 0x3040 && value <= 0x30FF) ||   // Hiragana + Katakana
                    (value >= 0xAC00 && value <= 0xD7AF) ||   // Hangul
                    (value >= 0x3000 && value <= 0x303F) ||   // CJK punctuation
                    (value >= 0xFF00 && value <= 0xFFEF))     // Full-width forms
                    return true;
            }

            return false;
        }

        public static byte[] RenderToPng(TextAnnotation annotation)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return RenderToPngOnSta(annotation);

            byte[]? png = null;
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { png = RenderToPngOnSta(annotation); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error is not null) throw error;
            return png ?? [];
        }

        private static byte[] RenderToPngOnSta(TextAnnotation ta)
        {
            double width = Math.Max(1, ta.Width);
            double height = Math.Max(1, ta.Height);
            var text = new TextBlock
            {
                Text = ta.Content,
                Foreground = new SolidColorBrush(ta.GetColor()),
                FontFamily = ResolveFontFamily(ta.FontName),
                FontWeight = ta.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = ta.Italic ? FontStyles.Italic : FontStyles.Normal,
                TextDecorations = BuildDecorations(ta.Underline, ta.Strike),
                FontSize = Math.Max(1, ta.FontSize),
                Padding = new Thickness(2),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(text, TextRenderingMode.Grayscale);

            var box = new Border
            {
                Width = width,
                Height = height,
                Background = ta.HasFill ? new SolidColorBrush(ta.GetFill()) : Brushes.Transparent,
                ClipToBounds = true,
                Child = text
            };

            box.Measure(new Size(width, height));
            box.Arrange(new Rect(0, 0, width, height));
            box.UpdateLayout();

            int pixelW = Math.Max(1, (int)Math.Ceiling(width * Scale));
            int pixelH = Math.Max(1, (int)Math.Ceiling(height * Scale));
            var bitmap = new RenderTargetBitmap(pixelW, pixelH, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            bitmap.Render(box);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        private static FontFamily ResolveFontFamily(string? requested)
        {
            string[] names =
            [
                requested ?? "",
                "Microsoft JhengHei",
                "Microsoft JhengHei UI",
                "Microsoft YaHei",
                "Segoe UI"
            ];

            foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
            {
                try { return new FontFamily(name); }
                catch { }
            }

            return SystemFonts.MessageFontFamily;
        }

        private static TextDecorationCollection? BuildDecorations(bool underline, bool strike)
        {
            if (!underline && !strike) return null;
            var decorations = new TextDecorationCollection();
            if (underline) decorations.Add(TextDecorations.Underline[0]);
            if (strike) decorations.Add(TextDecorations.Strikethrough[0]);
            decorations.Freeze();
            return decorations;
        }
    }
}
