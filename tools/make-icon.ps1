# Génère assets\tomee.ico (PNG multi-tailles) avec System.Drawing — même dessin que AppIcons.Draw.
param([string]$Out = (Join-Path $PSScriptRoot '..\assets\tomee.ico'))

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $bg = [System.Drawing.Color]::FromArgb(31, 41, 55)
    $fg = [System.Drawing.Color]::FromArgb(255, 196, 0)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $bg), 0.5, 0.5, $s - 1, $s - 1)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, $fg))), $s * 0.2, $s * 0.2, $s * 0.6, $s * 0.6)
    $g.DrawEllipse((New-Object System.Drawing.Pen $fg, ([Math]::Max(1.6, $s * 0.1))), $s * 0.2, $s * 0.2, $s * 0.6, $s * 0.6)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $fg), $s * 0.41, $s * 0.41, $s * 0.18, $s * 0.18)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
}

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([Byte]$dim); $w.Write([Byte]$dim); $w.Write([Byte]0); $w.Write([Byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Host "Icône générée : $Out"
