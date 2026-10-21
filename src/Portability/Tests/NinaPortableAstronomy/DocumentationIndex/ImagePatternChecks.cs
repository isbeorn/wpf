using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using NINA.Core.Model;

internal static class ImagePatternChecks {
    internal static int Run(string[] args) {
        if (args.Length != 2) {
            Console.Error.WriteLine("Supply the original NINA.Profile DLL and expected NINA.Core SHA-256.");
            return 2;
        }
        var checks = new List<object>();
        bool failed = false;
        string corePath = typeof(ImagePatterns).Assembly.Location;
        string profilePath = Path.GetFullPath(args[0]);
        string coreHash = Hash(corePath);
        string defaultPattern = "";
        string profileIdentity = "";
        try {
            Require(coreHash == args[1].ToUpperInvariant(), "The loaded Core differs from the selected PE.");
            Require(typeof(ImagePatterns).Assembly.GetName().Version == new Version(3, 3, 0, 1068), "Core identity changed.");
            Require(Hash(profilePath) == "26D5851508B41FF69674FA4F075E0508F14432B73C984F4D9C94260F8D828B72", "The original Profile PE differs.");
            var profileAssembly = Assembly.LoadFrom(profilePath);
            Require(AssemblyLoadContext.GetLoadContext(profileAssembly) == AssemblyLoadContext.Default,
                "The original Profile must use the default load context.");
            profileIdentity = profileAssembly.FullName!;
            var settingsType = profileAssembly.GetType("NINA.Profile.ImageFileSettings", true)!;
            object settings = Activator.CreateInstance(settingsType)!;
            defaultPattern = (string)settingsType.GetProperty("FilePattern")!.GetValue(settings)!;
            var patterns = ImagePatterns.CreateExample();
            string expected = Path.Combine("2015-12-31", "LIGHT", "2016-01-01_12-00-00_L_-15_10.21s_0001");

            Check("actual-default-settings-directories", () => {
                Require(defaultPattern.Contains('\\'), "The genuine default must exercise its persisted Windows separators.");
                Equal(patterns.GetImageFileString(defaultPattern), expected);
            });
            Check("default-forward-separators", () => Equal(patterns.GetImageFileString(defaultPattern.Replace('\\', '/')), expected));
            Check("default-mixed-separators", () => {
                int separator = defaultPattern.IndexOf('\\');
                string mixed = defaultPattern[..separator] + "/" + defaultPattern[(separator + 1)..];
                Equal(patterns.GetImageFileString(mixed), expected);
            });
            Check("default-repeated-separators", () => Equal(
                patterns.GetImageFileString("/\\/" + defaultPattern.Replace("\\", "\\//\\") + "\\/\\"), expected));
            Check("actual-default-type-overrides-and-restoration", () => {
                foreach (string type in new[] { "LIGHT", "DARK", "FLAT", "BIAS" }) {
                    string configured = (string)settingsType.GetMethod("GetFilePattern")!.Invoke(settings, [type])!;
                    Equal(patterns.GetImageFileString(configured, type), Path.Combine("2015-12-31", type,
                        "2016-01-01_12-00-00_L_-15_10.21s_0001"));
                }
                Equal(patterns.GetImageFileString(defaultPattern), expected);
            });
            Check("original-set-sanitization-and-invariant-precision", () => {
                var values = new ImagePatterns();
                Require(values.Set(ImagePatternKeys.Filter, " Ha/OIII ") && values.Set(ImagePatternKeys.ExposureTime, 120.567, 1) &&
                    values.Set(ImagePatternKeys.StarCount, 1234) && !values.Set("$$DOESNOTEXIST$$", "value") &&
                    !values.Set(ImagePatternKeys.HFR, double.NaN), "Original Set results changed.");
                Equal(values.GetImageFileString(ImagePatternKeys.Filter + "_" + ImagePatternKeys.ExposureTime + "_" + ImagePatternKeys.StarCount),
                    "Ha-OIII_120.6_1234");
            });
            Check("original-native-segments-and-image-type-override", () => {
                var values = new ImagePatterns();
                values.Set(ImagePatternKeys.Filter, "Lum"); values.Set(ImagePatternKeys.TargetName, "M31:Core");
                values.Set(ImagePatternKeys.ImageType, "LIGHT");
                Equal(values.GetImageFileString(ImagePatternKeys.Filter + Path.DirectorySeparatorChar +
                    ImagePatternKeys.TargetName + "_" + ImagePatternKeys.ImageType, "DARK"),
                    Path.Combine("Lum", OperatingSystem.IsWindows() ? "M31_Core_DARK" : "M31:Core_DARK"));
            });
            Check("original-custom-token-add-once", () => {
                var values = new ImagePatterns();
                var custom = new ImagePattern("$$SESSION$$", "Session", "Acquisition") { Value = "S01" };
                Require(values.Add(custom) && !values.Add(new ImagePattern("$$SESSION$$", "Duplicate") { Value = "S02" }), "Original Add results changed.");
                Equal(values.GetImageFileString("Target_" + custom.Key), "Target_S01");
            });
            Check("original-example-metadata", () => Equal(patterns.GetImageFileString(
                ImagePatternKeys.TargetName + "_" + ImagePatternKeys.Filter + "_" + ImagePatternKeys.Binning + "_" + ImagePatternKeys.SequenceTitle),
                "M33_L_1x1_SequenceTitle"));
            Check("literal-backslash-custom-value-stays-native", () => {
                var values = new ImagePatterns();
                Require(values.Add(new ImagePattern("$$CUSTOM$$", "Literal") { Value = @"part\name" }), "Custom token was rejected.");
                Equal(values.GetImageFileString(@"date\$$CUSTOM$$\frame"), OperatingSystem.IsWindows()
                    ? Path.Combine("date", "part", "name", "frame") : Path.Combine("date", @"part\name", "frame"));
            });
            Check("metadata-separators-remain-sanitized-filenames", () => {
                var values = new ImagePatterns();
                values.Set(ImagePatternKeys.TargetName, @"M/31\core");
                Equal(values.GetImageFileString(@"date\$$TARGETNAME$$\frame"), Path.Combine("date", "M-31-core", "frame"));
            });
            var original = new ImagePatterns();
            var local = new DateTime(2022, 1, 1, 12, 0, 0, DateTimeKind.Utc).ToLocalTime();
            string date = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string time = local.ToString("HH-mm-ss", CultureInfo.InvariantCulture);
            original.Set(ImagePatternKeys.Date, date); original.Set(ImagePatternKeys.Time, time);
            original.Set(ImagePatternKeys.TargetName, @"C/2020 F3 NEOWISE ?//_\\-A Comet");
            original.Set(ImagePatternKeys.Binning, "1x1"); original.Set(ImagePatternKeys.FrameNr, "-0001");
            string target = OperatingSystem.IsWindows() ? "C-2020 F3 NEOWISE _--_---A Comet" : "C-2020 F3 NEOWISE ?--_---A Comet";
            Check("original-file-pattern-whitespace-example", () => Equal(original.GetImageFileString(
                @"$$DATE$$\Telescope = $$TELESCOPE$$\   Target = $$TARGETNAME$$ \Type = $$IMAGETYPE$$\ Filter = $$FILTER$$\ $$DATE$$ @ $$TIME$$; Target = $$TARGETNAME$$; Type = $$IMAGETYPE$$; Filter = $$FILTER$$; Gain = $$GAIN$$; Bin = $$BINNING$$; Exp = $$EXPOSURETIME$$ s; Temp = $$SENSORTEMP$$ C; Frame # = $$FRAMENR$$ "),
                Path.Combine(date, "Telescope =", "Target = " + target, "Type =", "Filter =",
                    date + " @ " + time + "; Target = " + target + "; Type = ; Filter = ; Gain = ; Bin = 1x1; Exp =  s; Temp =  C; Frame # = -0001")));
            Check("original-string-file-pattern-example", () => Equal(original.GetImageFileString(ImagePatternKeys.TargetName), target));
            Check("original-double-file-pattern-example", () => {
                original.Set(ImagePatternKeys.SQM, 20d); Equal(original.GetImageFileString(ImagePatternKeys.SQM), "20.00");
            });
            Check("original-integer-file-pattern-example", () => {
                original.Set(ImagePatternKeys.Gain, 139); Equal(original.GetImageFileString(ImagePatternKeys.Gain), "139");
            });
        } catch (Exception error) { failed = true; checks.Add(new { name = "fixture-initialization", status = "fail", detail = error.ToString() }); }
        Console.WriteLine(JsonSerializer.Serialize(new { probe = "public-image-patterns", corePath, coreHash, profilePath,
            profileIdentity, defaultPattern, nativeSeparator = Path.DirectorySeparatorChar.ToString(), checks },
            new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;

        void Check(string name, Action action) {
            try { action(); checks.Add(new { name, status = "pass", detail = "Public ImagePatterns result matched the expected native path and tokens." }); }
            catch (Exception error) { failed = true; checks.Add(new { name, status = "fail", detail = error.ToString() }); }
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Equal(string actual, string expected) => Require(actual == expected, $"Expected '{expected}' but observed '{actual}'.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
