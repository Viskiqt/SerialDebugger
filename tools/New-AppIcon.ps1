# Draws the Serial Debugger icon (blue rounded square with a white ">_" prompt)
# and writes a multi-size .ico file. Run: powershell -File tools\New-AppIcon.ps1
param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\src\SerialDebugger\App.ico')
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded square with a subtle top-to-bottom gradient
    $inset = [Math]::Max(0.5, $size * 0.04)
    $rect = New-Object System.Drawing.RectangleF($inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset))
    $r = [Math]::Max(2.0, $size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $r, $r, 180, 90)
    $path.AddArc($rect.Right - $r, $rect.Y, $r, $r, 270, 90)
    $path.AddArc($rect.Right - $r, $rect.Bottom - $r, $r, $r, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $top = [System.Drawing.Color]::FromArgb(255, 30, 125, 214)
    $bottom = [System.Drawing.Color]::FromArgb(255, 0, 90, 176)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, 90.0)
    $g.FillPath($brush, $path)

    # White ">_" prompt, drawn as lines so it stays crisp at 16 px
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single][Math]::Max(1.6, $size * 0.095))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $s = [single]$size
    $chevron = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(($s * 0.27), ($s * 0.31))),
        (New-Object System.Drawing.PointF(($s * 0.47), ($s * 0.50))),
        (New-Object System.Drawing.PointF(($s * 0.27), ($s * 0.69))))
    $g.DrawLines($pen, $chevron)
    $g.DrawLine($pen, ($s * 0.54), ($s * 0.69), ($s * 0.74), ($s * 0.69))

    $pen.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# Small sizes as classic 32-bit bitmaps (most compatible), large sizes as PNG.
function Get-FrameBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    if ($size -ge 64) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return $ms.ToArray()
    }
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    # BITMAPINFOHEADER (height is doubled: colour image + AND mask)
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($size * $size * 4 + $maskRow * $size)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {          # bottom-up rows, BGRA
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $w.Write((New-Object byte[] ($maskRow * $size)))  # AND mask unused (alpha channel is used)
    $w.Flush()
    return $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($size in $sizes) { $b = New-IconBitmap $size; ,(Get-FrameBytes $b); $b.Dispose() }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)      # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                     # ICONDIRENTRY
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$frames[$i].Length); $w.Write([int]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f) }
$w.Flush()

$full = [System.IO.Path]::GetFullPath($OutFile)
[System.IO.File]::WriteAllBytes($full, $out.ToArray())
"Icon written: $full ($($out.Length) bytes, sizes: $($sizes -join ', '))"
