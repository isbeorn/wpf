using System.Runtime.CompilerServices;
using System.Text.Json;
using Accord.Imaging;
using Accord.Imaging.Filters;
using DrawingFormat = System.Drawing.Imaging.PixelFormat;

internal static partial class Program {
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunAccordCases(string directory, bool writeReference, bool packageRuntime) {
        if (writeReference && !OperatingSystem.IsWindows()) throw new InvalidOperationException("Only original Windows Accord supplies references.");
        Directory.CreateDirectory(directory);
        Check("Accord-PixelFormat-all-named-unknown-negative-flags-and-subbyte-metadata", () => AccordPixelFormats(directory, writeReference));
        foreach (string filter in new[] { "gaussian", "median", "resize", "blobs" }) {
            Check("Accord-borrowed-odd-width-padded-Gray8-" + filter, () => AccordBorrowedBuffer(directory, writeReference, filter));
        }
        if (!packageRuntime) Check("Accord-exported-contract-matches-original", () => PublicContract(directory, writeReference, typeof(UnmanagedImage).Assembly, "accord-public-contract.json"));
        if (packageRuntime) Check("Accord-package-does-not-load-GdiPlus", () => Require(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "System.Private.Windows.GdiPlus"), "Accord metadata or borrowed kernels loaded GdiPlus."));
    }

    private static unsafe void AccordPixelFormats(string directory, bool write) {
        var formats = Enum.GetValues<DrawingFormat>().Concat(new[] { (DrawingFormat)(-1), (DrawingFormat)int.MinValue, (DrawingFormat)(-256), (DrawingFormat)0x12345678, (DrawingFormat)0x7fffffff }).Distinct();
        var values = new List<object>();
        byte sample = 0;
        foreach (var format in formats) {
            using var view = new UnmanagedImage((IntPtr)(&sample), 1, 1, 1, format);
            int bits = Accord.Imaging.Image.GetPixelFormatSize(format);
            int bytes = Accord.Imaging.Image.GetPixelFormatSizeInBytes(format);
            Require(bytes == bits / 8 && view.PixelSize == bytes, "Accord bit, byte and borrowed-view metadata disagree.");
            values.Add(new { format = (int)format, bits, bytes, pixelSize = view.PixelSize });
        }
        Reference(Path.Combine(directory, "accord-pixel-formats.json"), JsonSerializer.Serialize(values, scienceJson), write);
    }

    private static unsafe void AccordBorrowedBuffer(string directory, bool write, string filter) {
        const int width = 7, height = 5, stride = 8;
        var input = Enumerable.Repeat((byte)0xA7, stride * height).ToArray();
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) input[y * stride + x] = filter == "blobs"
            ? (byte)(y is 1 or 2 && (x is 0 or 1 or 4 or 5) ? 255 : 0)
            : (byte)((x * 37 + y * 53 + (x % 2) * 71) % 256);
        var before = input.ToArray();
        int outWidth = filter == "resize" ? 3 : width, outHeight = filter == "resize" ? 3 : height;
        int outStride = (outWidth + 3) & ~3;
        var output = Enumerable.Repeat((byte)0xCB, outStride * outHeight).ToArray();
        fixed (byte* sourcePointer = input) fixed (byte* outputPointer = output) {
            using var source = new UnmanagedImage((IntPtr)sourcePointer, width, height, stride, DrawingFormat.Format8bppIndexed);
            if (filter == "blobs") {
                var blobs = new BlobCounter();
                blobs.ProcessImage(source);
                var objects = blobs.GetObjectsInformation();
                Require(objects.Length == 2 && objects.All(blob => blob.Area == 4), "Borrowed blob detection lost distinct components.");
                Reference(Path.Combine(directory, "accord-blobs.json"), JsonSerializer.Serialize(objects, scienceJson), write);
            } else {
                using var destination = new UnmanagedImage((IntPtr)outputPointer, outWidth, outHeight, outStride, DrawingFormat.Format8bppIndexed);
                if (filter == "gaussian") new GaussianBlur(1.4, 5).Apply(source, destination);
                else if (filter == "median") new Median().Apply(source, destination);
                else new ResizeBicubic(outWidth, outHeight).Apply(source, destination);
                for (int y = 0; y < outHeight; y++) Require(output.AsSpan(y * outStride + outWidth, outStride - outWidth).ToArray().All(value => value == 0xCB), "A borrowed kernel overwrote destination padding.");
                ReferenceBytes(Path.Combine(directory, "accord-" + filter + ".gray8"), output, write);
            }
        }
        Require(input.SequenceEqual(before), "A borrowed kernel modified source samples or padding.");
    }
}
