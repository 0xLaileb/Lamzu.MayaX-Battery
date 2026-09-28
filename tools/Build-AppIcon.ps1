# Rebuild the original vector app emblem into PNG-backed, multi-resolution Windows ICO.
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/Assets'))
Add-Type -AssemblyName System.Drawing
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
$frames = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
foreach ($size in $sizes) {
    $canvas = [Drawing.Bitmap]::new(1024, 1024)
    $g = [Drawing.Graphics]::FromImage($canvas)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform(4, 4)
    $tile = [Drawing.Drawing2D.GraphicsPath]::new()
    $tile.AddArc(8, 8, 88, 88, 180, 90)
    $tile.AddArc(160, 8, 88, 88, 270, 90)
    $tile.AddArc(160, 160, 88, 88, 0, 90)
    $tile.AddArc(8, 160, 88, 88, 90, 90)
    $tile.CloseFigure()
    $fill = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#192125'))
    $g.FillPath($fill, $tile)
    $mouse = [Drawing.Drawing2D.GraphicsPath]::new()
    $mouse.AddBezier(78, 98, 78, 25, 178, 25, 178, 98)
    $mouse.AddLine(178, 98, 178, 159)
    $mouse.AddBezier(178, 159, 178, 231, 78, 231, 78, 159)
    $mouse.CloseFigure()
    $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#91E5CB'), 10)
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen, $mouse)
    $g.DrawLine($pen, 128, 73, 128, 96)
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
    $ms.Dispose(); $result.Dispose(); $canvas.Dispose(); $tile.Dispose(); $mouse.Dispose(); $fill.Dispose(); $pen.Dispose()
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