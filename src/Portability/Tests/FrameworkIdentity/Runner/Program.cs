using FrameworkIdentity.Companion;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using System.Xaml;

var checks = new List<object>();
var failures = 0;
void Require(bool condition, string detail)
{
    if (!condition) throw new InvalidOperationException(detail);
}
void Check(string name, Action action)
{
    try
    {
        action();
        checks.Add(new { name, status = "pass", detail = "Observed expected behavior." });
    }
    catch (Exception exception)
    {
        failures++;
        checks.Add(new { name, status = "fail", detail = exception.ToString() });
    }
}

Check("app-local-source-assembly-default-context", () =>
{
    var assembly = typeof(DependencyObject).Assembly;
    Require(assembly.GetName().Version == new Version(10, 0, 0, 0), assembly.FullName!);
    Require(Convert.ToHexString(assembly.GetName().GetPublicKeyToken()!) == "31BF3856AD364E35", "WindowsBase public key token mismatch.");
    Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "Unexpected custom context.");
    Require(Path.GetDirectoryName(assembly.Location) == AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), assembly.Location);
    Require(typeof(ValueOwner).BaseType == typeof(DependencyObject), "Companion uses a different DependencyObject identity.");
});
Check("dependency-property-set-clear-callbacks", () =>
{
    var owner = new ValueOwner();
    Require(owner.Number == 7, "Default mismatch.");
    owner.Number = 23;
    Require(owner.Number == 23 && owner.CallbackCount == 1 && owner.LastOldValue == 7 && owner.LastNewValue == 23, "Set callback mismatch.");
    owner.ClearValue(ValueOwner.NumberProperty);
    Require(owner.Number == 7 && owner.CallbackCount == 2 && owner.LastOldValue == 23 && owner.LastNewValue == 7, "Clear callback mismatch.");
    Require(owner.ReadLocalValue(ValueOwner.NumberProperty) == DependencyProperty.UnsetValue, "Clear retained a local value.");
});
Check("dependency-property-validation-and-coercion", () =>
{
    var owner = new ValueOwner { Number = -5 };
    Require(owner.Number == 0, "Coercion did not clamp negative value.");
    var rejected = false;
    try { owner.Number = 101; } catch (ArgumentException) { rejected = true; }
    Require(rejected && owner.Number == 0, "Validation accepted invalid value or changed the previous value.");
});
Check("freezable-clone-freeze-and-reject-write", () =>
{
    var original = new FrozenValue { Number = 42 };
    var clone = (FrozenValue)original.Clone();
    Require(clone.Number == 42 && !clone.IsFrozen && !ReferenceEquals(original, clone), "Clone mismatch.");
    clone.Number = 19;
    Require(original.Number == 42, "Clone changed original.");
    original.Freeze();
    Require(original.IsFrozen, "Freeze failed.");
    var rejected = false;
    try { original.Number = 21; } catch (InvalidOperationException) { rejected = true; }
    Require(rejected && original.Number == 42, "Frozen write accepted.");
});
Check("freezable-thread-affinity-and-frozen-sharing", () =>
{
    var mutable = new FrozenValue { Number = 27 };
    var frozen = (FrozenValue)mutable.GetAsFrozen();
    var result = Task.Run(() =>
    {
        var rejected = false;
        try { _ = mutable.Number; } catch (InvalidOperationException) { rejected = true; }
        return (rejected, number: frozen.Number, access: frozen.CheckAccess());
    }).GetAwaiter().GetResult();
    Require(result.rejected && result.number == 27 && result.access, "Mutable affinity or frozen cross-thread read mismatch.");
});
Check("source-resource-message-is-present", () =>
{
    var owner = new ValueOwner();
    try { owner.SetValue(ValueOwner.NumberProperty, "wrong type"); }
    catch (ArgumentException exception)
    {
        Require(!string.IsNullOrWhiteSpace(exception.Message) && exception.Message.Contains("Number", StringComparison.Ordinal), exception.Message);
        return;
    }
    throw new InvalidOperationException("Expected invalid dependency property value exception.");
});
Check("source-xaml-parses-cross-assembly-dependency-object", () =>
{
    const string markup = "<ValueOwner xmlns='clr-namespace:FrameworkIdentity.Companion;assembly=Portability.FrameworkIdentity.Companion' Number='47' />";
    var owner = (ValueOwner)XamlServices.Parse(markup);
    Require(owner.Number == 47 && owner.CallbackCount == 1, "XAML CLR property write did not use dependency property semantics.");
    Require(typeof(XamlServices).Assembly.GetName().Version == new Version(10, 0, 0, 0), "System.Xaml portable product version mismatch.");
    Require(Convert.ToHexString(typeof(XamlServices).Assembly.GetName().GetPublicKeyToken()!) == "B77A5C561934E089", "System.Xaml public key token mismatch.");
    Require(typeof(System.Windows.Markup.MarkupExtension).Assembly == typeof(XamlServices).Assembly, "WindowsBase markup typeforwarder identity mismatch.");
});
Check("runtime-config-requires-only-net-core", () =>
{
    var configPath = Path.Combine(AppContext.BaseDirectory, "Portability.FrameworkIdentity.runtimeconfig.json");
    using var configuration = JsonDocument.Parse(File.ReadAllText(configPath));
    var framework = configuration.RootElement.GetProperty("runtimeOptions").GetProperty("framework").GetProperty("name").GetString();
    Require(framework == "Microsoft.NETCore.App", framework ?? "Missing framework.");
    Require(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal)),
        "A WindowsDesktop shared framework assembly was loaded.");
});

Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "framework-identity",
    os = RuntimeInformation.OSDescription,
    scope = "actual-fork-windowsbase-app-local-default-context-net10",
    assembly = typeof(DependencyObject).Assembly.FullName,
    path = typeof(DependencyObject).Assembly.Location,
    xamlAssembly = typeof(XamlServices).Assembly.FullName,
    xamlPath = typeof(XamlServices).Assembly.Location,
    checks
}, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;
