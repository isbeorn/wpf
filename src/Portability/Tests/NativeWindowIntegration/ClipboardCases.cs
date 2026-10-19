extern alias WpfFork;

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Data;
using WpfFork::System.Windows.Input;
using WpfFork::System.Windows.Media;
using WpfFork::System.Windows.Threading;
using Clipboard = WpfFork::System.Windows.Clipboard;
using DataObject = WpfFork::System.Windows.DataObject;

internal static class ClipboardCases
{
    private const string Text = "WPF \u00e6\u00f8\u00e5 \u65e5\u672c\u8a9e \U0001f52d\r\nsecond line";

    internal static int ManagedSmoke()
    {
        try
        {
            ManagedDataObject();
            var helper = typeof(DataObject).GetInterfaces().Select(type => type.Assembly).First(assembly => assembly.GetName().Name == "System.Private.Windows.Core");
            using var stream = File.OpenRead(helper.Location);
            using var pe = new PEReader(stream);
            Require(pe.PEHeaders.CorHeader!.Flags.HasFlag(CorFlags.ILOnly) && pe.PEHeaders.CorHeader.ManagedNativeHeaderDirectory.Size == 0, "The loaded helper is not IL-only.");
            Console.WriteLine(JsonSerializer.Serialize(new { status = "pass", helper = helper.FullName, path = helper.Location,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(helper.Location))), corFlags = pe.PEHeaders.CorHeader.Flags.ToString(),
                nativeHeaderSize = pe.PEHeaders.CorHeader.ManagedNativeHeaderDirectory.Size,
                readyToRunSetting = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"), interfaces = typeof(DataObject).GetInterfaces().Select(type => type.FullName) }));
            return 0;
        }
        catch (Exception exception) { Console.WriteLine(JsonSerializer.Serialize(new { status = "fail", exception = exception.ToString() })); return 1; }
    }

    private static void ManagedDataObject()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, Text, true);
        Require(data.GetText() == Text && (string?)data.GetData(DataFormats.StringFormat, true) == Text, "Managed text conversion failed.");
        Require(data.TryGetData<string>(DataFormats.UnicodeText, out var typed) && typed == Text, "Typed text retrieval failed.");
        var json = new DataObject();
        json.SetDataAsJson("application/wpf-json", new[] { 1, 2 });
        Require(json.GetData("application/wpf-json") is int[] values && values.SequenceEqual(new[] { 1, 2 }), "Object-valued retrieval exposed the internal JSON container.");
        var wrapped = new DataObject((IDataObject)data);
        Require(wrapped.GetText() == Text, "Wrapping a genuine IDataObject changed text.");
        Require((string?)new DataObject(Text).GetData(DataFormats.UnicodeText, true) == Text && (string?)new DataObject(typeof(string), Text).GetData(typeof(string)) == Text, "Object/type constructors lost stored text.");
        var exact = new DataObject(DataFormats.UnicodeText, Text, false);
#pragma warning disable CA1859 // This case exercises the public IDataObject interface.
        IDataObject contract = exact;
        Require(contract.GetFormats(false).SequenceEqual(new[] { DataFormats.UnicodeText }) && !contract.GetDataPresent(DataFormats.Text, false), "Exact format storage invented aliases.");
        Require((string?)contract.GetData(DataFormats.Text, true) == Text, "Requested managed auto-conversion failed.");
#pragma warning restore CA1859
        data.SetText("updated");
        Require(wrapped.GetText() == "updated", "Wrapped IDataObject stopped forwarding managed changes.");
        Expect<PlatformNotSupportedException>(() => ((System.Runtime.InteropServices.ComTypes.IDataObject)data).EnumFormatEtc(System.Runtime.InteropServices.ComTypes.DATADIR.DATADIR_GET));
    }

    internal static void Exercise(string fontPath, Action<string, Action> run, Action<Func<bool>> wait, Func<ControlTemplate> template)
    {
        run("clipboard-managed-dataobject-text-closure", ManagedDataObject);
        run("clipboard-external-owner-requestor-and-primary-isolation", () =>
        {
            using var primary = Start("serve", "PRIMARY", "primary text");
            Ready(primary, wait);
            var data = new DataObject(DataFormats.UnicodeText, Text);
            Clipboard.SetDataObject(data, false);
            Require(Clipboard.IsCurrent(data), "The genuine selection owner was not current.");
            data.SetText("later mutation");
            Require(Clipboard.GetText() == Text, "The native text snapshot changed after the input IDataObject was mutated.");
            Clipboard.Flush();
            Require(Read("CLIPBOARD", wait) == Text && Read("PRIMARY", wait) == "primary text", "External selection transfer or PRIMARY isolation failed.");
            using var external = Start("serve", "CLIPBOARD", "external \u03bb\U0001f680");
            Ready(external, wait);
            Require(!Clipboard.IsCurrent(data) && Clipboard.ContainsText() && Clipboard.GetText() == "external \u03bb\U0001f680", "External ownership replacement was not observed.");
            var snapshot = Clipboard.GetDataObject();
            Require(snapshot?.GetDataPresent(DataFormats.UnicodeText) == true && (string?)snapshot.GetData(DataFormats.UnicodeText) == "external \u03bb\U0001f680", "External IDataObject text failed.");
            Clipboard.Clear();
            Require(!Clipboard.IsCurrent(data) && !Clipboard.ContainsText() && Clipboard.GetText() == string.Empty && Read("CLIPBOARD", wait) == string.Empty && Read("PRIMARY", wait) == "primary text", "Clear did not release CLIPBOARD independently of PRIMARY.");
            Stop(external, wait);
            Stop(primary, wait);
        });
        run("clipboard-textbox-copy-cut-paste-events-and-binding", () =>
        {
            const string editorText = "WPF \u00e6\u00f8\u00e5 \u03bb\r\nsecond line";
            const string pastedText = "pasted \u00f8\u03bb";
            var model = new TextModel { Value = editorText };
            var box = new TextBox { OverridesDefaultStyle = true, Width = 260, Height = 70, FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans"), Template = template() };
            box.SetBinding(TextBox.TextProperty, new Binding(nameof(TextModel.Value)) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var window = new Window { Title = "WPF clipboard commands", Width = 280, Height = 100, Content = box };
            try
            {
                window.Show();
                wait(() => box.ActualWidth > 0);
                box.SelectAll();
                box.Copy();
                Require(Read("CLIPBOARD", wait) == editorText && box.Text == editorText, "Copy did not publish actual TextBox selection.");
                bool cancelCopy = true;
                int copying = 0;
                DataObjectCopyingEventHandler onCopy = (_, e) => { copying++; if (cancelCopy) e.CancelCommand(); };
                DataObject.AddCopyingHandler(box, onCopy);
                Clipboard.SetText("unchanged");
                box.Copy();
                Require(copying == 1 && box.Text == editorText && Clipboard.GetText() == "unchanged", "Cancelled copy changed text or clipboard.");
                box.Cut();
                Require(copying == 2 && box.Text == editorText && Clipboard.GetText() == "unchanged", "Cancelled cut changed text or clipboard.");
                cancelCopy = false;
                box.Cut();
                Require(copying == 3 && box.Text == string.Empty && model.Value == string.Empty && Read("CLIPBOARD", wait) == editorText, "Cut did not update binding and external clipboard.");
                DataObject.RemoveCopyingHandler(box, onCopy);
                using var external = Start("serve", "CLIPBOARD", pastedText);
                Ready(external, wait);
                bool cancelPaste = true;
                int pasting = 0;
                DataObjectPastingEventHandler onPaste = (_, e) => { pasting++; if (cancelPaste) e.CancelCommand(); };
                DataObject.AddPastingHandler(box, onPaste);
                box.Paste();
                Require(pasting == 1 && box.Text == string.Empty, "Cancelled paste changed text.");
                cancelPaste = false;
                box.Paste();
                wait(() => model.Value == pastedText);
                Require(pasting == 2 && box.Text == model.Value, "External paste did not update genuine text binding.");
                DataObject.RemovePastingHandler(box, onPaste);
                box.IsReadOnly = true;
                box.SelectAll();
                Clipboard.SetText("readonly source");
                Require(ApplicationCommands.Copy.CanExecute(null, box) && !ApplicationCommands.Cut.CanExecute(null, box) && !ApplicationCommands.Paste.CanExecute(null, box),
                    "Read-only command availability did not preserve Copy while disabling Cut and Paste.");
                ApplicationCommands.Cut.Execute(null, box);
                ApplicationCommands.Paste.Execute(null, box);
                Require(box.Text == pastedText && model.Value == pastedText && Clipboard.GetText() == "readonly source", "Read-only cut or paste changed text, binding or clipboard.");
                ApplicationCommands.Copy.Execute(null, box);
                Require(Read("CLIPBOARD", wait) == pastedText && box.Text == pastedText, "Read-only copy did not publish the selected text.");
                box.IsReadOnly = false;
                Clipboard.SetText("editable again");
                Require(ApplicationCommands.Paste.CanExecute(null, box), "Paste remained disabled after read-only was removed.");
                ApplicationCommands.Paste.Execute(null, box);
                wait(() => model.Value == "editable again");
                Require(box.Text == model.Value, "Paste remained disabled after read-only was removed.");
                Stop(external, wait);
            }
            finally { window.Close(); }
        });
        run("clipboard-validation-processing-and-selection-preservation", () =>
        {
            foreach (string format in new[] { DataFormats.UnicodeText, DataFormats.Text, DataFormats.StringFormat })
            {
                Clipboard.SetDataObject(new DataObject(format, "alias " + format, false), true);
                Require(Clipboard.GetText() == "alias " + format, "A supported text alias did not reach the native clipboard.");
            }
            Clipboard.SetText("preserved");
            Expect<PlatformNotSupportedException>(() => Clipboard.SetData("image/png", new byte[] { 1 }));
            var conflict = new DataObject(DataFormats.UnicodeText, "first", false);
            conflict.SetData(DataFormats.Text, "second", false);
            Expect<NotSupportedException>(() => Clipboard.SetDataObject(conflict, true));
            Expect<EncoderFallbackException>(() => Clipboard.SetText("\ud800"));
            Require(Clipboard.GetText() == "preserved", "Unsupported data replaced the selection.");
            Expect<ArgumentNullException>(() => Clipboard.SetText(null!));
            Expect<InvalidEnumArgumentException>(() => Clipboard.GetText((TextDataFormat)999));
            using (Dispatcher.CurrentDispatcher.DisableProcessing())
                Expect<InvalidOperationException>(() => Clipboard.GetText());
            DispatcherProcessingDisabled? disabledByDataObject = null;
            try
            {
                var callbackData = new CallbackDataObject(new DataObject(DataFormats.UnicodeText, "blocked"),
                    () => disabledByDataObject = Dispatcher.CurrentDispatcher.DisableProcessing());
                Expect<InvalidOperationException>(() => Clipboard.SetDataObject(callbackData));
            }
            finally { disabledByDataObject?.Dispose(); }
            var foreign = Task.Run(() => { try { Clipboard.GetText(); return false; } catch (InvalidOperationException) { return true; } catch (PlatformNotSupportedException) { return true; } });
            wait(() => foreign.IsCompleted);
            Require(foreign.GetAwaiter().GetResult(), "A foreign thread accessed the native clipboard.");
            Require(Read("CLIPBOARD", wait) == "preserved", "Validation changed selection content.");
        });
        ExerciseManager(fontPath, run, wait, template);
        run("clipboard-pending-native-read-unwinds-at-dispatcher-shutdown", () =>
        {
            using var owner = Start("stall", "CLIPBOARD");
            Ready(owner, wait);
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(dispatcher.InvokeShutdown));
            var elapsed = Stopwatch.StartNew();
            Expect<OperationCanceledException>(() => Clipboard.GetText());
            Require(dispatcher.HasShutdownStarted && elapsed.Elapsed < TimeSpan.FromSeconds(2), "Shutdown waited for the external selection timeout.");
            Expect<InvalidOperationException>(() => Clipboard.GetText());
        });
    }

    internal static void ExerciseManager(string fontPath, Action<string, Action> run, Action<Func<bool>> wait, Func<ControlTemplate> template)
    {
        run("clipboard-session-copy-and-flush-ignore-responsive-manager", () =>
        {
            using var manager = Start("manager-ack", "CLIPBOARD_MANAGER");
            Ready(manager, wait);
            var request = manager.StandardOutput.ReadLineAsync();
            var elapsed = Stopwatch.StartNew();
            Clipboard.SetText("session text");
            Require(elapsed.Elapsed < TimeSpan.FromMilliseconds(900), "SetText waited for clipboard persistence.");
            elapsed.Restart();
            Clipboard.Flush();
            Require(elapsed.Elapsed < TimeSpan.FromMilliseconds(900), "Flush waited for clipboard persistence.");
            Require(Read("CLIPBOARD", wait) == "session text" && !request.IsCompleted, "Session text was unavailable or the responsive manager received a request.");
            Stop(manager, wait);
            wait(() => request.IsCompleted);
            Require(request.GetAwaiter().GetResult() == null, "Session copy or Flush contacted the clipboard manager.");
        });
        run("clipboard-stalled-manager-cut-replacement-and-clear", () =>
        {
            using var manager = Start("stall", "CLIPBOARD_MANAGER");
            Ready(manager, wait);
            var request = manager.StandardOutput.ReadLineAsync();
            const string cutText = "manager cut \u03bb";
            var model = new TextModel { Value = cutText };
            var box = new TextBox { OverridesDefaultStyle = true, Width = 260, Height = 70, FontFamily = new FontFamily(new Uri(Path.GetFullPath(fontPath)), "DejaVu Sans"), Template = template() };
            box.SetBinding(TextBox.TextProperty, new Binding(nameof(TextModel.Value)) { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var window = new Window { Title = "WPF stalled clipboard manager", Width = 280, Height = 100, Content = box };
            try
            {
                window.Show();
                wait(() => box.ActualWidth > 0);
                box.SelectAll();
                var elapsed = Stopwatch.StartNew();
                box.Copy();
                Require(elapsed.Elapsed < TimeSpan.FromMilliseconds(900) && box.Text == cutText && Read("CLIPBOARD", wait) == cutText,
                    "Copy waited for the stalled manager or changed text.");
                elapsed.Restart();
                box.Cut();
                Require(elapsed.Elapsed < TimeSpan.FromMilliseconds(900), "Cut waited for the stalled clipboard manager.");
                Require(box.Text == string.Empty && model.Value == string.Empty && Read("CLIPBOARD", wait) == cutText,
                    "Session Cut did not preserve the genuine editor, binding and native text result.");
                elapsed.Restart();
                Clipboard.SetText("session replacement");
                Require(elapsed.Elapsed < TimeSpan.FromMilliseconds(900) && Read("CLIPBOARD", wait) == "session replacement", "Selection replacement waited for the stalled manager.");
                elapsed.Restart();
                Clipboard.Flush();
                Require(elapsed.Elapsed < TimeSpan.FromMilliseconds(900) && Read("CLIPBOARD", wait) == "session replacement", "Flush waited for the stalled manager or lost session text.");
                Clipboard.Clear();
                Require(Read("CLIPBOARD", wait) == string.Empty && !request.IsCompleted, "Clear failed or session operations contacted the stalled manager.");
                Stop(manager, wait);
                wait(() => request.IsCompleted);
                Require(request.GetAwaiter().GetResult() == null, "Copy, Cut, SetText, Flush or Clear contacted the clipboard manager.");
            }
            finally { window.Close(); }
        });
    }

    private static PeerProcess Start(string mode, string selection, string text = "")
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        start.ArgumentList.Add("--clipboard-peer");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(selection);
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(text)));
        return new PeerProcess(Process.Start(start) ?? throw new InvalidOperationException("Cannot start external clipboard process."));
    }
    private static void Ready(PeerProcess peer, Action<Func<bool>> wait)
    {
        var ready = peer.StandardOutput.ReadLineAsync();
        wait(() => ready.IsCompleted);
        Require(ready.GetAwaiter().GetResult() == "READY", "External owner did not become ready.");
    }
    private static string Read(string selection, Action<Func<bool>> wait)
    {
        using var peer = Start("read", selection);
        var output = peer.StandardOutput.ReadToEndAsync();
        wait(() => peer.HasExited && output.IsCompleted);
        Require(peer.ExitCode == 0, "External requestor failed: " + peer.StandardError.ReadToEnd());
        return Encoding.UTF8.GetString(Convert.FromBase64String(output.GetAwaiter().GetResult().Trim()));
    }
    private static void Stop(PeerProcess peer, Action<Func<bool>> wait)
    {
        peer.StandardInput.WriteLine("quit");
        peer.StandardInput.Flush();
        wait(() => peer.HasExited);
        Require(peer.ExitCode == 0, "External owner failed: " + peer.StandardError.ReadToEnd());
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private sealed class TextModel : INotifyPropertyChanged
    {
        private string _value = "";
        public string Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class CallbackDataObject(IDataObject data, Action enumerate) : IDataObject
    {
        public object? GetData(string format) => data.GetData(format);
        public object? GetData(Type format) => data.GetData(format);
        public object? GetData(string format, bool autoConvert) => data.GetData(format, autoConvert);
        public bool GetDataPresent(string format) => data.GetDataPresent(format);
        public bool GetDataPresent(Type format) => data.GetDataPresent(format);
        public bool GetDataPresent(string format, bool autoConvert) => data.GetDataPresent(format, autoConvert);
        public string[] GetFormats() => GetFormats(true);
        public string[] GetFormats(bool autoConvert) { enumerate(); return data.GetFormats(autoConvert); }
        public void SetData(object? value) => data.SetData(value);
        public void SetData(string format, object? value) => data.SetData(format, value);
        public void SetData(Type format, object? value) => data.SetData(format, value);
        public void SetData(string format, object? value, bool autoConvert) => data.SetData(format, value, autoConvert);
    }

    private sealed class PeerProcess(Process process) : IDisposable
    {
        internal StreamReader StandardOutput => process.StandardOutput;
        internal StreamReader StandardError => process.StandardError;
        internal StreamWriter StandardInput => process.StandardInput;
        internal bool HasExited => process.HasExited;
        internal int ExitCode => process.ExitCode;
        public void Dispose()
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(3000); }
            }
            process.Dispose();
        }
    }

    internal static int Peer(string[] args)
    {
        try
        {
            var display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) throw new InvalidOperationException("External XOpenDisplay failed.");
            try
            {
                var window = XCreateSimpleWindow(display, XDefaultRootWindow(display), 0, 0, 1, 1, 0, IntPtr.Zero, IntPtr.Zero);
                var selection = XInternAtom(display, args[1], false);
                var utf8 = XInternAtom(display, "UTF8_STRING", false);
                var targets = XInternAtom(display, "TARGETS", false);
                var saveTargets = XInternAtom(display, "SAVE_TARGETS", false);
                var property = XInternAtom(display, "WPF_CLIPBOARD_FIXTURE", false);
                if (args[0] == "read")
                {
                    if (XGetSelectionOwner(display, selection) == IntPtr.Zero) { Console.WriteLine(""); return 0; }
                    XConvertSelection(display, selection, utf8, property, window, IntPtr.Zero);
                    XFlush(display);
                    var timeout = Stopwatch.StartNew();
                    while (timeout.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        if (XPending(display) == 0) { Thread.Sleep(2); continue; }
                        XNextEvent(display, out var ev);
                        if (ev.Type != 31) continue;
                        if (ev.Notify.Property == IntPtr.Zero) throw new InvalidOperationException("Owner rejected UTF8_STRING.");
                        XGetWindowProperty(display, window, property, IntPtr.Zero, (IntPtr)(1024 * 1024), true, IntPtr.Zero,
                            out var actualType, out var format, out var count, out var remaining, out var data);
                        try
                        {
                            Require(actualType == utf8 && format == 8 && remaining == IntPtr.Zero, "External request returned invalid UTF8 property.");
                            byte[] bytes = new byte[checked((int)count)];
                            if (bytes.Length > 0) Marshal.Copy(data, bytes, 0, bytes.Length);
                            Console.WriteLine(Convert.ToBase64String(bytes));
                            return 0;
                        }
                        finally { if (data != IntPtr.Zero) XFree(data); }
                    }
                    throw new TimeoutException("External selection request timed out.");
                }
                byte[] text = Convert.FromBase64String(args[2]);
                XSetSelectionOwner(display, selection, window, IntPtr.Zero);
                XSync(display, false);
                Require(XGetSelectionOwner(display, selection) == window, "External owner did not acquire selection.");
                Console.WriteLine("READY");
                Console.Out.Flush();
                var quit = Task.Run(() => Console.ReadLine());
                var deadline = Stopwatch.StartNew();
                while ((!quit.IsCompleted || XPending(display) != 0) && deadline.Elapsed < TimeSpan.FromSeconds(20))
                {
                    if (XPending(display) == 0) { Thread.Sleep(2); continue; }
                    XNextEvent(display, out var ev);
                    if (ev.Type != 30) continue;
                    var request = ev.Request;
                    if (args[1] == "CLIPBOARD_MANAGER")
                    {
                        Console.WriteLine(request.Target == saveTargets ? "SAVE_TARGETS" : "SELECTION_REQUEST:" + request.Target);
                        Console.Out.Flush();
                    }
                    if (args[0] == "stall") continue;
                    var resultProperty = request.Property == IntPtr.Zero ? request.Target : request.Property;
                    if (request.Target == utf8)
                        XChangeProperty(display, request.Requestor, resultProperty, utf8, 8, 0, text, text.Length);
                    else if (request.Target == targets)
                    {
                        IntPtr[] values = { targets, utf8 };
                        XChangePropertyAtoms(display, request.Requestor, resultProperty, (IntPtr)4, 32, 0, values, values.Length);
                    }
                    else if (request.Target != saveTargets || args[0] != "manager-ack") resultProperty = IntPtr.Zero;
                    var notify = new XEvent { Notify = new SelectionEvent { Type = 31, Display = display, Requestor = request.Requestor,
                        Selection = request.Selection, Target = request.Target, Property = resultProperty, Time = request.Time } };
                    XSendEvent(display, request.Requestor, false, IntPtr.Zero, ref notify);
                    XFlush(display);
                }
                return 0;
            }
            finally { XCloseDisplay(display); }
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent { [FieldOffset(0)] public int Type; [FieldOffset(0)] public SelectionRequest Request; [FieldOffset(0)] public SelectionEvent Notify; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SelectionRequest { public int Type; public ulong Serial; public int SendEvent; public IntPtr Display, Owner, Requestor, Selection, Target, Property, Time; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SelectionEvent { public int Type; public ulong Serial; public int SendEvent; public IntPtr Display, Requestor, Selection, Target, Property, Time; }
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint borderWidth, IntPtr border, IntPtr background);
    [DllImport("libX11.so.6")] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
    [DllImport("libX11.so.6")] private static extern int XSetSelectionOwner(IntPtr display, IntPtr selection, IntPtr owner, IntPtr time);
    [DllImport("libX11.so.6")] private static extern int XConvertSelection(IntPtr display, IntPtr selection, IntPtr target, IntPtr property, IntPtr requestor, IntPtr time);
    [DllImport("libX11.so.6")] private static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(IntPtr display, out XEvent ev);
    [DllImport("libX11.so.6")] private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr mask, ref XEvent ev);
    [DllImport("libX11.so.6")] private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, byte[] data, int count);
    [DllImport("libX11.so.6", EntryPoint = "XChangeProperty")] private static extern int XChangePropertyAtoms(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr[] data, int count);
    [DllImport("libX11.so.6")] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr offset, IntPtr length, bool delete, IntPtr requestedType, out IntPtr actualType, out int format, out IntPtr count, out IntPtr remaining, out IntPtr data);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, bool discard);
    [DllImport("libX11.so.6")] private static extern int XFree(IntPtr data);
}
