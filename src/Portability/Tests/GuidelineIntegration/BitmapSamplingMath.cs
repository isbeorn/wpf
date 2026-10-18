using System.Windows.Media;

internal static class BitmapSamplingMath
{
    internal sealed record Statistics(int DifferentChannels, int MaximumDifference, int InteriorMaximum,
        int EdgeMaximum, int BoundaryPixels, int Failures, string? FirstFailure,
        string? BoundaryExample = null, double MaximumCoordinateUncertainty = 0);

    internal static Statistics CompareNearest(byte[] actual, byte[] stock, byte[] actualGeometry, byte[] stockGeometry, BitmapSamplingCases.RasterCase sample, int width, int height)
    {
        EnsureAxisAligned(sample);
        if (actual.Length != checked(width * height * 4) || actual.Length != stock.Length)
            throw new InvalidOperationException("Sampling raster dimensions differ.");
        if (actualGeometry.Length != width * height || stockGeometry.Length != width * height)
            throw new InvalidOperationException("Geometry coverage dimensions differ.");
        var horizontal = new SourceAxis(sample.Rectangle.Left, sample.Rectangle.Width, sample.Source.PixelWidth,
            sample.Transform.M11, sample.Transform.OffsetX, sample.Pan.X, sample.Dpi.DpiScaleX, width, true);
        var vertical = new SourceAxis(sample.Rectangle.Top, sample.Rectangle.Height, sample.Source.PixelHeight,
            sample.Transform.M22, sample.Transform.OffsetY, sample.Pan.Y, sample.Dpi.DpiScaleY, height, false);
        bool gray = sample.Source.Format == PixelFormats.Gray16;
        bool premultiplied = sample.Source.Format == PixelFormats.Pbgra32;
        bool opaque = !premultiplied || Enumerable.Range(0, sample.SourceBytes.Length / 4).All(index => sample.SourceBytes[index * 4 + 3] == 255);
        int sourceWidth = sample.Source.PixelWidth, bytesPerPixel = sample.Source.Format.BitsPerPixel / 8;
        var horizontalCandidates = Enumerable.Range(0, width).Select(horizontal.Candidates).ToArray();
        var verticalCandidates = Enumerable.Range(0, height).Select(vertical.Candidates).ToArray();
        int different = 0, maximum = 0, interiorMaximum = 0, edgeMaximum = 0, boundaryPixels = 0, failures = 0;
        double maximumUncertainty = 0;
        string? firstFailure = null, boundaryExample = null;
        void Fail(int x, int y, string reason)
        {
            failures++;
            firstFailure ??= $"{sample.Name}: ({x},{y}): {reason}";
        }
        ValidateGeometryControls(actualGeometry, stockGeometry, sample, width, height, Fail);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double portableCoverage = actualGeometry[y * width + x] / 255d, stockCoverage = stockGeometry[y * width + x] / 255d;
                bool interior = portableCoverage == 1 && stockCoverage == 1;
                int offset = (y * width + x) * 4;
                if (opaque && (actual[offset + 3] != actualGeometry[y * width + x] || stock[offset + 3] != stockGeometry[y * width + x]))
                    Fail(x, y, "Opaque nearest image alpha differs from its genuine geometry coverage control.");
                // Candidate indices depend only on geometry and the pinned arithmetic contracts, never on pixel differences.
                var xs = horizontalCandidates[x];
                var ys = verticalCandidates[y];
                bool boundary = xs.Minimum != xs.Maximum || ys.Minimum != ys.Maximum;
                if (portableCoverage > 0 || stockCoverage > 0)
                {
                    maximumUncertainty = Math.Max(maximumUncertainty, Math.Max(xs.Uncertainty, ys.Uncertainty));
                    if (boundary)
                    {
                        boundaryPixels++;
                        boundaryExample ??= $"({x},{y}) allows source X[{xs.Minimum},{xs.Maximum}] Y[{ys.Minimum},{ys.Maximum}]";
                    }
                    if (!MatchesSource(actual, offset, portableCoverage, sample.SourceBytes, sourceWidth, bytesPerPixel,
                        xs.Minimum, xs.Maximum, ys.Minimum, ys.Maximum, gray, premultiplied))
                        Fail(x, y, "Portable pixel is not a geometry-derived nearest source candidate.");
                    if (!MatchesSource(stock, offset, stockCoverage, sample.SourceBytes, sourceWidth, bytesPerPixel,
                        xs.Minimum, xs.Maximum, ys.Minimum, ys.Maximum, gray, premultiplied))
                        Fail(x, y, "Stock pixel is not a geometry-derived nearest source candidate.");
                }
                for (int channel = 0; channel < 4; channel++)
                {
                    int a = actual[offset + channel], e = stock[offset + channel], difference = Math.Abs(a - e);
                    if (difference != 0) different++;
                    maximum = Math.Max(maximum, difference);
                    if (interior) interiorMaximum = Math.Max(interiorMaximum, difference);
                    else edgeMaximum = Math.Max(edgeMaximum, difference);
                    if (channel < 3 && a > actual[offset + 3]) Fail(x, y, "Color is not premultiplied by alpha.");
                    if (portableCoverage == 0 && stockCoverage == 0 && (a != 0 || e != 0))
                        Fail(x, y, "The exterior is not exactly transparent.");
                    if (interior && !boundary && difference > (gray && channel < 3 ? 1 : 0))
                        Fail(x, y, "Nearest pixels away from quantization boundaries differ.");
                }
            }
        return new Statistics(different, maximum, interiorMaximum, edgeMaximum, boundaryPixels, failures, firstFailure, boundaryExample, maximumUncertainty);
    }

    private static bool MatchesSource(byte[] raster, int offset, double coverage, byte[] source, int sourceWidth, int bytesPerPixel,
        int minimumX, int maximumX, int minimumY, int maximumY, bool gray, bool premultiplied)
    {
        for (int y = minimumY; y <= maximumY; y++)
            for (int x = minimumX; x <= maximumX; x++)
            {
                int sourceOffset = (y * sourceWidth + x) * bytesPerPixel;
                bool matches = true;
                for (int channel = 0; channel < 4; channel++)
                {
                    int value;
                    if (premultiplied) value = source[sourceOffset + channel];
                    else if (channel == 3) value = 255;
                    else if (gray)
                    {
                        int sixteen = source[sourceOffset] | source[sourceOffset + 1] << 8;
                        value = (sixteen + 128) / 257;
                    }
                    else value = source[sourceOffset + (2 - channel) * 2 + 1];
                    double limit = (gray && channel < 3 ? coverage : 0) + (coverage is > 0 and < 1 ? 1 : 0);
                    if (Math.Abs(raster[offset + channel] - value * coverage) > limit + 1e-6) matches = false;
                }
                if (matches) return true;
            }
        return false;
    }

    private sealed class SourceAxis
    {
        private readonly Interval _inverseScale, _inverseTranslation, _ipcTranslation;
        private readonly double _scale, _origin, _nativeError;
        private readonly int _pixels;

        internal SourceAxis(double rectangleOrigin, double extent, int pixels, double transform, double offset,
            double pan, double dpi, int viewport, bool horizontal)
        {
            _pixels = pixels;
            _scale = extent * transform * dpi / pixels;
            _origin = (rectangleOrigin * transform + offset + pan) * dpi;
            Interval worldScale = Interval.Multiply(Interval.Point((float)dpi), Interval.Point((float)transform));
            Interval worldTranslation = Interval.Add(Interval.Multiply(Interval.Point((float)dpi), Interval.Point((float)offset)),
                Interval.Multiply(Interval.Point((float)dpi), Interval.Point((float)pan)));
            Interval scale = Interval.Multiply(worldScale, Interval.Point((float)(extent / pixels)));
            Interval translation = Interval.Add(Interval.Multiply(worldScale, Interval.Point((float)rectangleOrigin)), worldTranslation);
            _inverseScale = Interval.Divide(Interval.Point(1), scale);
            _inverseTranslation = Interval.Multiply(Interval.Multiply(translation, _inverseScale), Interval.Point(-1));
            _ipcTranslation = Interval.Add(Interval.Add(_inverseTranslation, Interval.Multiply(_inverseScale, Interval.Point(0.5f))), Interval.Point(-0.5f));
            // Native NN rounds each scan's origin and X increment to 16.16. Any clipped span is no longer than the viewport.
            double inverseMagnitude = Math.Max(Math.Abs(_inverseScale.Low), Math.Abs(_inverseScale.High));
            double stepArithmetic = horizontal ? (Spacing(inverseMagnitude * viewport) + viewport * Spacing(inverseMagnitude)) / 2 : 0;
            _nativeError = Math.Max(ErrorAt(0.5), ErrorAt(viewport + 0.5)) + stepArithmetic + (horizontal ? viewport + 2 : 2) / 131072d;
        }

        internal (int Minimum, int Maximum, double Uncertainty) Candidates(int pixel)
        {
            double center = pixel + 0.5, ideal = (center - _origin) / _scale;
            Interval floating = Coordinates(center);
            double low = Math.Min(floating.Low, ideal - _nativeError), high = Math.Max(floating.High, ideal + _nativeError);
            int minimum = (int)Math.Clamp(Math.Floor(low), 0, _pixels - 1);
            int maximum = (int)Math.Clamp(Math.Floor(high), 0, _pixels - 1);
            if (!double.IsFinite(low) || !double.IsFinite(high) || high - low >= 1 || maximum - minimum > 1)
                throw new InvalidOperationException("The measured nearest profile cannot qualify more than adjacent source texels.");
            return (minimum, maximum, high - low);
        }

        private double ErrorAt(double center)
        {
            double ideal = (center - _origin) / _scale;
            Interval range = Coordinates(center);
            return Math.Max(Math.Abs(range.Low - ideal), Math.Abs(range.High - ideal));
        }

        private static double Spacing(double value)
        {
            float single = (float)Math.Abs(value);
            return (double)MathF.BitIncrement(single) - single;
        }

        private Interval Coordinates(double center)
        {
            Interval direct = Interval.Add(Interval.Multiply(_inverseScale, Interval.Point((float)center)), _inverseTranslation);
            Interval ipc = Interval.Add(Interval.Add(Interval.Multiply(_inverseScale, Interval.Point((float)(center - 0.5))), _ipcTranslation), Interval.Point(0.5f));
            return new Interval(Math.Min(direct.Low, ipc.Low), Math.Max(direct.High, ipc.High));
        }
    }

    private readonly record struct Interval(double Low, double High)
    {
        internal static Interval Point(float value) => new(value, value);
        private static Interval Rounded(double low, double high) => new(MathF.BitDecrement((float)low), MathF.BitIncrement((float)high));
        internal static Interval Add(Interval a, Interval b) => Rounded(a.Low + b.Low, a.High + b.High);
        internal static Interval Multiply(Interval a, Interval b)
        {
            double first = a.Low * b.Low, second = a.Low * b.High, third = a.High * b.Low, fourth = a.High * b.High;
            return Rounded(Math.Min(Math.Min(first, second), Math.Min(third, fourth)), Math.Max(Math.Max(first, second), Math.Max(third, fourth)));
        }
        internal static Interval Divide(Interval numerator, Interval denominator)
        {
            if (denominator.Low <= 0 && denominator.High >= 0) throw new InvalidOperationException("Sampling scale interval crosses zero.");
            return Multiply(numerator, Rounded(1 / denominator.High, 1 / denominator.Low));
        }
    }

    internal static Statistics CompareLinear(byte[] actual, byte[] stock, byte[] actualGeometry, byte[] stockGeometry, BitmapSamplingCases.RasterCase sample, int width, int height)
    {
        EnsureAxisAligned(sample);
        if (actual.Length != checked(width * height * 4) || actual.Length != stock.Length)
            throw new InvalidOperationException("Sampling raster dimensions differ.");
        if (actualGeometry.Length != width * height || stockGeometry.Length != width * height)
            throw new InvalidOperationException("Geometry coverage dimensions differ.");
        bool opaque = sample.Source.Format != PixelFormats.Pbgra32;
        if (!opaque)
            opaque = Enumerable.Range(0, sample.SourceBytes.Length / 4).All(index => sample.SourceBytes[index * 4 + 3] == 255);
        int colorLimit = sample.LinearColorLimit ?? (sample.Source.Format == PixelFormats.Rgb48 ? 1 : 2);
        byte[]? uniform = UniformPremultipliedSource(sample);
        int different = 0, maximum = 0, interiorMaximum = 0, edgeMaximum = 0, failures = 0;
        string? firstFailure = null;
        void Fail(int x, int y, int channel, string reason)
        {
            failures++;
            firstFailure ??= $"{sample.Name}: ({x},{y}) channel {channel}: {reason}";
        }
        ValidateGeometryControls(actualGeometry, stockGeometry, sample, width, height,
            (x, y, reason) => Fail(x, y, 3, reason));
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double portableCoverage = actualGeometry[y * width + x] / 255d;
                double stockCoverage = stockGeometry[y * width + x] / 255d;
                bool interior = portableCoverage == 1 && stockCoverage == 1;
                int offset = (y * width + x) * 4;
                bool unpaired = (portableCoverage == 0) != (stockCoverage == 0);
                if (unpaired && uniform == null)
                    Fail(x, y, 3, "Only one coverage model reaches this pixel; a separate source-color observation is required.");
                if (opaque)
                {
                    if (stock[offset + 3] != stockGeometry[y * width + x])
                        Fail(x, y, 3, "Stock image alpha differs from the genuine geometry coverage control.");
                    if (actual[offset + 3] != actualGeometry[y * width + x])
                        Fail(x, y, 3, "Portable image alpha differs from the genuine geometry coverage control.");
                }
                for (int channel = 0; channel < 4; channel++)
                {
                    int a = actual[offset + channel], e = stock[offset + channel];
                    int difference = Math.Abs(a - e);
                    if (difference != 0) different++;
                    maximum = Math.Max(maximum, difference);
                    if (interior) interiorMaximum = Math.Max(interiorMaximum, difference);
                    else edgeMaximum = Math.Max(edgeMaximum, difference);
                    if (channel < 3 && a > actual[offset + 3]) Fail(x, y, channel, "Color is not premultiplied by alpha.");
                    if (portableCoverage == 0 && stockCoverage == 0)
                    {
                        if (a != 0 || e != 0) Fail(x, y, channel, "The exterior is not exactly transparent.");
                    }
                    else if (unpaired && uniform != null)
                    {
                        if (Math.Abs(a - uniform[channel] * portableCoverage) > 1 || Math.Abs(e - uniform[channel] * stockCoverage) > 1)
                            Fail(x, y, channel, "Uncovered edge color differs from the separately known uniform source color and coverage.");
                    }
                    else if (interior)
                    {
                        if (difference > colorLimit) Fail(x, y, channel, $"Linear color difference {difference} exceeds the measured {colorLimit}-value bound.");
                    }
                    else
                    {
                        // Cross-multiplication preserves premultiplied error bounds without dividing by small alpha.
                        double error = Math.Abs(a * stockCoverage - e * portableCoverage);
                        double bound = colorLimit * stockCoverage * portableCoverage + stockCoverage + portableCoverage;
                        if (error > bound + 1e-6) Fail(x, y, channel, $"Coverage-weighted color error {error} exceeds {bound}.");
                    }
                }
            }
        return new Statistics(different, maximum, interiorMaximum, edgeMaximum, 0, failures, firstFailure);
    }

    private static void ValidateGeometryControls(byte[] actual, byte[] stock, BitmapSamplingCases.RasterCase sample,
        int width, int height, Action<int, int, string> fail)
    {
        var rectangle = DeviceBounds(sample.Rectangle, sample, sample.Guided);
        if (sample.Clip is System.Windows.Rect clip)
        {
            var clipped = DeviceBounds(clip, sample, false);
            rectangle = (Math.Max(rectangle.LeftLow, clipped.LeftLow), Math.Max(rectangle.LeftHigh, clipped.LeftHigh),
                Math.Min(rectangle.RightLow, clipped.RightLow), Math.Min(rectangle.RightHigh, clipped.RightHigh),
                Math.Max(rectangle.TopLow, clipped.TopLow), Math.Max(rectangle.TopHigh, clipped.TopHigh),
                Math.Min(rectangle.BottomLow, clipped.BottomLow), Math.Min(rectangle.BottomHigh, clipped.BottomHigh));
        }
        int interiorPixels = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // Pixel cells wholly inside every possible rectangle have full coverage; disjoint cells have none.
                // The remaining boundary cells keep each backend's own AA values rather than comparing edge alpha.
                bool interior = x >= rectangle.LeftHigh && x + 1 <= rectangle.RightLow &&
                    y >= rectangle.TopHigh && y + 1 <= rectangle.BottomLow;
                bool exterior = x + 1 <= rectangle.LeftLow || x >= rectangle.RightHigh ||
                    y + 1 <= rectangle.TopLow || y >= rectangle.BottomHigh;
                if (!interior && !exterior) continue;
                if (interior) interiorPixels++;
                byte expected = interior ? (byte)255 : (byte)0;
                int index = y * width + x;
                if (actual[index] != expected) fail(x, y, $"Portable geometry control violates independent {(interior ? "interior" : "exterior")} coverage.");
                if (stock[index] != expected) fail(x, y, $"Stock geometry control violates independent {(interior ? "interior" : "exterior")} coverage.");
            }
        if (interiorPixels == 0)
            throw new InvalidOperationException($"{sample.Name}: the measured coverage profile requires a geometrically guaranteed interior pixel.");
    }

    private static (double LeftLow, double LeftHigh, double RightLow, double RightHigh,
        double TopLow, double TopHigh, double BottomLow, double BottomHigh) DeviceBounds(
        System.Windows.Rect rectangle, BitmapSamplingCases.RasterCase sample, bool guided)
    {
        static Interval Position(double coordinate, double scale, double offset, double pan, double dpi)
        {
            Interval worldScale = Interval.Multiply(Interval.Point((float)dpi), Interval.Point((float)scale));
            Interval translation = Interval.Add(Interval.Multiply(Interval.Point((float)dpi), Interval.Point((float)offset)),
                Interval.Multiply(Interval.Point((float)dpi), Interval.Point((float)pan)));
            Interval composed = Interval.Add(Interval.Multiply(worldScale, Interval.Point((float)coordinate)), translation);
            double direct = (float)((coordinate * scale + offset + pan) * dpi);
            return new Interval(Math.Min(composed.Low, direct), Math.Max(composed.High, direct));
        }
        Interval x0 = Position(rectangle.Left, sample.Transform.M11, sample.Transform.OffsetX, sample.Pan.X, sample.Dpi.DpiScaleX);
        Interval x1 = Position(rectangle.Right, sample.Transform.M11, sample.Transform.OffsetX, sample.Pan.X, sample.Dpi.DpiScaleX);
        Interval y0 = Position(rectangle.Top, sample.Transform.M22, sample.Transform.OffsetY, sample.Pan.Y, sample.Dpi.DpiScaleY);
        Interval y1 = Position(rectangle.Bottom, sample.Transform.M22, sample.Transform.OffsetY, sample.Pan.Y, sample.Dpi.DpiScaleY);
        // Static rectangle guidelines move either edge by at most half a device pixel.
        double movement = guided ? 0.5 : 0;
        return (Math.Min(x0.Low, x1.Low) - movement, Math.Min(x0.High, x1.High) + movement,
            Math.Max(x0.Low, x1.Low) - movement, Math.Max(x0.High, x1.High) + movement,
            Math.Min(y0.Low, y1.Low) - movement, Math.Min(y0.High, y1.High) + movement,
            Math.Max(y0.Low, y1.Low) - movement, Math.Max(y0.High, y1.High) + movement);
    }

    private static byte[]? UniformPremultipliedSource(BitmapSamplingCases.RasterCase sample)
    {
        if (sample.Source.Format != PixelFormats.Pbgra32) return null;
        byte[] bytes = sample.SourceBytes;
        for (int index = 4; index < bytes.Length; index++)
            if (bytes[index] != bytes[index % 4]) return null;
        return bytes.AsSpan(0, 4).ToArray();
    }

    private static void EnsureAxisAligned(BitmapSamplingCases.RasterCase sample)
    {
        if (sample.Transform.M12 != 0 || sample.Transform.M21 != 0)
            throw new InvalidOperationException("The measured sampling profile requires axis-aligned geometry.");
    }

}
