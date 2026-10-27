using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Filters;
using KillerPdf.Engine.Objects;
using KillerPdf.Engine.Writing;

namespace KillerPDF.Services;

// Fork (jensen0915/KillerPDF): text placed in KillerPDF stays editable across close and reopen.
//
// A save draws the placed text into a Form XObject of its own (PdfEngineBurn), so every reader
// shows it as ordinary page content. That form also carries the boxes' settings in its /PieceInfo
// (PDF's place for an application's private data), with fingerprints of the page's content and of
// the form itself. On open, a page whose fingerprints still match - nothing else has rewritten it -
// gets that form emptied in a working copy and its boxes back as text annotations, so they can be
// dragged and double-clicked again; the next save draws them afresh. A page another program has
// changed since keeps its text as page content, as before.
internal static class EditableTextStore
{
    private const string Marker = "KillerPDFEditableText";
    private const int MaxDataBytes = 8 * 1024 * 1024;
    private const int MaxBoxesPerPage = 5000;

    /// <summary>One placed text box, in the page's visual frame (points from the top-left, after
    /// rotation), which does not depend on the screen's render size.</summary>
    internal sealed record Box
    {
        public string Text { get; init; } = "";
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public double Size { get; init; }
        public double LetterSpacing { get; init; }
        public string Font { get; init; } = "Segoe UI";
        public bool Bold { get; init; }
        public bool Italic { get; init; }
        public bool Strike { get; init; }
        public bool Underline { get; init; }
        public bool Vertical { get; init; }
        public uint Color { get; init; } = 0xFF000000;
        public uint Fill { get; init; }
    }

    internal sealed record Restored(byte[] CleanPdf, IReadOnlyDictionary<int, List<Box>> Pages, int KeptFixedPages);

    /// <summary>True when the bytes may hold editable text (a cheap check before parsing).</summary>
    internal static bool MayContain(ReadOnlySpan<byte> pdf) => pdf.IndexOf(Encoding.ASCII.GetBytes(Marker)) >= 0;

    /// <summary>Records each page's boxes on the text form a burn just appended to it (the last
    /// KPO form on the page). Returns the PDF with one more incremental revision.</summary>
    internal static byte[] Attach(byte[] pdf, IReadOnlyDictionary<int, List<Box>> pages)
    {
        if (pages.Count == 0 || pages.Values.All(list => list.Count == 0)) return pdf;
        var document = PdfDocument.Open(pdf);
        var tree = PageList(document);
        var update = new PdfIncrementalUpdateBuilder(document);
        bool changed = false;
        foreach ((int pageIndex, List<Box> boxes) in pages)
        {
            if (boxes.Count == 0 || pageIndex < 0 || pageIndex >= tree.Count) continue;
            var page = tree[pageIndex];
            if (LastOverlayForm(document, page.Resources) is not { } form) continue;
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(boxes);
            PdfIndirectReference data = update.AddObject(new PdfStream(Dict(), json));
            var privateData = Dict(
                ("Type", Name(Marker)),
                ("Version", new PdfInteger(1)),
                ("PageHash", Ascii(ContentHash(document, page.Dictionary))),
                ("FormHash", Ascii(Hash(Decode(document, form.Stream)))),
                ("Data", data));
            var pieceInfo = Dict(("KillerPDF", Dict(
                ("LastModified", Ascii(DateTimeOffset.UtcNow.ToString("'D:'yyyyMMddHHmmss'Z'"))),
                ("Private", privateData))));
            var entries = form.Stream.Dictionary.Where(e => !e.Key.Equals(Name("PieceInfo")))
                .Append(new KeyValuePair<PdfName, PdfObject>(Name("PieceInfo"), pieceInfo));
            update.ReplaceObject(form.Reference.ObjectNumber,
                new PdfStream(new PdfDictionary(entries), form.Stream.EncodedData.Span));
            changed = true;
        }
        return changed ? update.Build() : pdf;
    }

    /// <summary>Lifts editable text out of a saved PDF. Null when there is none to restore.</summary>
    internal static Restored? Extract(byte[] pdf)
    {
        if (!MayContain(pdf)) return null;
        var document = PdfDocument.Open(pdf);
        var tree = PageList(document);
        var update = new PdfIncrementalUpdateBuilder(document);
        var restored = new Dictionary<int, List<Box>>();
        int keptFixed = 0;
        for (int pageIndex = 0; pageIndex < tree.Count; pageIndex++)
        {
            var page = tree[pageIndex];
            var forms = TextForms(document, page.Resources).ToList();
            if (forms.Count == 0) continue;
            string pageHash = ContentHash(document, page.Dictionary);
            bool pageKept = false;
            foreach (var (form, privateData) in forms)
            {
                List<Box>? boxes = ReadBoxes(document, privateData);
                bool intact = boxes is not null
                    && Text(privateData, "PageHash") == pageHash
                    && Text(privateData, "FormHash") == Hash(Decode(document, form.Stream));
                if (!intact) { pageKept = true; continue; }
                Empty(document, update, form);
                if (!restored.TryGetValue(pageIndex, out var list)) restored[pageIndex] = list = [];
                list.AddRange(boxes!);
            }
            if (pageKept) keptFixed++;
        }
        if (restored.Count == 0) return keptFixed == 0 ? null : new Restored(pdf, restored, keptFixed);
        return new Restored(update.Build(), restored, keptFixed);
    }

