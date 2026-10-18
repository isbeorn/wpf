using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.FreeDesktop;
using Avalonia.FreeDesktop.DBusIme;
using Avalonia.FreeDesktop.DBusIme.IBus;
using Avalonia.FreeDesktop.DBusIme.IBus.DBus;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using Avalonia.Media.TextFormatting.Unicode;
using Tmds.DBus.Protocol;

namespace Avalonia.X11;

// Adapted from Avalonia 12.1.4 IBusX11TextInputMethod (2d79ba55), under its MIT license.
// The project verifies the original source hash before compiling this maintained derivative.
internal sealed class PortableIbusTextInputMethod : DBusTextInputMethodBase, IPortableIbusTransport, IDisposable
{
    private Service? _service;
    private InputContext? _context;
    private string _text = "";
    private int _caret;
    private bool _visible;
    private int _insideReset;
    private bool _handoverPending;
    private bool _disposed;
    private long _generation;
    private TextInputMethodClient? _requestedClient;
    private readonly HashSet<TaskCompletionSource<bool>> _pending = new();

    public PortableIbusTextInputMethod(DBusConnection connection) : base(connection, "org.freedesktop.portal.IBus") { }
    public event Action<PortableIbusPreedit>? PreeditChanged;
    private bool IsCurrent(InputContext context) => !_disposed && ReferenceEquals(_context, context);
    private bool CanReceive(InputContext context) => IsCurrent(context) && !_handoverPending && _insideReset == 0;
    private bool CanDeliver(InputContext context) => CanReceive(context) && Client?.SupportsPreedit == true;

    // Native IBus state and callbacks remain on the existing UI synchronization context.
    protected override async Task<bool> Connect(string name)
    {
        if (_disposed) return false;
        Service? service = null;
        var subscriptions = new List<IDisposable?>();
        bool published = false;
        try
        {
            var portal = new Portal(Connection, name, "/org/freedesktop/IBus");
            var path = await portal.CreateInputContextAsync(GetAppName()).ConfigureAwait(true);
            service = new Service(Connection, name, path);
            var context = new InputContext(Connection, name, path);
            if (_disposed) return false;
            subscriptions.Add(await context.WatchCommitTextAsync(value => OnCommit(context, value)).ConfigureAwait(true));
            if (_disposed) return false;
            subscriptions.Add(await context.WatchForwardKeyEventAsync(value => OnForwardKey(context, value)).ConfigureAwait(true));
            if (_disposed) return false;
            subscriptions.Add(await context.WatchUpdatePreeditTextAsync(value => OnUpdate(context, value)).ConfigureAwait(true));
            if (_disposed) return false;
            subscriptions.Add(await context.WatchShowPreeditTextAsync(() => SetVisibility(context, true)).ConfigureAwait(true));
            if (_disposed) return false;
            subscriptions.Add(await context.WatchHidePreeditTextAsync(() => SetVisibility(context, false)).ConfigureAwait(true));
            if (_disposed) return false;
            _service = service; _context = context;
            foreach (var subscription in subscriptions) AddDisposable(subscription);
            published = true;
            Enqueue(() => IsCurrent(context) ? context.SetCapabilitiesAsync((uint)IBusCapability.CapFocus) : Task.CompletedTask);
            return true;
        }
        finally
        {
            if (!published)
            {
                foreach (var subscription in subscriptions) subscription?.Dispose();
                // A context returned after Dispose is still ours, even though the base
                // never reached IsConnected and therefore cannot destroy it for us.
                if (service is not null) await service.DestroyAsync().ConfigureAwait(true);
            }
        }
    }
    private void OnUpdate(InputContext context, (VariantValue Text, uint CursorPos, bool Visible) value)
    {
        if (!CanDeliver(context)) return;
        string text = value.Text is { Type: VariantValueType.Struct, Count: >= 3 } item &&
            item.GetItem(2) is { Type: VariantValueType.String } textItem ? textItem.GetString() : "";
        int caret = text.Length == 0 ? 0 : Utf16Utils.CharacterOffsetToStringOffset(text, (int)Math.Min(value.CursorPos, int.MaxValue), false);
        _text = text; _caret = caret; _visible = value.Visible;
        Publish(context, PortableIbusPreeditKind.Update);
    }

    private void SetVisibility(InputContext context, bool visible)
    {
        if (!CanDeliver(context)) return;
        _visible = visible;
        Publish(context, visible ? PortableIbusPreeditKind.Show : PortableIbusPreeditKind.Hide);
    }

    private void Publish(InputContext context, PortableIbusPreeditKind kind)
    {
        var update = new PortableIbusPreedit(kind, _text, _caret, _visible);
        var client = Client;
        if (kind == PortableIbusPreeditKind.Update) client?.SetPreeditText(_text, _caret);
        else if (kind == PortableIbusPreeditKind.Hide) client?.SetPreeditText("", 0);
        if (CanDeliver(context) && ReferenceEquals(client, Client)) PreeditChanged?.Invoke(update);
    }

    private void OnCommit(InputContext context, VariantValue value)
    {
        if (!CanReceive(context)) return;
        if (value.Count >= 3 && value.GetItem(2) is { Type: VariantValueType.String } item) FireCommit(item.GetString());
    }

