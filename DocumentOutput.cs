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
                _doc.Save(clean);
                using var copy = PdfReader.Open(clean, PdfDocumentOpenMode.Modify);
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