    // The form stops drawing: no content, no resources (so its font can be dropped), no private
    // data. A tagged form keeps the empty marked-content sequence its structure element points at,
    // and that element's description is cleared so assistive technology does not read old text.
    private static void Empty(PdfDocument document, PdfIncrementalUpdateBuilder update, (PdfIndirectReference Reference, PdfStream Stream) form)
    {
        var dictionary = form.Stream.Dictionary;
        bool tagged = dictionary.TryGetValue(Name("StructParents"), out PdfObject? parents);
        var entries = dictionary
            .Where(e => !e.Key.Equals(Name("PieceInfo")) && !e.Key.Equals(Name("Filter"))
                        && !e.Key.Equals(Name("DecodeParms")) && !e.Key.Equals(Name("Length"))
                        && !e.Key.Equals(Name("Resources")))
            .Append(new KeyValuePair<PdfName, PdfObject>(Name("Resources"), Dict()));
        byte[] content = tagged ? "/Figure <</MCID 0>> BDC\nEMC\n"u8.ToArray() : [];
        update.ReplaceObject(form.Reference.ObjectNumber, new PdfStream(new PdfDictionary(entries), content));
        if (tagged && Resolve(document, parents) is PdfInteger key)
            foreach (var element in StructureElements(document, key.Value))
                if (element.Dictionary.ContainsKey(Name("Alt")))
                    update.ReplaceObject(element.Reference.ObjectNumber, new PdfDictionary(element.Dictionary
                        .Where(e => !e.Key.Equals(Name("Alt")))
                        .Append(new KeyValuePair<PdfName, PdfObject>(Name("Alt"),
                            new PdfString(ReadOnlySpan<byte>.Empty, PdfStringForm.Literal)))));
    }

    private static List<Box>? ReadBoxes(PdfDocument document, PdfDictionary privateData)
    {
        try
        {
            if (Resolve(document, privateData.TryGetValue(Name("Data"), out var d) ? d : null) is not PdfStream data) return null;
            byte[] json = PdfStreamDecoder.Decode(data, document.Resolve, MaxDataBytes);
            var boxes = JsonSerializer.Deserialize<List<Box>>(json);
            if (boxes is null || boxes.Count == 0 || boxes.Count > MaxBoxesPerPage) return null;
            foreach (var box in boxes)
                if (box.Text is null || box.Text.Length > 100_000 || box.Font is null
                    || !Finite(box.X, box.Y, box.Width, box.Height, box.Size, box.LetterSpacing)
                    || box.Width <= 0 || box.Height <= 0 || box.Size <= 0 || box.Size > 5000)
                    return null;
            return boxes;
        }
        catch { return null; }   // unreadable private data: leave the page as it is
    }

    private static bool Finite(params double[] values) => values.All(double.IsFinite);

    // ── Page tree (the engine keeps its own reader internal) ─────────────────────────────────

    private sealed record PageEntry(PdfDictionary Dictionary, PdfDictionary? Resources);

    private static List<PageEntry> PageList(PdfDocument document)
    {
        var pages = new List<PageEntry>();
        if (Resolve(document, document.Trailer.TryGetValue(Name("Root"), out var r) ? r : null) is not PdfDictionary root
            || Resolve(document, root.TryGetValue(Name("Pages"), out var p) ? p : null) is not PdfDictionary top)
            return pages;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void Walk(PdfDictionary node, PdfDictionary? inherited, int depth)
        {
            if (depth > 64 || !seen.Add(node)) return;
            var resources = Resolve(document, node.TryGetValue(Name("Resources"), out var res) ? res : null) as PdfDictionary ?? inherited;
            if (node.TryGetValue(Name("Kids"), out var kidsValue) && Resolve(document, kidsValue) is PdfArray kids)
            {
                foreach (var kid in kids)
                    if (Resolve(document, kid) is PdfDictionary child) Walk(child, resources, depth + 1);
            }
            else pages.Add(new PageEntry(node, resources));
        }
        Walk(top, null, 0);
        return pages;
    }

    private static IEnumerable<(PdfName Name, PdfIndirectReference Reference, PdfStream Stream)> Forms(
        PdfDocument document, PdfDictionary? resources)
    {
        if (resources is null
            || Resolve(document, resources.TryGetValue(Name("XObject"), out var x) ? x : null) is not PdfDictionary xObjects)
            yield break;
        foreach (var (name, value) in xObjects)
            if (value is PdfIndirectReference reference && document.Resolve(reference) is PdfStream stream
                && stream.Dictionary.TryGetValue(Name("Subtype"), out var subtype) && subtype.Equals(Name("Form")))
                yield return (name, reference, stream);
    }

