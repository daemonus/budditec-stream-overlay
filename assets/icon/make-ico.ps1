# Builds the app icons from the master PNGs. Run from anywhere:
#   powershell -ExecutionPolicy Bypass -File assets/icon/make-ico.ps1
# icon.ico: 256 px is stored as PNG; smaller sizes as 32-bit bitmaps, which every Windows API and GDI+ can read.
param(
    [string]$Source = "$PSScriptRoot/icon-512.png",
    [string]$Monochrome = "$PSScriptRoot/icon-monochrome.png",
    [string]$Output = "$PSScriptRoot/../../src/Budditec.Overlay.Desktop/Assets/icon.ico",
    [string]$TrayOutput = "$PSScriptRoot/../../src/Budditec.Overlay.Desktop/Assets/tray-template.png",
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 256),
    [int]$TraySize = 64
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$master = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
$images = foreach ($size in $Sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($master, 0, 0, $size, $size)
    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # BITMAPINFOHEADER (height doubled for the AND mask), bottom-up BGRA rows, then an empty AND mask.
        $w = New-Object System.IO.BinaryWriter $stream
        $maskRow = [int][Math]::Ceiling($size / 32) * 4
        $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
        $w.Write([uint32]($size * $size * 4 + $maskRow * $size))
        $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
        $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
        $bits = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $row = New-Object byte[] ($size * 4)
        for ($y = $size - 1; $y -ge 0; $y--) {
            [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($bits.Scan0.ToInt64() + $y * $bits.Stride), $row, 0, $row.Length)
            $w.Write($row)
        }
        $bitmap.UnlockBits($bits)
        $w.Write((New-Object byte[] ($maskRow * $size)))
        $w.Flush()
    }
    $bitmap.Dispose()
    , @($size, $stream.ToArray())
}
$master.Dispose()

New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null
$out = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($Output))
# ICONDIR, then one ICONDIRENTRY per image, then the image data.
$out.Write([uint16]0); $out.Write([uint16]1); $out.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $size, $data = $image
    $dim = if ($size -ge 256) { 0 } else { $size }  # 0 means 256
    $out.Write([byte]$dim); $out.Write([byte]$dim); $out.Write([byte]0); $out.Write([byte]0)
    $out.Write([uint16]1); $out.Write([uint16]32)
    $out.Write([uint32]$data.Length); $out.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($image in $images) { $out.Write([byte[]]$image[1]) }
$out.Close()
Write-Host "Wrote $((Resolve-Path $Output).Path) ($($Sizes -join ', ') px)"

# macOS menu bar "template" icon: the monochrome artwork cropped to its opaque pixels, centred in a square.
# macOS recolours it for light/dark menu bars and scales it to the menu bar height.
$mono = New-Object System.Drawing.Bitmap ((Resolve-Path $Monochrome).Path)
$minX = $mono.Width; $minY = $mono.Height; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $mono.Height; $y++) {
    for ($x = 0; $x -lt $mono.Width; $x++) {
        if ($mono.GetPixel($x, $y).A -gt 16) {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$side = [Math]::Max($maxX - $minX, $maxY - $minY) + 1
$tray = New-Object System.Drawing.Bitmap $TraySize, $TraySize, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($tray)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$src = New-Object System.Drawing.Rectangle ($minX - ($side - ($maxX - $minX + 1)) / 2), ($minY - ($side - ($maxY - $minY + 1)) / 2), $side, $side
$g.DrawImage($mono, (New-Object System.Drawing.Rectangle 0, 0, $TraySize, $TraySize), $src, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $mono.Dispose()
$tray.Save($TrayOutput, [System.Drawing.Imaging.ImageFormat]::Png)
$tray.Dispose()
Write-Host "Wrote $((Resolve-Path $TrayOutput).Path) ($TraySize px macOS menu bar template)"
