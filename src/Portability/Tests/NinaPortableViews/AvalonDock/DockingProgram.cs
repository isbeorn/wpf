using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvalonDock;
using AvalonDock.Layout;

internal static class DockingProgram
{
    private static readonly List<object> Checks = [];
    private static readonly List<object> Observations = [];
    private static readonly List<object> NativeFailures = [];
    private static string phase = "desktop-prerequisite";
    private static string ExpectedAvalonDockHash => Environment.GetEnvironmentVariable("AVALONDOCK_EXPECTED_SHA256") ?? "E238E124A3A832EFEBCAD2FBCD1DECD1C6228F223A6462E80FBD16DD7981649D";

    [STAThread]
    private static int Main(string[] args)
    {
#if !NINA_STOCK_REFERENCE
        X11Fixture? native = null;
#endif
        Window? owner = null;
        DockingManager? manager = null;
        var loaded = 0;
        var unloaded = 0;
        Application? app = null;
        NINA.Profile.ProfileService? profile = null;
        Exception? failure = null;
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (e.Exception is DllNotFoundException or BadImageFormatException or MarshalDirectiveException)
                NativeFailures.Add(new { phase, exception = e.Exception.ToString() });
        };
        try
        {
#if NINA_STOCK_REFERENCE
            Check("private-noninput-desktop", () => VerifyDesktop(args));
#else
            Check("private-Xvfb-bootstrap", () => { Require(OperatingSystem.IsLinux() && args.SequenceEqual(new[] { "--private-xvfb" }), "Dedicated private Xvfb is required."); native = new X11Fixture("little"); Wpf.Portable.NativeHost.NativeWindowHost.InitializeForCurrentThread(Avalonia.AppBuilder.Configure<Avalonia.Application>()); });
#endif
            Check("original-NINA-and-selected-AvalonDock-closure", VerifyClosure);
            Check("selected-WPF-and-AvalonDock", () =>
            {
                foreach (var assembly in new[] { typeof(Window).Assembly, typeof(Visual).Assembly, typeof(DependencyObject).Assembly })
#if NINA_STOCK_REFERENCE
                    Require(assembly.Location.Contains("Microsoft.WindowsDesktop.App" + Path.DirectorySeparatorChar + "10.0.12", StringComparison.Ordinal)
                        && AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "Expected stock WindowsDesktop10.0.12.");
#else
                    Require(Path.GetDirectoryName(assembly.Location) == Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)
                        && AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default, "Expected copied sealed source WPF in default context.");
#endif
                var docking = typeof(DockingManager).Assembly;
                Require(docking.FullName == "AvalonDock, Version=4.74.1.0, Culture=neutral, PublicKeyToken=3e4669d2f30244f4", "AvalonDock identity differs.");
                Require(Hash(docking.Location) == ExpectedAvalonDockHash, "Selected source-adapted AvalonDock bytes differ.");
            });
            var state = Environment.GetEnvironmentVariable("NINA_PORTABLE_VIEWS_STATE")!;
            Require(Path.IsPathFullyQualified(state) && !Directory.Exists(state), "Fresh absolute owned state is required.");
            Directory.CreateDirectory(state);
            NINA.Core.Utility.CoreUtil.APPLICATIONTEMPPATH = state;
            NINA.Profile.ProfileService.PROFILEFOLDER = Path.Combine(state, "Profiles");
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Check("original-33-resource-dictionaries-and-profile", () =>
            {
                var sources = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ResourceSources.json")))!;
                Require(sources.Length == 33, "Resource closure differs.");
                foreach (var source in sources)
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
                profile = (NINA.Profile.ProfileService)app.Resources["ProfileService"];
                Require(profile.TryLoad(string.Empty), "Original profile failed.");
            });
            var pixels = Enumerable.Range(0, 64 * 48).Select(i => (ushort)(i * 19)).ToArray();
            var image = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Gray16, null, pixels, 128);
            var imageView = new NINA.WPF.Base.View.ImageView { Image = image };
            var simulation = new GraphSimulation(profile!);
            var graph = new NINA.View.Equipment.Guider.GuiderGraph { DataContext = simulation };
            var imagePane = new LayoutAnchorable { ContentId = "original-image", Title = "Original ImageView", Content = imageView };
            var graphPane = new LayoutAnchorable { ContentId = "original-guider", Title = "Original GuiderGraph", Content = graph };
            manager = new DockingManager
            {
                Layout = new LayoutRoot { RootPanel = new LayoutPanel { Children = { new LayoutAnchorablePane(imagePane), new LayoutAnchorablePane(graphPane) } } }
            };
            manager.Loaded += (_, _) => loaded++;
            manager.Unloaded += (_, _) => unloaded++;
            owner = new Window { Title = "AvalonDock original NINA panes", Content = manager, Width = 1200, Height = 800,
                Left = 20, Top = 20, ShowActivated = false };
            Check("real-DockingManager-Loaded-with-original-views", () =>
            {
                owner.Show();
                Settle();
                Require(loaded == 1 && manager.IsLoaded && imageView.ActualWidth > 0 && graph.ActualWidth > 0, "Genuine Loaded/positive original-view layout failed.");
                VerifyContainers(manager, "initial-layout");
                Record(manager, imagePane, graphPane, "docked");
            });
            foreach (var pane in new[] { imagePane, graphPane })
            {
                Check(pane.ContentId + "/Float-Dock", () =>
                {
                    var content = pane.Content;
                    Console.WriteLine("stage: before-Float/" + pane.ContentId);
                    pane.Float();
                    Settle();
                    VerifyContainers(manager, "after-Float/" + pane.ContentId);
                    Require(pane.IsFloating && manager.FloatingWindows.Count() == 1, "Original Float did not create one floating control.");
                    var floating = manager.FloatingWindows.Single();
                    Require(floating.IsVisible && PresentationSource.FromVisual(floating) is not null && ReferenceEquals(content, pane.Content), "Floating content/source identity failed.");
#if !NINA_STOCK_REFERENCE
                    var initialSize = ObserveFloatingSize(native!, floating, pane, "before-grow");
#endif
                    floating.Width += 64; floating.Height += 32; Settle();
#if !NINA_STOCK_REFERENCE
                    var grownSize = ObserveFloatingSize(native!, floating, pane, "after-grow");
                    Require(grownSize.Width > initialSize.Width && grownSize.Height > initialSize.Height,
                        "The achieved floating size did not grow in both axes.");
#endif
                    floating.Width -= 32; floating.Height -= 16; Settle();
#if !NINA_STOCK_REFERENCE
                    var shrunkSize = ObserveFloatingSize(native!, floating, pane, "after-shrink");
                    Require(shrunkSize.Width < grownSize.Width && shrunkSize.Height < grownSize.Height,
                        "The achieved floating size did not shrink in both axes.");
#endif
                    Record(manager, imagePane, graphPane, pane.ContentId + "/floating-resized");
                    Console.WriteLine("stage: before-Dock/" + pane.ContentId);
                    pane.Dock(); Settle();
                    VerifyContainers(manager, "after-Dock/" + pane.ContentId);
                    Require(!pane.IsFloating && !manager.FloatingWindows.Any() && ReferenceEquals(content, pane.Content), "Original Dock failed to restore content and close floating control.");
                    Require(!floating.IsVisible && PresentationSource.FromVisual(floating) is null,
                        "Dock left the saved floating Window visible or attached to a source.");
                });
            }
            Check("original-XML-docked-roundtrip-and-live-history", () =>
            {
                var serializer = new AvalonDock.Layout.Serialization.XmlLayoutSerializer(manager);
                using var xml = new StringWriter(); serializer.Serialize(xml);
                serializer.LayoutSerializationCallback += (_, e) => e.Content = e.Model.ContentId switch
                { "original-image" => imageView, "original-guider" => graph, _ => throw new InvalidDataException("Unknown ContentId.") };
                serializer.Deserialize(new StringReader(xml.ToString())); Settle();
                VerifyContainers(manager, "after-XML-restore");
                Require(ReferenceEquals(manager.Layout.Descendents().OfType<LayoutAnchorable>().Single(p => p.ContentId == "original-image").Content, imageView), "Restored ImageView differs.");
                Require(ReferenceEquals(manager.Layout.Descendents().OfType<LayoutAnchorable>().Single(p => p.ContentId == "original-guider").Content, graph)
                    && ReferenceEquals(graph.DataContext, simulation), "Restored GuiderGraph or its DataContext differs.");
                var previous = simulation.GuideStepsHistory.ChangeId; simulation.AppendFourth(); Settle();
                Require(simulation.GuideStepsHistory.ChangeId > previous, "Original guide history failed after restoration.");
                Observations.Add(new { stage = "serialized-docked-layout", xml = xml.ToString() });
            });
            Check("accepted-owner-close", () => { owner.Close(); Settle(); Require(unloaded == 1 && !manager.IsLoaded, "Manager did not unload once."); });
        }
        catch (Exception e)
        {
            failure = e;
            Checks.Add(new { name = phase, status = "fail", detail = e.ToString() });
            Observations.Add(new { stage = "failure-state", loadedCallbacks = loaded, unloadedCallbacks = unloaded,
                ownerLoaded = owner?.IsLoaded, managerLoaded = manager?.IsLoaded });
        }
        finally
        {
            try { owner?.Close(); profile?.Release(); app?.Shutdown();
#if !NINA_STOCK_REFERENCE
                native?.Dispose();
#endif
            }
            catch (Exception e) { failure ??= e; Checks.Add(new { name = "cleanup", status = "fail", detail = e.ToString() }); }
        }
        var result = JsonSerializer.Serialize(new { probe = "AvalonDock-two-original-NINA-anchorables", failed = failure is not null,
            phase, checks = Checks, observations = Observations, nativeFailures = NativeFailures,
            scope = "Explicitly hash-selected4.74.1 component with original NINA views/resources. Private desktop or Xvfb replay only. No interactive activation/dragging, substitute controls or full Overview/equipment claim.",
            assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && (a.GetName().Name!.StartsWith("NINA", StringComparison.Ordinal) || a.GetName().Name == "AvalonDock"))
                .Select(a => new { identity = a.FullName, path = a.Location, sha256 = Hash(a.Location) }) }, new JsonSerializerOptions { WriteIndented = true });
        var resultPath = Environment.GetEnvironmentVariable("NINA_AVALONDOCK_RESULT_PATH");
        if (resultPath != null) File.WriteAllText(resultPath, result);
        Console.WriteLine(result);
        return failure is null ? 0 : 1;
    }

