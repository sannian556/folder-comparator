# Generates a multi-size application icon (16/24/32/48/64/128/256) for the
# folder-diff desktop app. Uses only .NET GDI+ ; entries are embedded PNGs.
param(
    [string]$Out = "$PSScriptRoot\assets\app.ico"
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -lt 0.5) { $r = 0.5 }
    $d = $r * 2
    $p.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$size

    # background: rounded square, blue gradient
    $pad = [Math]::Max(0.0, $s * 0.04)
    $bgPath = New-RoundedPath $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ($s * 0.22)
    $rectF = New-Object System.Drawing.RectangleF($pad, $pad, [single]($s - 2 * $pad), [single]($s - 2 * $pad))
    $c1 = [System.Drawing.Color]::FromArgb(255, 92, 124, 255)
    $c2 = [System.Drawing.Color]::FromArgb(255, 46, 74, 205)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rectF, $c1, $c2, 55.0)
    $g.FillPath($bgBrush, $bgPath)

    # two documents
    $top = $s * 0.27
    $hgt = $s * 0.46
    $wid = $s * 0.245
    $r = [Math]::Max(0.5, $s * 0.035)

    $leftX = $s * 0.185
    $leftPath = New-RoundedPath $leftX $top $wid $hgt $r
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $g.FillPath($white, $leftPath)

    $rightX = $s - $leftX - $wid
    $rightPath = New-RoundedPath $rightX $top $wid $hgt $r
    $light = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 205, 216, 255))
    $g.FillPath($light, $rightPath)

    # gold divider = the "diff" seam
    $dw = [Math]::Max(1.0, $s * 0.045)
    $dx = ($s - $dw) / 2.0
    $dy = $s * 0.185
    $dh = $s * 0.63
    $gold = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 199, 74))
    $g.FillRectangle($gold, [single]$dx, [single]$dy, [single]$dw, [single]$dh)

    $gold.Dispose(); $light.Dispose(); $white.Dispose()
    $leftPath.Dispose(); $rightPath.Dispose(); $bgBrush.Dispose(); $bgPath.Dispose()
    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-LogoBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
}

# assemble the .ico container
$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$fs = New-Object System.IO.FileStream($Out, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type = icon
$bw.Write([uint16]$sizes.Count)      # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim)            # width
    $bw.Write([byte]$dim)            # height
    $bw.Write([byte]0)               # palette
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # planes
    $bw.Write([uint16]32)            # bpp
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush(); $bw.Close(); $fs.Close()

$fi = Get-Item $Out
Write-Output ("icon written: {0}  ({1} bytes, {2} sizes)" -f $fi.FullName, $fi.Length, $sizes.Count)
