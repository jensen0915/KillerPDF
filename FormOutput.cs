using System;
using System.Linq;
using System.Text;
using PdfSharpCore.Pdf;

namespace KillerPDF
{
    public partial class MainWindow
    {
        // PDFium's page renderer excludes interactive widgets. Only raster/print copies
        // place their existing appearance streams into page content; editable saves do not.
        private static void FlattenWidgetAppearances(PdfDocument document)
        {
            foreach (PdfPage page in document.Pages)
            {
                var annots = page.Elements.GetArray("/Annots");
                if (annots is null) continue;
                for (int i = annots.Elements.Count - 1; i >= 0; i--)
                {
                    var widget = annots.Elements[i] as PdfDictionary ?? DerefItem(annots.Elements[i]) as PdfDictionary;
                    if (widget?.Elements.GetName("/Subtype") != "/Widget") continue;
                    int flags = widget.Elements.GetInteger("/F");
                    if ((flags & 3) != 0) { annots.Elements.RemoveAt(i); continue; } // invisible/hidden
                    var appearance = widget.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
                    if (appearance != null && appearance.Stream is null)
                        appearance = appearance.Elements.GetDictionary(widget.Elements.GetName("/AS"));
                    if (appearance?.Stream is null)
                    {
                        // Never silently print a populated field whose appearance is unavailable.
                        PdfDictionary? field = widget;
                        while (field != null)
                        {
                            if (field.Elements["/V"] is PdfString value && !string.IsNullOrEmpty(value.Value))
                                throw new InvalidOperationException("A populated form field has no printable appearance.");
                            if (field.Elements["/V"] is PdfName state && state.Value != "/Off" && !string.IsNullOrEmpty(state.Value))
                                throw new InvalidOperationException("A selected form field has no printable appearance.");
                            field = field.Elements.GetDictionary("/Parent");
                        }
                        annots.Elements.RemoveAt(i);
                        continue;
                    }
                    var rect = widget.Elements.GetRectangle("/Rect");
                    var box = appearance.Elements.GetRectangle("/BBox");
                    double[] matrix = { 1, 0, 0, 1, 0, 0 };
                    var m = appearance.Elements.GetArray("/Matrix");
                    if (m != null && m.Elements.Count == 6)
                        for (int k = 0; k < 6; k++) matrix[k] = m.Elements.GetReal(k);
                    var xs = new[] { box.X1, box.X2 }; var ys = new[] { box.Y1, box.Y2 };
                    var x = xs.SelectMany(px => ys.Select(py => matrix[0] * px + matrix[2] * py + matrix[4])).ToArray();
                    var y = xs.SelectMany(px => ys.Select(py => matrix[1] * px + matrix[3] * py + matrix[5])).ToArray();
                    double w = x.Max() - x.Min(), h = y.Max() - y.Min();
                    if (w <= 0 || h <= 0 || rect.Width <= 0 || rect.Height <= 0)
                        throw new InvalidOperationException("A form field has an invalid appearance rectangle.");
                    double sx = rect.Width / w, sy = rect.Height / h;
                    double tx = rect.X1 - x.Min() * sx, ty = rect.Y1 - y.Min() * sy;
                    // Copy inherited resources before adding page-local names.
                    PdfDictionary? parent = page;
                    PdfDictionary? inherited = null;
                    while (parent != null && inherited == null)
                    { inherited = parent.Elements.GetDictionary("/Resources"); parent = parent.Elements.GetDictionary("/Parent"); }
                    var resources = new PdfDictionary(document);
                    if (inherited != null) foreach (string key in inherited.Elements.Keys) resources.Elements[key] = inherited.Elements[key];
                    var objects = new PdfDictionary(document);
                    var previous = resources.Elements.GetDictionary("/XObject");
                    if (previous != null) foreach (string key in previous.Elements.Keys) objects.Elements[key] = previous.Elements[key];
                    string name = "/KillerForm" + Guid.NewGuid().ToString("N");
                    if (appearance.Reference == null) document.Internals.AddObject(appearance);
                    objects.Elements[name] = appearance.Reference;
                    resources.Elements["/XObject"] = objects; page.Elements["/Resources"] = resources;
                    string commands = FormattableString.Invariant($"q\n{sx:R} 0 0 {sy:R} {tx:R} {ty:R} cm\n{name} Do\nQ\n");
                    if (!TryAttachStreamBytes(page.Contents.AppendContent(), Encoding.ASCII.GetBytes(commands)))
                        throw new InvalidOperationException("Could not create a printable form appearance stream.");
                    annots.Elements.RemoveAt(i);
                }
            }
        }
    }
}
