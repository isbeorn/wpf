using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using System.Xaml;
using System.Xaml.Schema;
using Portability.MarkupIntegration.Fixture;

internal static class NamespaceResolutionChecks
{
    private const string WpfNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string FixtureNamespace = "urn:portability:namespace-resolution";
    private const string Contributor = "Portability.Markup.NamespaceContributor";
    private const string Missing = "Portability.Markup.AbsentNamespaceCandidate";
    private static readonly Type SchemaType = typeof(FrameworkElement).Assembly.GetType("System.Windows.Baml2006.Baml2006SchemaContext", throwOnError: true)!;

    internal static string MissingNamespaceCandidate()
    {
        var context = NewContext();
        AddAssembly(context, 0, Missing);
        Map(context, WpfNamespace, [0]);
        var result = context.GetXamlType(new XamlTypeName(WpfNamespace, nameof(Validation)));
        Require(result?.UnderlyingType == typeof(Validation), "An absent same-xmlns candidate blocked the available Validation owner type.");
        return "Resolved actual Validation with an absent namespace candidate; no substitute assembly or type was supplied.";
    }

    internal static string AvailableNamespaceContributor()
    {
        Require(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == Contributor), "Namespace contributor was already loaded; the check cannot prove available-candidate loading.");
        var parent = new XamlSchemaContext();
        var existing = parent.GetXamlType(new XamlTypeName(FixtureNamespace, "ExistingValue"));
        Require(existing?.UnderlyingType?.Assembly == typeof(ProbeView).Assembly, "Existing same-xmlns type was not available before contributor loading.");
        var context = NewContext(parent);
        AddAssembly(context, 0, Contributor);
        Map(context, FixtureNamespace, [0]);
        Require(context.GetXamlType(new XamlTypeName(FixtureNamespace, "ExistingValue"))?.UnderlyingType == existing!.UnderlyingType, "Existing namespace type changed.");
        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(item => item.GetName().Name == Contributor);
        Require(AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "Contributor escaped the default load context.");
        var contributed = context.GetXamlType(new XamlTypeName(FixtureNamespace, "ContributedValue"));
        Require(contributed?.UnderlyingType?.Assembly == assembly, "Available contributor type did not resolve after namespace loading.");
        return "An initially unloaded real XmlnsDefinition contributor loaded even though the requested type already existed; its distinct type also resolves.";
    }

    internal static string RequiredAssemblyFailures()
    {
        var direct = NewContext();
        AddAssembly(direct, 0, Missing);
        var flags = SchemaType.GetNestedType("TypeInfoFlags", BindingFlags.NonPublic)!;
        Invoke(direct, "AddXamlType", [typeof(short), typeof(short), typeof(string), flags], [(short)0, (short)0, "Missing.RequiredType", Enum.ToObject(flags, 0)]);
        var failure = ExpectFailure(() => Invoke(direct, "GetXamlType", [typeof(short)], [(short)0]));
        Require(Contains<FileNotFoundException>(failure, exception => exception.FileName is not null && new AssemblyName(exception.FileName).Name == Missing), "Explicit BAML type-ID assembly failure was suppressed.");

        var malformed = NewContext();
        AddAssembly(malformed, 0, "Invalid, Version=not-a-version");
        Map(malformed, WpfNamespace, [0]);
        failure = ExpectFailure(() => malformed.GetXamlType(new XamlTypeName(WpfNamespace, nameof(Validation))));
        Require(failure is ArgumentException or FileLoadException, "Malformed mapped identity lost its parse/load diagnostic.");

        failure = ExpectFailure(() => System.Windows.Markup.XamlReader.Parse("<local:AbsentRequiredType xmlns:local=\"clr-namespace:Portability.MarkupIntegration.NamespaceFixture;assembly=Portability.MarkupIntegration.Fixture\"/>"));
        Require(failure is System.Windows.Markup.XamlParseException && failure.Message.Contains("AbsentRequiredType", StringComparison.Ordinal), "Explicit missing XAML type lost its named parse diagnostic.");

        const string requested = "Portability.Markup.ResolverFailureCandidate";
        const string badImage = "Portability.Markup.BadImageCandidate";
        const string dependency = "Portability.Markup.AbsentTransitiveDependency";
        Assembly? FailFixtureLoad(AssemblyLoadContext _, AssemblyName name)
        {
            if (name.Name == requested)
                throw new FileNotFoundException("The requested candidate has an absent dependency.", dependency);
            if (name.Name == badImage)
                throw new BadImageFormatException("The requested candidate is not a valid managed image.", name.FullName);
            return null;
        }
        AssemblyLoadContext.Default.Resolving += FailFixtureLoad;
        try
        {
            var context = NewContext();
            AddAssembly(context, 0, requested);
            Map(context, WpfNamespace, [0]);
            failure = ExpectFailure(() => context.GetXamlType(new XamlTypeName(WpfNamespace, nameof(Validation))));
            Require(Contains<FileNotFoundException>(failure, exception => exception.FileName == dependency), "A different-name dependency failure was treated as an optional absent candidate.");

            context = NewContext();
            AddAssembly(context, 0, badImage);
            Map(context, WpfNamespace, [0]);
            failure = ExpectFailure(() => context.GetXamlType(new XamlTypeName(WpfNamespace, nameof(Validation))));
            Require(Contains<BadImageFormatException>(failure, _ => true), "A bad-image failure was treated as an optional absent candidate.");
        }
        finally { AssemblyLoadContext.Default.Resolving -= FailFixtureLoad; }
        return "Direct BAML type-ID missing assembly, named missing XAML type, malformed identity, bad image and different-name dependency failure all remain errors. The scoped failure callback supplies no assembly and is removed in finally.";
    }

    internal static string CompiledValidationTemplate()
    {
        Require(!File.Exists(Path.Combine(AppContext.BaseDirectory, "System.Windows.Controls.Ribbon.dll")), "Compiler-only Ribbon metadata leaked into runtime output.");
        var dictionary = (ResourceDictionary)Application.LoadComponent(new Uri("/Portability.MarkupIntegration.Fixture;component/NamespaceValidation.xaml", UriKind.Relative));
        var textBox = new TextBox { Template = (ControlTemplate)dictionary["ValidationTemplate"] };
        Require(textBox.ApplyTemplate(), "Compiled validation template did not apply.");
        var errors = (ItemsControl)textBox.Template.FindName("Errors", textBox);
        textBox.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Require(ReferenceEquals(errors.ItemsSource, Validation.GetErrors(textBox)), "Genuine compiled attached Validation.Errors binding did not resolve its actual owner/collection.");
        Require(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "System.Windows.Controls.Ribbon"), "Optional namespace candidate loaded as a runtime dependency.");
        return "Real compiled deferred template resolved (Validation.Errors) with unchanged compile-only Ribbon namespace metadata and no Ribbon runtime assembly.";
    }

    private static XamlSchemaContext NewContext(XamlSchemaContext? parent = null) =>
        (XamlSchemaContext)Activator.CreateInstance(SchemaType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [typeof(ProbeView).Assembly, parent ?? new XamlSchemaContext()], culture: null)!;

    private static void AddAssembly(XamlSchemaContext context, short id, string name) =>
        Invoke(context, "AddAssembly", [typeof(short), typeof(string)], [id, name.Contains(',') ? name : name + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"]);

    private static void Map(XamlSchemaContext context, string uri, short[] ids) =>
        Invoke(context, "AddXmlnsMapping", [typeof(string), typeof(short[])], [uri, ids]);

    private static object? Invoke(object instance, string name, Type[] types, object?[] arguments)
    {
        try { return SchemaType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, binder: null, types, modifiers: null)!.Invoke(instance, arguments); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static Exception ExpectFailure(Action action)
    {
        try { action(); }
        catch (Exception exception) { return exception; }
        throw new InvalidOperationException("A required assembly/type operation unexpectedly succeeded.");
    }

    private static bool Contains<T>(Exception exception, Func<T, bool> predicate) where T : Exception =>
        exception is T typed && predicate(typed) || exception.InnerException is not null && Contains(exception.InnerException, predicate);

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
