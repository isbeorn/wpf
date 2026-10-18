using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class BitmapSamplingCorpus
{
    internal static IEnumerable<BitmapSamplingCases.RasterCase> AlphaEdgeCases()
    {
        foreach (byte alpha in new byte[] { 0, 1, 16, 128, 255 })
        {
            byte[] bytes = new byte[16];
            for (int index = 0; index < bytes.Length; index += 4)
            {
                bytes[index] = (byte)(alpha / 2);
                bytes[index + 1] = (byte)(alpha / 4);
                bytes[index + 2] = (byte)(alpha * 3 / 4);
                bytes[index + 3] = alpha;
            }
            BitmapSource source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null, bytes, 8);
            yield return new BitmapSamplingCases.RasterCase($"sampling-uncovered-alpha-{alpha}", source, new Rect(0, 0, 9, 7),
                new Vector(0.9375, 0.9375), new DpiScale(1, 1), BitmapScalingMode.Linear, Matrix.Identity, bytes);
        }
    }

    internal static IEnumerable<BitmapSamplingCases.RasterCase> LargeCases()
    {
        const int width = 6000, height = 4000;
        foreach (PixelFormat format in new[] { PixelFormats.Gray16, PixelFormats.Rgb48, PixelFormats.Pbgra32 })
        {
            bool gray = format == PixelFormats.Gray16, rgb = format == PixelFormats.Rgb48;
            int bytesPerPixel = format.BitsPerPixel / 8, stride = width * bytesPerPixel;
            byte[] input = new byte[height * stride];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int offset = y * stride + x * bytesPerPixel;
                    if (gray)
                    {
                        ushort value = (ushort)(((long)x * 37 + (long)y * 97) * 997 & 65535);
                        input[offset] = (byte)value;
                        input[offset + 1] = (byte)(value >> 8);
                    }
                    else if (rgb)
                    {
                        // High bytes identify adjacent X/Y coordinates; low bytes still carry independent 16-bit source values.
                        input[offset] = (byte)(x * 17 + y * 13);
                        input[offset + 1] = (byte)x;
                        input[offset + 2] = (byte)(x * 19 + y * 11);
                        input[offset + 3] = (byte)y;
                        input[offset + 4] = (byte)(x * 23 + y * 7);
                        input[offset + 5] = (byte)(x * 37 + y * 17);
                    }
                    else
                    {
                        int alpha = ((x / 251 + y / 257) % 6) switch { 0 => 0, 1 => 1, 2 => 16, 3 => 64, 4 => 128, _ => 255 };
                        input[offset] = (byte)(((x * 37 + y * 17) & 255) * alpha / 255);
                        input[offset + 1] = (byte)((y & 255) * alpha / 255);
                        input[offset + 2] = (byte)((x & 255) * alpha / 255);
                        input[offset + 3] = (byte)alpha;
                    }
                }
            byte[] expected = input.ToArray();
            string sourceHash = Convert.ToHexString(SHA256.HashData(expected));
            BitmapSource source = BitmapSource.Create(width, height, 96, 96, format, null, input, stride);
            Array.Clear(input);
            source.CopyPixels(input, stride, 0);
            if (!input.SequenceEqual(expected)) throw new InvalidOperationException($"Large {format} source bytes or caller ownership changed.");
            foreach (BitmapScalingMode mode in new[] { BitmapScalingMode.Linear, BitmapScalingMode.NearestNeighbor })
                foreach (var view in new[]
                {
                    (Name: "fit", Rectangle: new Rect(0, 0, 1184, 4000 * 1184d / 6000), Pan: new Vector(0, (811 - 4000 * 1184d / 6000) / 2), Dpi: new DpiScale(1, 1), Transform: Matrix.Identity, Clip: (Rect?)null, Guided: false),
                    (Name: "positive-pan", Rectangle: new Rect(0, 0, width, height), Pan: new Vector(3.375, 2.25), Dpi: new DpiScale(1, 1), Transform: Matrix.Identity, Clip: (Rect?)null, Guided: false),
                    (Name: "negative-pan", Rectangle: new Rect(0, 0, width, height), Pan: new Vector(-3000.375, -2000.25), Dpi: new DpiScale(1, 1), Transform: Matrix.Identity, Clip: (Rect?)null, Guided: false),
                    (Name: "flip-xy", Rectangle: new Rect(0, 0, width, height), Pan: new Vector(-2800.625, -1800.75), Dpi: new DpiScale(1, 1), Transform: new Matrix(-1, 0, 0, -1, width, height), Clip: (Rect?)null, Guided: false),
                    (Name: "dpi", Rectangle: new Rect(0, 0, width, height), Pan: new Vector(-3000.375, -2000.25), Dpi: new DpiScale(1.5, 1.25), Transform: Matrix.Identity, Clip: (Rect?)null, Guided: false),
                    (Name: "clip", Rectangle: new Rect(0, 0, width, height), Pan: new Vector(-3000.375, -2000.25), Dpi: new DpiScale(1, 1), Transform: Matrix.Identity, Clip: (Rect?)new Rect(3012.375, 2009.25, 1000, 700), Guided: false),
                    (Name: "guidelines", Rectangle: new Rect(0.25, 0.25, 1183, 789), Pan: new Vector(0.375, 10.25), Dpi: new DpiScale(1, 1), Transform: Matrix.Identity, Clip: (Rect?)null, Guided: true)
                })
                    yield return new BitmapSamplingCases.RasterCase($"sampling-large-{format}-{mode}-{view.Name}", source, view.Rectangle,
                        view.Pan, view.Dpi, mode, view.Transform, expected)
                    {
                        SurfaceWidth = 1184, SurfaceHeight = 811, Guided = view.Guided, Clip = view.Clip, SourceHash = sourceHash,
                        LinearColorLimit = view.Name == "guidelines" ? 3 : rgb && (view.Name is "fit" or "dpi") ? 2 : rgb ? 1 : 2
                    };
        }
    }
}
