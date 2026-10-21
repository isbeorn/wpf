using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Security.Cryptography;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Image.FileFormat;
using NINA.Image.FileFormat.FITS;
using NINA.Image.ImageData;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;

internal static partial class Program {
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunFitsCases(string directory) {
        Directory.CreateDirectory(directory);
        CoreUtil.APPLICATIONTEMPPATH = directory;
        Check("default-legacy-ushort-FITS-save-and-original-load", () => {
            ushort[] raw = FitsSamples3x5();
            var before = raw.ToArray();
            var metadata = FitsMetadata();
            var source = FitsImage(raw, 3, 5, metadata);
            var save = new FileSaveInfo { FilePath = directory, FilePattern = "legacy-3x5" };
            Require(save.FITSUseLegacyWriter && save.FITSCompressionType == FITSCompressionTypeEnum.NONE, "Default FITS writer changed.");
            string path = source.SaveToDisk(save, CancellationToken.None).GetAwaiter().GetResult();
            Require(path.EndsWith(".fits", StringComparison.Ordinal), "Default FITS extension changed.");
            var loaded = FITS.Load(new Uri(path), false, new FitsFactory(), CancellationToken.None).GetAwaiter().GetResult();
            Require(loaded.Properties.Width == 3 && loaded.Properties.Height == 5 && loaded.Data.FlatArray.SequenceEqual(before), "Legacy FITS dimensions or ushort samples changed.");
            Require(loaded.MetaData.Target.Name == metadata.Target.Name && loaded.MetaData.Image.ExposureTime == 1.25 && loaded.MetaData.Camera.Gain == 139, "FITS metadata changed.");
            AssertFitsPixels(path, before, 3, 5);
            Require(raw.SequenceEqual(before) && source.Data.FlatArray.SequenceEqual(before), "FITS save/load changed the source array.");
            if (OperatingSystem.IsLinux()) {
                string expected = Path.Combine(AppContext.BaseDirectory, "External", "x64", "Cfitsio", "libcfitsio.so.10");
                Require(Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(module => module.FileName == expected), "The owned packaged CFITSIO library was not loaded.");
                Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expected))) == "4558BDC93601DAB4ADCA9FB36502198BC98C926BA9BB7038A417E6A4E17420E8", "The loaded CFITSIO artifact differs from the qualified prototype.");
            }
        });
        foreach (var profile in new[] { ("native-none", FITSCompressionTypeEnum.NONE, false), ("rice-fits", FITSCompressionTypeEnum.RICE, false), ("rice-fz", FITSCompressionTypeEnum.RICE, true) }) {
            Check("ushort-17x3-" + profile.Item1 + "-roundtrip-and-row-order", () => {
                var (path, raw) = SaveFitsFixture(directory, profile.Item1, profile.Item2, profile.Item3);
                Require(path.EndsWith(profile.Item3 ? ".fits.fz" : ".fits", StringComparison.Ordinal), "FITS compression extension changed.");
                AssertFitsPixels(path, raw, 17, 3);
            });
        }
        Check("failed-compressed-reader-releases-file-and-temporary-output-then-recovers", () => {
            var (valid, raw) = SaveFitsFixture(directory, "reader-recovery", FITSCompressionTypeEnum.RICE, true);
            string broken = Path.Combine(directory, "truncated-compressed.fits.fz");
            byte[] bytes = File.ReadAllBytes(valid);
            File.WriteAllBytes(broken, bytes[..(bytes.Length - FITS.BLOCKSIZE)]);
            var before = Directory.GetFiles(Path.GetTempPath(), "*.fits").Order().ToArray();
            bool failed = false;
            try { using var reader = new CFitsioFITSReader(broken); _ = reader.ReadAllPixels<ushort>(); }
            catch (CfitsioNative.cfitsioException) { failed = true; }
            Require(failed, "Truncated compressed FITS was accepted.");
            failed = false;
            try { using var reader = new ConstructorCleanupReader(broken); }
            catch (CfitsioNative.cfitsioException) { failed = true; }
            Require(failed, "A reader override intercepted failed-constructor ownership cleanup.");
            Require(Directory.GetFiles(Path.GetTempPath(), "*.fits").Order().SequenceEqual(before), "Failed compressed reader leaked its temporary FITS output.");
            Require(OpenFitsDescriptors(broken) == 0, "Failed reader retained the source file handle.");
            AssertFitsPixels(valid, raw, 17, 3);
        });
        Check("failed-writer-releases-created-file-without-reading-outside-source", () => {
            string path = Path.Combine(directory, "invalid-source-length.fits");
            ushort[] raw = [65535];
            bool failed = false;
            try { var writer = new CFitsioFITS(path, raw, 17, 3); writer.Close(); }
            catch (ArgumentException) { failed = true; }
            Require(failed && raw[0] == 65535, "Writer accepted dimensions larger than the source buffer.");
            Require(OpenFitsDescriptors(path) == 0, "Failed writer retained its created file handle.");
            string floatPath = Path.Combine(directory, "unsupported-float-writer.fits");
            float[] floats = [0f, .25f, .5f, 1f];
            var floatBefore = floats.ToArray();
            failed = false;
            try { var writer = new CFitsioFITS(floatPath, floats, 2, 2); writer.Close(); }
            catch (NotSupportedException) { failed = true; }
            Require(failed && floats.SequenceEqual(floatBefore), "The unsafe float writer was accepted or changed its source.");
            Require(OpenFitsDescriptors(floatPath) == 0, "Rejected float writer retained its created file handle.");
            var (valid, expected) = SaveFitsFixture(directory, "writer-recovery", FITSCompressionTypeEnum.NONE, false);
            AssertFitsPixels(valid, expected, 17, 3);
        });
        Check("failed-writer-creation-preserves-existing-destination", () => {
            string path = Path.Combine(directory, "existing-destination.fits");
            byte[] sentinel = [0, 255, 1, 254, 2, 253, 3, 252, 65, 66, 67];
            File.WriteAllBytes(path, sentinel);
            byte[] hash = SHA256.HashData(sentinel);
            bool failed = false;
            try { var writer = new CFitsioFITS(path, FitsSamples3x5(), 3, 5); writer.Close(); }
            catch (CfitsioNative.cfitsioException) { failed = true; }
            Require(failed && File.ReadAllBytes(path).SequenceEqual(sentinel) && SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash), "Failed writer changed an existing destination.");
            Require(OpenFitsDescriptors(path) == 0, "Rejected existing destination retained a file handle.");
            var (valid, expected) = SaveFitsFixture(directory, "existing-destination-recovery", FITSCompressionTypeEnum.NONE, false);
            AssertFitsPixels(valid, expected, 17, 3);
        });
        Check("malformed-truncated-and-missing-inputs-release-and-recover", () => {
            var (valid, raw) = SaveFitsFixture(directory, "open-recovery", FITSCompressionTypeEnum.NONE, false);
            string malformed = Path.Combine(directory, "malformed.fits"), truncated = Path.Combine(directory, "truncated.fits");
            File.WriteAllText(malformed, "not a FITS file");
            File.WriteAllBytes(truncated, File.ReadAllBytes(valid)[..100]);
            foreach (string broken in new[] { malformed, truncated, Path.Combine(directory, "missing.fits") }) {
                bool failed = false;
                try { using var reader = new CFitsioFITSReader(broken); }
                catch (CfitsioNative.cfitsioException) { failed = true; }
                Require(failed && OpenFitsDescriptors(broken) == 0, "Failed disk open was accepted or retained its file handle.");
                failed = false;
                try { _ = FITS.Load(new Uri(broken), false, new FitsFactory(), CancellationToken.None).GetAwaiter().GetResult(); }
                catch (CfitsioNative.cfitsioException) { failed = true; }
                catch (FileNotFoundException) { failed = true; }
                Require(failed, "Failed memory open was accepted.");
                AssertFitsPixels(valid, raw, 17, 3);
            }
        });
        Check("failed-compressed-memory-load-cleans-temporary-output-and-recovers", () => {
            var (valid, raw) = SaveFitsFixture(directory, "memory-recovery", FITSCompressionTypeEnum.RICE, true);
            string broken = Path.Combine(directory, "truncated-memory.fits.fz");
            byte[] bytes = File.ReadAllBytes(valid);
            File.WriteAllBytes(broken, bytes[..(bytes.Length - FITS.BLOCKSIZE)]);
            var before = Directory.GetFiles(Path.GetTempPath(), "*.fits").Order().ToArray();
            bool failed = false;
            try { _ = FITS.Load(new Uri(broken), false, new FitsFactory(), CancellationToken.None).GetAwaiter().GetResult(); }
            catch (CfitsioNative.cfitsioException) { failed = true; }
            Require(failed && Directory.GetFiles(Path.GetTempPath(), "*.fits").Order().SequenceEqual(before), "Failed compressed memory load accepted invalid data or leaked its temporary FITS output.");
            AssertFitsPixels(valid, raw, 17, 3);
        });
        Check("original-normalized-double-FITS-import-keeps-conversion-and-source", () => {
            double[] raw = [0d, 1d / 65535d, .25d, .5d, 32768d / 65535d, 1d];
            var before = raw.ToArray();
            string path = Path.Combine(directory, "normalized-double.fits");
            var writer = new CFitsioFITS(path, raw, 3, 2);
            try { writer.PopulateHeaderCards(FitsMetadata()); }
            finally { writer.Close(); }
            AssertNormalizedFitsImport(path, CfitsioNative.BITPIX.DOUBLE_IMG);
            using var reader = new CFitsioFITSReader(path);
            Require(reader.ReadAllPixels<double>().SequenceEqual(before) && raw.SequenceEqual(before), "Double FITS import changed its stored values or source array.");
        });
        Check("original-normalized-float-FITS-import-through-safe-typed-write", () => {
            float[] raw = [0f, 1f / 65535f, .25f, .5f, 32768f / 65535f, 1f];
            var before = raw.ToArray();
            string path = Path.Combine(directory, "normalized-float.fits");
            IntPtr pointer = IntPtr.Zero;
            try {
                CfitsioNative.fits_create_diskfile(out pointer, path, out var status);
                CfitsioNative.CheckStatus("fits_create_diskfile", status);
                CfitsioNative.fits_create_img(pointer, CfitsioNative.BITPIX.FLOAT_IMG, 2, [3, 2], out status);
                CfitsioNative.CheckStatus("fits_create_img", status);
                CfitsioNative.fits_write_img_float(pointer, CfitsioNative.DATATYPE.TFLOAT, 1, raw.Length, raw, out status);
                CfitsioNative.CheckStatus("fits_write_img_float", status);
            } finally {
                if (pointer != IntPtr.Zero) CfitsioNative.fits_close_file(pointer, out _);
            }
            AssertNormalizedFitsImport(path, CfitsioNative.BITPIX.FLOAT_IMG);
            using var reader = new CFitsioFITSReader(path);
            Require(reader.ReadAllPixelsAsFloat().SequenceEqual(before) && raw.SequenceEqual(before), "Float FITS import changed its stored values or source array.");
        });
        Check("three-dimensional-FITS-load-keeps-first-plane-and-guards-short-coordinates", () => {
            string path = Path.Combine(directory, "distinct-planes.fits");
            ushort[] raw = [0, 1, 32768, 65535, 54321, 257, 511, 12345];
            var before = raw.ToArray();
            IntPtr pointer = IntPtr.Zero;
            try {
                int status = 0;
                CfitsioNative.fits_create_diskfile(out pointer, path, out status);
                CfitsioNative.CheckStatus("fits_create_diskfile", status);
                CfitsioNative.fits_create_img(pointer, CfitsioNative.BITPIX.USHORT_IMG, 3, [2, 2, 2], out status);
                CfitsioNative.CheckStatus("fits_create_img", status);
                CfitsioNative.fits_write_img(pointer, CfitsioNative.DATATYPE.TUSHORT, 1, raw.Length, raw, out status);
                CfitsioNative.CheckStatus("fits_write_img", status);
                ushort[] output = [42, 42, 42, 42];
                bool rejected = false;
                unsafe {
                    fixed (ushort* buffer = output) {
                        try { CfitsioNative.fits_read_pix(pointer, CfitsioNative.DATATYPE.TUSHORT, [1, 1], output.Length, IntPtr.Zero, (IntPtr)buffer, out _, out _); }
                        catch (ArgumentException) { rejected = true; }
                    }
                }
                Require(rejected && output.All(value => value == 42), "An undersized coordinate vector reached the native read.");
            } finally {
                if (pointer != IntPtr.Zero) { int status = 0; CfitsioNative.fits_close_file(pointer, out status); CfitsioNative.CheckStatus("fits_close_file", status); }
            }
            var fileBefore = File.ReadAllBytes(path);
            var loaded = FITS.Load(new Uri(path), false, new FitsFactory(), CancellationToken.None).GetAwaiter().GetResult();
            Require(loaded.Properties.Width == 2 && loaded.Properties.Height == 2 && loaded.Data.FlatArray.SequenceEqual(before.Take(4)), "Three-dimensional FITS import did not retain the original first-plane contract.");
            Require(raw.SequenceEqual(before) && File.ReadAllBytes(path).SequenceEqual(fileBefore), "First-plane import changed its source samples or FITS file.");
            Require(OpenFitsDescriptors(path) == 0, "First-plane import retained its source file handle.");
        });
        Check("two-independent-FITS-files-roundtrip-concurrently", () => {
            Require(failures == 0, "Concurrency qualification requires the serial baseline to pass.");
            Task.WhenAll(Task.Run(() => {
                var (path, raw) = SaveFitsFixture(directory, "concurrent-none", FITSCompressionTypeEnum.NONE, false);
                AssertFitsPixels(path, raw, 17, 3);
            }), Task.Run(() => {
                var (path, raw) = SaveFitsFixture(directory, "concurrent-rice", FITSCompressionTypeEnum.RICE, true, 0x1555);
                AssertFitsPixels(path, raw, 17, 3);
            })).GetAwaiter().GetResult();
        });
    }

    private static void AssertNormalizedFitsImport(string path, CfitsioNative.BITPIX format) {
        // The unchanged normalized conversion truncates sample * 65535.
        ushort[] expected = [0, 1, 16383, 32767, 32768, 65535];
        var loaded = FITS.Load(new Uri(path), false, new FitsFactory(), CancellationToken.None).GetAwaiter().GetResult();
        Require(loaded.Properties.Width == 3 && loaded.Properties.Height == 2 && loaded.Data.FlatArray.SequenceEqual(expected), "Normalized floating FITS import changed the original ushort conversion.");
        using var reader = new CFitsioFITSReader(path);
        Require(reader.BitPix == format && reader.ReadAllPixelsAsUshort().SequenceEqual(expected), "Reader floating conversion differs from the original load path.");
    }

    private static ushort[] FitsSamples3x5() => [0, 1, 255, 256, 257, 511, 512, 32767, 32768, 32769, 65280, 65534, 65535, 12345, 54321];

    private static void RunLegacyFitsCase(string directory, bool writeReference) {
        Directory.CreateDirectory(directory);
        string work = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory))!, writeReference ? "legacy-stock-state" : "legacy-portable-state");
        Directory.CreateDirectory(work);
        CoreUtil.APPLICATIONTEMPPATH = work;
        Check("default-legacy-FITS-deterministic-stock-bytes-and-source-preservation", () => {
            ushort[] raw = FitsSamples3x5(), before = raw.ToArray();
            var source = FitsImage(raw, 3, 5, FitsMetadata());
            var save = new FileSaveInfo { FilePath = work, FilePattern = "fixed-legacy" };
            Require(save.FITSUseLegacyWriter && save.FITSCompressionType == FITSCompressionTypeEnum.NONE, "Default legacy settings changed.");
            string path = source.SaveToDisk(save, CancellationToken.None).GetAwaiter().GetResult();
            byte[] bytes = File.ReadAllBytes(path);
            Require(bytes.Length % FITS.BLOCKSIZE == 0 && raw.SequenceEqual(before) && source.Data.FlatArray.SequenceEqual(before), "Legacy FITS blocks or source samples changed.");
            ReferenceBytes(Path.Combine(directory, "legacy-3x5.fits"), bytes, writeReference);
            Require(FitsNativeLibraries().Length == 0, "Managed legacy writing unexpectedly loaded CFITSIO.");
        });
    }

    private static (string Path, ushort[] Raw) SaveFitsFixture(string directory, string name, FITSCompressionTypeEnum compression, bool fz, ushort mask = 0) {
        ushort[] levels = [0, 1, 255, 256, 257, 511, 512, 32767, 32768, 32769, 65280, 65534, 65535, 12345, 54321, 42, 60001];
        var raw = Enumerable.Range(0, 51).Select(index => (ushort)(levels[index % 17] ^ (index / 17) ^ mask)).ToArray();
        var before = raw.ToArray();
        var image = FitsImage(raw, 17, 3, FitsMetadata());
        string path = image.SaveToDisk(new FileSaveInfo { FilePath = directory, FilePattern = name, FITSUseLegacyWriter = false, FITSCompressionType = compression, FITSAddFzExtension = fz }, CancellationToken.None).GetAwaiter().GetResult();
        Require(raw.SequenceEqual(before) && image.Data.FlatArray.SequenceEqual(before), "Native FITS writing modified the scientific samples.");
        return (path, raw);
    }

    private static void AssertFitsPixels(string path, ushort[] raw, int width, int height) {
        var loaded = new FitsFactory().CreateFromFile(path, 16, false).GetAwaiter().GetResult();
        Require(loaded.Properties.Width == width && loaded.Properties.Height == height && loaded.Data.FlatArray.SequenceEqual(raw), "FITS load changed dimensions, samples or row order.");
        Require(loaded.MetaData.Target.Name == "FITS boundary" && loaded.MetaData.Image.ExposureTime == 1.25 && loaded.MetaData.Camera.Gain == 139, "Native FITS metadata changed.");
        using var reader = new CFitsioFITSReader(path);
        Require(reader.Width == width && reader.Height == height && reader.ReadAllPixels<ushort>().SequenceEqual(raw), "FITS reader changed dimensions or samples.");
        foreach (int row in new[] { 0, height / 2, height - 1 }) Require(reader.ReadPixelRow<ushort>(row).SequenceEqual(raw.Skip(row * width).Take(width)), "FITS row reader changed row coordinates or samples.");
        if (OperatingSystem.IsLinux()) foreach (int row in new[] { -1, height }) {
            bool failed = false;
            try { _ = reader.ReadPixelRow<ushort>(row); }
            catch (ArgumentOutOfRangeException) { failed = true; }
            Require(failed && reader.ReadPixelRow<ushort>(0).SequenceEqual(raw.Take(width)), "An out-of-range row was accepted or poisoned the reader.");
        }
        reader.Dispose();
        reader.Dispose();
        Require(OpenFitsDescriptors(path) == 0, "Disposed FITS reader retained the file handle.");
    }

    private static int OpenFitsDescriptors(string path) => !OperatingSystem.IsLinux() ? 0 : Directory.EnumerateFiles("/proc/self/fd")
        .Count(fd => new FileInfo(fd).LinkTarget == Path.GetFullPath(path));

    private static object[] FitsNativeLibraries() => Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
        .Where(module => Path.GetFileName(module.FileName).Contains("cfitsio", StringComparison.OrdinalIgnoreCase))
        .Select(module => (object)new { path = module.FileName, sha256 = File.Exists(module.FileName) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module.FileName))) : null }).ToArray();

    private static ImageMetaData FitsMetadata() {
        var metadata = new ImageMetaData();
        metadata.Target.Name = "FITS boundary";
        metadata.Image.ImageType = "LIGHT";
        metadata.Image.ExposureTime = 1.25;
        metadata.Camera.Gain = 139;
        return metadata;
    }

    private static BaseImageData FitsImage(ushort[] raw, int width, int height, ImageMetaData metadata) =>
        new(raw, width, height, 16, false, metadata, null!, new StarDetection(), null!);

    private sealed class FitsFactory : IImageDataFactory {
        public BaseImageData CreateBaseImageData(ushort[] input, int width, int height, int bitDepth, bool isBayered, ImageMetaData metaData) =>
            new(input, width, height, bitDepth, isBayered, metaData, null!, new StarDetection(), null!);
        public BaseImageData CreateBaseImageData(IImageArray input, int width, int height, int bitDepth, bool isBayered, ImageMetaData metaData) =>
            new(input, width, height, bitDepth, isBayered, metaData, null!, new StarDetection(), null!);
        public Task<IImageData> CreateFromFile(string path, int bitDepth, bool isBayered, CancellationToken ct = default) =>
            BaseImageData.FromFile(path, bitDepth, isBayered, null!, this, ct);
        [Obsolete]
        public Task<IImageData> CreateFromFile(string path, int bitDepth, bool isBayered, RawConverterEnum rawConverter, CancellationToken ct = default) =>
            CreateFromFile(path, bitDepth, isBayered, ct);
    }

    private sealed class ConstructorCleanupReader : CFitsioFITSReader {
        internal ConstructorCleanupReader(string path) : base(path) { }
        protected override void Dispose(bool disposing) => throw new InvalidOperationException("Failed construction dispatched virtual Dispose.");
    }
}
