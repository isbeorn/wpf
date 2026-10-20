# Decoder fixture provenance

`nina.ico` is the unchanged `NINA/Resources/Logo_Nina.ico` from pinned NINA
4742afae0a09c79be772f23fcf163fb5852d2775. SHA-256:
9446DA2A471DAE3A3B16526418C927F28BFB330862540DBE5333009009EB4BB8.
The upstream MPL 2.0 license is retained in `NINA-LICENSE.txt`.

`dockleft.png`, `dockright.png`, `docktop.png` and `dockbottom.png` are unchanged
AvalonDock `Themes/Generic/Images/DockLeft.PNG`, `DockRight.PNG`, `DockTop.PNG`
and `DockBottom.PNG` payloads from revision
028a5f328eb86a7abfb2195bbaae37683c818d1e. Its Ms-PL license is retained in
`AvalonDock-LICENSE.txt`. Full upstream pins and the source manifest are in
`src/Portability/Dependencies/AvalonDock/upstream.json`.

`rgb-trns.png` adds a valid PNG tRNS chunk for the existing RGB fixture's single
sample to exercise transparent RGB without changing its color bytes. Installed
WindowsDesktop 10.0.12 and Skia 3.119.4 agree on Bgra32 and exact CopyPixels.
The original RGB and RGBA fixtures retain their existing provenance.

`cache-color-q70.jpg` and `cache-gray-q70.jpg` were encoded by the genuine
WindowsDesktop 10.0.12 `JpegBitmapEncoder` with QualityLevel 70. They contain
5x3 asymmetric samples at 120x144 DPI. The disposable source and full stock
observations are retained under
`artifacts/portability/current/nina-cache-codecs-20261020-1/stock` and
`stock-results.json`. They exercise the actual NINA cache's typed decoder,
file URI, PreservePixelFormat and decode-size paths. JPEG pixels are lossy;
the tests assert formats, dimensions, density, complete owned storage and
quality tables rather than byte equivalence between the codecs.

`linear-rgb-profile.png` was generated with existing SkiaSharp 3.119.4 using
CreateSrgbLinear and three opaque 64/128/192 RGB samples. Its iCCP profile
describes linear RGB. Conversion to sRGB gives 137/188/225. The source is
retained in the existing stock probe's `--profile` branch, with generation
output in `profile-fixture-generation.json`. This catches PreservePixelFormat
incorrectly disabling embedded profile conversion without changing raw decoder
samples or adding a test dependency.

ICO directory enumeration intentionally differs from WIC's undocumented sorting.
All six original frames and their thumbnails retain stock-calibrated pixels,
format and DPI. Resource bytes are not rewritten to imitate native sorting.
