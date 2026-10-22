using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;

internal static class NinaReferenceEnvironment
{
    [ModuleInitializer]
    internal static void ConfigureArtifactPaths()
    {
        string directory = Environment.GetEnvironmentVariable("NINA_WINDOWS_REFERENCE_STATE")
            ?? throw new InvalidOperationException("The baseline runner must supply an isolated state directory.");
        Directory.CreateDirectory(directory);
        NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH = directory;
        NINA.Profile.ProfileService.PROFILEFOLDER = Path.Combine(directory, "Profiles");
    }
}

[SetUpFixture]
public sealed class AssemblyEvidence
{
    [OneTimeTearDown]
    public void WriteLoadedAssemblies()
    {
        string? destination = Environment.GetEnvironmentVariable("NINA_WINDOWS_REFERENCE_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(destination))
        {
            Assert.Fail("The baseline runner must supply an assembly evidence destination.");
            return;
        }

        string[] names = ["WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml", "NINA", "NINA.WPF.Base", "NINA.CustomControlLibrary", "OxyPlot.Wpf"];
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && names.Contains(assembly.GetName().Name, StringComparer.OrdinalIgnoreCase))
            .Select(assembly => new
            {
                name = assembly.GetName().Name,
                version = assembly.GetName().Version?.ToString(),
                publicKeyToken = Convert.ToHexString(assembly.GetName().GetPublicKeyToken() ?? []),
                fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion,
                path = assembly.Location
            }).OrderBy(assembly => assembly.name).ToArray();
        File.WriteAllText(destination, JsonSerializer.Serialize(new
        {
            probe = "NinaWindowsReferenceAssemblies",
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription,
            scope = "Pinned NINA view tests on Windows stock WPF 10.0.12; native payload omitted; not this fork or whole NINA baseline",
            compilerOrigin = "Microsoft.NET.Sdk.WindowsDesktop 10.0.401; pinned NINA XAML compiled without source changes",
            assemblies
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
