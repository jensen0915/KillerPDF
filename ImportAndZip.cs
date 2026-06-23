using System;
using System.IO;
using System.IO.Compression;
using System.Windows;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace KillerPDF
{
    // Import images as a PDF, and compress the current PDF to a .zip. Kept in its own partial-class
    // file rather than the MainWindow monolith. User-facing strings go through Loc() (keys live in
    // Strings/*.xaml) so the feature is fully localized.
    public partial class MainWindow
    {
        // ----- Import images as a single PDF -------------------------------------------------

        private void ImportImages_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title       = Loc("Str_Menu_Import"),
                Filter      = $"{Loc("Str_Filter_Images")}|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|{Loc("Str_Filter_AllFiles")}|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) != true) return;

            // Open in its own tab using the same flow as New, so the import is treated as UNSAVED work:
            // dirty (orange Save icon), Save routes to Save As (no real file yet), and Close warns.
            var target = BeginTabLoad(out var prev, out bool createdNew);
            try
            {
                string tempPath = BuildPdfFromImages(dlg.FileNames);
                _doc = PdfReader.Open(tempPath, PdfDocumentOpenMode.Modify);
                FinishOpenFile("Imported.pdf", tempPath);
                _originalFile = null;   // no saved location yet -> Save becomes Save As
                MarkDirty(true);        // unsaved -> orange icon + close warns
                SetStatus(string.Format(Loc("Str_Status_Imported"), dlg.FileNames.Length));
                CaptureSessionState(_active!);
                SetTool(_currentTool);
                RebuildTabStrip();
            }
            catch (Exception ex)
            {
                AbortTabLoad(target, prev, createdNew);
                KillerDialog.Show(this, Loc("Str_Err_ImportFailed") + "\n" + ex.Message,
                    "KillerPDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Builds a PDF where each page is exactly one source image. Multi-frame TIFF/GIF expand to one
        // page per frame. Page size matches the image's physical size (pixels at its own DPI, 96 if the
        // image declares none). Returns a temp PDF path; the caller opens it and the user can Save As.
        private static string BuildPdfFromImages(string[] imagePaths)
        {
            using var pdf = new PdfDocument();

            foreach (var path in imagePaths)
            {
                using var img = System.Drawing.Image.FromFile(path);
                var dim = new System.Drawing.Imaging.FrameDimension(img.FrameDimensionsList[0]);
                int frameCount = Math.Max(1, img.GetFrameCount(dim));

                for (int f = 0; f < frameCount; f++)
                {
                    img.SelectActiveFrame(dim, f);

                    int wpx = img.Width, hpx = img.Height;
                    double dpiX = img.HorizontalResolution > 0 ? img.HorizontalResolution : 96.0;
                    double dpiY = img.VerticalResolution   > 0 ? img.VerticalResolution   : 96.0;
                    double wPt = wpx * 72.0 / dpiX;
                    double hPt = hpx * 72.0 / dpiY;

                    // Copy the active frame to a fresh 32bpp bitmap, then encode PNG (XImage reads that).
                    byte[] png;
                    using (var frame = new System.Drawing.Bitmap(wpx, hpx,
                               System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (var g = System.Drawing.Graphics.FromImage(frame))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(img, 0, 0, wpx, hpx);
                        }
                        using var ms = new MemoryStream();
                        frame.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        png = ms.ToArray();
                    }

                    var page = pdf.AddPage();
                    page.Width  = wPt;   // XUnit implicitly treats a double as points
                    page.Height = hPt;

                    using var gfx  = XGraphics.FromPdfPage(page);
                    using var xImg = XImage.FromStream(() => new MemoryStream(png));
                    gfx.DrawImage(xImg, 0, 0, wPt, hPt);
                }
            }

            if (pdf.PageCount == 0)
                throw new InvalidOperationException(
                    Application.Current.TryFindResource("Str_Err_NoImages") as string ?? "No images could be read.");

            string outPath = Path.Combine(Path.GetTempPath(),
                $"Imported-{DateTime.Now:yyyyMMdd-HHmmss-fff}.pdf");
            pdf.Save(outPath);
            return outPath;
        }

        // ----- Compress the current PDF to a .zip --------------------------------------------

        private void CompressToZip_Click(object sender, RoutedEventArgs e)
        {
            if (_doc is null) { KillerDialog.Show(this, Loc("Str_Msg_OpenFirst")); return; }

            // The zip wraps the PDF on disk, so make sure what's on screen is saved first.
            if (_isDirty || string.IsNullOrEmpty(_originalFile) || !File.Exists(_originalFile))
            {
                var ask = KillerDialog.Show(this, Loc("Str_Dlg_SaveBeforeZip"),
                    "KillerPDF", MessageBoxButton.OKCancel);
                if (ask != MessageBoxResult.OK) return;
                SaveInPlace();
                if (_isDirty || string.IsNullOrEmpty(_originalFile) || !File.Exists(_originalFile))
                    return;   // save was cancelled or failed
            }

            string sourcePdf = _originalFile!;
            var dlg = new SaveFileDialog
            {
                Filter   = $"{Loc("Str_Filter_Zip")}|*.zip",
                Title    = Loc("Str_Menu_CompressZip"),
                FileName = Path.GetFileNameWithoutExtension(sourcePdf) + ".zip"
            };
            var srcDir = Path.GetDirectoryName(sourcePdf);
            if (!string.IsNullOrEmpty(srcDir) && Directory.Exists(srcDir)) dlg.InitialDirectory = srcDir;
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                using (var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create))
                    zip.CreateEntryFromFile(sourcePdf, Path.GetFileName(sourcePdf), CompressionLevel.Optimal);

                long before = new FileInfo(sourcePdf).Length;
                long after  = new FileInfo(dlg.FileName).Length;
                SetStatus(string.Format(Loc("Str_Status_Zipped"),
                    Path.GetFileName(dlg.FileName), FormatSize(after), FormatSize(before)));
            }
            catch (Exception ex)
            {
                KillerDialog.Show(this, Loc("Str_Err_ZipFailed") + "\n" + ex.Message,
                    "KillerPDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
            if (bytes >= 1024)        return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }
    }
}