#if !NINA_STOCK_REFERENCE
    private static Size ObserveFloatingSize(X11Fixture native, Window floating, LayoutAnchorable pane, string stage)
    {
        var source = PresentationSource.FromVisual(floating);
        Require(source is not null, "Floating source detached before size observation.");
        object peer = source!.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
        object bridge = peer.GetType().GetField("_native", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(peer)!;
        var xid = (IntPtr)bridge.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bridge)!;
        var client = (Size)peer.GetType().GetProperty("ClientSize")!.GetValue(peer)!;
        var outer = (Size)peer.GetType().GetProperty("OuterSize")!.GetValue(peer)!;
        var peerDpi = (DpiScale)peer.GetType().GetProperty("DpiScale")!.GetValue(peer)!;
        var dpi = VisualTreeHelper.GetDpi(floating);
        var pixels = native.Size(xid);
        var view = (FrameworkElement)pane.Content;
        Observations.Add(new { stage = "achieved-floating-size/" + pane.ContentId + "/" + stage,
            xid = xid.ToInt64(), pixels.Width, pixels.Height,
            clientWidth = client.Width, clientHeight = client.Height, outerWidth = outer.Width, outerHeight = outer.Height,
            actualWidth = floating.ActualWidth, actualHeight = floating.ActualHeight,
            renderWidth = floating.RenderSize.Width, renderHeight = floating.RenderSize.Height,
            dpiX = dpi.DpiScaleX, dpiY = dpi.DpiScaleY, modelWidth = pane.FloatingWidth, modelHeight = pane.FloatingHeight,
            viewWidth = view.ActualWidth, viewHeight = view.ActualHeight });
        Require(xid != IntPtr.Zero && native.IsMapped(xid), "Floating native XID is not mapped.");
        Require(dpi.DpiScaleX > 0 && dpi.DpiScaleY > 0
            && Equal(dpi.DpiScaleX, peerDpi.DpiScaleX) && Equal(dpi.DpiScaleY, peerDpi.DpiScaleY),
            "Visual and native-host DPI disagree.");
        Require(Equal(outer.Width, client.Width) && Equal(outer.Height, client.Height),
            "This private no-WM Xvfb calibration requires zero native frame extents.");
        Require(Equal(pixels.Width, client.Width * dpi.DpiScaleX) && Equal(pixels.Height, client.Height * dpi.DpiScaleY),
            "Native client pixels and acknowledged client DIPs disagree.");
        Require(Equal(floating.ActualWidth, outer.Width) && Equal(floating.ActualHeight, outer.Height)
            && Equal(floating.RenderSize.Width, floating.ActualWidth) && Equal(floating.RenderSize.Height, floating.ActualHeight)
            && Equal(pane.FloatingWidth, floating.ActualWidth) && Equal(pane.FloatingHeight, floating.ActualHeight),
            "Achieved floating layout, acknowledged native size and model geometry disagree.");
        Require(view.ActualWidth > 0 && view.ActualHeight > 0 && ReferenceEquals(PresentationSource.FromVisual(view), source),
            "The original floating view lost positive layout or its genuine source.");
        return new Size(floating.ActualWidth, floating.ActualHeight);

        static bool Equal(double left, double right) => Math.Abs(left - right) < 0.000001;
    }

