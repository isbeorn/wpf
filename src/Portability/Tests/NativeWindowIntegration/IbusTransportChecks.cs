extern alias WpfFork;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Linq.Expressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using Avalonia.Platform;
using Avalonia.X11;
using Tmds.DBus.Protocol;
using Wpf.Portable.NativeHost;
using Dispatcher = WpfFork::System.Windows.Threading.Dispatcher;
using DispatcherFrame = WpfFork::System.Windows.Threading.DispatcherFrame;
using DispatcherTimer = WpfFork::System.Windows.Threading.DispatcherTimer;
using DispatcherPriority = WpfFork::System.Windows.Threading.DispatcherPriority;

internal static class IbusTransportChecks
{
    private static readonly ConcurrentQueue<object> Trace = new();
    private static readonly List<object> Results = new();
    private static long _sequence;
    private static string _phase = "startup";
    private static string _output = "";
    private static bool _completed;
    private static readonly List<string> Delivery = new();
    private static readonly List<string> Commits = new();
    private static int _rawKeys;
    private static readonly List<(string type, string key)> Keys = new();
    private static void Record(string kind, object? value = null) => Trace.Enqueue(new { sequence = Interlocked.Increment(ref _sequence), ticks = Stopwatch.GetTimestamp(), thread = Environment.CurrentManagedThreadId, phase = _phase, kind, value });
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Write() => File.WriteAllText(_output, JsonSerializer.Serialize(new { scope = "private protocol counterpart and pinned native client; no WPF editor wiring or real composing-engine proof", completed = _completed, results = Results, trace = Trace }, new JsonSerializerOptions { WriteIndented = true }));
    internal static int Run(string[] args)
    {
        _output = Path.GetFullPath(args[0]);
        string? factory = args.FirstOrDefault(value => value.StartsWith("--factory=", StringComparison.Ordinal));
        if (factory is not null) return Factory(factory[10..]);
        using var watchdog = new Timer(_ => { Record("watchdog"); Write(); Environment.Exit(7); }, null, 25000, Timeout.Infinite);
        try
        {
            Require(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WPF_IBUS_PRIVATE_BUS") == "1", "Require the owned service-free private session bus and Xvfb.");
            using var connection = new DBusConnection(new DBusConnectionOptions(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")!) { AutoConnect = false });
            connection.ConnectAsync().AsTask().GetAwaiter().GetResult();
            var portal = new Portal(connection) { HoldCreate = args.Contains("--held-create"), HoldDestroy = args.Contains("--hold-destroy") };
            connection.AddMethodHandler(portal);
            connection.RequestNameAsync("org.freedesktop.portal.IBus", RequestNameOptions.Default).GetAwaiter().GetResult();
            using var native = new X11Fixture("little");
            NativeWindowHost.InitializeForCurrentThread(AppBuilder.Configure<Application>());
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                IWindowImpl? window = null;
                ExternalWindowBridge? bridge = null;
                ITextInputMethodImpl? method = null;
                try
                {
                    window = AvaloniaLocator.Current.GetRequiredService<IWindowingPlatform>().CreateWindow();
                    bridge = new ExternalWindowBridge(window);
                    bridge.RequestClientSize(new Size(320, 180)); window.SetTitle("Private native IBus observer");
                    window.Input = raw =>
                    {
                        if (raw is RawTextInputEventArgs text) { Commits.Add(text.Text); Delivery.Add("C:" + text.Text); Record("raw-commit", new { text.Text }); }
                        else if (raw is RawKeyEventArgs key) { ++_rawKeys; Keys.Add((key.Type.ToString(), key.Key.ToString())); Record("raw-key", new { type = key.Type.ToString(), key = key.Key.ToString() }); }
                    };
                    window.Show(true, false); native.Focus(window.Handle!.Handle);
                    method = window.TryGetFeature(typeof(ITextInputMethodImpl)) as ITextInputMethodImpl;
                    if (method is null) throw new InvalidOperationException("The genuine native IBus feature is unavailable.");
                    Record("native-feature", new { type = method.GetType().FullName, assembly = method.GetType().Assembly.Location });
                    File.WriteAllText(Path.ChangeExtension(_output, ".maps.txt"), File.ReadAllText("/proc/self/maps"));
                    File.WriteAllText(Path.ChangeExtension(_output, ".assemblies.json"), JsonSerializer.Serialize(AppDomain.CurrentDomain.GetAssemblies().Where(item => !item.IsDynamic).Select(item => new { name = item.FullName, location = item.Location }), new JsonSerializerOptions { WriteIndented = true }));
                    if (args.Contains("--held-create"))
                    {
                        Wait(() => portal.CreatePending);
                        portal.AfterWindowDispose = true;
                        window.Dispose(); window = null;
                        Record("review-disposed-before-create");
                        portal.ReleaseCreate(); Drain(); Drain();
                        Record("review-after-create", new { portal.CreateCount, portal.DestroyCount, caps = portal.Capabilities.ToArray() });
                        Require(portal.DestroyCount == 1 && portal.Capabilities.IsEmpty, "A context created after window disposal was not destroyed or was activated.");
                        Require(portal.ContextMethodsAfterDispose.IsEmpty, "A disposed connecting Window made native context calls.");
                        if (args.Contains("--hold-destroy")) Require(portal.DestroyPending, "The late-created context teardown was not held.");
                        AssertNoSignals(portal, method, "LATE-CREATED-CONTEXT");
                        Passed("disposed-before-create-destroys-unpublished-context");
                        _completed = true; return;
                    }
                    Wait(() => portal.Capabilities.Contains(8u)); Drain();
                    Require(portal.FocusInCount == 0, "An unwired bridge unexpectedly attached a native text client.");
                    var a = new Client("A"); var b = new Client("B");
                    method.SetClient(a);
                    Wait(() => portal.Capabilities.Contains(9u) && portal.FocusInCount > 0); Drain();
                    _phase = "smoke";
                    portal.Preedit("smoke", 2, true);
                    Wait(() => a.Calls.Count == 1);
                    Require(a.Calls.Single() == ("smoke", 2), "The actual native preedit callback did not match the wire text/caret.");
                    Results.Add(new { name = "native-feature-context-capabilities-preedit", status = "pass", feature = method.GetType().FullName });
                    if (!args.Contains("--smoke-only")) Observe(portal, method, a, b, native, window, args);
                    method.SetClient(null); Drain();
                    window.Dispose(); window = null;
                    if (!args.Contains("--case=disconnect")) Wait(() => portal.DestroyCount == 1);
                    else Drain();
                    Require(portal.CreateCount == 1, "Factory replacement created a discarded or extra native context.");
                    if (args.Contains("--case=disconnect")) Require(portal.DestroyCount == 0, "Disconnected context received a remote Destroy call.");
                    Results.Add(new { name = args.Contains("--case=disconnect") ? "native-close-after-service-loss" : "native-context-destroy-on-window-close", status = "pass" });
                    _completed = true;
                }
                catch (Exception error) { Record("failure", error.ToString()); Results.Add(new { name = "observer", status = "fail", error = error.ToString() }); }
                finally
                {
                    method?.SetClient(null); portal.ReleaseReset(); portal.ReleaseKey(); portal.ReleaseCursor(); portal.ReleaseDestroy(); portal.ReleaseCreate();
                    try { window?.Dispose(); } finally { bridge?.Dispose(); }
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            }));
            Dispatcher.Run();
        }
        catch (Exception error) { Record("startup-failure", error.ToString()); Results.Add(new { name = "startup", status = "fail", error = error.ToString() }); }
        Write();
        return _completed ? 0 : 1;
    }

    private static int Factory(string mode)
    {
        IWindowImpl? window = null;
        ExternalWindowBridge? bridge = null;
        try
        {
            Require(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WPF_IBUS_PRIVATE_BUS") == "1", "Factory tests require the private Xvfb/session bus.");
            foreach (string name in new[] { "AVALONIA_IM_MODULE", "GTK_IM_MODULE", "QT_IM_MODULE", "XMODIFIERS" }) Environment.SetEnvironmentVariable(name, null);
            string? expected = "Avalonia.X11.PortableIbusTextInputMethod";
            switch (mode)
            {
                case "ibus": Environment.SetEnvironmentVariable("AVALONIA_IM_MODULE", "ibus"); break;
                case "disable": Environment.SetEnvironmentVariable("AVALONIA_IM_MODULE", "none"); Environment.SetEnvironmentVariable("GTK_IM_MODULE", "ibus"); expected = null; break;
                case "gtk-disable": Environment.SetEnvironmentVariable("GTK_IM_MODULE", "none"); Environment.SetEnvironmentVariable("QT_IM_MODULE", "ibus"); expected = null; break;
                case "fcitx": case "fcitx5":
                    Environment.SetEnvironmentVariable("AVALONIA_IM_MODULE", mode); Environment.SetEnvironmentVariable("GTK_IM_MODULE", "ibus");
                    expected = "Avalonia.FreeDesktop.DBusIme.Fcitx.FcitxX11TextInputMethod"; break;
                case "gtk-precedence": Environment.SetEnvironmentVariable("AVALONIA_IM_MODULE", "unknown"); Environment.SetEnvironmentVariable("GTK_IM_MODULE", "ibus"); Environment.SetEnvironmentVariable("QT_IM_MODULE", "fcitx"); break;
                case "qt": Environment.SetEnvironmentVariable("QT_IM_MODULE", "ibus"); break;
                case "xmodifiers": Environment.SetEnvironmentVariable("XMODIFIERS", "@im=ibus"); break;
                case "missing-connection": Environment.SetEnvironmentVariable("AVALONIA_IM_MODULE", "ibus"); Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", "unix:path=" + Path.Combine(Path.GetDirectoryName(_output)!, "absent-session-bus.sock")); expected = null; break;
                default: throw new ArgumentException("Unknown factory test.");
            }
            using var native = new X11Fixture("little");
            NativeWindowHost.InitializeForCurrentThread(AppBuilder.Configure<Application>());
            window = AvaloniaLocator.Current.GetRequiredService<IWindowingPlatform>().CreateWindow();
            bridge = new ExternalWindowBridge(window);
            object? method = window.TryGetFeature(typeof(ITextInputMethodImpl));
            string? actual = method?.GetType().FullName;
            Record("factory-feature", new { mode, expected, actual });
            Require(actual == expected, "Actual native factory selection/fallback changed for " + mode + ": " + actual);
            Drain();
            Results.Add(new { name = "native-factory-" + mode, status = "pass", expected, actual });
            _completed = true;
        }
        catch (Exception error) { Results.Add(new { name = "factory", status = "fail", error = error.ToString() }); }
        finally { try { window?.Dispose(); } finally { bridge?.Dispose(); } Write(); }
        return _completed ? 0 : 1;
    }
    private static void Observe(Portal portal, ITextInputMethodImpl method, Client a, Client b, X11Fixture native, IWindowImpl window, string[] args)
    {
        if (args.Any(value => value.StartsWith("--case=", StringComparison.Ordinal)))
        {
            FailureCase(portal, method, a, b, args.Single(value => value.StartsWith("--case=", StringComparison.Ordinal))[7..]);
            return;
        }
        using var watch = new PreeditWatch(method);
        Complete(Change(method, a));
        _phase = "caret-only";
        a.Calls.Clear(); portal.Preedit("same", 1, true); Wait(() => a.Calls.Count == 1);
        portal.Preedit("same", 3, true); Wait(() => a.Calls.Count == 2);
        Require(a.Calls.Select(call => call.caret).SequenceEqual(new int?[] { 1, 3 }), "The native client dropped a same-text caret-only update.");
        Require(watch.Updates.TakeLast(2).All(value => value.kind == "Update") && watch.Updates.Last().caret == 3, "Transport lost caret-only state.");
        Passed("same-text-caret-only");

        _phase = "unicode-caret";
        portal.Preedit("a\uD83D\uDE00b", 2, true); Wait(() => a.Calls.Count == 3);
        Require(a.Calls.Last() == ("a\uD83D\uDE00b", 3) && watch.Updates.Last().caret == 3, "IBus scalar caret must become UTF-16 exactly once.");
        Passed("scalar-caret-converts-once-to-utf16");

        _phase = "visibility";
        watch.Updates.Clear();
        portal.Preedit("candidate", 2, true); Wait(() => watch.Updates.Count == 1);
        portal.Hide(); Wait(() => watch.Updates.Count == 2);
        Require(a.Calls.Last() == ("", 0), "Legacy Hide callback changed.");
        int callsBeforeShow = a.Calls.Count;
        portal.Show(); Wait(() => watch.Updates.Count == 3);
        Require(a.Calls.Count == callsBeforeShow, "Legacy Show callback changed.");
        Require(watch.Updates[1] == ("Hide", "candidate", 2, false) && watch.Updates[2] == ("Show", "candidate", 2, true), "Show/Hide must preserve complete candidate state without defining document policy.");
        portal.Preedit("", 0, true); Wait(() => watch.Updates.Count == 4);
        Require(watch.Updates.Last() == ("Update", "", 0, true), "Visible empty candidate was conflated with Hide.");
        portal.Preedit("invisible", 4, false); Wait(() => watch.Updates.Count == 5);
        Require(watch.Updates.Last() == ("Update", "invisible", 4, false), "Visible=false was lost.");
        Require(a.Calls.Last() == ("invisible", 4), "Legacy Update callback text/caret must remain unchanged when visibility is false.");
        portal.Preedit("invisible", 5, false); Wait(() => watch.Updates.Count == 6);
        Require(watch.Updates.Last().caret == 5 && !watch.Updates.Last().visible, "Hidden caret-only state was lost.");
        Passed("distinct-empty-hidden-show-hide-and-hidden-caret");

        _phase = "wire-order";
        Delivery.Clear(); Commits.Clear();
        portal.Preedit("before-commit", 1, true); portal.Commit("COMMITTED"); portal.Preedit("after-commit", 2, true);
        Wait(() => Delivery.Count == 3);
        Require(Delivery.SequenceEqual(new[] { "P:before-commit", "C:COMMITTED", "P:after-commit" }), "The observed native Preedit/Commit sequence changed.");
        Passed("actual-preedit-commit-preedit-order-preserved");

        _phase = "attached-forward-key-control";
        Keys.Clear(); portal.Forward(0); portal.Forward(1u << 30);
        Wait(() => Keys.Count == 2);
        Require(Keys.SequenceEqual(new[] { ("KeyDown", "A"), ("KeyUp", "A") }), "Ordinary attached ForwardKeyEvent down/up changed.");
        Passed("ordinary-attached-native-forward-key-down-up");
        _phase = "pending-reset-handover";
        var c = new Client("C"); a.Calls.Clear(); b.Calls.Clear(); Commits.Clear();
        portal.HoldReset = true;
        Task first = Change(method, b); Wait(() => portal.ResetPending);
        Require(CurrentClient(method) is null && !first.IsCompleted, "Handover did not detach immediately or wait for the actual Reset reply.");
        Task middle = Change(method, a);
        Task latest = Change(method, c);
        portal.Preedit("old-context-during-reset", 4, true); portal.Commit("RESET-COMMIT"); Drain();
        Require(a.Calls.Count == 0 && b.Calls.Count == 0 && c.Calls.Count == 0 && Commits.Count == 0 && !latest.IsCompleted, "Old composition escaped while the native Reset was held.");
        AssertNoSignals(portal, method, "RESET-SIGNAL-AUDIT", a, b, c);
        portal.ReleaseReset(); Complete(latest); Wait(() => first.IsCompleted && middle.IsCompleted);
        Require(first.IsCanceled && middle.IsCanceled && ReferenceEquals(CurrentClient(method), c), "Only the latest valid target may attach after native Reset.");
        portal.Preedit("current", 2, true); Wait(() => c.Calls.Count == 1);
        Require(a.Calls.Count == 0 && b.Calls.Count == 0, "A superseded client received composition.");
        Passed("actual-reset-barrier-and-latest-target-only");

        _phase = "same-client-reset";
        portal.HoldReset = true;
        Task reset = (Task)method.GetType().GetMethod("ResetAsync")!.Invoke(method, null)!;
        Wait(() => portal.ResetPending);
        Require(CurrentClient(method) is null && !reset.IsCompleted, "Same-client Reset skipped the native barrier.");
        portal.ReleaseReset(); Complete(reset);
        Require(ReferenceEquals(CurrentClient(method), c), "Same-client Reset did not restore its target.");
        Passed("same-client-asynchronous-reset");

        _phase = "real-key-filter";
        Complete(Change(method, a)); Drain(); a.Calls.Clear(); Commits.Clear();
        int keysBefore = _rawKeys;
        portal.HoldKey = true;
        native.Key('x', true); Wait(() => portal.KeyPending);
        portal.Preedit("key-pending", 2, true); portal.Commit("KEY-COMMIT");
        Wait(() => a.Calls.Count == 1 && Commits.Count == 1);
        Require(portal.KeyPending && Commits.Single() == "KEY-COMMIT", "Actual preedit/Commit callbacks did not arrive while native ProcessKeyEvent was held.");
        portal.ReleaseKey(); Drain(); native.Key('x', false); Wait(() => portal.KeyCount == 2); Drain();
        Require(_rawKeys == keysBefore, "Filtered native key transitions unexpectedly escaped.");
        Passed("actual-held-process-key-event-and-filtered-down-up");

        Complete(Change(method, null));
        portal.Preedit("detached", 1, true); Drain();
        Require(CurrentClient(method) is null && a.Calls.Count == 1, "Null-target handover did not detach.");
        Passed("null-target-handover-detaches");
    }

    private static void FailureCase(Portal portal, ITextInputMethodImpl method, Client a, Client b, string mode)
    {
        _phase = mode;
        if (mode == "reset-signals")
        {
            portal.HoldReset = true;
            Task reset = Change(method, b); Wait(() => portal.ResetPending);
            AssertNoSignals(portal, method, "RESET-SIGNAL-AUDIT", a, b);
            portal.ReleaseReset(); Complete(reset);
            Passed("all-native-signals-suppressed-during-pending-reset");
            return;
        }
        if (mode == "queue-failure")
        {
            portal.HoldCursor = true;
            method.SetCursorRect(new Rect(17, 23, 1, 12));
            Wait(() => portal.CursorPending);
        }
        portal.HoldReset = mode != "queue-failure";
        Task first = Change(method, b);
        if (mode != "queue-failure") Wait(() => portal.ResetPending);
        Task latest = Change(method, a);
        Require(CurrentClient(method) is null, "Pending reset must detach the client.");
        if (mode == "queue-failure") { portal.HoldDestroy = true; portal.FailCursor(); Wait(() => portal.DestroyPending); }
        else if (mode == "reset-failure") { portal.HoldDestroy = true; portal.FailReset(); }
        else if (mode == "disconnect") portal.DropName();
        else if (mode == "dispose") ((IDisposable)method).Dispose();
        else throw new ArgumentException("Unknown transport failure case.");
        Wait(() => first.IsCompleted && latest.IsCompleted);
        Require(first.IsFaulted && latest.IsFaulted && CurrentClient(method) is null, "Failure/disconnect/dispose stranded a task or attached a stale client.");
        _ = first.Exception; _ = latest.Exception;
        if (mode == "queue-failure")
        {
            int before = Commits.Count;
            portal.Commit("LATE-FAILED-CONTEXT"); Drain();
            Record("review-commit-after-failure", new { before, after = Commits.Count });
            Require(Commits.Count == before, "The failed detached context delivered a late raw Commit while Destroy was held.");
        }
        if (mode is "reset-failure" or "queue-failure") Wait(() => portal.DestroyPending);
        AssertNoSignals(portal, method, "LATE-TERMINAL-CONTEXT", a, b);
        if (mode != "reset-failure") portal.ReleaseReset();
        if (mode is "queue-failure" or "reset-failure") portal.ReleaseDestroy();
        Drain();
        Require(CurrentClient(method) is null, "An old queued completion reattached after failure.");
        Passed("pending-handover-completes-detached-on-" + mode);
    }

    private static void AssertNoSignals(Portal portal, ITextInputMethodImpl method, string commit, params Client[] clients)
    {
        using var watch = new PreeditWatch(method);
        int commits = Commits.Count, keys = _rawKeys;
        int[] calls = clients.Select(client => client.Calls.Count).ToArray();
        portal.Preedit("stale-visible", 3, true); portal.Preedit("stale-hidden", 2, false);
        portal.Hide(); portal.Show(); portal.Commit(commit);
        portal.Forward(0); portal.Forward(1u << 30); Drain();
        Record("ineligible-signal-audit", new { commitDelta = Commits.Count - commits, keyDelta = _rawKeys - keys,
            preeditDelta = clients.Select((client, index) => client.Calls.Count - calls[index]).ToArray(), privateEvents = watch.Updates.Count });
        Require(Commits.Count == commits && _rawKeys == keys && watch.Updates.Count == 0 &&
            clients.Select(client => client.Calls.Count).SequenceEqual(calls), "An ineligible native context delivered Commit, ForwardKey, preedit or visibility.");
    }
    private static object? CurrentClient(ITextInputMethodImpl method) => method.GetType().GetProperty("Client")!.GetValue(method);
    private static Task Change(ITextInputMethodImpl method, TextInputMethodClient? client) => (Task)method.GetType().GetMethod("ChangeClientAsync")!.Invoke(method, new object?[] { client })!;
    private static void Complete(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Passed(string name) => Results.Add(new { name, status = "pass" });
    private sealed class PreeditWatch : IDisposable
    {
        private readonly object _source;
        private readonly System.Reflection.EventInfo _event;
        private readonly Delegate _handler;
        internal List<(string kind, string text, int caret, bool visible)> Updates { get; } = new();
        internal PreeditWatch(object source)
        {
            _source = source;
            _event = source.GetType().GetEvent("PreeditChanged") ?? throw new InvalidOperationException("The native transport contract is unavailable.");
            Type eventType = _event.EventHandlerType!;
            var value = Expression.Parameter(eventType.GenericTypeArguments.Single(), "update");
            Action<object> receive = item =>
            {
                object Read(string name) => item.GetType().GetProperty(name)!.GetValue(item)!;
                var update = (Read("Kind").ToString()!, (string)Read("Text"), (int)Read("Utf16Caret"), (bool)Read("Visible"));
                Updates.Add(update); Record("transport-preedit", new { kind = update.Item1, text = update.Item2, caret = update.Item3, visible = update.Item4 });
            };
            _handler = Expression.Lambda(eventType, Expression.Invoke(Expression.Constant(receive), Expression.Convert(value, typeof(object))), value).Compile();
            _event.AddEventHandler(source, _handler);
        }
        public void Dispose() => _event.RemoveEventHandler(_source, _handler);
    }

    private static void Wait(Func<bool> ready)
    {
        var deadline = Stopwatch.StartNew(); var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(4), DispatcherPriority.Background, (_, _) =>
        { if (ready() || deadline.Elapsed > TimeSpan.FromSeconds(3)) frame.Continue = false; }, Dispatcher.CurrentDispatcher);
        try { if (!ready()) Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Require(ready(), "The actual native/portal observable did not arrive within 3 seconds.");
    }
    private static void Drain()
    {
        bool ready = false;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) => ready = true, Dispatcher.CurrentDispatcher);
        try { Wait(() => ready); } finally { timer.Stop(); }
    }
    private sealed class Client(string name) : TextInputMethodClient
    {
        internal List<(string? text, int? caret)> Calls { get; } = new();
        public override Visual TextViewVisual { get; } = new Control();
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => false;
        public override string SurroundingText => "";
        public override Rect CursorRectangle => new(3, 4, 1, 12);
        public override TextSelection Selection { get; set; }
        public override void SetPreeditText(string? text, int? caret) { Calls.Add((text, caret)); Delivery.Add("P:" + text); Record("client-preedit", new { client = name, text, caret }); }
    }
    private sealed class Portal(DBusConnection connection) : IPathMethodHandler
    {
        public string Path => "/org/freedesktop/IBus";
        public bool HandlesChildPaths => true;
        internal readonly ConcurrentQueue<uint> Capabilities = new();
        internal readonly ConcurrentQueue<string> ContextMethodsAfterDispose = new();
        internal volatile bool AfterWindowDispose;
        internal int FocusInCount, DestroyCount, KeyCount, CreateCount;
        internal volatile bool HoldReset, HoldKey, HoldCursor, HoldDestroy, HoldCreate;
        private MethodContext? _reset, _key, _cursor, _destroy, _create;
        private string? _sender;
        private const string ContextPath = "/org/freedesktop/IBus/InputContext_1";
        internal bool CreatePending => Volatile.Read(ref _create) is not null;
        internal bool ResetPending => Volatile.Read(ref _reset) is not null;
        internal bool KeyPending => Volatile.Read(ref _key) is not null;
        internal bool CursorPending => Volatile.Read(ref _cursor) is not null;
        internal bool DestroyPending => Volatile.Read(ref _destroy) is not null;
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var message = context.Request; var reader = message.GetBodyReader();
            Record("portal-method", new { path = message.PathAsString, iface = message.InterfaceAsString, member = message.MemberAsString });
            if (AfterWindowDispose && message.InterfaceAsString == "org.freedesktop.IBus.InputContext")
                ContextMethodsAfterDispose.Enqueue(message.MemberAsString ?? "<none>");
            switch (message.MemberAsString)
            {
                case "CreateInputContext":
                    Interlocked.Increment(ref CreateCount);
                    string clientName = reader.ReadString(); _sender = message.SenderAsString;
                    Record("create-context", new { clientName, sender = _sender });
                    if (HoldCreate) { context.DisposesAsynchronously = true; Interlocked.Exchange(ref _create, context); return ValueTask.CompletedTask; }
                    using (var reply = context.CreateReplyWriter("o")) { reply.WriteObjectPath(ContextPath); context.Reply(reply.CreateMessage()); }
                    return ValueTask.CompletedTask;
                case "SetCapabilities": Capabilities.Enqueue(reader.ReadUInt32()); break;
                case "FocusIn": Interlocked.Increment(ref FocusInCount); break;
                case "SetCursorLocation":
                    Record("cursor-rect", new[] { reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() });
                    if (HoldCursor) { context.DisposesAsynchronously = true; Interlocked.Exchange(ref _cursor, context); return ValueTask.CompletedTask; }
                    break;
                case "Destroy":
                    Interlocked.Increment(ref DestroyCount);
                    if (HoldDestroy) { context.DisposesAsynchronously = true; Interlocked.Exchange(ref _destroy, context); return ValueTask.CompletedTask; }
                    break;
                case "Reset" when HoldReset:
                    context.DisposesAsynchronously = true; Interlocked.Exchange(ref _reset, context); return ValueTask.CompletedTask;
                case "ProcessKeyEvent":
                    Interlocked.Increment(ref KeyCount); Record("key-request", new[] { reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32() });
                    if (HoldKey) { context.DisposesAsynchronously = true; Interlocked.Exchange(ref _key, context); return ValueTask.CompletedTask; }
                    using (var reply = context.CreateReplyWriter("b")) { reply.WriteBool(true); context.Reply(reply.CreateMessage()); }
                    return ValueTask.CompletedTask;
            }
            using (var reply = context.CreateReplyWriter("")) context.Reply(reply.CreateMessage());
            return ValueTask.CompletedTask;
        }
        internal void ReleaseCreate()
        {
            HoldCreate = false;
            var pending = Interlocked.Exchange(ref _create, null);
            if (pending is null) return;
            using (var reply = pending.CreateReplyWriter("o")) { reply.WriteObjectPath(ContextPath); pending.Reply(reply.CreateMessage()); }
            pending.Dispose();
        }
        internal void FailCursor()
        {
            HoldCursor = false;
            var pending = Interlocked.Exchange(ref _cursor, null) ?? throw new InvalidOperationException("No held native cursor request.");
            pending.ReplyError("org.example.NativeIbus.CursorFailed", "Controlled queued cursor failure.");
            pending.Dispose();
        }
        internal void ReleaseCursor() { HoldCursor = false; ReplyEmpty(Interlocked.Exchange(ref _cursor, null)); }
        internal void ReleaseDestroy() { HoldDestroy = false; ReplyEmpty(Interlocked.Exchange(ref _destroy, null)); }
        private static void ReplyEmpty(MethodContext? pending)
        {
            if (pending is null) return;
            using (var reply = pending.CreateReplyWriter("")) pending.Reply(reply.CreateMessage());
            pending.Dispose();
        }
        internal void DropName() => connection.ReleaseNameAsync("org.freedesktop.portal.IBus").GetAwaiter().GetResult();
        internal void FailReset()
        {
            HoldReset = false;
            var pending = Interlocked.Exchange(ref _reset, null) ?? throw new InvalidOperationException("No pending native Reset.");
            pending.ReplyError("org.example.NativeIbus.ResetFailed", "Controlled native Reset failure.");
            pending.Dispose();
        }
        internal void ReleaseReset()
        {
            HoldReset = false; var pending = Interlocked.Exchange(ref _reset, null); if (pending is null) return;
            Record("reset-reply-release"); using (var reply = pending.CreateReplyWriter("")) pending.Reply(reply.CreateMessage()); pending.Dispose();
        }
        internal void ReleaseKey()
        {
            HoldKey = false; var pending = Interlocked.Exchange(ref _key, null); if (pending is null) return;
            using (var reply = pending.CreateReplyWriter("b")) { reply.WriteBool(true); pending.Reply(reply.CreateMessage()); } pending.Dispose();
        }
        private static VariantValue Text(string text) => VariantValue.Struct("IBusText", new Dict<string, VariantValue>(), text,
            VariantValue.Variant(VariantValue.Struct("IBusAttrList", new Dict<string, VariantValue>(), VariantValue.ArrayOfVariant(Array.Empty<VariantValue>()))));
        internal void Forward(uint state)
        {
            Record("wire-signal", new { member = "ForwardKeyEvent", keyval = 97, keycode = 38, state });
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(destination: _sender, path: ContextPath, @interface: "org.freedesktop.IBus.InputContext", member: "ForwardKeyEvent", signature: "uuu");
            writer.WriteUInt32(97); writer.WriteUInt32(38); writer.WriteUInt32(state);
            Require(connection.TrySendMessage(writer.CreateMessage()), "Private bus rejected ForwardKeyEvent.");
        }
        internal void Preedit(string text, uint cursor, bool visible) => Signal("UpdatePreeditText", "vub", text, cursor, visible);
        internal void Commit(string text) => Signal("CommitText", "v", text);
        internal void Hide() => Signal("HidePreeditText", "");
        internal void Show() => Signal("ShowPreeditText", "");
        private void Signal(string member, string signature, string? text = null, uint cursor = 0, bool visible = true)
        {
            Record("wire-signal", new { member, signature, text, cursor, visible });
            using var writer = connection.GetMessageWriter();
            writer.WriteSignalHeader(destination: _sender, path: ContextPath, @interface: "org.freedesktop.IBus.InputContext", member: member, signature: signature);
            if (text is not null) writer.WriteVariant(Text(text));
            if (member == "UpdatePreeditText") { writer.WriteUInt32(cursor); writer.WriteBool(visible); }
            Require(connection.TrySendMessage(writer.CreateMessage()), "The private bus rejected a test signal.");
        }
    }
}
