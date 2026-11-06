# Builds the app's multi-size Windows icon from the master PNG. Run from anywhere:
#   powershell -ExecutionPolicy Bypass -File assets/icon/make-ico.ps1
# 256 px is stored as PNG; smaller sizes as 32-bit bitmaps, which every Windows API and GDI+ can read.
param(
    [string]$Source = "$PSScriptRoot/icon-512.png",
    [string]$Output = "$PSScriptRoot/../../src/Budditec.Overlay.Desktop/Assets/icon.ico",
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
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
