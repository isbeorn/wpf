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

function Edit-Fits([string] $Name, [scriptblock] $Edit) {
    $path = Join-Path $Source "NINA.Image/FileFormat/FITS/$Name.cs"
    $text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    $text = & $Edit $text
    [IO.File]::WriteAllText($path, $text, $utf8)
}
function Reset-PortableFitsStatus([string] $Text) {
    [regex]::Replace($Text, '(?m)^([ \t]*)([^\n]*\bfits_[^\n]*\bout status\);)$', {
        param($match)
        $match.Groups[1].Value + "if (OperatingSystem.IsLinux()) status = 0;`n" + $match.Value
    })
}
function Protect-FitsConstructor([string] $Text, [string] $Cleanup, [int] $ExpectedCount) {
    $pattern = '(?ms)^(        (?:private|public) CFitsioFITS(?:Reader)?\([^\n]*\)[^{\n]*\{\n)(.*?)(^        \})'
    if ([regex]::Matches($Text, $pattern).Count -ne $ExpectedCount) { throw 'Unexpected FITS constructor ownership boundary.' }
    [regex]::Replace($Text, $pattern, {
        param($match)
        $validation = ''
        if ($match.Groups[1].Value.Contains('[] data')) {
            $validation = "                if (OperatingSystem.IsLinux() && (data == null || width < 1 || height < 1 || (long)width * height > data.LongLength)) throw new ArgumentException(`"FITS dimensions exceed the supplied image buffer.`");`n"
        }
        $body = [regex]::Replace((Reset-PortableFitsStatus $match.Groups[2].Value), '(?m)^', '    ').TrimEnd() + "`n"
        $match.Groups[1].Value + "            try {`n$validation$body            } catch {`n                if (OperatingSystem.IsLinux()) $Cleanup;`n                throw;`n            }`n" + $match.Groups[3].Value
    })
}

