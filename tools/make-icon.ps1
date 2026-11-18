# Generates assets\cleanfolder.ico: teal rounded square, white folder and a sparkle (16..256 px, PNG frames).
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets\cleanfolder.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function P($x, $y, $s) { New-Object System.Drawing.PointF ([float]($x * $s)), ([float]($y * $s)) }

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Rounded square background
    $r = 56 * $s; $m = 8 * $s; $w = 240 * $s
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bg.AddArc($m, $m, $r, $r, 180, 90); $bg.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
    $bg.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90); $bg.AddArc($m, $m + $w - $r, $r, $r, 90, 90); $bg.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 128, 116))), $bg)

    # Folder
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $tab = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tab.AddPolygon([System.Drawing.PointF[]]@((P 48 84 $s), (P 104 84 $s), (P 120 102 $s), (P 120 116 $s), (P 48 116 $s)))
    $g.FillPath($white, $tab)
    $g.FillRectangle($white, [float](48 * $s), [float](100 * $s), [float](160 * $s), [float](108 * $s))

    # Sparkle (4-point star) in teal on the folder
    $teal = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 128, 116))
    $cx = 128; $cy = 154; $a = 38; $b = 9
    $star = [System.Drawing.PointF[]]@((P $cx ($cy - $a) $s), (P ($cx + $b) ($cy - $b) $s), (P ($cx + $a) $cy $s), (P ($cx + $b) ($cy + $b) $s),
        (P $cx ($cy + $a) $s), (P ($cx - $b) ($cy + $b) $s), (P ($cx - $a) $cy $s), (P ($cx - $b) ($cy - $b) $s))
    $g.FillPolygon($teal, $star)
    if ($size -ge 32) {
        $cx = 176; $cy = 70; $a = 22; $b = 6
        $star2 = [System.Drawing.PointF[]]@((P $cx ($cy - $a) $s), (P ($cx + $b) ($cy - $b) $s), (P ($cx + $a) $cy $s), (P ($cx + $b) ($cy + $b) $s),
            (P $cx ($cy + $a) $s), (P ($cx - $b) ($cy + $b) $s), (P ($cx - $a) $cy $s), (P ($cx - $b) ($cy - $b) $s))
        $g.FillPolygon($white, $star2)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = $sizes | ForEach-Object { ,(New-Frame $_) }

$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $frames[$i]
    $b = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $frames) { $w.Write([byte[]]$data) }
$w.Close()
[System.IO.File]::WriteAllBytes((Join-Path $root 'assets\icon-256.png'), $frames[-1])
"ok: $out"
