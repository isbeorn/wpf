using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xaml;
using System.Xaml.Permissions;
using PackageCompanion;
using Wpf.Portable.NativeHost;

namespace PackageConsumer;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var checks = new List<object>(); bool failed = false;
        using var watchdog = new Timer(_ => Environment.Exit(3), null, TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
        void Check(string name, Action action)
        {
            try { action(); checks.Add(new { name, status="pass", detail="Observed through packaged framework assemblies." }); }
            catch (Exception exception) { failed=true; checks.Add(new { name, status="fail", detail=exception.ToString() }); }
        }
        X11Fixture? native = null;
        if (args.Contains("--native"))
        {
            Check("native-startup", () =>
            {
                native = new X11Fixture("little");
                NativeWindowHost.InitializeForCurrentThread(Avalonia.AppBuilder.Configure<Avalonia.Application>());
            });
        }
        View? view = null;
        Check("application-definition-and-xclass-baml", () =>
        {
            var application = new App(); application.InitializeComponent();
            view = new View();
            Require(view.ActionButton is not null && view.Editor is not null, "Generated fields were not connected.");
            Require(((SolidColorBrush)view.Resources["CompanionBrush"]).Color == Colors.Teal, "Companion dictionary did not load.");
        });
        Check("cross-assembly-dp-binding-both-directions", () =>
        {
            Require(view is not null, "BAML construction failed.");
            var source = (PackageCompanion.ValueSource)view!.Resources["Source"];
            Require(view.Bound.Width == 9, "Initial BAML binding value differed.");
            source.Value = 12; Require(view.Bound.Width == 12, "Increasing the companion DP did not update binding.");
            source.Value = 3; Require(view.Bound.Width == 3, "Decreasing the companion DP did not update binding.");
        });
        Check("compiled-template-and-event-handler", () =>
        {
            Require(view is not null, "BAML construction failed.");
            view!.ActionButton.ApplyTemplate();
            Require(VisualTreeHelper.GetChildrenCount(view.ActionButton) == 1, "Compiled template has no genuine visual child.");
            view.ActionButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Require(view.Clicks == 1, "Generated event connector did not invoke code behind.");
        });
        Check("pack-resource-case-and-content", () =>
        {
            using var reader = new StreamReader(System.Windows.Application.GetResourceStream(new Uri("/App;component/Assets/CaseSensitive.txt",UriKind.Relative)).Stream);
            Require(reader.ReadToEnd().Contains("Packaged Unicode resource: λ Я"), "Pack resource bytes differed.");
            using var unicode=new StreamReader(System.Windows.Application.GetResourceStream(new Uri("/App;component/Assets/Διάγραμμα.txt",UriKind.Relative)).Stream);
            Require(unicode.ReadToEnd().Contains("Non-ASCII pack resource URI: λ Я"),"Non-ASCII resource URI did not resolve its original bytes.");
        });
        Check("xaml-access-level-four-factories", () =>
        {
            var assembly=typeof(Program).Assembly;
            var cases=new[] {
                (level:XamlAccessLevel.AssemblyAccessTo(assembly),type:typeof(InternalModel)),
                (level:XamlAccessLevel.AssemblyAccessTo(assembly.GetName()),type:typeof(InternalModel)),
                (level:XamlAccessLevel.PrivateAccessTo(typeof(PrivateModel)),type:typeof(PrivateModel)),
                (level:XamlAccessLevel.PrivateAccessTo(typeof(PrivateModel).AssemblyQualifiedName!),type:typeof(PrivateModel)) };
            var schema=new XamlSchemaContext();
            foreach(var item in cases)
            {
                var type=schema.GetXamlType(item.type);
                using var writer=new XamlObjectWriter(schema,new XamlObjectWriterSettings { AccessLevel=item.level });
                writer.WriteStartObject(type); writer.WriteStartMember(type.GetMember("Value")); writer.WriteValue("scoped");
                writer.WriteEndMember(); writer.WriteEndObject();
                Require(item.type.IsInstanceOfType(writer.Result) && (string)item.type.GetProperty("Value")!.GetValue(writer.Result)! == "scoped","Access scope did not construct the real non-public object.");
            }
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(XamlAccessLevel).Assembly.Location)))=="54163AEF0B78412550C50FFE0826E53B6F39DD3CC718D1EB9D16B73C27FD75F9","Extensions implementation differed.");
        });
        Check("windowsbase-preserved-type-forwarders",()=>
        {
            var windowsBase=typeof(DependencyObject).Assembly;
            foreach(var expected in new[]{typeof(MarkupExtension),typeof(IComponentConnector)})
            {
                var actual=windowsBase.GetType(expected.FullName!,throwOnError:true)!;
                Require(actual==expected && actual.Assembly==typeof(XamlServices).Assembly &&
                    actual.Assembly.GetName().FullName=="System.Xaml, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" &&
                    AssemblyLoadContext.GetLoadContext(actual.Assembly)==AssemblyLoadContext.Default,"WindowsBase forwarder resolved to a different type identity or context.");
            }
        });
        bool microsoftOriginVerified=false;
        Check("unchanged-microsoft-baml-origin",()=>
        {
            using var origin=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"MicrosoftCompilerOracle.json")));
            var path=Path.Combine(AppContext.BaseDirectory,"FixtureControls.dll");
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))==origin.RootElement.GetProperty("fixtureSha256").GetString(),"The frozen Microsoft fixture bytes changed.");
            var generated=typeof(FixtureControls.FixtureView).GetMethod("InitializeComponent")!.GetCustomAttribute<System.CodeDom.Compiler.GeneratedCodeAttribute>()!;
            Require(generated.Tool=="PresentationBuildTasks" && generated.Version=="10.0.12.0","The frozen fixture does not identify the Microsoft SDK compiler.");
            Require(typeof(FixtureControls.FixtureView).Assembly.Location==path && AssemblyLoadContext.GetLoadContext(typeof(FixtureControls.FixtureView).Assembly)==AssemblyLoadContext.Default,"Frozen fixture did not use its ordinary app-local binary reference.");
            microsoftOriginVerified=true;
        });
        Check("unchanged-microsoft-baml-view-dictionary-template",()=>
        {
            Require(microsoftOriginVerified,"Microsoft origin verification failed.");
            var frozen=new FixtureControls.FixtureView();
            foreach(var name in new[]{"Metric","Editor","Increment","Preview"})
                Require(ReferenceEquals(frozen.FindName(name),typeof(FixtureControls.FixtureView).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(frozen)),"Microsoft-generated connector differed: "+name);
            frozen.InitializeComponent();
            var dictionary=new ResourceDictionary {Source=new Uri("/FixtureControls;component/fixtureresources.xaml",UriKind.Relative)};
            frozen.Resources.MergedDictionaries.Add(dictionary);
            var metric=(FixtureControls.MetricControl)frozen.FindName("Metric");
            metric.Style=(Style)dictionary[typeof(FixtureControls.MetricControl)];metric.Value=9;
            Require(metric.ApplyTemplate(),"Unchanged Microsoft BAML template did not instantiate.");
            var text=(System.Windows.Controls.TextBlock)metric.Template.FindName("PART_Value",metric);
            text.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            Require(text.Text=="9" && ((SolidColorBrush)dictionary["FixtureAccent"]).Color.ToString()=="#FF1177BB","Unchanged dictionary or templated-parent binding differed.");
        });
        Check("runtime-manifest-bytes-and-compile-contract-exclusion",()=>
        {
            var manifest=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"PortableWpf/RuntimeManifest.xml"));
            foreach(var file in manifest.Root!.Elements("file"))
            {
                var name=Path.GetFileName((string)file.Attribute("path")!);
                var candidates=new[]{Path.Combine(AppContext.BaseDirectory,name),Path.Combine(AppContext.BaseDirectory,(string)file.Attribute("path")!)}.Distinct().Where(File.Exists).ToArray();
                Require(candidates.Length==1,"Runtime asset is absent or has ambiguous deployment locations: "+name);
                Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(candidates[0])))==(string?)file.Attribute("sha256"),"Runtime manifest mismatch: "+name);
            }
            foreach(var name in new[]{"System.Private.Windows.Core","System.Private.Windows.GdiPlus","DirectWriteForwarder","ReachFramework","System.Printing","PresentationUI"})
                Require(!File.Exists(Path.Combine(AppContext.BaseDirectory,name+".dll")),"Compile-only contract reached runtime output: "+name);
            Require(Directory.Exists(Path.Combine(AppContext.BaseDirectory,"ThirdPartyNotices/Wpf.Portable.Runtime/TextUnicode")),"Unicode source notices were not propagated into application output.");
        });
        if (native is not null && !failed)
        {
            Check("packaged-native-window-input-and-baml", () =>
            {
                var font=new FontFamily(new Uri(Path.Combine(AppContext.BaseDirectory,"DejaVuSans.ttf")),"DejaVu Sans");
                view!.FontFamily=font;
                var window=new Window { Title="Packaged genuine WPF", Width=320, Height=220, Content=view, FontFamily=font };
                int releases=0; view.Editor.KeyUp+=(_,_)=>releases++;
                try
                {
                    window.Show(); var xid=native.FindWindow(window.Title);
                    Wait(()=>xid!=IntPtr.Zero && native.IsMapped(xid) && native.Pixel(xid,5,5)==0x00008000,
                        ()=>$"frame xid={xid}, mapped={native.IsMapped(xid)}, window={window.ActualWidth}x{window.ActualHeight}, view={view.ActualWidth}x{view.ActualHeight}, button={view.ActionButton.ActualWidth}x{view.ActionButton.ActualHeight}, pixel={native.Pixel(xid,5,5):X8}");
                    native.Focus(xid); native.Click(xid,30,25); Wait(()=>view.Clicks==2);
                    native.Click(xid,30,85); Wait(()=>view.Editor.IsKeyboardFocused);
                    native.Type('a'); native.Type('b'); Wait(()=>view.Editor.Text=="ab" && releases>=2);
                }
                finally { window.Close(); }
            });
        }
        native?.Dispose();
        var assemblies=new[]{typeof(DependencyObject).Assembly,typeof(Visual).Assembly,typeof(Window).Assembly,
            typeof(System.Xaml.XamlServices).Assembly,typeof(XamlAccessLevel).Assembly,typeof(NativeWindowHost).Assembly};
        Check("ordinary-default-load-context",()=>Require(assemblies.All(a=>AssemblyLoadContext.GetLoadContext(a)==AssemblyLoadContext.Default),"A framework assembly used a custom load context."));
        var sharedFrameworks=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic).Select(a=>a.Location.Replace('\\','/'))
            .Where(path=>path.Contains("/shared/",StringComparison.Ordinal)).Select(path=>path.Split("/shared/",StringSplitOptions.None)[1].Split('/')[0]).Distinct().ToArray();
        Check("netcore-only-framework-load",()=>Require(sharedFrameworks.All(name=>name=="Microsoft.NETCore.App") &&
            !AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name!.StartsWith("Microsoft.AspNetCore",StringComparison.Ordinal)),"An application framework outside NETCore.App was loaded."));
        var nativeModules=OperatingSystem.IsLinux()?Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Where(module=>module.FileName.Contains(".so",StringComparison.Ordinal))
            .Select(module=>new {name=module.ModuleName,path=module.FileName}).ToArray():[];
        Console.WriteLine(JsonSerializer.Serialize(new { probe="portable-package-consumer",os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            scope="Local package net10 Linux x64 profile; explicit templates and Xvfb input qualify a bounded native slice.",checks,
            sharedFrameworks,coreLibrary=typeof(object).Assembly.Location,nativeModules,
            assemblies=assemblies.Select(a=>new { identity=a.FullName,path=a.Location,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))) }) }));
        return failed?1:0;
    }
    private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    private sealed class PrivateModel { public string Value { get; set; }=""; }
    private static void Wait(Func<bool> ready,Func<string>? diagnostic=null)
    {
        var elapsed=Stopwatch.StartNew(); var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(TimeSpan.FromMilliseconds(5),DispatcherPriority.Background,(_,_)=>
        { if(ready() || elapsed.Elapsed>TimeSpan.FromSeconds(5)) frame.Continue=false; },Dispatcher.CurrentDispatcher);
        try { if(!ready()) Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Require(ready(),"Packaged native observation timed out. " + diagnostic?.Invoke());
    }
}
internal sealed class InternalModel { public string Value { get; set; }=""; }
