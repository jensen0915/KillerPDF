using System;
using System.IO;
using System.Linq;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using KillerPDF.Services;

namespace KillerPDF
{
    public partial class MainWindow
    {
        // Complete the snapshot synchronously on the UI thread. Consumers may then render the
        // resulting file in the background without accessing the editable document or UI state.
        private string CreateDocumentOutputSnapshot()
        {
            string clean;
            string output = CreateDocumentOutputSnapshot(out clean, forRaster: true);
            DeleteOutputTemp(clean);
            return output;
        }

        private string CreateDocumentOutputSnapshot(out string clean, bool forRaster = false)
        {
            Dispatcher.VerifyAccess();
            if (_doc is null) throw new InvalidOperationException(Loc("Str_OpenFirst"));
            CommitActiveTextBox();
            clean = App.MakeTempFile("output-base");
            string output = App.MakeTempFile("output");
            try
            {
                var widgetKeys = CaptureFormWidgetKeys();
                RemoveEmptyOutlineRoot(_doc);
                _doc.Save(clean);
                using var copy = OpenOutputBase(ref clean);
                WriteFormValuesToDocument(copy, widgetKeys);
                StripLinkAnnotationBorders(copy);
                StripInvalidPdfXMetadata(copy);
                DrawStampsIntoDoc(copy, _docStampSpec?.Clone());
                DrawAnnotationsIntoDoc(copy, _annotations, _renderDims, failOnError: true);
                if (forRaster) FlattenWidgetAppearances(copy);
                copy.Save(output);
                return output;
            }
            catch { DeleteOutputTemp(output); DeleteOutputTemp(clean); throw; }
        }

        // PdfSharpCore lists an outline root without entries in the xref but never writes it, so the
        // saved file cannot be reopened ("Unexpected token 'xref'"). Drop such a root before saving.
        private static void RemoveEmptyOutlineRoot(PdfDocument doc)
        {
            var catalog = doc.Internals.Catalog.Elements;
            try
            {
                if (catalog.ContainsKey("/Outlines") && doc.Outlines.Count == 0) catalog.Remove("/Outlines");
            }
            catch { /* Malformed outline: leave it as it is. */ }
        }

        // PdfSharpCore can write a file that its own reader then rejects (for example
        // "Unexpected token 'xref'"). The editable document is still intact, so rewrite the
        // snapshot losslessly through PDFium and continue from that copy.
        private static PdfDocument OpenOutputBase(ref string clean)
        {
            try { return PdfReader.Open(clean, PdfDocumentOpenMode.Modify); }
            catch
            {
                string repaired = App.MakeTempFile("output-base-fixed");
                try
                {
                    if (TryPdfiumStripEncryption(clean, repaired))
                    {
                        var document = PdfReader.Open(repaired, PdfDocumentOpenMode.Modify);
                        DeleteOutputTemp(clean);
                        clean = repaired;
                        return document;
                    }
                }
                catch { /* Report the original reader error below. */ }
                DeleteOutputTemp(repaired);
                throw;
            }
        }

        private bool SaveDocumentTo(string target)
        {
            string? snapshot = null;
            string? clean = null;
            try
            {
                snapshot = CreateDocumentOutputSnapshot(out clean);
                AtomicFile.Copy(snapshot, target);
                // PDFium must continue reading the unburned backing PDF; otherwise a repaint
                // would show exported annotations underneath the editable overlays a second time.
                _currentFile = clean;
                clean = null; // owned by this session, removed by App session-temp cleanup
                _originalFile = target;
                FileNameLabel.Text = Path.GetFileName(target);
                MarkDirty(false);
                if (_active != null) CaptureSessionState(_active);
                RebuildTabStrip();
                SetStatus(string.Format(Loc("Str_FileSaved"), Path.GetFileName(target)));
                return true;
            }
            catch (Exception ex)
            {
                KillerDialog.Show(this, string.Format(Loc("Str_SaveFailed"), ex.Message), "KillerPDF",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }
            finally { DeleteOutputTemp(snapshot); DeleteOutputTemp(clean); }
        }

        private static void DeleteOutputTemp(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { File.Delete(path); } catch { /* Session cleanup retries temporary files. */ }
        }
    }
}
