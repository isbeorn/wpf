param([Parameter(Mandatory)][string] $Source)
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
function Replace-Once([string] $Text, [string] $Before, [string] $After) {
    if ($Text.IndexOf($Before, [StringComparison]::Ordinal) -lt 0 -or $Text.IndexOf($Before, [StringComparison]::Ordinal) -ne $Text.LastIndexOf($Before, [StringComparison]::Ordinal)) { throw "Expected exactly one boundary: $Before" }
    $Text.Replace($Before, $After)
}
$path = "$Source/NINA.WPF.Base/SkySurvey/SkyMapRasterRenderer.cs"
$text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
$start = $text.IndexOf('        private static readonly DrawingBrush BoundaryBrush')
$end = $text.IndexOf('        private readonly int height;', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Original renderer GDI resource boundary absent.' }
$resources = $text.Substring($start, $end - $start)
$names = @([regex]::Matches($resources, 'private static readonly \w+ (\w+) =') | ForEach-Object { $_.Groups[1].Value })
$text = $text.Remove($start, $end - $start)
foreach ($name in $names) { $text = [regex]::Replace($text, "\b$name\b", "WindowsResources.$name") }
$dsoStart = $text.IndexOf('        private static class DsoResources {')
$dsoEnd = $text.IndexOf('        private readonly record struct RasterCacheKey', $dsoStart)
if ($dsoStart -lt 0 -or $dsoEnd -lt 0) { throw 'Original DSO GDI resource boundary absent.' }
$dso = $text.Substring($dsoStart, $dsoEnd - $dsoStart).Replace('private static class DsoResources', 'internal static class DsoResources')
$text = $text.Remove($dsoStart, $dsoEnd - $dsoStart)
$text = $text.Replace('DsoResources.', 'WindowsResources.DsoResources.')
$holder = "        private static class WindowsResources {`n" + $resources.Replace('private static readonly', 'internal static readonly') + $dso + "        }`n`n"
$text = Replace-Once $text '        private readonly record struct RasterCacheKey' ($holder + '        private readonly record struct RasterCacheKey')
$text = Replace-Once $text '        private readonly Bitmap previewBitmap;' "        private readonly Bitmap previewBitmap;`n        private readonly SkyMapWpfDrawing portableDrawing;"
$text = Replace-Once $text '            previewBitmap = softwareComposition' "            if (OperatingSystem.IsLinux()) portableDrawing = new SkyMapWpfDrawing(width, height, softwareComposition);`n            previewBitmap = !OperatingSystem.IsLinux() && softwareComposition"
$text = Replace-Once $text '            writeableBitmap.Dispatcher.VerifyAccess();' @'
            writeableBitmap.Dispatcher.VerifyAccess();
            if (OperatingSystem.IsLinux()) {
                portableDrawing.Render(writeableBitmap, scene, softwareComposition ? images : [], telescopePosition, softwareComposition, quality);
                return !softwareComposition && images.Count != 0 ? Composite(images, writeableBitmap) : writeableBitmap;
            }
'@
$text = Replace-Once $text '            previewBitmap?.Dispose();' "            portableDrawing?.Clear();`n            previewBitmap?.Dispose();"
[IO.File]::WriteAllText($path, $text, $utf8)
$path = "$Source/NINA.WPF.Base/SkySurvey/SkyMapImageCache.cs"
$text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
$text = Replace-Once $text '        internal DrawingBitmap RasterImage { get; init; }' "        internal DrawingBitmap RasterImage { get; init; }`n        internal bool IsDecodedCacheImage { get; init; }"
$text = Replace-Once $text '                    RasterImage = loaded.RasterImage' "                    RasterImage = loaded.RasterImage,`n                    IsDecodedCacheImage = true"
$text = Replace-Once $text '                rasterImage = SkyMapRasterRenderer.CreateRasterImage(image);' '                if (!OperatingSystem.IsLinux()) rasterImage = SkyMapRasterRenderer.CreateRasterImage(image);'
$text = Replace-Once $text '                    entry.RasterImage.Dispose();' '                    entry.RasterImage?.Dispose();'
$text = Replace-Once $text '            removed.RasterImage.Dispose();' '            removed.RasterImage?.Dispose();'
[IO.File]::WriteAllText($path, $text, $utf8)
[IO.File]::Copy("$PSScriptRoot/SkyMapWpfDrawing.cs", "$Source/NINA.WPF.Base/SkySurvey/SkyMapWpfDrawing.cs")
