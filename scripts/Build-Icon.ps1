# Native export of the code/vector ScreenDash mark at Windows icon sizes.
# Geometry matches ScreenDash/Assets/screendash.svg; no bitmap source is read.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconFrames = [Collections.Generic.List[byte[]]]::new()
foreach ($iconSize in @(16, 32, 48, 64, 128, 256)) {
    $iconBitmap = [Drawing.Bitmap]::new($iconSize, $iconSize, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $iconGraphics = [Drawing.Graphics]::FromImage($iconBitmap)
    $iconStream = [IO.MemoryStream]::new()
    $iconGradient = $null; $iconEdge = $null; $iconPen = $null; $iconDot = $null
    try {
        $iconGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $iconGraphics.Clear([Drawing.Color]::FromArgb(8, 9, 11))
        $iconGraphics.ScaleTransform($iconSize / 44.0, $iconSize / 44.0)
        $iconEdge = [Drawing.Pen]::new([Drawing.Color]::FromArgb(67, 73, 81), 1)
        $iconGraphics.DrawRectangle($iconEdge, [single]4, [single]4, [single]39, [single]39)
        $iconGradient = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.RectangleF]::new(0, 0, 40, 40),
            [Drawing.Color]::FromArgb(237, 240, 244), [Drawing.Color]::FromArgb(169, 177, 188), 45.0)
        $iconGraphics.FillRectangle($iconGradient, [single]0, [single]0, [single]40, [single]40)
        $iconPen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(18, 20, 24), [single]1.7)
        $iconPen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        $iconPen.StartCap = $iconPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $iconGraphics.DrawRectangle($iconPen, [single]8, [single]9, [single]24, [single]18)
        $iconGraphics.DrawLine($iconPen, [single]20, [single]27, [single]20, [single]32)
        $iconGraphics.DrawLine($iconPen, [single]15, [single]32, [single]25, [single]32)
        $iconDot = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(18, 20, 24))
        $iconGraphics.FillEllipse($iconDot, [single]16, [single]14, [single]8, [single]8)
        $iconBitmap.Save($iconStream, [Drawing.Imaging.ImageFormat]::Png)
        $iconFrames.Add($iconStream.ToArray())
    } finally {
        foreach ($iconResource in @($iconDot, $iconPen, $iconEdge, $iconGradient, $iconGraphics, $iconBitmap, $iconStream)) {
            if ($null -ne $iconResource) { $iconResource.Dispose() }
        }
    }
}
$iconOutput = Join-Path (Split-Path $PSScriptRoot -Parent) 'ScreenDash\Assets\screendash.ico'
$iconFile = [IO.File]::Create($iconOutput)
$iconWriter = [IO.BinaryWriter]::new($iconFile)
try {
    $iconWriter.Write([uint16]0); $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]$iconFrames.Count)
    $iconOffset = [uint32](6 + 16 * $iconFrames.Count)
    $iconIndex = 0
    foreach ($iconSize in @(16, 32, 48, 64, 128, 256)) {
        $iconDimension = if ($iconSize -eq 256) { [byte]0 } else { [byte]$iconSize }
        $iconWriter.Write($iconDimension); $iconWriter.Write($iconDimension)
        $iconWriter.Write([byte]0); $iconWriter.Write([byte]0)
        $iconWriter.Write([uint16]1); $iconWriter.Write([uint16]32)
        $iconWriter.Write([uint32]$iconFrames[$iconIndex].Length); $iconWriter.Write($iconOffset)
        $iconOffset += [uint32]$iconFrames[$iconIndex].Length; $iconIndex++
    }
    foreach ($iconFrame in $iconFrames) { $iconWriter.Write($iconFrame) }
} finally { $iconWriter.Dispose() }
Write-Output 'ScreenDash.ico: Schwarz-Silber, 16 bis 256 Pixel.'