Edit-Fits 'CFitsioFITSReader' {
    param($text)
    $text = Replace-Once $text '        private string tempFile;' "        private string tempFile;`n        private IntPtr portableTempPtr;"
    $text = Replace-Once $text '                CfitsioNative.fits_create_diskfile(out var ptr, tempFile, out status);' "                CfitsioNative.fits_create_diskfile(out var ptr, tempFile, out status);`n                if (OperatingSystem.IsLinux()) portableTempPtr = ptr;"
    $text = Replace-Once $text '                    CfitsioNative.fits_close_file(filePtr, out status);' "                    CfitsioNative.fits_close_file(filePtr, out status);`n                    if (OperatingSystem.IsLinux()) filePtr = IntPtr.Zero;"
    $text = Replace-Once $text '                    CfitsioNative.fits_close_file(ptr, out status);' "                    CfitsioNative.fits_close_file(ptr, out status);`n                    if (OperatingSystem.IsLinux()) portableTempPtr = IntPtr.Zero;"
    $text = Replace-Once $text '        public T[] ReadPixelRow<T>(int row) where T : unmanaged {' "        public T[] ReadPixelRow<T>(int row) where T : unmanaged {`n            if (OperatingSystem.IsLinux() && (row < 0 || row >= Height)) throw new ArgumentOutOfRangeException(nameof(row));"
    foreach ($axis in @(@('Width', 'NAXIS1'), @('Height', 'NAXIS2'))) {
        $value = "CfitsioNative.fits_read_key_long(filePtr, `"$($axis[1])`")"
        $text = Replace-Once $text "$($axis[0]) = (int)$value;" "$($axis[0]) = OperatingSystem.IsLinux() ? checked((int)$value) : (int)$value;"
    }
    $text = Replace-Once $text '            BitPix = (CfitsioNative.BITPIX)(int)CfitsioNative.fits_read_key_long(filePtr, "BITPIX");' @'
            BitPix = (CfitsioNative.BITPIX)(int)CfitsioNative.fits_read_key_long(filePtr, "BITPIX");
            if (OperatingSystem.IsLinux() && (Width < 1 || Height < 1)) throw new InvalidDataException("FITS image dimensions must be positive.");
'@
    $text = $text.Replace('Width * Height', '(OperatingSystem.IsLinux() ? checked(Width * Height) : Width * Height)')
    $snippet = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CFitsioFITSReader.Portable.txt')).Replace("`r`n", "`n")
    $text = Replace-Once $text '        private bool _disposed = false;' ($snippet + "`n        private bool _disposed = false;")
    $text = Replace-Once $text '        protected virtual void Dispose(bool disposing) {' "        protected virtual void Dispose(bool disposing) {`n            if (OperatingSystem.IsLinux()) { ReleasePortableReader(); return; }"
    Protect-FitsConstructor $text 'ReleasePortableReader()' 1
}

Edit-Fits 'CFitsioFITS' {
    param($text)
    $text = Replace-Once $text '            CfitsioNative.fits_create_diskfile(out var ptr, filePath, out var status);' "            CfitsioNative.fits_create_diskfile(out var ptr, filePath, out var status);`n            if (OperatingSystem.IsLinux()) this.filePtr = ptr;"
    $text = Replace-Once $text '        public void Close() {' "        public void Close() {`n            if (OperatingSystem.IsLinux()) { ClosePortable(); return; }"
    $snippet = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CFitsioFITS.Portable.txt')).Replace("`r`n", "`n")
    $text = Replace-Once $text '        public void PopulateHeaderCards(ImageMetaData metaData) {' ($snippet + "`n        public void PopulateHeaderCards(ImageMetaData metaData) {")
    Protect-FitsConstructor $text 'ClosePortableAfterFailure()' 6
}

Edit-Fits 'FITS' {
    param($text)
    $text = Replace-Once $text '            IntPtr buffer = IntPtr.Zero;' "            IntPtr buffer = IntPtr.Zero;`n            IntPtr portableTempPtr = IntPtr.Zero;`n            string portableTempFile = null;"
    $text = Replace-Once $text '                    var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".fits");' "                    var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + `".fits`");`n                    if (OperatingSystem.IsLinux()) portableTempFile = tempFile;"
    $text = Replace-Once $text '                    CfitsioNative.fits_create_diskfile(out var ptr, tempFile, out status);' "                    CfitsioNative.fits_create_diskfile(out var ptr, tempFile, out status);`n                    if (OperatingSystem.IsLinux()) portableTempPtr = ptr;"
    $text = Replace-Once $text '                        CfitsioNative.fits_close_file(ptr, out status);' "                        CfitsioNative.fits_close_file(ptr, out status);`n                        if (OperatingSystem.IsLinux()) portableTempPtr = IntPtr.Zero;"
    $text = [regex]::Replace($text, '(?m)^([ \t]*)CfitsioNative\.fits_close_file\(fitsPtr, out var status\);$', '$0' + "`n" + '$1if (OperatingSystem.IsLinux()) fitsPtr = IntPtr.Zero;')
    $text = Replace-Once $text '                        CfitsioNative.fits_close_file(fitsPtr, out status);' "                        CfitsioNative.fits_close_file(fitsPtr, out status);`n                        if (OperatingSystem.IsLinux()) fitsPtr = IntPtr.Zero;"
    $text = [regex]::Replace($text, '(?m)^([ \t]*)Marshal\.FreeHGlobal\(buffer\);$', '$0' + "`n" + '$1if (OperatingSystem.IsLinux()) buffer = IntPtr.Zero;')
    $text = Replace-Once $text '                    File.Delete(tempFile);' "                    File.Delete(tempFile);`n                    if (OperatingSystem.IsLinux()) portableTempFile = null;"
    foreach ($axis in @(@('width', 'NAXIS1'), @('height', 'NAXIS2'))) {
        $value = "CfitsioNative.fits_read_key_long(fitsPtr, `"$($axis[1])`")"
        $text = Replace-Once $text "var $($axis[0]) = (int)$value;" "var $($axis[0]) = OperatingSystem.IsLinux() ? checked((int)$value) : (int)$value;"
    }
    $text = Replace-Once $text '                var pixels = CfitsioNative.read_ushort_pixels(fitsPtr, bitPix, 2, width * height);' @'
                if (OperatingSystem.IsLinux() && (width < 1 || height < 1)) throw new InvalidDataException("FITS image dimensions must be positive.");
                var pixels = CfitsioNative.read_ushort_pixels(fitsPtr, bitPix, 2, OperatingSystem.IsLinux() ? checked(width * height) : width * height);
'@
    $marker = "            } finally {`n"
    $cleanup = @'
            } finally {
                if (OperatingSystem.IsLinux()) {
                    if (portableTempPtr != IntPtr.Zero) {
                        int closeStatus = 0;
                        CfitsioNative.fits_close_file(portableTempPtr, out closeStatus);
                    }
                    if (portableTempFile != null) {
                        try { File.Delete(portableTempFile); }
                        catch (Exception cleanupError) { Logger.Error("Failed to delete temporary FITS file", cleanupError); }
                    }
                }
'@
    $text = Replace-Once $text $marker ($cleanup + "`n")
    Reset-PortableFitsStatus $text
}
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

$path = Join-Path $Source 'NINA.Image/FileFormat/FITS/CfitsioNative.cs'
$text = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
$text = Replace-Once $text '            DllLoader.LoadDll(Path.Combine("Cfitsio", DLLNAME));' @'
            if (OperatingSystem.IsLinux()) {
                NativeLibrary.SetDllImportResolver(typeof(CfitsioNative).Assembly, ResolvePortableCfitsio);
            } else {
                DllLoader.LoadDll(Path.Combine("Cfitsio", DLLNAME));
            }
'@
foreach ($name in @('fits_open_memory', 'fits_read_pix', 'fits_create_img', 'fits_get_img_size', 'fits_write_img', 'fits_write_img_int', 'fits_write_img_uint', 'fits_write_img_float', 'fits_write_img_double')) {
    $text = Replace-Once $text "public static extern int $name(" "private static extern int Windows_$name("
}
foreach ($name in @('fits_read_key_long', 'fits_read_key_lng')) {
    $before = "        public static long $name(IntPtr fptr, string keyname) {"
    $text = Replace-Once $text $before ($before + "`n            if (OperatingSystem.IsLinux()) return ReadPortableIntegerKey(fptr, keyname);")
}
$text = Replace-Once $text '        public static T[] read_pixels<T>(IntPtr fptr, int naxes, int nelem) where T : unmanaged {' @'
        public static T[] read_pixels<T>(IntPtr fptr, int naxes, int nelem) where T : unmanaged {
            if (OperatingSystem.IsLinux()) naxes = Math.Max(naxes, GetPortableAxisCount(fptr));
'@
$snippet = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CfitsioNative.Portable.txt')).Replace("`r`n", "`n")
$text = Replace-Once $text '        #region CONSTANTS' ($snippet + "`n        #region CONSTANTS")
[IO.File]::WriteAllText($path, $text, $utf8)
