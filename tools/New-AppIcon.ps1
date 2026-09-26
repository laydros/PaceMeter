# Generates src/PaceMeter/PaceMeter.ico: the two-bar tray meter drawn natively at each icon size.
# Colors and proportions match Theme.cs and TrayIconRenderer.cs.
# Usage: pwsh -NoProfile -File tools/New-AppIcon.ps1

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$topLeft = 0.80
$bottomLeft = 0.50
$track = [System.Drawing.Color]::FromArgb(70, 70, 76)
$good = [System.Drawing.Color]::FromArgb(80, 200, 120)
$out = Join-Path $PSScriptRoot "..\src\PaceMeter\PaceMeter.ico"

function New-MeterPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)

    $gap = [Math]::Max(1, [int][Math]::Floor($size / 8))
    $barH = [int][Math]::Floor(($size - $gap * 3) / 2)
    $trackBrush = New-Object System.Drawing.SolidBrush $track
    $fillBrush = New-Object System.Drawing.SolidBrush $good

    $rows = @(@{ Y = $gap; Left = $topLeft }, @{ Y = $gap * 2 + $barH; Left = $bottomLeft })
    foreach ($row in $rows) {
        $g.FillRectangle($trackBrush, 0, $row.Y, $size, $barH)
        $g.FillRectangle($fillBrush, 0, $row.Y, [int][Math]::Round($size * $row.Left), $barH)
    }

    $trackBrush.Dispose(); $fillBrush.Dispose(); $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# ICO container with PNG-compressed entries (supported since Windows Vista).
$images = foreach ($s in $sizes) { , (New-MeterPng $s) }
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Dispose()
Write-Host "Wrote $out"
