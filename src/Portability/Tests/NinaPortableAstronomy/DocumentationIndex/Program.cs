using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using NINA.Core.Utility;

internal static class Program {
    private static int Main(string[] args) {
        if (args.Length != 1) {
            Console.Error.WriteLine("Specify an absolute application directory or --assembly-directory.");
            return 2;
        }
        string original = CoreUtil.APPLICATIONDIRECTORY;
        string requested = args[0] == "--assembly-directory" ? original : args[0];
        if (!Path.IsPathFullyQualified(requested)) {
            Console.Error.WriteLine("Application directory must be fully qualified.");
            return 2;
        }
        string root = Path.GetFullPath(requested);
        string assemblyPath = typeof(CoreUtil).Assembly.Location;
        Console.WriteLine(JsonSerializer.Serialize(new {
            kind = "intake", os = Environment.OSVersion.ToString(), root,
            originalRoot = original, assignment = args[0] != "--assembly-directory",
            coreIdentity = typeof(CoreUtil).Assembly.FullName, corePath = assemblyPath,
            coreSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))),
            documentationExists = Directory.Exists(Path.Combine(root, "docs"))
        }));
        try {
            if (args[0] != "--assembly-directory") { CoreUtil.APPLICATIONDIRECTORY = root; }
            return VerifyGetters(root);
        } finally {
            CoreUtil.APPLICATIONDIRECTORY = original;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int VerifyGetters(string root) {
        var cases = new (string Name, string[] Relative, string Fragment, Func<Uri> Get)[] {
            (nameof(DocumentationIndex.Tabs_Framing_General), ["docs", "tabs", "framing.html"], "", () => DocumentationIndex.Tabs_Framing_General),
            (nameof(DocumentationIndex.Tabs_FlatWizard_General), ["docs", "tabs", "flatwizard.html"], "", () => DocumentationIndex.Tabs_FlatWizard_General),
            (nameof(DocumentationIndex.Advanced_MeridianFlip), ["docs", "advanced", "meridianflip.html"], "", () => DocumentationIndex.Advanced_MeridianFlip),
            (nameof(DocumentationIndex.Tabs_Options_General), ["docs", "tabs", "options", "general.html"], "#general", () => DocumentationIndex.Tabs_Options_General),
            (nameof(DocumentationIndex.Tabs_Options_AstrometrySettings), ["docs", "tabs", "options", "general.html"], "#astrometry-settings", () => DocumentationIndex.Tabs_Options_AstrometrySettings),
            (nameof(DocumentationIndex.Tabs_Options_Autofocus), ["docs", "tabs", "options", "autofocus.html"], "#general-settings", () => DocumentationIndex.Tabs_Options_Autofocus),
            (nameof(DocumentationIndex.Tabs_Options_MeridianFlip), ["docs", "tabs", "options", "imaging.html"], "#auto-meridian-flip", () => DocumentationIndex.Tabs_Options_MeridianFlip),
            (nameof(DocumentationIndex.Tabs_Options_FileSettings), ["docs", "tabs", "options", "imaging.html"], "#file-settings", () => DocumentationIndex.Tabs_Options_FileSettings),
            (nameof(DocumentationIndex.Tabs_Options_ImageOptions), ["docs", "tabs", "options", "imaging.html"], "#image-options", () => DocumentationIndex.Tabs_Options_ImageOptions),
            (nameof(DocumentationIndex.Tabs_Options_Sequence), ["docs", "tabs", "options", "imaging.html"], "#sequence", () => DocumentationIndex.Tabs_Options_Sequence),
            (nameof(DocumentationIndex.Tabs_Options_Layout), ["docs", "tabs", "options", "imaging.html"], "#layout", () => DocumentationIndex.Tabs_Options_Layout),
            (nameof(DocumentationIndex.Tabs_Options_PlateSolving), ["docs", "tabs", "options", "platesolving.html"], "#plate-solving", () => DocumentationIndex.Tabs_Options_PlateSolving),
            (nameof(DocumentationIndex.Tabs_Options_Dome), ["docs", "tabs", "options", "dome.html"], "#mount-type", () => DocumentationIndex.Tabs_Options_Dome)
        };
        int failed = 0;
        foreach (var test in cases) {
            string expectedPath = Path.GetFullPath(Path.Combine([root, .. test.Relative]));
            try {
                Uri uri = test.Get();
                bool pass = uri.IsAbsoluteUri && uri.IsFile &&
                    StringComparer.Ordinal.Equals(uri.LocalPath, expectedPath) &&
                    StringComparer.Ordinal.Equals(uri.Fragment, test.Fragment);
                if (!pass) { failed++; }
                Console.WriteLine(JsonSerializer.Serialize(new {
                    kind = "getter", test.Name, pass, expectedPath, expectedFragment = test.Fragment,
                    uri = uri.OriginalString, uri.IsAbsoluteUri, uri.IsFile, uri.LocalPath, uri.Fragment
                }));
            } catch (Exception error) {
                failed++;
                Console.WriteLine(JsonSerializer.Serialize(new {
                    kind = "getter", test.Name, pass = false, expectedPath, expectedFragment = test.Fragment,
                    error = error.ToString()
                }));
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "result", total = cases.Length, passed = cases.Length - failed, failed }));
        return failed == 0 ? 0 : 1;
    }
}
