using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerPDF
{
    // Signature pad built on WPF's InkCanvas: stylus packets and the dynamic renderer instead of
    // mouse events promoted from pen input, so a pen keeps every sample and draws without lag.
    internal sealed class SignatureInkCanvas : InkCanvas
    {
        public SignatureInkCanvas()
        {
            Background = Brushes.White;
            ClipToBounds = true;
            MinWidth = MinHeight = 0;
            EditingMode = InkCanvasEditingMode.Ink;
            EditingModeInverted = InkCanvasEditingMode.None;   // the pen's eraser end does nothing
            DefaultDrawingAttributes = new DrawingAttributes
            {
                Color = Colors.Black, Width = 4.5, Height = 4.5,
                // Saved signatures and the PDF output use one fixed width and straight segments,
                // so the preview must not show pressure or curve smoothing it cannot keep.
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

        public void UndoLastStroke()
        {
            if (Strokes.Count > 0) Strokes.RemoveAt(Strokes.Count - 1);
        }

        protected override void OnStrokeCollected(InkCanvasStrokeCollectedEventArgs e)
        {
            NormalizeStroke(e.Stroke);
            base.OnStrokeCollected(e);
        }

        // Keeps the stroke inside the pad and turns a tap into a dot. The signature renderers draw
        // segments, so a single sample gets a subpixel partner that round caps show as a dot.
        internal void NormalizeStroke(Stroke stroke)
        {
            var points = stroke.StylusPoints;
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                point.X = Math.Max(0, Math.Min(ActualWidth, point.X));
                point.Y = Math.Max(0, Math.Min(ActualHeight, point.Y));
                points[i] = point;
            }
            if (points.All(p => p.X == points[0].X && p.Y == points[0].Y))
            {
                var point = points[0];
                point.X += point.X >= ActualWidth / 2 ? -0.01 : 0.01;
                points.Add(point);
            }
        }
    }
}
