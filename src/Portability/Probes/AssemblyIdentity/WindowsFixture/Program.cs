using AssemblyIdentity.Companion;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;

var checks = new List<object>();
var success = false;
try
{
    var instance = PropertyOperations.Create();
    var result = PropertyOperations.Exercise(instance);
    success = result == "default=7;set=23;clear=7;callbacks=2" && instance is DependencyObject;
    checks.Add(new
    {
        name = "cross-assembly-dependency-property",
        status = success ? "pass" : "fail",
        detail = (object)new
        {
            result,
            assembly = typeof(DependencyObject).Assembly.FullName,
            path = typeof(DependencyObject).Assembly.Location,
            loadContext = AssemblyLoadContext.GetLoadContext(typeof(DependencyObject).Assembly)?.Name,
            companion = typeof(PropertyOperations).Assembly.Location
        }
    });
}
catch (Exception exception)
{
    checks.Add(new { name = "execution", status = "fail", detail = (object)exception.ToString() });
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "assembly-identity",
    os = RuntimeInformation.OSDescription,
    scope = "stock-windows-desktop-runtime-not-fork",
    checks
}, new JsonSerializerOptions { WriteIndented = true }));
return success ? 0 : 1;
