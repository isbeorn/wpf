using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xaml;
using Portability.XamlFixtures;

var checks = new List<object>();
var failed = false;
void Check(string name, Func<string> action)
{
    try { var detail = action(); checks.Add(new { name, status = "pass", detail }); }
    catch (Exception exception) { failed = true; checks.Add(new { name, status = "fail", detail = exception.ToString() }); }
}
void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

const string source = """
<Catalog xmlns="clr-namespace:Portability.XamlFixtures;assembly=Portability.XamlFixtures"
         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
         Title="Source-built XAML" Selected="{x:Reference second}">
  <Entry x:Name="first" Label="{Echo Text=alpha}" Location="-7,42" />
  <Entry x:Name="second" Label="beta" Location="0,-1" />
</Catalog>
""";
Catalog? catalog = null;
string? saved = null;
Check("actual-source-assembly", () =>
{
    var assembly = typeof(XamlServices).Assembly;
    Require(assembly.GetName().Name == "System.Xaml", "Unexpected implementation assembly name.");
    Require(assembly.GetManifestResourceNames().Contains("FxResources.System.Xaml.SR.resources"), "Original resource manifest was not embedded.");
    Require(assembly.GetReferencedAssemblies().All(reference => reference.Name is not ("PresentationFramework" or "PresentationCore" or "WindowsBase")), "Unexpected WPF implementation dependency.");
    Require(typeof(Catalog).Assembly != assembly, "CLR fixtures must be in a separate assembly.");
    return $"{assembly.FullName}; original resource manifest present; fixtures resolved from {typeof(Catalog).Assembly.GetName().Name}; no WPF implementation dependency.";
});
Check("parse-clr-content-collection", () =>
{
    catalog = (Catalog)XamlServices.Parse(source);
    Require(catalog.Title == "Source-built XAML" && catalog.Items.Count == 2, "CLR properties or content collection did not populate.");
    return $"Title={catalog.Title}; implicit content collection count={catalog.Items.Count}.";
});
Check("markup-extension-and-type-converter", () =>
{
    Require(catalog is not null, "Parse did not succeed.");
    Require(catalog!.Items[0].Label == "echo:alpha", "Markup extension did not run.");
    Require(catalog.Items[0].Location == new Coordinate(-7, 42) && catalog.Items[1].Location == new Coordinate(0, -1), "Type converter did not run.");
    return $"Label={catalog.Items[0].Label}; converted coordinates={catalog.Items[0].Location} and {catalog.Items[1].Location}.";
});
Check("forward-name-reference", () =>
{
    Require(catalog is not null && ReferenceEquals(catalog.Selected, catalog.Items[1]), "Forward x:Reference did not preserve object identity.");
    return "Selected resolved to the later x:Name=second entry with reference identity preserved.";
});
Check("save-and-reload", () =>
{
    Require(catalog is not null, "Parse did not succeed.");
    saved = XamlServices.Save(catalog!);
    var reloaded = (Catalog)XamlServices.Parse(saved);
    Require(reloaded.Title == catalog!.Title && reloaded.Items.Count == 2, "Save/reload lost properties or collection items.");
    Require(reloaded.Items[0].Label == "echo:alpha" && reloaded.Items[0].Location == new Coordinate(-7, 42), "Save/reload lost converted values.");
    Require(ReferenceEquals(reloaded.Selected, reloaded.Items[1]), "Save/reload lost shared object identity.");
    return $"Serialized {saved.Length} characters; reloaded properties, both items, converted values and shared identity.";
});
Check("malformed-input-line-information", () =>
{
    try
    {
        XamlServices.Parse("<Catalog xmlns=\"clr-namespace:Portability.XamlFixtures;assembly=Portability.XamlFixtures\">\n<Entry>\n</Catalog>");
        throw new InvalidOperationException("Malformed input was accepted.");
    }
    catch (System.Xml.XmlException exception)
    {
        Require(exception.LineNumber == 3 && exception.LinePosition > 0, "XML exception did not retain line information.");
        return $"{exception.GetType().Name}; line={exception.LineNumber}; position={exception.LinePosition}; {exception.Message}";
    }
    catch (XamlException exception)
    {
        Require(exception.LineNumber > 0 && exception.LinePosition > 0, "XAML exception did not retain line information.");
        return $"{exception.GetType().Name}; line={exception.LineNumber}; position={exception.LinePosition}; {exception.Message}";
    }
});
Check("original-resource-backed-error", () =>
{
    try
    {
        XamlServices.Parse("<Missing xmlns=\"clr-namespace:Portability.XamlFixtures;assembly=Portability.XamlFixtures\" />");
        throw new InvalidOperationException("Unknown type was accepted.");
    }
    catch (XamlException exception)
    {
        Require(exception.Message.Contains("Missing", StringComparison.Ordinal), "Original diagnostic did not identify the missing type.");
        return $"{exception.GetType().Name}; line={exception.LineNumber}; position={exception.LinePosition}; {exception.Message}";
    }
});
Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "system-xaml-real-source",
    os = RuntimeInformation.OSDescription,
    scope = "Current fork System.Xaml CLR object parsing and serialization only; WPF XAML, BAML, compiled markup and NINA execution are unsupported by this probe.",
    assembly = typeof(XamlServices).Assembly.FullName,
    checks,
    savedXaml = saved
}, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;
