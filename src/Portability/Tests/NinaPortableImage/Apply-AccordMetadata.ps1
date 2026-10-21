param([Parameter(Mandatory)][string] $Source)
$ErrorActionPreference = 'Stop'
$encoding = [Text.Encoding]::Latin1
$changes = @(
    @('AForge.Imaging/Image.cs', 'return System.Drawing.Image.GetPixelFormatSize(format) / 8;', 'return GetPixelFormatSize(format) / 8;', ''),
    @('AForge.Imaging/Image.cs', 'return System.Drawing.Image.GetPixelFormatSize(format);', 'return ((int)format >> 8) & 255;', ''),
    @('AForge.Imaging/UnmanagedImage.cs', 'return System.Drawing.Bitmap.GetPixelFormatSize(pixelFormat) / 8;', 'return Accord.Imaging.Image.GetPixelFormatSizeInBytes(pixelFormat);', 'public int PixelSize'),
    @('AForge.Imaging/Filters/Convolution/Convolution.cs', 'Image.GetPixelFormatSize(sourceData.PixelFormat) / 8', 'Accord.Imaging.Image.GetPixelFormatSizeInBytes(sourceData.PixelFormat)', ''),
    @('AForge.Imaging/Filters/Smooting/Median.cs', 'Image.GetPixelFormatSize(sourceData.PixelFormat) / 8', 'Accord.Imaging.Image.GetPixelFormatSizeInBytes(sourceData.PixelFormat)', ''),
    @('Blob Processing/BlobCounter.cs', 'Bitmap.GetPixelFormatSize(image.PixelFormat) / 8', 'Accord.Imaging.Image.GetPixelFormatSizeInBytes(image.PixelFormat)', ''),
    @('Blob Processing/BlobCounterBase.cs', 'Bitmap.GetPixelFormatSize(image.PixelFormat) / 8', 'Accord.Imaging.Image.GetPixelFormatSizeInBytes(image.PixelFormat)', 'private unsafe void CollectObjectsInfo(UnmanagedImage image)'),
    @('AForge.Imaging/Drawing.cs', 'System.Drawing.Image.GetPixelFormatSize(image.PixelFormat) / 8', 'Accord.Imaging.Image.GetPixelFormatSizeInBytes(image.PixelFormat)', 'public static void Line(UnmanagedImage image, IntPoint point1, IntPoint point2, Color color)')
)
foreach ($group in $changes | Group-Object { $_[0] }) {
    $path = Join-Path $Source $group.Name
    $original = [IO.File]::ReadAllBytes($path)
    $text = $encoding.GetString($original)
    foreach ($change in $group.Group) {
        $start = if ($change[3]) { $text.IndexOf($change[3], [StringComparison]::Ordinal) } else { 0 }
        if ($start -lt 0) { throw "Missing Accord metadata method: $($change[3])" }
        $body = $text.Substring($start)
        $index = $body.IndexOf($change[1], [StringComparison]::Ordinal)
        if ($index -lt 0 -or $index -ne $body.LastIndexOf($change[1], [StringComparison]::Ordinal)) { throw "Expected one Accord metadata expression: $($change[1])" }
        $text = $text.Substring(0, $start) + $body.Replace($change[1], $change[2])
    }
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $notice = '// Modified for portable NINA metadata on 2026-10-21. Pixel-processing algorithms are unchanged.' + $newline
    [IO.File]::WriteAllBytes($path, $encoding.GetBytes($notice + $text))
    $restored = $text
    foreach ($change in $group.Group) {
        $start = if ($change[3]) { $restored.IndexOf($change[3], [StringComparison]::Ordinal) } else { 0 }
        $restored = $restored.Substring(0, $start) + $restored.Substring($start).Replace($change[2], $change[1])
    }
    if (![Linq.Enumerable]::SequenceEqual[byte]($original, $encoding.GetBytes($restored))) { throw 'Accord adaptation changed bytes outside the declared expressions.' }
}
