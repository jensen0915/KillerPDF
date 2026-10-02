# Run with Windows PowerShell: powershell -NoProfile -STA -File build/test-signature-ink.ps1
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run with -STA.' }
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
$refs = @(
    [Windows.Controls.InkCanvas].Assembly.Location,
    [Windows.Ink.Stroke].Assembly.Location,
    [Windows.Point].Assembly.Location,
    [System.Linq.Enumerable].Assembly.Location,
    [System.Xaml.XamlReader].Assembly.Location
)
$types = Add-Type -Path (Join-Path $PSScriptRoot '../SignatureInkCanvas.cs') -ReferencedAssemblies $refs -PassThru
$type = $types | Where-Object Name -eq 'SignatureInkCanvas'
$canvas = [Activator]::CreateInstance($type, $true)
$canvas.Measure([Windows.Size]::new(400, 150))
$canvas.Arrange([Windows.Rect]::new(0, 0, 400, 150))
$collect = $type.GetMethod('OnStrokeCollected', [Reflection.BindingFlags]'Instance,NonPublic')
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Collect($coordinates) {
    $points = [Windows.Input.StylusPointCollection]::new()
    foreach ($p in $coordinates) { $points.Add([Windows.Input.StylusPoint]::new($p[0], $p[1])) }
    $stroke = [Windows.Ink.Stroke]::new($points, $canvas.DefaultDrawingAttributes.Clone())
    $canvas.Strokes.Add($stroke)
    $collect.Invoke($canvas, @([Windows.Controls.InkCanvasStrokeCollectedEventArgs]::new($stroke))) | Out-Null
    return $stroke
}
$stroke = Collect @(@(10,20), @(11,21), @(12,19), @(13,23), @(450,160))
Assert ($stroke.StylusPoints.Count -eq 5) 'Lost intermediate stylus samples.'
Assert ($stroke.StylusPoints[2].Y -eq 19) 'Changed an in-bounds sample.'
Assert ($stroke.StylusPoints[4].X -eq 400 -and $stroke.StylusPoints[4].Y -eq 150) 'Out-of-bounds ink.'
$dot = Collect @(@(30,40), @(30,40))
Assert ($dot.StylusPoints.Count -eq 3) 'Stationary tap was lost.'
Assert ($dot.StylusPoints[2].X -gt 30) 'Tap needs a nonzero segment for PDF export.'
$single = Collect (, @(400,150))
Assert ($single.StylusPoints.Count -eq 2 -and $single.StylusPoints[1].X -lt 400) 'Single tap at edge lost.'
$canvas.SetPenWidth(9)
foreach ($s in $canvas.Strokes) {
    Assert ($s.DrawingAttributes.Width -eq 9 -and $s.DrawingAttributes.Height -eq 9) 'Preview width differs from saved width.'
    Assert ($s.DrawingAttributes.IgnorePressure -and -not $s.DrawingAttributes.FitToCurve) 'Preview differs from fixed-width PDF geometry.'
}
Assert (-not [Windows.Input.Stylus]::GetIsPressAndHoldEnabled($canvas)) 'Hold gesture enabled.'
Assert (-not [Windows.Input.Stylus]::GetIsFlicksEnabled($canvas)) 'Flick gesture enabled.'
$canvas.Strokes.Clear()
Assert ($canvas.Strokes.Count -eq 0) 'Clear left ink behind.'
Write-Output 'PASS: native ink sample retention, bounds, taps, pen width, gestures and clear.'
