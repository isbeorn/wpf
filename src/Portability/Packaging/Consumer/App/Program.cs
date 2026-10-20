using System.Diagnostics;
using System.Reflection;
using System.Resources;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
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
        if (args.Length == 2 && args[0] == "--audit-complete-view-results") return AuditCompleteViewResults(args[1]);
        var checks = new List<CheckResult>(); bool failed = false;
        object? classicTheme = null;
        bool completeView = args.Contains("--complete-view");
        using var watchdog = new Timer(_ =>
        {
            if (completeView) Console.WriteLine(JsonSerializer.Serialize(new { probe="complete-portable-wpf-view", os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                scope="Packaged unchanged view watchdog", checks=new[] { new { name="watchdog", status="fail", detail="The complete view workflow exceeded 45 seconds." } } }));
            Environment.Exit(3);
        }, null, TimeSpan.FromSeconds(45), Timeout.InfiniteTimeSpan);
        bool interactive = completeView && !args.Contains("--verify");
        if (interactive) watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        void Check(string name, Action action)
        {
            try { action(); checks.Add(new(name,"pass","Observed through packaged framework assemblies.")); }
            catch (Exception exception) { failed=true; checks.Add(new(name,"fail",exception.ToString())); }
        }
        X11Fixture? native = null;
        if (args.Contains("--native") || completeView)
        {
            Check("native-startup", () =>
            {
                native = new X11Fixture("little");
                NativeWindowHost.InitializeForCurrentThread();
            });
        }
        if (completeView)
        {
            Check("complete-view-setup-and-cleanup", () =>
            {
                Require(native is not null, "Native startup failed.");
                CompleteViewWorkflow.Run(new X11CompleteViewDesktop(native!), interactive, Check, Wait);
            });
        }
        else
        {
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
        Check("classic-default-controls-and-resource-origin", () => classicTheme = VerifyClassicDefaultControls());
        if(checks[^1].status=="pass") checks[^1]=checks[^1] with {detail=OperatingSystem.IsLinux()
            ? "Classic templates, UserControl content and Button/TextBox positive geometry/text layout; packaged Linux native dependencies."
            : "Classic managed templates/content host and native-free UserControl/Grid layout; Button/TextBox text layout is not qualified on this host."};
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
        }
        Check("runtime-manifest-bytes-and-compile-contract-exclusion",AuditRuntimeAssets);
        string? settingsOrigin = native?.SettingsOrigin;
        native?.Dispose();
        var assemblies=new[]{typeof(DependencyObject).Assembly,typeof(Visual).Assembly,typeof(Window).Assembly,
            typeof(System.Xaml.XamlServices).Assembly,typeof(XamlAccessLevel).Assembly,typeof(NativeWindowHost).Assembly};
        Check("ordinary-default-load-context",()=>Require(assemblies.All(a=>AssemblyLoadContext.GetLoadContext(a)==AssemblyLoadContext.Default) && !AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name?.StartsWith("Avalonia",StringComparison.Ordinal)==true),"A framework assembly used a custom load context or an Avalonia assembly was loaded."));
        var sharedFrameworks=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic).Select(a=>a.Location.Replace('\\','/'))
            .Where(path=>path.Contains("/shared/",StringComparison.Ordinal)).Select(path=>path.Split("/shared/",StringSplitOptions.None)[1].Split('/')[0]).Distinct().ToArray();
        Check("netcore-only-framework-load",()=>Require(sharedFrameworks.All(name=>name=="Microsoft.NETCore.App") &&
            !AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name!.StartsWith("Microsoft.AspNetCore",StringComparison.Ordinal)),"An application framework outside NETCore.App was loaded."));
        if (completeView) Check("complete-view-all-checks-completed", () => VerifyRequiredChecks(checks.Select(check => check.name), interactive, includeCompletion:false));
        var nativeModules=OperatingSystem.IsLinux()?Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Where(module=>module.FileName.Contains(".so",StringComparison.Ordinal))
            .Select(module=>new {name=module.ModuleName,path=module.FileName}).ToArray():[];
        using var completeViewDocument = completeView ? JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"CompleteViewOrigin.json"))) : null;
        JsonElement? completeViewOrigin = completeViewDocument?.RootElement.Clone();
        watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Console.WriteLine(JsonSerializer.Serialize(new { probe=completeView?"complete-portable-wpf-view":"portable-package-consumer",os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            scope=completeView?"Unchanged FixtureControls with explicit host templates, CPU rendering and virtual X11/XTest input; declared XSETTINGS test values, physical desktop unqualified":"Local net10 Linux x64 package; Windows managed Classic templates, Linux positive text layout and explicit-template Xvfb input qualify bounded slices.",checks,
            completeViewOrigin,settingsOrigin,classicTheme,
            sharedFrameworks,coreLibrary=typeof(object).Assembly.Location,nativeModules,
            assemblies=assemblies.Select(a=>new { identity=a.FullName,path=a.Location,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))) }) }));
        return failed?1:0;
    }
    private sealed record CheckResult(string name, string status, string detail);

    private static object VerifyClassicDefaultControls()
    {
        static void Prepare(Control control)
        {
            control.BeginInit(); control.EndInit();
            control.ApplyTemplate();
            Require(control.ReadLocalValue(Control.TemplateProperty)==DependencyProperty.UnsetValue,
                "The default control check supplied a local template.");
            Require(control.Template is not null && VisualTreeHelper.GetChildrenCount(control)>0,
                control.GetType().Name+" did not resolve and construct its ordinary default template.");
        }
        static void Layout(Control control)
        {
            control.Measure(new Size(320,200));
            control.Arrange(new Rect(0,0,320,200));
            control.UpdateLayout();
            Require(control.IsMeasureValid && control.IsArrangeValid && control.ActualWidth>0 && control.ActualHeight>0,
                control.GetType().Name+" did not resolve and lay out its ordinary default template.");
        }
        var content=new Grid {Width=80,Height=30};
        var userControl=new UserControl {Content=content};
        Prepare(userControl);
        Layout(userControl);
        Require(VisualTreeHelper.GetParent(content) is ContentPresenter && content.IsMeasureValid && content.IsArrangeValid &&
            content.ActualWidth==80 && content.ActualHeight==30,"The default UserControl template did not connect and lay out content.");
        var button=new Button {Content="Classic",Width=120,Height=32};
        Prepare(button);
        var textBox=new TextBox {Text="Classic",Width=160,Height=32};
        Prepare(textBox);
        Require(textBox.Template.FindName("PART_ContentHost",textBox) is ScrollViewer,
            "The default TextBox template did not connect its genuine ScrollViewer content host.");
        bool measuredTextLayout=OperatingSystem.IsLinux();
        if(measuredTextLayout) { Layout(button); Layout(textBox); }
        var classic=AppDomain.CurrentDomain.GetAssemblies().Single(assembly=>assembly.GetName().Name=="PresentationFramework.Classic");
        var identity=classic.GetName();
        Require(identity.Version==new Version(10,0,0,0) && Convert.ToHexString(identity.GetPublicKeyToken()!)=="31BF3856AD364E35" &&
            AssemblyLoadContext.GetLoadContext(classic)==AssemblyLoadContext.Default,"Classic resolved with a different identity or load context.");
        const string resourceName="PresentationFramework.Classic.g.resources";
        const string bamlKey="themes/classic.baml";
        using var stream=classic.GetManifestResourceStream(resourceName);
        Require(stream is not null,"Classic has no genuine compiled resource manifest.");
        using var reader=new ResourceReader(stream!);
        var entries=reader.GetEnumerator(); string? bamlSha256=null;
        while(entries.MoveNext())
        {
            if(entries.Key is not string key || key!=bamlKey) continue;
            var baml=entries.Value as Stream;
            Require(baml is not null && baml.Length>0,"Classic has no compiled theme BAML payload.");
            bamlSha256=Convert.ToHexString(SHA256.HashData(baml!));
            break;
        }
        Require(bamlSha256 is not null,"Classic's resource manifest omitted its real theme dictionary.");
        Require(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly=>assembly.GetName().Name=="PresentationUI"),
            "The ordinary Classic control path unexpectedly loaded document-viewer PresentationUI.");
        var windowsNativeTextModules=OperatingSystem.IsWindows()?Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Where(module=>module.ModuleName.Equals("libHarfBuzzSharp.dll",StringComparison.OrdinalIgnoreCase) ||
                module.ModuleName.Equals("libSkiaSharp.dll",StringComparison.OrdinalIgnoreCase))
            .Select(module=>module.FileName).ToArray():[];
        Require(windowsNativeTextModules.Length==0,"Managed Windows template verification loaded a native text library.");
        return new {identity=classic.FullName,path=classic.Location,loadContext=AssemblyLoadContext.GetLoadContext(classic)?.Name,
            sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(classic.Location))),resourceName,bamlKey,bamlSha256,
            measuredTextLayout,userControlContentWidth=content.ActualWidth,userControlContentHeight=content.ActualHeight,windowsNativeTextModules};
    }

    private static void AuditRuntimeAssets()
    {
        var manifest=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"PortableWpf/RuntimeManifest.xml"));
        foreach(var file in manifest.Root!.Elements("file"))
        {
            var name=Path.GetFileName((string)file.Attribute("path")!);
            var candidates=new[]{Path.Combine(AppContext.BaseDirectory,name),Path.Combine(AppContext.BaseDirectory,(string)file.Attribute("path")!)}.Distinct().Where(File.Exists).ToArray();
            Require(candidates.Length==1,"Runtime asset is absent or has ambiguous deployment locations: "+name);
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(candidates[0])))==(string?)file.Attribute("sha256"),"Runtime manifest mismatch: "+name);
        }
        foreach(var name in new[]{"System.Private.Windows.GdiPlus","DirectWriteForwarder","ReachFramework","System.Printing"})
            Require(!File.Exists(Path.Combine(AppContext.BaseDirectory,name+".dll")),"Compile-only contract reached runtime output: "+name);
        Require(manifest.Root!.Elements("file").Any(file=>Path.GetFileName((string?)file.Attribute("path"))=="System.Private.Windows.Core.dll"),
            "This consumer requires the qualified managed Windows.Core runtime profile.");
        VerifyManagedWindowsCore();
        using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"PortableWpf/Provenance.json")));
        var helperOrigin = provenance.RootElement.GetProperty("assets").EnumerateArray()
            .Single(asset=>asset.GetProperty("path").GetString()=="lib/net10.0/System.Private.Windows.Core.dll").GetProperty("origin").GetString()!;
        var noticeRoot = Path.Combine(AppContext.BaseDirectory,"ThirdPartyNotices/Wpf.Portable.Runtime");
        var helperNotices = helperOrigin.StartsWith("source:",StringComparison.Ordinal)
            ? Path.Combine(noticeRoot,"Windows.Core")
            : Path.Combine(noticeRoot,"packages",helperOrigin.Split('/')[0],helperOrigin.Split('/')[1]);
        Require(Directory.Exists(helperNotices),"Windows.Core origin notices were not propagated.");
        Require(Directory.Exists(Path.Combine(AppContext.BaseDirectory,"ThirdPartyNotices/Wpf.Portable.Runtime/TextUnicode")),"Unicode source notices were not propagated into application output.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void VerifyManagedWindowsCore()
    {
        var data=new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.UnicodeText,"Managed \u03bb",true);
        Require(data.GetText()=="Managed \u03bb" && new System.Windows.DataObject((System.Windows.IDataObject)data).GetText()=="Managed \u03bb",
            "The genuine managed DataObject store or IDataObject wrapping failed.");
        var helper=typeof(System.Windows.DataObject).GetInterfaces().Select(type=>type.Assembly).First(assembly=>assembly.GetName().Name=="System.Private.Windows.Core");
        Require(AssemblyLoadContext.GetLoadContext(helper)==AssemblyLoadContext.Default && helper.GetName().Version==new Version(10,0,0,0) &&
            Convert.ToHexString(helper.GetName().GetPublicKeyToken()!)=="B77A5C561934E089","Windows.Core did not load its preserved identity in Default ALC.");
    }

    private static void VerifyRequiredChecks(IEnumerable<string> names, bool interactive, bool includeCompletion)
    {
        string[] expected=CompleteViewWorkflow.RequiredChecks(interactive).Concat(new[] {"native-startup", "complete-view-setup-and-cleanup",
            "runtime-manifest-bytes-and-compile-contract-exclusion", "ordinary-default-load-context", "netcore-only-framework-load"})
            .Concat(includeCompletion ? new[] {"complete-view-all-checks-completed"} : Array.Empty<string>()).ToArray();
        string[] actual=names.ToArray();
        Require(actual.Length==expected.Length && new HashSet<string>(actual,StringComparer.Ordinal).SetEquals(expected),
            "The complete view result omitted, duplicated or added a required check. Expected="+string.Join(",",expected)+"; actual="+string.Join(",",actual));
    }

    private static int AuditCompleteViewResults(string path)
    {
        try
        {
            using var document=JsonDocument.Parse(File.ReadAllText(path));
            Require(document.RootElement.GetProperty("probe").GetString()=="complete-portable-wpf-view","The result has the wrong probe identity.");
            var checks=document.RootElement.GetProperty("checks").EnumerateArray().ToArray();
            VerifyRequiredChecks(checks.Select(check=>check.GetProperty("name").GetString()!),interactive:false,includeCompletion:true);
            Require(checks.All(check=>check.GetProperty("status").GetString()=="pass"),"The complete view contains a failed check.");
            Console.WriteLine(JsonSerializer.Serialize(new {probe="complete-view-result-audit",checks=new[]{new{name="required-probe-and-check-set",status="pass",detail="All 19 required checks completed once and passed."}}}));
            return 0;
        }
        catch(Exception exception)
        {
            Console.WriteLine(JsonSerializer.Serialize(new {probe="complete-view-result-audit",checks=new[]{new{name="required-probe-and-check-set",status="fail",detail=exception.ToString()}}}));
            return 1;
        }
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
