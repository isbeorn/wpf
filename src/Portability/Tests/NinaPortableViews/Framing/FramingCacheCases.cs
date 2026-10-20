using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.WPF.Base.SkySurvey;

internal static partial class Program
{
    private static void CacheRoundTrip()
    {
        bool priorSave = GetPublic<bool>(vm!, "SaveImageInOfflineCache");
        BitmapSource originalSource = vm!.ImageParameter.Image;
        byte[] originalSamples = CacheSourceSamples(originalSource);
        SkySurveyImage saved = null!;
        XElement selected = null!;
        Dictionary<string, string> savedFiles = null!;
        int requestsAfterSave = 0;
        try
        {
            Check(required[18], () =>
            {
                Require(vm.Cache.Cache.Elements("Image").Count() == 0, "Fresh framing fixture unexpectedly contains a saved image.");
                SetPublic(vm, "SaveImageInOfflineCache", true);
                Require(GetPublic<bool>(vm, "SaveImageInOfflineCache"), "Public cache-save toggle did not enable.");
                int requestsBefore = survey!.Requests;
                Await(vm.LoadImageCommand.ExecuteAsync(null)); Loaded();
                Require(survey.Requests == requestsBefore + 1, "Fresh uncached NASA load did not request the original survey boundary.");
                requestsAfterSave = survey.Requests;
                saved = vm.ImageParameter;
                selected = vm.SelectedImageCacheInfo;
                Require(selected != null && vm.Cache.Cache.Elements("Image").Count() == 1, "Original VM save did not select one actual cache entry.");
                Require(Guid.Parse(selected!.Attribute("Id")!.Value) == saved.Id && selected.Attribute("Source")!.Value == saved.Source, "VM-selected cache ID/source metadata differs.");
                Near(double.Parse(selected.Attribute("RA")!.Value, CultureInfo.InvariantCulture), saved.Coordinates.RA, "Saved cache RA");
                Near(double.Parse(selected.Attribute("Dec")!.Value, CultureInfo.InvariantCulture), saved.Coordinates.Dec, "Saved cache Dec");
                Near(double.Parse(selected.Attribute("FoVW")!.Value, CultureInfo.InvariantCulture), saved.FoVWidth, "Saved cache width FoV");
                Near(double.Parse(selected.Attribute("FoVH")!.Value, CultureInfo.InvariantCulture), saved.FoVHeight, "Saved cache height FoV");
                Near(double.Parse(selected.Attribute("Rotation")!.Value, CultureInfo.InvariantCulture), saved.Rotation, "Saved cache rotation");
                string cacheRoot = Owned(vm.Cache.framingAssistantCachePath);
                string metadata = Owned(Path.Combine(cacheRoot, "CacheInfo.xml"));
                XElement persisted = XElement.Load(metadata).Elements("Image").Single();
                Require(XNode.DeepEquals(selected, persisted), "Actual persisted cache metadata differs from the original selected element.");
                string original = Owned(Path.Combine(cacheRoot, selected.Attribute("FileName")!.Value));
                savedFiles = new Dictionary<string, string> { [metadata] = Hash(metadata) };
                foreach (int size in new[] { 0, CacheImage.BigThumbnailSize, CacheImage.MediumThumbnailSize, CacheImage.SmallThumbnailSize })
                {
                    string file = size == 0 ? original : Owned(CacheImage.GetImagePathForThumbnail(original, size));
                    Require(File.Exists(file), "Actual VM cache image/thumbnail is absent: " + file);
                    var decoder = new JpegBitmapDecoder(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    BitmapFrame frame = decoder.Frames[0];
                    Require(frame.PixelWidth == (size == 0 ? 800 : size) && frame.PixelHeight == (size == 0 ? 600 : size), "Original VM cache image/thumbnail dimensions differ.");
                    CacheDisplayContent(frame);
                    savedFiles.Add(file, Hash(file));
                }
                Require(originalSource.IsFrozen && CacheSourceSamples(originalSource).SequenceEqual(originalSamples), "VM cache save changed original frozen source samples.");
                observations.Add(new { stage = "real-vm-cache-save", saved.Id, selected = selected.ToString(SaveOptions.DisableFormatting), files = savedFiles.Select(f => new { path = f.Key, sha256 = f.Value }).ToArray(), requestsAfterSave });
            });
            Check(required[19], () =>
            {
                SetPublic(vm, "SaveImageInOfflineCache", false);
                Require(!GetPublic<bool>(vm, "SaveImageInOfflineCache"), "Public cache-save toggle did not disable.");
                vm.SelectedImageCacheInfo = selected;
                vm.FramingAssistantSource = SkySurveySource.CACHE;
                Await(vm.LoadImageCommand.ExecuteAsync(null));
                PumpUntil(() => vm.ImageParameter != null && !ReferenceEquals(vm.ImageParameter, saved) && vm.SkyMapAnnotator.Initialized && vm.RectangleCalculated, "Original CACHE command image/annotator");
                Layout();
                SkySurveyImage restored = vm.ImageParameter;
                Require(restored.Id == saved.Id && restored.Name == selected.Attribute("Name")!.Value, "Original CACHE command restored wrong ID/name.");
                Near(restored.Coordinates.RA, saved.Coordinates.RA, "Reload cache RA"); Near(restored.Coordinates.Dec, saved.Coordinates.Dec, "Reload cache Dec");
                Near(restored.FoVWidth, saved.FoVWidth, "Reload cache width FoV"); Near(restored.FoVHeight, saved.FoVHeight, "Reload cache height FoV"); Near(restored.Rotation, saved.Rotation, "Reload cache rotation");
                Require(restored.Image.IsFrozen && restored.Image.PixelWidth == 800 && restored.Image.PixelHeight == 600 && restored.Image.DpiX == 96 && restored.Image.DpiY == 96, "Original CACHE command bitmap ownership/dimensions/DPI differ.");
                Require(survey!.Requests == requestsAfterSave, "Explicit CACHE reload unexpectedly requested a survey provider.");
                Require(vm.SkyMapAnnotator.Projection != null && vm.SkyMapAnnotator.SkyMapOverlay != null && vm.CameraRectangles.Count > 0, "Original cached-image annotator/projected placements are absent.");
                var imageView = Visuals(view!).OfType<NINA.WPF.Base.View.ImageView>().Single();
                PumpUntil(() => ReferenceEquals(imageView.Image, restored.Image), "Original CACHE image binding");
                Require(ReferenceEquals(((Image)imageView.FindName("PART_Image")).Source, restored.Image), "Original bound image child does not display actual cache source.");
                CacheDisplayContent(restored.Image);
                var target = new RenderTargetBitmap(1280, 900, 96, 96, PixelFormats.Pbgra32); target.Render(view!);
                byte[] actualView = new byte[1280 * 900 * 4]; target.CopyPixels(actualView, 5120, 0);
                Require(actualView.Where((_, i) => i % 4 != 3).Distinct().Count() > 32, "Actual cached framing view raster has insufficient content.");
                Require(CacheSourceSamples(originalSource).SequenceEqual(originalSamples), "CACHE reload changed the original scientific/display source samples.");
                observations.Add(new { stage = "real-vm-cache-reload", restored.Id, restored.Name, restored.Source, xmlSource = selected.Attribute("Source")!.Value, sourceOmission = "Original Load reads XML Source but does not assign returned SkySurveyImage.Source.", requests = survey.Requests, viewRasterSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(actualView)) });
            });
            Check(required[20], () =>
            {
                Require(!GetPublic<bool>(vm, "SaveImageInOfflineCache"), "Cache-save off state was lost.");
                vm.FramingAssistantSource = SkySurveySource.NASA;
                double recoveryFoV = AstroUtil.ArcminToDegree(saved.FoVWidth) + 1;
                vm.FieldOfView = recoveryFoV;
                string requestedFoV = AstroUtil.DegreeToArcmin(recoveryFoV).ToString("R", CultureInfo.InvariantCulture);
                Require(!vm.Cache.Cache.Elements("Image").Any(e => e.Attribute("FoVW")!.Value == requestedFoV), "Public recovery FoV did not arrange a genuine parameter-cache miss.");
                int before = survey!.Requests;
                Await(vm.LoadImageCommand.ExecuteAsync(null)); Loaded();
                Require(survey.Requests == before + 1 && vm.FramingAssistantSource == SkySurveySource.NASA, "Cache-miss recovery did not make a fresh NASA survey request.");
                Require(vm.Cache.Cache.Elements("Image").Count() == 1 && savedFiles.All(f => Hash(f.Key) == f.Value), "Disabled-save recovery changed the existing cache entry/files.");
                Require(originalSource.IsFrozen && CacheSourceSamples(originalSource).SequenceEqual(originalSamples), "Cache recovery changed original source samples.");
                SetPublic(vm, "SaveImageInOfflineCache", priorSave);
                Require(GetPublic<bool>(vm, "SaveImageInOfflineCache") == priorSave, "Public cache-save setting was not restored.");
                observations.Add(new { stage = "cache-disabled-fov-miss-recovery", recoveryFoV, requestedFoV, beforeRequests = before, afterRequests = survey.Requests, priorSave, restoredSave = GetPublic<bool>(vm, "SaveImageInOfflineCache"), sourceSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(originalSamples)) });
            });
        }
        finally { SetPublic(vm!, "SaveImageInOfflineCache", priorSave); }
    }
    private static byte[] CacheSourceSamples(BitmapSource source)
    {
        int stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        byte[] bytes = new byte[stride * source.PixelHeight]; source.CopyPixels(bytes, stride, 0); return bytes;
    }
    private static void CacheDisplayContent(BitmapSource source)
    {
        int width = source.PixelWidth, height = source.PixelHeight;
        var image = new Image { Source = source, Width = width, Height = height };
        image.Measure(new Size(width, height)); image.Arrange(new Rect(0, 0, width, height)); image.UpdateLayout();
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); target.Render(image);
        byte[] pixels = new byte[width * height * 4]; target.CopyPixels(pixels, width * 4, 0);
        Require(pixels.Where((_, i) => i % 4 != 3).Distinct().Count() > 32, "Cache JPEG display image has insufficient content.");
    }
}