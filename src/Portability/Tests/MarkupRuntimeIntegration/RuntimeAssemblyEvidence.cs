using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

internal static class RuntimeAssemblyEvidence
{
    internal static string Describe(Assembly assembly)
    {
        if (AssemblyLoadContext.GetLoadContext(assembly) != AssemblyLoadContext.Default)
            throw new InvalidDataException($"Assembly did not use the default load context: {assembly.FullName}");
        return $"{assembly.Location}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))}; loadContext=Default";
    }

    internal static string ExtensionsAndHost()
    {
        const string expectedHash = "54163AEF0B78412550C50FFE0826E53B6F39DD3CC718D1EB9D16B73C27FD75F9";
        var assembly = Assembly.Load("System.Windows.Extensions");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
        if (hash != expectedHash || Path.GetDirectoryName(assembly.Location) != AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))
            throw new InvalidDataException("The unchanged official System.Windows.Extensions 10.0.13 win/net10.0 managed asset did not load app-locally.");
        var entryName = Assembly.GetEntryAssembly()!.GetName().Name!;
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, entryName + ".runtimeconfig.json")));
        var options = config.RootElement.GetProperty("runtimeOptions");
        var frameworks = options.TryGetProperty("framework", out var single)
            ? new[] { single.GetProperty("name").GetString() }
            : options.GetProperty("frameworks").EnumerateArray().Select(framework => framework.GetProperty("name").GetString()).ToArray();
        if (!frameworks.SequenceEqual(new[] { "Microsoft.NETCore.App" }))
            throw new InvalidDataException("The runtime gate requires only Microsoft.NETCore.App, without WindowsDesktop framework loading.");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, entryName + ".deps.json")));
        var assets = deps.RootElement.GetProperty("targets").EnumerateObject()
            .Where(target => target.Value.TryGetProperty("System.Windows.Extensions/10.0.13", out _))
            .SelectMany(target => target.Value.GetProperty("System.Windows.Extensions/10.0.13").GetProperty("runtime").EnumerateObject())
            .Select(asset => asset.Name).Distinct().ToArray();
        if (!assets.SequenceEqual(new[] { "runtimes/win/lib/net10.0/System.Windows.Extensions.dll" }))
            throw new InvalidDataException("Dependency metadata does not select the qualified official managed XAML access asset: " + string.Join(", ", assets));
        return $"{assembly.FullName}: {Describe(assembly)}; NuGet=System.Windows.Extensions/10.0.13; asset={assets.Single()}; sharedFramework={frameworks.Single()}";
    }
}
