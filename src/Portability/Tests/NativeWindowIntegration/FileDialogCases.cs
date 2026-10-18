extern alias WpfFork;

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Platform;
using Tmds.DBus.Protocol;
using Wpf.Portable.NativeHost;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Interop;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using WpfFork::System.Windows.Threading;
using OpenFileDialog = WpfFork::Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = WpfFork::Microsoft.Win32.SaveFileDialog;
using Application = Avalonia.Application;

internal static class FileDialogCases
{
    internal static int Run(string[] args)
    {
        var checks = new ConcurrentQueue<object>();
        var trace = new ConcurrentQueue<object>();
        bool completed = false;
        ProtocolPortal? activePortal = null;
        string? wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        using var watchdog = new Timer(_ => { Write(); Environment.Exit(9); }, null, 30000, Timeout.Infinite);
        try
        {
            Require(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WPF_FILE_DIALOG_PRIVATE_BUS") == "1",
                "This protocol fixture requires its explicitly owned private bus and Xvfb runner.");
            string directory = Path.GetFullPath(args.FirstOrDefault() ?? throw new ArgumentException("Pass the artifact fixture directory."));
            Directory.CreateDirectory(directory);
            string first = Path.Combine(directory, "first space \u65e5\u672c.txt"), second = Path.Combine(directory, "second.txt");
            string noExtension = Path.Combine(directory, "native-final-name");
            File.WriteAllText(first, "first"); File.WriteAllText(second, "second"); File.WriteAllText(noExtension, "preserve");
            using var connection = new DBusConnection(new DBusConnectionOptions(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")!) { AutoConnect = false });
            connection.ConnectAsync().AsTask().GetAwaiter().GetResult();
            var portal = new ProtocolPortal(connection, trace);
            activePortal = portal;
            connection.AddMethodHandler(portal);
            connection.RequestNameAsync("org.freedesktop.portal.Desktop", RequestNameOptions.Default).GetAwaiter().GetResult();
            using var native = new X11Fixture("little");
            NativeWindowHost.InitializeForCurrentThread(AppBuilder.Configure<Application>());
            var dispatcher = Dispatcher.CurrentDispatcher;
            portal.InvokeOnDispatcher = action => dispatcher.Invoke(action);
            _ = dispatcher.BeginInvoke(new Action(() =>
            {
                Window? owner = null;
                try
                {
                    owner = NewOwner();
                    var open = new OpenFileDialog { Title = "Choose local file", FileName = second, Filter = "Text|*.txt|All files|*.*", InitialDirectory = directory };
                    int events = 0;
                    System.ComponentModel.CancelEventHandler observe = (_, _) =>
                    {
                        events++;
                        Require(open.FileName == first && open.FilterIndex == 2, "FileOk did not see tentative real fields.");
                    };
                    open.FileOk += observe;
                    Check("open-accept-real-fields-filter-owner-modal", () =>
                    {
                        portal.Enqueue(new(0, [UriFor(first)], 2, BeforeResponse: () =>
                        {
                            Require(ComponentDispatcher.IsThreadModal && owner.IsEnabled && !PeerEnabled(owner), "Native owner disable or real WPF modal state is incorrect.");
                        }));
                        Require(open.ShowDialog(owner) == true && open.FileName == first && events == 1 && open.FilterIndex == 2, "Open acceptance failed.");
                        Require(portal.LastParent == Parent(owner) && PeerEnabled(owner) && !ComponentDispatcher.IsThreadModal, "Real owner or restoration failed.");
                        Require(portal.LastPatterns.Contains("*") && !portal.LastPatterns.Contains("*.*"), "All-files filter hides extensionless native files.");
                        Require(portal.LastDirectory == directory && portal.LastName == Path.GetFileName(second) && portal.LastTitle == open.Title && portal.LastInitialFilter == "Text",
                            "Native initial directory/name/title/filter hints were lost.");
                    });
                    Check("open-cancel-reuses-state-and-connection", () =>
                    {
                        portal.Enqueue(new(1));
                        Require(open.ShowDialog(owner) == false && open.FileName == first && open.FilterIndex == 2 && events == 1, "Cancellation changed prior accepted state.");
                    });
                    open.FileOk -= observe;
                    Check("open-multiple-and-array-copy", () =>
                    {
                        open.Multiselect = true; portal.Enqueue(new(0, [UriFor(first), UriFor(second)]));
                        Require(open.ShowDialog(owner) == true && open.FileNames.SequenceEqual(new[] { first, second }), "Multiselect result failed.");
                        Require(portal.LastMultiple == true, "Multiselect was not requested from the native portal.");
                        string[] copy = open.FileNames; copy[0] = "changed";
                        Require(open.FileNames[0] == first, "FileNames exposes its mutable backing array.");
                        open.Multiselect = false;
                    });
                    Check("save-preserves-native-final-path-without-post-extension", () =>
                    {
                        var save = new SaveFileDialog { FileName = "sequence", DefaultExt = ".json", Filter = "N.I.N.A. sequence JSON|*.json" };
                        portal.Enqueue(new(0, [UriFor(noExtension)]));
                        Require(save.ShowDialog(owner) == true && save.FileName == noExtension && File.ReadAllText(noExtension) == "preserve", "Save rewrote the confirmed native path or file bytes.");
                        portal.Enqueue(new(1)); Require(save.ShowDialog(owner) == false && save.FileName == noExtension, "Save cancellation/reuse changed state.");
                    });
                    Check("fileok-veto-reopens-and-final-cancel-restores", () =>
                    {
                        string[] before = open.FileNames; int filter = open.FilterIndex, calls = portal.OpenCalls;
                        System.ComponentModel.CancelEventHandler veto = (_, e) => e.Cancel = true;
                        open.FileOk += veto;
                        try
                        {
                            portal.Enqueue(new(0, [UriFor(second)], 1)); portal.Enqueue(new(1));
                            Require(open.ShowDialog(owner) == false && open.FileNames.SequenceEqual(before) && open.FilterIndex == filter && portal.OpenCalls == calls + 2, "Veto did not reopen and roll back real state.");
                        }
                        finally { open.FileOk -= veto; }
                    });
                    Check("fileok-veto-then-accept", () =>
                    {
                        int count = 0;
                        System.ComponentModel.CancelEventHandler vetoOnce = (_, e) => e.Cancel = ++count == 1;
                        open.FileOk += vetoOnce;
                        try
                        {
                            portal.Enqueue(new(0, [UriFor(first)])); portal.Enqueue(new(0, [UriFor(second)]));
                            Require(open.ShowDialog(owner) == true && open.FileName == second && count == 2, "Reopened native acceptance failed.");
                        }
                        finally { open.FileOk -= vetoOnce; }
                    });
                    Check("fileok-exception-rolls-back-and-restores-owner", () =>
                    {
                        string before = open.FileName;
                        System.ComponentModel.CancelEventHandler throwing = (_, _) => throw new InvalidOperationException("fixture FileOk failure");
                        open.FileOk += throwing;
                        try { portal.Enqueue(new(0, [UriFor(first)])); Throws<InvalidOperationException>(() => open.ShowDialog(owner), "fixture FileOk failure"); }
                        finally { open.FileOk -= throwing; }
                        Require(open.FileName == before && PeerEnabled(owner) && !ComponentDispatcher.IsThreadModal, "Handler exception stranded state or modality.");
                        portal.Enqueue(new(1)); Require(open.ShowDialog(owner) == false, "Failed dialog cannot be reused.");
                    });
                    Check("same-instance-reentry-is-rejected", () =>
                    {
                        System.ComponentModel.CancelEventHandler reenter = (_, _) => Throws<InvalidOperationException>(() => open.ShowDialog(owner), "already showing");
                        open.FileOk += reenter;
                        try { portal.Enqueue(new(0, [UriFor(first)])); Require(open.ShowDialog(owner) == true, "Outer dialog did not finish after reentry rejection."); }
                        finally { open.FileOk -= reenter; }
                    });
                    Check("wrong-thread-is-rejected", () => Task.Run(() => Throws<InvalidOperationException>(() => open.ShowDialog(owner), null)).GetAwaiter().GetResult());
                    foreach (var bad in new[] { new Plan(2), new Plan(99), new Plan(0), new Plan(0, []), new Plan(0, ["https://example.invalid/file.txt"]), new Plan(0, ["file://remote-host/file.txt"]), new Plan(0, WrongUrisType: true) })
                        Check("protocol-failure-does-not-poison-shared-connection-" + bad.Response + "-" + (bad.Uris?.Length.ToString() ?? "missing") + "-" + bad.WrongUrisType + "-" + bad.Uris?.FirstOrDefault(), () =>
                        {
                            string before = open.FileName; portal.Enqueue(bad);
                            Throws<Exception>(() => open.ShowDialog(owner), null);
                            Require(open.FileName == before && PeerEnabled(owner), "Failed response changed fields or owner state.");
                            portal.Enqueue(new(1)); Require(open.ShowDialog(owner) == false, "Protocol failure disconnected the shared bus.");
                        });
                    Check("hidden-owner-cancel-restores-enabled-peer-before-show", () =>
                    {
                        var hiddenOwner = NewOwner();
                        try
                        {
                            portal.Enqueue(new(1, BeforeResponse: () => hiddenOwner.Hide()));
                            Require(new OpenFileDialog().ShowDialog(hiddenOwner) == false && !hiddenOwner.IsVisible,
                                "Native cancellation unexpectedly showed the hidden owner.");
                            hiddenOwner.Show();
                            Require(hiddenOwner.IsEnabled && PeerEnabled(hiddenOwner), "Hide/cancel/show stranded the same native peer disabled.");
                        }
                        finally { hiddenOwner.Close(); }
                    });
                    Check("hidden-owner-preserves-application-disabled-state-and-reenables", () =>
                    {
                        var hiddenOwner = NewOwner();
                        try
                        {
                            portal.Enqueue(new(1, BeforeResponse: () => { hiddenOwner.Hide(); hiddenOwner.IsEnabled = false; }));
                            Require(new OpenFileDialog().ShowDialog(hiddenOwner) == false && !hiddenOwner.IsVisible,
                                "Native cancellation showed an application-disabled hidden owner.");
                            hiddenOwner.Show();
                            Require(!hiddenOwner.IsEnabled && !PeerEnabled(hiddenOwner), "Hidden-owner cleanup enabled an application-disabled owner.");
                            hiddenOwner.IsEnabled = true;
                            Require(PeerEnabled(hiddenOwner), "The reused native peer did not follow application reenable.");
                        }
                        finally { hiddenOwner.Close(); }
                    });
                    Check("owner-hide-show-before-cancel-restores-enabled-peer", () =>
                    {
                        var hiddenOwner = NewOwner();
                        try
                        {
                            portal.Enqueue(new(1, BeforeResponse: () => { hiddenOwner.Hide(); hiddenOwner.Show(); }));
                            Require(new OpenFileDialog().ShowDialog(hiddenOwner) == false && hiddenOwner.IsVisible && hiddenOwner.IsEnabled && PeerEnabled(hiddenOwner),
                                "Hide/show during the picker stranded the reused native peer disabled.");
                        }
                        finally { hiddenOwner.Close(); }
                    });
                    Check("owner-current-disabled-state-survives-restoration", () =>
                    {
                        portal.Enqueue(new(1, BeforeResponse: () => owner.IsEnabled = false));
                        Require(open.ShowDialog(owner) == false && !owner.IsEnabled && !PeerEnabled(owner), "Restoration enabled an application-disabled owner.");
                        owner.IsEnabled = true;
                    });
                    Check("ownerless-uses-real-active-native-owner", () =>
                    {
                        owner.Activate(); Drain(); portal.Enqueue(new(1));
                        Require(open.ShowDialog() == false && portal.LastParent == Parent(owner), "Ownerless dialog did not use the real active owner.");
                    });
                    Check("owner-and-real-keyboard-focus-restore", () =>
                    {
                        owner.Activate(); Drain();
                        var target = (Border)owner.Content;
                        Require(Keyboard.Focus(target) == target, "The genuine owner could not acquire keyboard focus.");
                        portal.Enqueue(new(1));
                        Require(open.ShowDialog(owner) == false && PeerEnabled(owner) && Keyboard.FocusedElement == target,
                            "Native cancellation failed to restore the actual focused element.");
                    });
                    foreach (bool beforeReply in new[] { false, true })
                        Check("owner-close-closes-request-" + (beforeReply ? "before" : "after") + "-method-reply", () =>
                        {
                            var closingOwner = NewOwner(); var dialog = new OpenFileDialog { FileName = second };
                            int previousCloses = portal.CloseCalls;
                            Action close = () => closingOwner.Close();
                            portal.Enqueue(new(1, Hold: true, BeforeReply: beforeReply ? close : null, BeforeResponse: beforeReply ? null : close));
                            Require(dialog.ShowDialog(closingOwner) == false && portal.CloseCalls == previousCloses + 1 && dialog.FileName == second,
                                "Owner closure left a pending request, repeated Close or changed state.");
                            Require(portal.LastClosedPath == portal.LastRequestPath, "Cancellation closed a predicted rather than actual request path.");
                        });
                    Check("owner-cancellation-closes-alternate-returned-path", () =>
                    {
                        var closingOwner = NewOwner(); int previousCloses = portal.CloseCalls;
                        portal.Enqueue(new(1, Hold: true, BeforeReply: () => closingOwner.Close(), PathSuffix: "_actual"));
                        Require(new OpenFileDialog().ShowDialog(closingOwner) == false && portal.CloseCalls == previousCloses + 1 &&
                            portal.LastClosedPath == portal.LastRequestPath && portal.LastClosedPath!.EndsWith("_actual", StringComparison.Ordinal),
                            "Owner cancellation did not close the actual returned path exactly once.");
                    });
                    Check("request-close-failure-is-explicit", () =>
                    {
                        var closingOwner = NewOwner(); int previousCloses = portal.CloseCalls;
                        portal.Enqueue(new(1, Hold: true, BeforeResponse: () => closingOwner.Close(), CloseFails: true));
                        Throws<Exception>(() => new OpenFileDialog().ShowDialog(closingOwner), "fixture close failure");
                        Require(portal.CloseCalls == previousCloses + 1, "Failed cancellation retried Close or failed to send it.");
                    });
                    Check("unsupported-request-fails-before-portal-and-reuses", () =>
                    {
                        var save = new SaveFileDialog { CreatePrompt = true }; int calls = portal.OpenCalls;
                        Throws<PlatformNotSupportedException>(() => save.ShowDialog(owner), "CreatePrompt");
                        Require(portal.OpenCalls == calls && PeerEnabled(owner), "Unsupported option displayed a picker or stranded its owner.");
                        save.CreatePrompt = false; portal.Enqueue(new(1)); Require(save.ShowDialog(owner) == false, "Unsupported dialog instance cannot recover.");
                    });
                    Check("unavailable-portal-is-explicit-and-never-gtk", () =>
                    {
                        portal.Available = false;
                        try { Throws<PlatformNotSupportedException>(() => new OpenFileDialog().ShowDialog(owner), "portal is unavailable"); }
                        finally { portal.Available = true; }
                        Require(PeerEnabled(owner) && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") == wayland, "Unavailable portal changed the desktop environment or owner.");
                    });
                    Check("modal-entry-handler-exception-pairs-modal-state", () =>
                    {
                        EventHandler throwing = (_, _) => throw new InvalidOperationException("fixture modal entry failure");
                        ComponentDispatcher.EnterThreadModal += throwing;
                        try
                        {
                            Throws<InvalidOperationException>(() => open.ShowDialog(owner), "fixture modal entry failure");
                            Require(!ComponentDispatcher.IsThreadModal && PeerEnabled(owner), "A modal entry exception stranded the real modal count or native owner.");
                        }
                        finally
                        {
                            ComponentDispatcher.EnterThreadModal -= throwing;
                            if (ComponentDispatcher.IsThreadModal) ComponentDispatcher.PopModal();
                        }
                        portal.Enqueue(new(1)); Require(open.ShowDialog(owner) == false, "A modal entry failure prevented dialog reuse.");
                    });
                    Check("owner-close-during-modal-entry-cancels-before-request", () =>
                    {
                        var closingOwner = NewOwner(); int calls = portal.OpenCalls;
                        EventHandler close = (_, _) => closingOwner.Close();
                        ComponentDispatcher.EnterThreadModal += close;
                        try { Require(new OpenFileDialog().ShowDialog(closingOwner) == false && portal.OpenCalls == calls, "Owner closure during modal entry did not cancel before a native request."); }
                        finally { ComponentDispatcher.EnterThreadModal -= close; }
                        Require(!ComponentDispatcher.IsThreadModal, "Owner closure stranded modal state.");
                    });
                    Check("modal-exit-exception-restores-owner-and-reuse", () =>
                    {
                        EventHandler throwing = (_, _) => throw new InvalidOperationException("fixture modal exit failure");
                        ComponentDispatcher.LeaveThreadModal += throwing;
                        try { portal.Enqueue(new(1)); Throws<InvalidOperationException>(() => open.ShowDialog(owner), "fixture modal exit failure"); }
                        finally { ComponentDispatcher.LeaveThreadModal -= throwing; }
                        Require(!ComponentDispatcher.IsThreadModal && PeerEnabled(owner), "Modal exit failure stranded native owner state.");
                        portal.Enqueue(new(1)); Require(open.ShowDialog(owner) == false, "Modal exit failure prevented reuse.");
                    });
                    completed = true;
                }
                catch (Exception error) { checks.Enqueue(new { name = "unhandled", status = "fail", detail = error.ToString() }); }
                finally { owner?.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }

                Window NewOwner()
                {
                    var window = new Window { Title = "Native file chooser protocol " + Guid.NewGuid().ToString("N"), Width = 180, Height = 120,
                        ShowActivated = false, Content = new Border { Background = Brushes.Red, Focusable = true } };
                    window.Show(); return window;
                }
            }));
            Dispatcher.Run();
        }
        catch (Exception error) { checks.Enqueue(new { name = "initialization", status = "fail", detail = error.ToString() }); }
        Write();
        return completed && checks.All(check => check.GetType().GetProperty("status")!.GetValue(check)?.ToString() == "pass") ? 0 : 1;

        void Check(string name, Action action)
        {
            try { action(); checks.Enqueue(new { name, status = "pass", detail = "Observable native-owner/protocol result matched." }); }
            catch (Exception error) { checks.Enqueue(new { name, status = "fail", detail = error.ToString() }); }
            finally { activePortal?.DiscardUnconsumedPlans(); }
        }
        void Write() => Console.WriteLine(JsonSerializer.Serialize(new { scope = "actual WPF Open/Save APIs and native owner; private fake portal protocol, not native picker UI", completed,
            waylandUnchanged = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") == wayland, checks, trace }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Peer(Window owner) => PresentationSource.FromVisual(owner)!.GetType().GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(PresentationSource.FromVisual(owner))!;
    private static bool PeerEnabled(Window owner) => (bool)Peer(owner).GetType().GetProperty("IsEnabled")!.GetValue(Peer(owner))!;
    private static string Parent(Window owner)
    {
        object native = Peer(owner).GetType().GetField("_native", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Peer(owner))!;
        return "x11:" + ((IntPtr)native.GetType().GetField("_handle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(native)!).ToInt64().ToString("X");
    }
    private static string UriFor(string path) => new Uri(path).AbsoluteUri;
    private static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
    private static void Throws<T>(Action action, string? message) where T : Exception
    {
        try { action(); }
        catch (T error) when (message is null || error.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " with " + message);
    }
    private static void Drain()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed record Plan(uint Response, string[]? Uris = null, int? Filter = null, Action? BeforeReply = null,
        Action? BeforeResponse = null, bool Hold = false, bool CloseFails = false, bool WrongUrisType = false, string? PathSuffix = null);

    private sealed class ProtocolPortal(DBusConnection connection, ConcurrentQueue<object> trace) : IPathMethodHandler
    {
        private readonly ConcurrentQueue<Plan> _plans = new();
        private readonly ConcurrentDictionary<string, Plan> _requests = new();
        public string Path => "/org/freedesktop/portal/desktop";
        public bool HandlesChildPaths => true;
        public bool Available = true;
        public int OpenCalls, CloseCalls;
        public string? LastParent, LastRequestPath, LastClosedPath, LastDirectory, LastName, LastTitle, LastInitialFilter;
        public bool? LastMultiple;
        public string[] LastPatterns = [];
        public Action<Action>? InvokeOnDispatcher;
        public void Enqueue(Plan plan) => _plans.Enqueue(plan);
        public void DiscardUnconsumedPlans() { while (_plans.TryDequeue(out _)) { } }

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var message = context.Request;
            if (message.InterfaceAsString == "org.freedesktop.DBus.Properties" && message.MemberAsString == "Get")
            {
                var reader = message.GetBodyReader(); string target = reader.ReadString(), name = reader.ReadString();
                if (Available && target == "org.freedesktop.portal.FileChooser" && name == "version")
                { using var reply = context.CreateReplyWriter("v"); reply.WriteVariantUInt32(3); context.Reply(reply.CreateMessage()); }
                else context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", "Fixture portal unavailable.");
            }
            else if (message.InterfaceAsString == "org.freedesktop.portal.FileChooser" && message.MemberAsString is "OpenFile" or "SaveFile")
            {
                if (!_plans.TryDequeue(out var plan)) { context.ReplyError("org.example.Fixture", "No prepared protocol response."); return ValueTask.CompletedTask; }
                var reader = message.GetBodyReader(); string parent = reader.ReadString(), title = reader.ReadString(); var options = reader.ReadDictionaryOfStringToVariantValue();
                LastDirectory = options.TryGetValue("current_folder", out var folder) ? System.Text.Encoding.UTF8.GetString(folder.GetArray<byte>()).TrimEnd('\0') : null;
                LastName = options.TryGetValue("current_name", out var name) ? name.GetString() : null;
                LastTitle = title;
                LastInitialFilter = options.TryGetValue("current_filter", out var initialFilter) ? initialFilter.GetItem(0).GetString() : null;
                LastMultiple = options.TryGetValue("multiple", out var multiple) ? multiple.GetBool() : null;
                string sender = message.SenderAsString!;
                string path = "/org/freedesktop/portal/desktop/request/" + sender.TrimStart(':').Replace('.', '_') + "/" + options["handle_token"].GetString() + plan.PathSuffix;
                Interlocked.Increment(ref OpenCalls); LastParent = parent; LastRequestPath = path; _requests[path] = plan;
                var filters = options.TryGetValue("filters", out var value) ? value.GetArray<VariantValue>() : [];
                LastPatterns = filters.SelectMany(filter => filter.GetItem(1).GetArray<VariantValue>().Select(pattern => pattern.GetItem(1).GetString())).ToArray();
                trace.Enqueue(new { method = message.MemberAsString, parent, title, path });
                context.DisposesAsynchronously = true;
                _ = ReplyAndRespondAsync(context, plan, path, sender, filters);
            }
            else if (message.InterfaceAsString == "org.freedesktop.portal.Request" && message.MemberAsString == "Close")
            {
                Interlocked.Increment(ref CloseCalls); LastClosedPath = message.PathAsString;
                trace.Enqueue(new { method = "Close", path = LastClosedPath });
                if (_requests.TryGetValue(LastClosedPath!, out var plan) && plan.CloseFails)
                    context.ReplyError("org.example.Fixture.CloseFailed", "fixture close failure");
                else { using var reply = context.CreateReplyWriter(""); context.Reply(reply.CreateMessage()); }
            }
            else context.ReplyUnknownMethodError();
            return ValueTask.CompletedTask;
        }

        private async Task ReplyAndRespondAsync(MethodContext context, Plan plan, string path, string sender, VariantValue[] filters)
        {
            try
            {
                await Task.Delay(20).ConfigureAwait(false);
                if (plan.BeforeReply is not null) Invoke(plan.BeforeReply);
                using (var reply = context.CreateReplyWriter("o")) { reply.WriteObjectPath(path); context.Reply(reply.CreateMessage()); }
                context.Dispose();
                await Task.Delay(20).ConfigureAwait(false);
                if (plan.BeforeResponse is not null) Invoke(plan.BeforeResponse);
                if (plan.Hold) return;
                using var writer = connection.GetMessageWriter();
                writer.WriteSignalHeader(destination: sender, path: path, @interface: "org.freedesktop.portal.Request", member: "Response", signature: "ua{sv}");
                writer.WriteUInt32(plan.Response);
                var results = new Dictionary<string, VariantValue>();
                if (plan.Uris is not null) results["uris"] = VariantValue.Array(plan.Uris);
                if (plan.WrongUrisType) results["uris"] = VariantValue.String("invalid array");
                if (plan.Filter is int filter) results["current_filter"] = filters[filter - 1];
                writer.WriteDictionary(results); connection.TrySendMessage(writer.CreateMessage());
            }
            catch (Exception error)
            {
                trace.Enqueue(new { fixtureFailure = error.ToString() });
                if (!context.ReplySent) context.ReplyError("org.example.Fixture", error.Message);
                context.Dispose();
            }
        }

        private void Invoke(Action action) => (InvokeOnDispatcher ?? throw new InvalidOperationException("The protocol fixture has no dispatcher."))(action);
    }
}