    // The engine names appended overlays KPO1, KPO2, ... picking the first free number, and a burn
    // appends the text overlay after everything else, so the newest one has the highest number.
    private static (PdfIndirectReference Reference, PdfStream Stream)? LastOverlayForm(PdfDocument document, PdfDictionary? resources)
    {
        (PdfIndirectReference, PdfStream)? best = null;
        int bestNumber = -1;
        foreach (var (name, reference, stream) in Forms(document, resources))
        {
            string text = name.ValueAsLatin1();
            if (text.StartsWith("KPO", StringComparison.Ordinal) && int.TryParse(text.AsSpan(3), out int number)
                && number > bestNumber)
                (best, bestNumber) = ((reference, stream), number);
        }
        return best;
    }

    private static IEnumerable<((PdfIndirectReference Reference, PdfStream Stream) Form, PdfDictionary Private)> TextForms(
        PdfDocument document, PdfDictionary? resources)
    {
        foreach (var (_, reference, stream) in Forms(document, resources))
            if (Resolve(document, stream.Dictionary.TryGetValue(Name("PieceInfo"), out var pi) ? pi : null) is PdfDictionary pieceInfo
                && Resolve(document, pieceInfo.TryGetValue(Name("KillerPDF"), out var k) ? k : null) is PdfDictionary ours
                && Resolve(document, ours.TryGetValue(Name("Private"), out var pr) ? pr : null) is PdfDictionary privateData
                && privateData.TryGetValue(Name("Type"), out var type) && type.Equals(Name(Marker)))
                yield return ((reference, stream), privateData);
    }

    private static IEnumerable<(PdfIndirectReference Reference, PdfDictionary Dictionary)> StructureElements(PdfDocument document, long key)
    {
        if (Resolve(document, document.Trailer.TryGetValue(Name("Root"), out var r) ? r : null) is not PdfDictionary root
            || Resolve(document, root.TryGetValue(Name("StructTreeRoot"), out var s) ? s : null) is not PdfDictionary treeRoot
            || Resolve(document, treeRoot.TryGetValue(Name("ParentTree"), out var p) ? p : null) is not PdfDictionary parentTree
            || NumberTreeValue(document, parentTree, key, 0) is not { } value)
            yield break;
        IEnumerable<PdfObject> items = Resolve(document, value) is PdfArray array ? array : [value];
        foreach (var item in items)
            if (item is PdfIndirectReference reference && document.Resolve(reference) is PdfDictionary element)
                yield return (reference, element);
    }

    private static PdfObject? NumberTreeValue(PdfDocument document, PdfDictionary node, long key, int depth)
    {
        if (depth > 32) return null;
        if (Resolve(document, node.TryGetValue(Name("Nums"), out var n) ? n : null) is PdfArray nums)
            for (int i = 0; i + 1 < nums.Count; i += 2)
                if (Resolve(document, nums[i]) is PdfInteger k && k.Value == key) return nums[i + 1];
        if (Resolve(document, node.TryGetValue(Name("Kids"), out var kidsValue) ? kidsValue : null) is PdfArray kids)
            foreach (var kid in kids)
                if (Resolve(document, kid) is PdfDictionary child && NumberTreeValue(document, child, key, depth + 1) is { } hit)
                    return hit;
        return null;
    }

    // ── Fingerprints ─────────────────────────────────────────────────────────────────────────

    // The page's content as drawn: every content stream, decoded, in order. Recompressing leaves it
    // alone; any edit to what the page draws changes it.
    private static string ContentHash(PdfDocument document, PdfDictionary page)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        object? contents = Resolve(document, page.TryGetValue(Name("Contents"), out var c) ? c : null);
        IEnumerable<PdfObject> streams = contents is PdfArray array ? array : contents is PdfObject one ? [one] : [];
        foreach (var item in streams)
        {
            if (Resolve(document, item) is PdfStream stream) hash.AppendData(Decode(document, stream));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static byte[] Decode(PdfDocument document, PdfStream stream)
        => PdfStreamDecoder.Decode(stream, document.Resolve, 64 * 1024 * 1024);

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    // ── Object helpers ───────────────────────────────────────────────────────────────────────

    private static PdfObject? Resolve(PdfDocument document, PdfObject? value)
        => value is PdfIndirectReference reference ? document.Resolve(reference) : value;

    private static PdfName Name(string name) => new(Encoding.ASCII.GetBytes(name));

    private static PdfString Ascii(string text) => new(Encoding.ASCII.GetBytes(text), PdfStringForm.Literal);

    private static string? Text(PdfDictionary dictionary, string key)
        => dictionary.TryGetValue(Name(key), out var value) && value is PdfString s ? Encoding.ASCII.GetString(s.Bytes.Span) : null;

    private static PdfDictionary Dict(params (string Key, PdfObject Value)[] entries)
        => new(entries.Select(e => new KeyValuePair<PdfName, PdfObject>(Name(e.Key), e.Value)));
}
