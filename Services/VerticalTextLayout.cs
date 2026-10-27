using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace KillerPDF.Services
{
    // Fork (jensen0915/KillerPDF): vertical text (直書) - characters top to bottom, columns right to
    // left, as on Chinese forms and envelopes. One layout feeds the placed text, the preview shown
    // while typing, and the saved PDF, so all three put every character in the same cell.
    //
    // Geometry, in the box's own units (canvas units on screen; the burn scales the result):
    //   column c is centred at   right - Pad - Size / 2 - c * Pitch
    //   row r starts at          top + Pad + r * Advance
    // Pitch is the face's line spacing - the gap horizontal lines get - and Advance one em plus the
    // letter spacing. A column holds MaxRowsFor(box height) characters; Enter starts the next one.
    //
    // Brackets, dashes and ellipses use their vertical presentation forms when the face has them and
    // are turned a quarter clockwise when it does not. Taiwan faces centre ，。、：；！？ in the em
    // square, so those read correctly upright. Latin letters and digits stand upright, one per cell.
    internal static class VerticalTextLayout
    {
        internal const double Pad = 2;

        /// <summary>One character's place. Index and Length locate it in the source text.</summary>
        internal readonly record struct Cell(int Index, int Length, string Text, int Column, int Row, bool Rotated);

        /// <summary>Columns and Rows are the extent used (Rows is the fullest column). The caret
        /// slot is where the character at the requested caret index would go.</summary>
        internal sealed record Result(IReadOnlyList<Cell> Cells, int Columns, int Rows, int CaretColumn, int CaretRow);

        internal readonly record struct Metrics(double Size, double Advance, double Pitch, double EmCenter);

        private static readonly Dictionary<char, char> Forms = new()
        {
            ['，'] = '︐', ['、'] = '︑', ['。'] = '︒', ['：'] = '︓', ['；'] = '︔',
            ['！'] = '︕', ['？'] = '︖', ['〖'] = '︗', ['〗'] = '︘', ['…'] = '︙',
            ['‥'] = '︰', ['—'] = '︱', ['–'] = '︲', ['＿'] = '︳',
            ['（'] = '︵', ['）'] = '︶', ['｛'] = '︷', ['｝'] = '︸',
            ['〔'] = '︹', ['〕'] = '︺', ['【'] = '︻', ['】'] = '︼',
            ['《'] = '︽', ['》'] = '︾', ['〈'] = '︿', ['〉'] = '﹀',
            ['「'] = '﹁', ['」'] = '﹂', ['『'] = '﹃', ['』'] = '﹄',
            ['［'] = '﹇', ['］'] = '﹈',
        };

        // Characters that run along the line, so they turn with it when no vertical form is drawn.
        private const string TurnWithoutForm = "（）｛｝〔〕【】《》〈〉「」『』［］〖〗—–―…‥～〜＿()[]{}<>-~_";

        /// <summary>Lays the text out. maxRows bounds each column (at least 1); covers says whether
        /// the drawn face has a character, so a vertical form is only used when it will print.</summary>
        internal static Result Layout(string text, int maxRows, Func<string, bool> covers, int caretIndex = -1)
        {
            maxRows = Math.Max(1, maxRows);
            var cells = new List<Cell>();
            int column = 0, row = 0, rows = 0;
            int caretColumn = -1, caretRow = 0;
            var elements = StringInfo.GetTextElementEnumerator(text ?? "");
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                int index = elements.ElementIndex;
                if (caretColumn < 0 && index >= caretIndex && caretIndex >= 0)
                    (caretColumn, caretRow) = row >= maxRows && element is not ("\r\n" or "\n" or "\r")
                        ? (column + 1, 0) : (column, row);
                if (element is "\r\n" or "\n" or "\r")
                {
                    column++;
                    row = 0;
                    continue;
                }
                if (row >= maxRows) { column++; row = 0; }

                string draw = element;
                bool rotated = false;
                if (element.Length == 1 && Forms.TryGetValue(element[0], out char form) && covers(form.ToString()))
                    draw = form.ToString();
                else if (element.Length == 1 && TurnWithoutForm.Contains(element[0]))
                    rotated = true;
                cells.Add(new Cell(index, element.Length, draw, column, row, rotated));
                row++;
                rows = Math.Max(rows, row);
            }
            if (caretColumn < 0 && caretIndex >= 0)
                (caretColumn, caretRow) = row >= maxRows ? (column + 1, 0) : (column, row);
            return new Result(cells, column + 1, rows, Math.Max(0, caretColumn), caretRow);
        }

        internal static Metrics MetricsFor(string family, double size, double letterSpacing, bool bold, bool italic)
        {
            size = Math.Max(1, size);
            return new Metrics(size, Math.Max(size * .3, size + letterSpacing),
                size * PdfEngineBurn.LineSpacingRatio(family), size * EmCenterRatio(family, bold, italic));
        }

        internal static int MaxRowsFor(double height, Metrics m)
            => Math.Max(1, (int)Math.Floor((height - 2 * Pad) / m.Advance + 1e-6));

        internal static Size BoxSize(int columns, int rows, Metrics m)
            => new(2 * Pad + m.Size + Math.Max(0, columns - 1) * m.Pitch, 2 * Pad + Math.Max(1, rows) * m.Advance);

        internal static double ColumnCenter(int column, double right, Metrics m)
            => right - Pad - m.Size / 2 - column * m.Pitch;

        internal static double RowTop(int row, double top, Metrics m) => top + Pad + row * m.Advance;

        /// <summary>Where the glyph is drawn: for an upright glyph the left end of its baseline, for a
        /// turned one the top end. advanceWidth is the glyph's horizontal advance.</summary>
        internal static Point GlyphOrigin(Cell cell, double right, double top, Metrics m, double advanceWidth)
        {
            double cx = ColumnCenter(cell.Column, right, m);
            double cy = RowTop(cell.Row, top, m) + m.Size / 2;
            return cell.Rotated
                ? new Point(cx - m.EmCenter, cy - advanceWidth / 2)
                : new Point(cx - advanceWidth / 2, cy + m.EmCenter);
        }

        /// <summary>True when a column lies inside a box whose left edge is at left (a narrowed box
        /// hides its last columns rather than drawing them over the page).</summary>
        internal static bool ColumnFits(int column, double left, double right, Metrics m)
            => ColumnCenter(column, right, m) - m.Size / 2 >= left - .5;

        private static readonly Dictionary<string, double> EmCenters = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>How far the middle of the face's em square sits above the baseline, in ems:
        /// the middle of 國's ink (an ideograph fills the em square), or of H's for a face with no
        /// Han. Centres an upright character in its cell and a turned one across its column.</summary>
        internal static double EmCenterRatio(string family, bool bold, bool italic)
        {
            string key = $"{family}|{bold}|{italic}";
            lock (EmCenters)
                if (EmCenters.TryGetValue(key, out double hit)) return hit;
            double ratio = .38;   // the usual ideographic em square: .88 em above the baseline, .12 below
            try
            {
                var typeface = new Typeface(new FontFamily(string.IsNullOrWhiteSpace(family) ? "Segoe UI" : family),
                    italic ? FontStyles.Italic : FontStyles.Normal,
                    bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                string probe = FontCoverage.FamilyCovers(family, "國") ? "國" : "H";
                var ft = new FormattedText(probe, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, 100, Brushes.Black, null, TextFormattingMode.Ideal, 1.0);
                Rect ink = ft.BuildGeometry(new Point(0, 0)).Bounds;
                if (!ink.IsEmpty && double.IsFinite(ink.Top))
                {
                    double measured = (ft.Baseline - (ink.Top + ink.Bottom) / 2) / 100;
                    if (measured is > 0 and < 1) ratio = measured;
                }
            }
            catch { /* keep the em-square default */ }
            lock (EmCenters) EmCenters[key] = ratio;
            return ratio;
        }
    }
}
