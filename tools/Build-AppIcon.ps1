# Rebuild the app emblem (a charge ring around a mouse) into a PNG-backed, multi-resolution Windows ICO.
# The drawing mirrors src/Assets/MayaX-Battery.svg in a 256-unit space. Sizes up to 24 px use a
# thicker ring and a solid mouse, because the outlined mouse turns into a dot at that scale.
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/Assets'))
Add-Type -AssemblyName System.Drawing
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
$frames = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
function New-Color([string]$html) { [Drawing.ColorTranslator]::FromHtml($html) }
foreach ($size in $sizes) {
    $small = $size -le 24
    $canvas = [Drawing.Bitmap]::new(1024, 1024)
    $g = [Drawing.Graphics]::FromImage($canvas)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform(4, 4)

    # Rounded tile with a vertical slate gradient.
    $tile = [Drawing.Drawing2D.GraphicsPath]::new()
    $tile.AddArc(8, 8, 112, 112, 180, 90)
    $tile.AddArc(136, 8, 112, 112, 270, 90)
    $tile.AddArc(136, 136, 112, 112, 0, 90)
    $tile.AddArc(8, 136, 112, 112, 90, 90)
    $tile.CloseFigure()
    $fill = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.PointF]::new(0, 8), [Drawing.PointF]::new(0, 248), (New-Color '#222D33'), (New-Color '#0F1417'))
    $g.FillPath($fill, $tile)

    # Charge ring: a dark track and a mint-to-cyan arc covering 78 percent.
    $stroke = if ($small) { 28 } else { 20 }
    $radius = if ($small) { 82 } else { 86 }
    $ringBox = [Drawing.RectangleF]::new(128 - $radius, 128 - $radius, $radius * 2, $radius * 2)
    $track = [Drawing.Pen]::new((New-Color '#2A373D'), $stroke)
    $g.DrawEllipse($track, $ringBox)
    $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.PointF]::new(30, 30), [Drawing.PointF]::new(226, 226), (New-Color '#9BF0D2'), (New-Color '#56C4EA'))
    $arc = [Drawing.Pen]::new($gradient, $stroke)
    $arc.StartCap = $arc.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($arc, $ringBox, -90, 280.8)

    # Mouse in the centre.
    $ink = New-Color '#F1F6F5'
    $mouse = [Drawing.Drawing2D.GraphicsPath]::new()
    if ($small) {
        $mouse.AddBezier(100, 114, 100, 70, 156, 70, 156, 114)
        $mouse.AddLine(156, 114, 156, 142)
        $mouse.AddBezier(156, 142, 156, 186, 100, 186, 100, 142)
        $mouse.CloseFigure()
        $solid = [Drawing.SolidBrush]::new($ink)
        $g.FillPath($solid, $mouse)
        $solid.Dispose()
    } else {
        $mouse.AddBezier(102, 116, 102, 74, 154, 74, 154, 116)
        $mouse.AddLine(154, 116, 154, 142)
        $mouse.AddBezier(154, 142, 154, 184, 102, 184, 102, 142)
        $mouse.CloseFigure()
        $pen = [Drawing.Pen]::new($ink, 10)
        $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($pen, $mouse)
        $g.DrawLine($pen, 128, 98, 128, 118)
        $pen.Dispose()
    }
    $g.Dispose()

    $result = [Drawing.Bitmap]::new($size, $size)
    $down = [Drawing.Graphics]::FromImage($result)
    $down.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $down.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $down.DrawImage($canvas, 0, 0, $size, $size)
    $down.Dispose()
    $ms = [IO.MemoryStream]::new()
    $result.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($ms.ToArray())
    if ($size -eq 256) { $result.Save((Join-Path $OutputDirectory 'MayaX-Battery.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $ms.Dispose(); $result.Dispose(); $canvas.Dispose(); $tile.Dispose(); $mouse.Dispose()
    $fill.Dispose(); $track.Dispose(); $arc.Dispose(); $gradient.Dispose()
}
$stream = [IO.File]::Create((Join-Path $OutputDirectory 'MayaX-Battery.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([int]$frames[$i].Length); $writer.Write([int]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose(); $stream.Dispose() }
