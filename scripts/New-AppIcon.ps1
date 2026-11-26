[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePng,

    [Parameter(Mandatory = $true)]
    [string]$DestinationIco
)

$ErrorActionPreference = "Stop"

$source = (Resolve-Path -LiteralPath $SourcePng).Path
$destination = [IO.Path]::GetFullPath($DestinationIco)
$destinationDirectory = [IO.Path]::GetDirectoryName($destination)
if ([string]::IsNullOrWhiteSpace($destinationDirectory)) {
    throw "DestinationIco must include a parent directory."
}
[IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$sourceImage = [Drawing.Bitmap]::new($source)
$entries = [Collections.Generic.List[byte[]]]::new()
try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $bitmap.SetResolution(96, 96)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = if ($size -le 24) {
                    [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                } else {
                    [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                }
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.DrawImage($sourceImage, [Drawing.Rectangle]::new(0, 0, $size, $size))
            } finally {
                $graphics.Dispose()
            }

            $stream = [IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
                $entries.Add($stream.ToArray())
            } finally {
                $stream.Dispose()
            }
        } finally {
            $bitmap.Dispose()
        }
    }
} finally {
    $sourceImage.Dispose()
}

$output = [IO.File]::Open($destination, [IO.FileMode]::Create, [IO.FileAccess]::Write)
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$entries.Count)

    $offset = 6 + (16 * $entries.Count)
    for ($index = 0; $index -lt $entries.Count; $index++) {
        $size = $sizes[$index]
        $encodedSize = if ($size -eq 256) { 0 } else { $size }
        $writer.Write([byte]$encodedSize)
        $writer.Write([byte]$encodedSize)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$entries[$index].Length)
        $writer.Write([UInt32]$offset)
        $offset += $entries[$index].Length
    }

    foreach ($entry in $entries) {
        $writer.Write($entry)
    }
} finally {
    $writer.Dispose()
    $output.Dispose()
}

[PSCustomObject]@{
    Source = $source
    Destination = $destination
    Sizes = $sizes -join ", "
    Bytes = (Get-Item -LiteralPath $destination).Length
}
