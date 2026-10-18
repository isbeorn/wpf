using System.Collections;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var checks = new List<object>();
var failed = false;
void Check(string name, Func<string> action)
{
    try { checks.Add(new { name, status = "pass", detail = action() }); }
    catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
}
void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException(message);
}

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: Verify <fixture-assembly> <generated-view-source> [comparison-fixture-assembly]");
    return 2;
}
Dictionary<string, byte[]> resources = [];
Check("genuine-embedded-baml-and-pack-resources", () =>
{
    resources = ReadResources(args[0]);
    foreach (var name in new[] { "views/orbit%20view%20%ce%a9.baml", "dictionaries/palette.baml" })
    {
        Require(resources.TryGetValue(name, out var bytes), $"Missing compiled resource: {name}");
        Require(bytes!.Length > 32 && Encoding.Unicode.GetString(bytes).Contains("MSBAML", StringComparison.Ordinal), $"Missing genuine BAML header: {name}");
    }
    Require(resources.TryGetValue("assets/casesensitive%20%ce%a9.txt", out var text), "Missing canonical pack resource ID for case-sensitive source with spaces and Unicode.");
    Require(Encoding.UTF8.GetString(text!).Contains("Actual pack resource", StringComparison.Ordinal), "Pack resource contents changed.");
    return string.Join("; ", resources.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}: {entry.Value.Length} bytes, SHA256={Convert.ToHexString(SHA256.HashData(entry.Value))}"));
});
Check("generated-initialize-component-and-connectors", () =>
{
    var generated = File.ReadAllText(args[1]);
    foreach (var expected in new[] { "public void InitializeComponent()", "System.Windows.Application.LoadComponent(this, resourceLocater)", "IComponentConnector.Connect", "this.ActionButton.Click +=", "this.Card =", "this.TemplateTarget =", "orbit%20view%20%ce%a9.xaml" })
        Require(generated.Contains(expected, StringComparison.Ordinal), $"Generated hookup missing: {expected}");
    using var stream = File.OpenRead(args[0]);
    using var image = new PEReader(stream);
    var metadata = image.GetMetadataReader();
    var view = metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle)).Single(type => metadata.GetString(type.Name) == "ProbeView");
    var methods = view.GetMethods().Select(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name)).ToArray();
    Require(methods.Contains("InitializeComponent") && methods.Contains("System.Windows.Markup.IComponentConnector.Connect"), "Generated connectors were not compiled into the real fixture assembly.");
    return "Generated view contains guarded InitializeComponent, encoded pack URI, named fields and event hookup; compiled assembly contains both generated methods.";
});
Check("local-types-require-real-markup-pass2", () =>
{
    var generatedDirectory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
    var intermediate = File.Exists(Path.Combine(generatedDirectory, "markup-pass1.txt"))
        ? generatedDirectory : Directory.GetParent(generatedDirectory)!.FullName;
    var pass1 = File.ReadAllText(Path.Combine(intermediate, "markup-pass1.txt")).Trim();
    Require(pass1 is "RequirePass2=True" or "RequirePass2=False", "Actual markup pass1 did not report its second-pass decision.");
    var evidence = File.ReadAllLines(Path.Combine(intermediate, "markup-pass2.txt"));
    Require(evidence.FirstOrDefault() == "Pass2Completed=true", "Actual markup pass2 did not complete.");
    var hashes = evidence.Skip(1).Select(line => line.Split('|')).ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
    var baml = resources.Where(entry => entry.Key.EndsWith(".baml", StringComparison.Ordinal)).ToArray();
    Require(hashes.Count == baml.Length, "Pass2 certificate does not cover the current embedded BAML set.");
    foreach (var entry in baml)
    {
        var fileName = Path.GetFileName(Uri.UnescapeDataString(entry.Key));
        Require(hashes.TryGetValue(fileName, out var expected) && expected == Convert.ToHexString(SHA256.HashData(entry.Value)), $"Pass2 certificate does not match current embedded BAML: {entry.Key}");
    }
    return pass1 == "RequirePass2=True"
        ? "Original MarkupCompilePass1 required a second pass; original MarkupCompilePass2 completed and its payload hashes match the embedded BAML."
        : "Original MarkupCompilePass1 reused its incremental output; the previous actual pass2 certificate still matches every embedded BAML payload.";
});
if (args.Length == 3)
{
    Check("comparison-baml-and-resource-equivalence", () =>
    {
        var comparison = ReadResources(args[2]);
        Require(resources.Keys.Order().SequenceEqual(comparison.Keys.Order()), "Compiler resource IDs differ between platforms.");
        foreach (var entry in resources)
            Require(entry.Value.SequenceEqual(comparison[entry.Key]), $"Compiler bytes differ between platforms: {entry.Key}");
        return $"All {resources.Count} BAML/pack resources are byte-identical between the supplied fixture assemblies.";
    });
}
Console.WriteLine(JsonSerializer.Serialize(new { probe = "real-wpf-markup-build-integration", os = RuntimeInformation.OSDescription, scope = "Current fork PresentationBuildTasks and Microsoft.WinFX.targets compiling genuine WPF BAML and generated component code. This executable inspects build artifacts and does not infer runtime compatibility from compiler evidence.", fixture = Path.GetFullPath(args[0]), checks }, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

static Dictionary<string, byte[]> ReadResources(string assemblyPath)
{
    using var stream = File.OpenRead(assemblyPath);
    using var image = new PEReader(stream);
    var metadata = image.GetMetadataReader();
    var directory = image.PEHeaders.CorHeader!.ResourcesDirectory;
    var section = image.GetSectionData(directory.RelativeVirtualAddress).GetContent();
    var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    foreach (var handle in metadata.ManifestResources)
    {
        var resource = metadata.GetManifestResource(handle);
        if (!resource.Implementation.IsNil || !metadata.GetString(resource.Name).EndsWith(".g.resources", StringComparison.Ordinal)) continue;
        var offset = checked((int)resource.Offset);
        var length = BitConverter.ToInt32(section.AsSpan(offset, 4));
        using var resourceStream = new MemoryStream(section.AsSpan(offset + 4, length).ToArray());
        using var reader = new ResourceReader(resourceStream);
        foreach (DictionaryEntry entry in reader)
        {
            using var data = (Stream)entry.Value!;
            using var copy = new MemoryStream();
            data.CopyTo(copy);
            result.Add((string)entry.Key, copy.ToArray());
        }
    }
    if (result.Count == 0) throw new InvalidDataException("No real generated WPF resources were embedded.");
    return result;
}
