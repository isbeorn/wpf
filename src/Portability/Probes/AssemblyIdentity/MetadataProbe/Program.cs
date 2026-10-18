using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

var checks = new List<object>();
var failed = false;
var scope = "ordinary-net-core-host";
void Check(string name, bool success, object detail)
{
    failed |= !success;
    checks.Add(new { name, status = success ? "pass" : "fail", detail });
}
object Identity(Assembly assembly) => new
{
    name = assembly.FullName,
    path = assembly.Location,
    loadContext = AssemblyLoadContext.GetLoadContext(assembly)?.Name,
    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))
};

try
{
    if (args.Length == 0)
    {
        var shim = Assembly.Load("WindowsBase");
        Check("core-windowsbase-is-not-wpf", shim.GetType("System.Windows.DependencyObject") is null,
            new { assembly = Identity(shim), dependencyObjectPresent = shim.GetType("System.Windows.DependencyObject") is not null });
        Check("no-desktop-framework-loaded", !AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            a.Location.Contains("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase)),
            "This process requires only Microsoft.NETCore.App; its WindowsBase shim supplies no DependencyObject.");
    }
    else if (args.Length == 2 && args[0] == "--inspect")
    {
        scope = "metadata-only-no-runtime-compatibility-claim";
        var path = Path.GetFullPath(args[1]);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        Check("assembly-metadata", true, new
        {
            path,
            name = metadata.GetString(definition.Name),
            version = definition.Version.ToString(),
            publicKey = Convert.ToHexString(metadata.GetBlobBytes(definition.PublicKey)),
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            references = metadata.AssemblyReferences.Select(h =>
            {
                var reference = metadata.GetAssemblyReference(h);
                return new { name = metadata.GetString(reference.Name), version = reference.Version.ToString() };
            }).ToArray()
        });
    }
    else if (args.Length == 4 && args[0] == "--runtime-directory" && args[2] == "--companion")
    {
        scope = "supplied-runtime-isolated-context-not-source-build-or-app-local-publish-proof";
        var directory = Path.GetFullPath(args[1]);
        var context = new CandidateContext(directory);
        var windowsBase = context.LoadFromAssemblyPath(Path.Combine(directory, "WindowsBase.dll"));
        var dependencyObject = windowsBase.GetType("System.Windows.DependencyObject", throwOnError: true)!;
        Check("candidate-windowsbase-loaded", AssemblyLoadContext.GetLoadContext(windowsBase) == context,
            Identity(windowsBase));
        var companion = context.LoadFromAssemblyPath(Path.GetFullPath(args[3]));
        var operations = companion.GetType("AssemblyIdentity.Companion.PropertyOperations", throwOnError: true)!;
        var instance = operations.GetMethod("Create")!.Invoke(null, null)!;
        Check("cross-assembly-type-identity", dependencyObject.IsInstanceOfType(instance), new
        {
            companion = Identity(companion), actualBaseAssembly = Identity(instance.GetType().BaseType!.Assembly)
        });
        var result = (string)operations.GetMethod("Exercise")!.Invoke(null, [instance])!;
        Check("dependency-property-default-set-clear-callback", result == "default=7;set=23;clear=7;callbacks=2", result);
    }
    else
    {
        Check("arguments", false,
            "Use no arguments, --inspect <assembly> or --runtime-directory <directory> --companion <assembly>.");
    }
}
catch (Exception exception)
{
    Check("execution", false, exception.ToString());
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    probe = "assembly-identity",
    os = RuntimeInformation.OSDescription,
    scope,
    checks
}, new JsonSerializerOptions { WriteIndented = true }));
return failed ? 1 : 0;

sealed class CandidateContext(string directory) : AssemblyLoadContext("supplied-wpf-runtime", isCollectible: true)
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = Path.Combine(directory, assemblyName.Name + ".dll");
        return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }
}