    public Task ChangeClientAsync(TextInputMethodClient? client)
    {
        if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(PortableIbusTextInputMethod)));
        long generation = ++_generation;
        _requestedClient = client;
        _handoverPending = true;
        // Keep the inherited explicit interface implementation. Reimplementing it here and
        // casting back to the interface would recursively dispatch to the new implementation.
        ((ITextInputMethodImpl)this).SetClient(null);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Add(completion);
        Enqueue(async () =>
        {
            try
            {
                if (_disposed || generation != _generation) { completion.TrySetCanceled(); return; }
                if (!IsConnected || _context is null) throw new IOException("The native IBus context is disconnected.");
                await ResetContextCore().ConfigureAwait(true);
                if (_disposed || generation != _generation) { completion.TrySetCanceled(); return; }
                ((ITextInputMethodImpl)this).SetClient(client);
                _handoverPending = false;
                completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                FailPending(error);
                if (!_disposed) throw;
            }
            finally { _pending.Remove(completion); }
        });
        return completion.Task;
    }

    public Task ResetAsync() => ChangeClientAsync(_handoverPending ? _requestedClient : Client);

    private void FailPending(Exception error)
    {
        _context = null;
        ++_generation;
        _requestedClient = null;
        _handoverPending = false;
        ((ITextInputMethodImpl)this).SetClient(null);
        foreach (var completion in _pending) completion.TrySetException(error);
        _pending.Clear();
    }

    protected override async Task ResetContextCore()
    {
        var context = _context;
        if (context is null) return;
        _text = ""; _caret = 0; _visible = false;
        ++_insideReset;
        try { await context.ResetAsync().ConfigureAwait(true); }
        finally { --_insideReset; }
    }

    protected override Task DisconnectAsync()
    {
        var service = _service;
        _service = null; _context = null;
        // A prior queued operation may fail before handover runs. Release callers before
        // awaiting remote teardown, which need not reply after an engine failure.
        FailPending(new IOException("The native IBus context is disconnecting."));
        return service?.DestroyAsync() ?? Task.CompletedTask;
    }
    protected override void OnDisconnected()
    {
        _service = null; _context = null;
        FailPending(new IOException("The native IBus context disconnected during handover."));
        base.OnDisconnected();
    }
    void IDisposable.Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        FailPending(new ObjectDisposedException(nameof(PortableIbusTextInputMethod)));
        PreeditChanged = null;
        base.Dispose();
    }
    protected override Task SetCursorRectCore(PixelRect rect) => _context?.SetCursorLocationAsync(rect.X, rect.Y, rect.Width, rect.Height) ?? Task.CompletedTask;
    protected override Task SetActiveCore(bool active) => (active ? _context?.FocusInAsync() : _context?.FocusOutAsync()) ?? Task.CompletedTask;
    protected override Task SetCapabilitiesCore(bool preedit, bool surrounding)
    {
        var caps = IBusCapability.CapFocus;
        if (preedit) caps |= IBusCapability.CapPreeditText;
        return _context?.SetCapabilitiesAsync((uint)caps) ?? Task.CompletedTask;
    }
    public override void SetOptions(TextInputOptions options) { }
    protected override Task<bool> HandleKeyCore(RawKeyEventArgs args, int keyVal, int keyCode)
    {
        IBusModifierMask state = default;
        if (args.Modifiers.HasAllFlags(RawInputModifiers.Control)) state |= IBusModifierMask.ControlMask;
        if (args.Modifiers.HasAllFlags(RawInputModifiers.Alt)) state |= IBusModifierMask.Mod1Mask;
        if (args.Modifiers.HasAllFlags(RawInputModifiers.Shift)) state |= IBusModifierMask.ShiftMask;
        if (args.Modifiers.HasAllFlags(RawInputModifiers.Meta)) state |= IBusModifierMask.Mod4Mask;
        if (args.Type == RawKeyEventType.KeyUp) state |= IBusModifierMask.ReleaseMask;
        return _context?.ProcessKeyEventAsync((uint)keyVal, (uint)keyCode, (uint)state) ?? Task.FromResult(false);
    }
    private void OnForwardKey(InputContext context, (uint keyval, uint keycode, uint state) value)
    {
        if (!CanReceive(context)) return;
        var state = (IBusModifierMask)value.state;
        KeyModifiers modifiers = default;
        if (state.HasAllFlags(IBusModifierMask.ControlMask)) modifiers |= KeyModifiers.Control;
        if (state.HasAllFlags(IBusModifierMask.Mod1Mask)) modifiers |= KeyModifiers.Alt;
        if (state.HasAllFlags(IBusModifierMask.ShiftMask)) modifiers |= KeyModifiers.Shift;
        if (state.HasAllFlags(IBusModifierMask.Mod4Mask)) modifiers |= KeyModifiers.Meta;
        FireForward(new X11InputMethodForwardedKey { KeyVal = (int)value.keyval,
            Type = state.HasAllFlags(IBusModifierMask.ReleaseMask) ? RawKeyEventType.KeyUp : RawKeyEventType.KeyDown, Modifiers = modifiers });
    }
}
