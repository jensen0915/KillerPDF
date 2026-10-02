using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerPDF
{
    // Use WPF's stylus packets and dynamic renderer instead of promoted mouse events.
    internal sealed class SignatureInkCanvas : InkCanvas
    {
        public SignatureInkCanvas()
        {
            Background = Brushes.White;
            ClipToBounds = true;
            MinWidth = MinHeight = 0;
            EditingMode = InkCanvasEditingMode.Ink;
            EditingModeInverted = InkCanvasEditingMode.None;
            DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = Colors.Black, Width = 4.5, Height = 4.5,
                // The saved signature/PDF format has one fixed width and straight segments.
                IgnorePressure = true, FitToCurve = false
            };
            Stylus.SetIsPressAndHoldEnabled(this, false);
            Stylus.SetIsFlicksEnabled(this, false);
            Stylus.SetIsTapFeedbackEnabled(this, false);
            Stylus.SetIsTouchFeedbackEnabled(this, false);
        }

        public void SetPenWidth(double width)
        {
            DefaultDrawingAttributes.Width = DefaultDrawingAttributes.Height = width;
            foreach (var stroke in Strokes)
                stroke.DrawingAttributes.Width = stroke.DrawingAttributes.Height = width;
        }

        protected override void OnStrokeCollected(InkCanvasStrokeCollectedEventArgs e)
        {
            var points = e.Stroke.StylusPoints;
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                point.X = Math.Max(0, Math.Min(ActualWidth, point.X));
                point.Y = Math.Max(0, Math.Min(ActualHeight, point.Y));
                points[i] = point;
            }
            // Existing preview and PDF renderers need a segment. A subpixel segment with
            // round caps preserves taps as dots without changing the saved file format.
            if (points.All(p => p.X == points[0].X && p.Y == points[0].Y))
            {
                var point = points[0];
                point.X += point.X >= ActualWidth / 2 ? -0.01 : 0.01;
                points.Add(point);
            }
            base.OnStrokeCollected(e);
        }
    }
}
