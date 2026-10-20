using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

internal static partial class Program
{
    internal static string TestAssetDirectory => Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
    private static ReleasedInputs released = null!;
    private static void VerifyPrivateLinux(string[] args)
    {
        Require(OperatingSystem.IsLinux() && args.Length == 2, "Expected private Linux fixture input path and reviewed SHA256.");
        Require(Hash(args[0]) == args[1], "Released fixture inputs differ.");
        released = JsonSerializer.Deserialize<ReleasedInputs>(File.ReadAllText(args[0]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        string display = Environment.GetEnvironmentVariable("DISPLAY") ?? "";
        Require(display.StartsWith(':') && display == Environment.GetEnvironmentVariable("NINA_FRAMING_PRIVATE_DISPLAY"), "Dedicated Xvfb display required.");
        Require(Environment.Version == new Version(10, 0, 12), "Expected exact CLR10.0.12.");
    }
    private static void VerifyReleasedInputs()
    {
        Require(released.FrameworkFiles == 120 && released.Files.Count > 120, "Expected whole120 framework and coherent NINA consumer inputs.");
        foreach (ReleasedFile file in released.Files)
        {
            Require(Hash(file.Path) == file.Sha256, "Released input differs: " + file.Path);
            if (file.Identity != null) Require(AssemblyName.GetAssemblyName(file.Path).FullName == file.Identity, "Released identity differs: " + file.Path);
        }
        Require(released.Files.Single(f => f.Path == Path.Combine(released.ConsumerDirectory, "NINA.WPF.Base.dll")).Sha256 == "41955DF0ECB7D5D442403F0535967EE329DB470010F75AED73EBB7AA14A4A973", "Expected unchanged source-proven Base component4.");
        observations.Add(new { stage = "released-inputs", released.ConsumerDirectory, released.FrameworkDirectory, released.FrameworkFiles, files = released.Files.Count, released.RuntimeIntakeSha256, released.ComponentIntakeSha256 });
    }
    private static void VerifyReleasedRuntime()
    {
        foreach (string name in new[] { "WindowsBase", "System.Xaml", "PresentationCore", "PresentationFramework", "System.Windows.Extensions", "Wpf.Portable.NativeHost" })
        {
            Assembly assembly = Assembly.Load(name);
            ReleasedFile expected = released.Files.Single(f => f.Path == Path.Combine(released.FrameworkDirectory, name + ".dll"));
            Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default && assembly.Location == expected.Path && Hash(assembly.Location) == expected.Sha256, "Unexpected WPF runtime member: " + name);
            observations.Add(new { runtime = assembly.FullName, path = assembly.Location, sha256 = expected.Sha256 });
        }
    }
    private static IDisposable InitializeNative()
    {
        var fixture = new X11Fixture("little");
        try { Wpf.Portable.NativeHost.NativeWindowHost.InitializeForCurrentThread(); return fixture; }
        catch { fixture.Dispose(); throw; }
    }
    private static string[] ExpectedResourceSources() => released.ResourceSources;
    private sealed class ReleasedInputs
    {
        public string ConsumerDirectory { get; set; } = "";
        public string FrameworkDirectory { get; set; } = "";
        public int FrameworkFiles { get; set; }
        public string RuntimeIntakeSha256 { get; set; } = "";
        public string ComponentIntakeSha256 { get; set; } = "";
        public List<ReleasedFile> Files { get; set; } = [];
        public string[] ResourceSources { get; set; } = [];
    }
    private sealed class ReleasedFile
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string? Identity { get; set; }
    }
}