using System.Threading;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using Xunit;

namespace KillerPDF.Tests;

// Ported from the fork's build/test-signature-ink.ps1 STA script.
public sealed class SignatureInkCanvasTests
{
    [Fact]
    public void KeepsSamplesClampsToPadAndTurnsTapsIntoDots() => RunSta(() =>
    {
        var canvas = Pad();
        var stroke = Collect(canvas, (10, 20), (11, 21), (12, 19), (13, 23), (450, 160));
        Assert.Equal(5, stroke.StylusPoints.Count);                 // no intermediate sample lost
        Assert.Equal(19, stroke.StylusPoints[2].Y);                 // in-bounds sample untouched
        Assert.Equal(400, stroke.StylusPoints[4].X);                // clamped to the pad
        Assert.Equal(150, stroke.StylusPoints[4].Y);

        var dot = Collect(canvas, (30, 40), (30, 40));
        Assert.Equal(3, dot.StylusPoints.Count);                    // stationary tap kept as a segment
        Assert.True(dot.StylusPoints[2].X > 30);

        var edge = Collect(canvas, (400, 150));
        Assert.Equal(2, edge.StylusPoints.Count);
        Assert.True(edge.StylusPoints[1].X < 400);                  // the partner point stays inside
    });

    [Fact]
    public void PenWidthAppliesToPreviewAndExistingStrokes() => RunSta(() =>
    {
        var canvas = Pad();
        Collect(canvas, (10, 10), (50, 50));
        canvas.SetPenWidth(9);
        foreach (var s in canvas.Strokes)
        {
            Assert.Equal(9, s.DrawingAttributes.Width);
            Assert.Equal(9, s.DrawingAttributes.Height);
            Assert.True(s.DrawingAttributes.IgnorePressure);
            Assert.False(s.DrawingAttributes.FitToCurve);
        }
        Assert.Equal(9, canvas.DefaultDrawingAttributes.Width);
    });

    [Fact]
    public void UndoRemovesOnlyTheLastStrokeAndGesturesAreOff() => RunSta(() =>
    {
        var canvas = Pad();
        Collect(canvas, (10, 10), (50, 50));
        var second = Collect(canvas, (60, 60), (90, 90));
        canvas.UndoLastStroke();
        Assert.Single(canvas.Strokes);
        Assert.DoesNotContain(second, canvas.Strokes);
        canvas.UndoLastStroke();
        canvas.UndoLastStroke();                                    // nothing left: no throw
        Assert.Empty(canvas.Strokes);
        Assert.False(Stylus.GetIsPressAndHoldEnabled(canvas));
        Assert.False(Stylus.GetIsFlicksEnabled(canvas));
    });

    private static SignatureInkCanvas Pad()
    {
        var canvas = new SignatureInkCanvas();
        canvas.Measure(new Size(400, 150));
        canvas.Arrange(new Rect(0, 0, 400, 150));
        return canvas;
    }

    private static Stroke Collect(SignatureInkCanvas canvas, params (double X, double Y)[] coordinates)
    {
        var points = new StylusPointCollection();
        foreach (var (x, y) in coordinates) points.Add(new StylusPoint(x, y));
        var stroke = new Stroke(points, canvas.DefaultDrawingAttributes.Clone());
        canvas.Strokes.Add(stroke);
        canvas.NormalizeStroke(stroke);
        return stroke;
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
