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

function Edit-Image([string] $Name, [scriptblock] $Edit) {
    $path = Join-Path $Source "NINA.Image/ImageAnalysis/$Name.cs"
    $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    $text = & $Edit $text
    [IO.File]::WriteAllText($path, $text, $utf8)
}
function Snippet([string] $Name) { [IO.File]::ReadAllText((Join-Path $PSScriptRoot "$Name.Portable.txt")).Replace("`r`n", "`n") }
Edit-Image 'ImageUtility' {
    param($text)
    $text = Replace-Once $text '        public static BitmapSource ConvertBitmap(System.Drawing.Bitmap bitmap) {' ((Snippet 'ImageUtility') + "`n        public static BitmapSource ConvertBitmap(System.Drawing.Bitmap bitmap) {")
    $text = Replace-Once $text '        public static DebayeredImageData Debayer(BitmapSource source, System.Drawing.Imaging.PixelFormat pf, bool saveColorChannels = false, bool saveLumChannel = false, SensorType bayerPattern = SensorType.RGGB) {' @'
        public static DebayeredImageData Debayer(BitmapSource source, System.Drawing.Imaging.PixelFormat pf, bool saveColorChannels = false, bool saveLumChannel = false, SensorType bayerPattern = SensorType.RGGB) {
            if (!OperatingSystem.IsWindows()) {
                if (pf != System.Drawing.Imaging.PixelFormat.Format16bppGrayScale) throw new NotSupportedException();
                return DebayerPortable(source, saveColorChannels, saveLumChannel, bayerPattern);
            }
'@
    $text = Replace-Once $text '        public static Task<BitmapSource> Stretch(IRenderedImage image, double factor, double blackClipping) {' "        public static Task<BitmapSource> Stretch(IRenderedImage image, double factor, double blackClipping) {`n            if (!OperatingSystem.IsWindows()) return StretchPortable(image, factor, blackClipping);"
    Replace-Once $text '        public static Task<BitmapSource> StretchUnlinked(IDebayeredImage data, double factor, double blackClipping) {' "        public static Task<BitmapSource> StretchUnlinked(IDebayeredImage data, double factor, double blackClipping) {`n            if (!OperatingSystem.IsWindows()) return StretchUnlinkedPortable(data, factor, blackClipping);"
}
Edit-Image 'DetectionUtility' {
    param($text)
    Replace-Once $text '        public static Bitmap ResizeForDetection(Bitmap image, int maxWidth, double resizeFactor) {' ((Snippet 'DetectionUtility') + "`n        public static Bitmap ResizeForDetection(Bitmap image, int maxWidth, double resizeFactor) {")
}
Edit-Image 'FastGaussianBlur' {
    param($text)
    Replace-Once $text '        public FastGaussianBlur(Bitmap image) {' ((Snippet 'FastGaussianBlur') + "`n        public FastGaussianBlur(Bitmap image) {")
}
Edit-Image 'StarDetection' {
    param($text)
    $text = Replace-Once $text '            if (pf == PixelFormats.Rgb48) {' @'
            if (pf == PixelFormats.Rgb48 && !OperatingSystem.IsWindows()) {
                using (var gray = PortableImageBuffer.Grayscale(state._originalBitmapSource)) state._originalBitmapSource = gray.ToSource();
            } else if (pf == PixelFormats.Rgb48) {
'@
    $text = Replace-Once $text '        public async Task<StarDetectionResult> Detect(' ((Snippet 'StarDetection') + "`n        public async Task<StarDetectionResult> Detect(")
    $start = $text.IndexOf('                    bitmapToAnalyze = ImageUtility.Convert16BppTo8Bpp(', [StringComparison]::Ordinal)
    $endText = '                    result.StarList = IdentifyStars(p, state, blobCounter, bitmapToAnalyze, result, token, out var detectedStars);'
    $end = $text.IndexOf($endText, [StringComparison]::Ordinal)
    if ($start -lt 0 -or $end -le $start) { throw 'Missing original star detection preparation.' }
    $body = $text.Substring($start, $end + $endText.Length - $start).Replace('blobCounter, bitmapToAnalyze, result, token, out var detectedStars', 'blobCounter, new Size(bitmapToAnalyze.Width, bitmapToAnalyze.Height), result, token, out detectedStars')
    $replacement = "                    int detectedStars;`n                    if (!OperatingSystem.IsWindows()) {`n                        result.StarList = IdentifyStarsPortable(p, state, result, progress, token, out detectedStars);`n                    } else {`n$body`n                    }"
    $text = $text.Substring(0, $start) + $replacement + $text.Substring($end + $endText.Length)
    $text = Replace-Once $text 'BlobCounter blobCounter, Bitmap bitmapToAnalyze, StarDetectionResult result' 'BlobCounter blobCounter, Size imageSize, StarDetectionResult result'
    Replace-Once $text 'p.UseROI && !InROI(bitmapToAnalyze, p, blob)' 'p.UseROI && !DetectionUtility.InROI(imageSize, blob.Rectangle, p.OuterCropRatio, p.InnerCropRatio)'
}
