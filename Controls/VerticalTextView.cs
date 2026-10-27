using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KillerPDF.Services;

namespace KillerPDF.Controls
{
    // Fork (jensen0915/KillerPDF): draws a vertical (直書) text box from VerticalTextLayout - the
    // placed text, and the live preview beside the edit box while vertical text is typed. Columns
    // hang from the right edge; each character is drawn on its own in Ideal layout, as the saved
    // PDF draws it.
    internal sealed class VerticalTextView : FrameworkElement
    {
        public string Text { get; set; } = "";
        public string FontName { get; set; } = "Segoe UI";
        public double TextSize { get; set; } = 14;
        public double LetterSpacing { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public Brush Foreground { get; set; } = Brushes.Black;
        /// <summary>Characters per column; 0 takes it from the height.</summary>
        public int MaxRows { get; set; }
        /// <summary>Preview only: a caret mark before this character (-1 for none).</summary>
        public int CaretIndex { get; set; } = -1;
        public Brush? CaretBrush { get; set; }
        /// <summary>Preview only: the edit box's background and outline.</summary>
        public Brush? Background { get; set; }
        public Brush? OutlineBrush { get; set; }

        public VerticalTextView()
        {
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        }

        internal string DrawnFamily => FontCoverage.PickFamily(string.IsNullOrEmpty(FontName) ? "Segoe UI" : FontName, Text);

        internal VerticalTextLayout.Metrics GetMetrics()
            => VerticalTextLayout.MetricsFor(DrawnFamily, TextSize, LetterSpacing, Bold, Italic);

        internal VerticalTextLayout.Result GetLayout(VerticalTextLayout.Metrics m, double height)
        {
            string family = DrawnFamily;
            int rows = MaxRows > 0 ? MaxRows : VerticalTextLayout.MaxRowsFor(height, m);
            return VerticalTextLayout.Layout(Text, rows, s => FontCoverage.FamilyCovers(family, s), CaretIndex);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;
            if (!(width > 0) || !(height > 0)) return;
            if (Background is not null || OutlineBrush is not null)
                dc.DrawRoundedRectangle(Background, OutlineBrush is null ? null : new Pen(OutlineBrush, 1),
                    new Rect(.5, .5, Math.Max(0, width - 1), Math.Max(0, height - 1)), 2, 2);

            string family = DrawnFamily;
            var m = VerticalTextLayout.MetricsFor(family, TextSize, LetterSpacing, Bold, Italic);
            var layout = GetLayout(m, height);
            var typeface = new Typeface(new FontFamily(family), Italic ? FontStyles.Italic : FontStyles.Normal,
                Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (var cell in layout.Cells)
            {
                if (!VerticalTextLayout.ColumnFits(cell.Column, 0, width, m) || string.IsNullOrWhiteSpace(cell.Text)) continue;
                var ft = new FormattedText(cell.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    typeface, m.Size, Foreground, null, TextFormattingMode.Ideal, dip);
                Point o = VerticalTextLayout.GlyphOrigin(cell, width, 0, m, ft.WidthIncludingTrailingWhitespace);
                if (cell.Rotated) dc.PushTransform(new RotateTransform(90, o.X, o.Y));
                dc.DrawText(ft, new Point(o.X, o.Y - ft.Baseline));
                if (cell.Rotated) dc.Pop();
            }

            if (CaretIndex >= 0 && CaretBrush is not null
                && VerticalTextLayout.ColumnFits(layout.CaretColumn, 0, width, m))
            {
                double cx = VerticalTextLayout.ColumnCenter(layout.CaretColumn, width, m);
                double y = VerticalTextLayout.RowTop(layout.CaretRow, 0, m);
                dc.DrawRectangle(CaretBrush, null, new Rect(cx - m.Size / 2, y - .75, m.Size, 1.5));
            }
        }
    }
}