#endif

    private static void VerifyContainers(DockingManager manager, string stage)
    {
        var seen = new HashSet<DependencyObject>();
        var tabs = new List<object>();
        foreach (var root in manager.FloatingWindows.Cast<Visual>().Prepend(manager).ToArray())
            Visit(root);
        Require(tabs.Count > 0, "No live TabControl was inspected at " + stage);
        Observations.Add(new { stage = "post-Settle-container-roundtrip/" + stage, tabs });

        void Visit(DependencyObject current)
        {
            if (!seen.Add(current)) return;
            if (current is TabControl tab && tab.IsLoaded)
            {
                var realized = 0;
                for (var index = 0; index < tab.Items.Count; index++)
                {
                    var container = tab.ItemContainerGenerator.ContainerFromIndex(index);
                    if (container is null) continue;
                    realized++;
                    Require(Equals(tab.ItemContainerGenerator.ItemFromContainer(container), tab.Items[index]),
                        $"Container/item mismatch at {stage}: {tab.GetType().FullName}[{index}]");
                    Require(ReferenceEquals(tab.ItemContainerGenerator.ContainerFromItem(tab.Items[index]), container)
                        && tab.ItemContainerGenerator.IndexFromContainer(container) == index,
                        $"Container reverse lookup mismatch at {stage}: {tab.GetType().FullName}[{index}]");
                }
                var selected = tab.SelectedIndex;
                if (selected >= 0)
                {
                    Require(selected < tab.Items.Count && Equals(tab.SelectedItem, tab.Items[selected]),
                        "Selected item/index mismatch at " + stage);
                    var selectedContainer = tab.ItemContainerGenerator.ContainerFromIndex(selected) as TabItem;
                    Require(selectedContainer is not null && selectedContainer.IsSelected
                        && Equals(tab.SelectedContent, selectedContainer.Content),
                        "Selected container/content mismatch at " + stage);
                }
                else
                    Require(tab.SelectedItem is null, "Unselected tab retains SelectedItem at " + stage);
                tabs.Add(new { type = tab.GetType().FullName, items = tab.Items.Count, realized, selected });
            }
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                Visit(VisualTreeHelper.GetChild(current, index));
        }
    }

    private static void Record(DockingManager manager, LayoutAnchorable image, LayoutAnchorable graph, string stage) =>
        Observations.Add(new { stage, floating = manager.FloatingWindows.Select(w => new { w.Left, w.Top, w.Width, w.Height, w.IsVisible }).ToArray(),
            panes = new[] { image, graph }.Select(p => new { p.ContentId, p.IsFloating, p.IsActive, p.IsSelected, parent = p.Parent?.GetType().FullName,
                p.FloatingLeft, p.FloatingTop, p.FloatingWidth, p.FloatingHeight, content = p.Content.GetType().FullName }).ToArray() });

    private static void VerifyClosure()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Closure.json")));
        Require(manifest.RootElement.GetProperty("revision").GetString() == "4742afae0a09c79be772f23fcf163fb5852d2775", "NINA revision differs.");
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, file.GetProperty("outputPath").GetString()!));
            Require(path.StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal) && Hash(path) == (file.GetProperty("outputPath").GetString() == "AvalonDock.dll" ? ExpectedAvalonDockHash : file.GetProperty("sha256").GetString()), "Original NINA closure differs: " + path);
        }
    }

    private static void VerifyDesktop(string[] args)
    {
        Require(args.SequenceEqual(new[] { "--private-noninput-desktop" }), "A reviewed private noninput desktop is required before WPF construction.");
        var current = DesktopName(GetThreadDesktop(GetCurrentThreadId()));
        var input = OpenInputDesktop(0, false, 1);
        Require(input != IntPtr.Zero, "Cannot observe input desktop.");
        string inputName;
        try { inputName = DesktopName(input); } finally { Require(CloseDesktop(input), "Cannot close input desktop observation."); }
        Require(current != inputName && !current.Equals("Default", StringComparison.OrdinalIgnoreCase), "Refusing UI on input/Default desktop.");
        Observations.Add(new { stage = "desktop", current, inputName });
    }

    private static string DesktopName(IntPtr desktop)
    {
        char[] name = new char[256];
        Require(GetUserObjectInformation(desktop, 2, name, name.Length * 2, out _), "Cannot read desktop identity.");
        var end = Array.IndexOf(name, '\0'); Require(end >= 0, "Unterminated desktop identity."); return new string(name, 0, end);
    }
    private static void Settle() { var f = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => f.Continue = false)); Dispatcher.PushFrame(f); }
    private static void Check(string name, Action action) { phase = name; action(); Checks.Add(new { name, status = "pass" }); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint id);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, [Out] char[] data, int length, out int needed);
}
