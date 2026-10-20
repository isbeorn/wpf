param([Parameter(Mandatory)][string] $Source)
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
function Replace-Once([string] $Text, [string] $Before, [string] $After) {
    if ($Text.IndexOf($Before, [StringComparison]::Ordinal) -lt 0 -or $Text.IndexOf($Before, [StringComparison]::Ordinal) -ne $Text.LastIndexOf($Before, [StringComparison]::Ordinal)) { throw "Expected exactly one image boundary: $Before" }
    $Text.Replace($Before, $After)
}
$path = Join-Path $Source 'NINA.Image/ImageAnalysis/StarAnnotator.cs'
$text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
$start = $text.IndexOf('        private static Pen ELLIPSEPEN', [StringComparison]::Ordinal)
$end = $text.IndexOf('        public string Name', [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Missing original StarAnnotator drawing resources.' }
$fields = $text.Substring($start, $end - $start).TrimEnd()
$fields = $fields.Replace('        private static ', '            internal static ')
$text = $text.Substring(0, $start) + "        private static class WindowsDrawing {`n$fields`n        }`n`n" + $text.Substring($end)
$bodyStart = $text.IndexOf('        public Task<BitmapSource> GetAnnotatedImage', [StringComparison]::Ordinal)
$head = $text.Substring(0, $bodyStart)
$body = $text.Substring($bodyStart)
foreach ($name in @('ELLIPSEPEN', 'STARBOXPEN', 'RECTPEN', 'CENTROIDPEN', 'TEXTBRUSH', 'FONTFAMILY', 'FONT')) { $body = [regex]::Replace($body, "\b$name\b", "WindowsDrawing.$name") }
$body = Replace-Once $body '            return Task.Run(() => {' "            if (!OperatingSystem.IsWindows()) return GetAnnotatedImagePortable(p, result, imageToAnnotate, maxStars, token);`n            return Task.Run(() => {"
$text = $head + $body
$snippet = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'StarAnnotator.Portable.txt')).Replace("`r`n", "`n")
$text = Replace-Once $text '        public string Name' ($snippet + "`n        public string Name")
[IO.File]::WriteAllText($path, $text, $utf8)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PortableImageBuffer.cs') -Destination (Join-Path $Source 'NINA.Image/ImageAnalysis/PortableImageBuffer.cs')
