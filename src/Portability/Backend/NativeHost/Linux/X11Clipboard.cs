extern alias WpfFork;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WpfFork::MS.Internal.Portability.Clipboard;

namespace Wpf.Portable.NativeHost.Linux;

// Implements the X11 selection protocol from ICCCM 2. No toolkit selection code is used.
internal sealed class X11Clipboard : IPortableClipboard, IDisposable
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private const int ChunkBytes = 32 * 1024;
    private const int MaximumTransfers = 32;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly TimeSpan ProgressTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(15);
    private readonly X11Connection _connection;
    private readonly nuint _window, _clipboard, _utf8, _text, _string, _targets, _timestamp, _incr, _property, _clock;
    private readonly Queue<Claim> _claims = new();
    private readonly Dictionary<nuint, Incoming> _incoming = new();
    private readonly Dictionary<(nuint Window, nuint Property), Outgoing> _outgoing = new();
    private readonly Timer _timer;
    private Snapshot? _current;
    private int _tickPosted;
    private bool _disposed;

    internal X11Clipboard(X11Connection connection)
    {
        connection.VerifyOwner();
        _connection = connection;
        _clipboard = connection.GetAtom("CLIPBOARD");
        _utf8 = connection.GetAtom("UTF8_STRING");
        _text = connection.GetAtom("TEXT");
        _string = connection.GetAtom("STRING");
        _targets = connection.GetAtom("TARGETS");
        _timestamp = connection.GetAtom("TIMESTAMP");
        _incr = connection.GetAtom("INCR");
        _property = connection.GetAtom("_WPF_CLIPBOARD_DATA");
        _clock = connection.GetAtom("_WPF_CLIPBOARD_CLOCK");
        _window = CreateWindow();
        connection.RegisterWindow(_window, HandleOwnerEvent);
        _timer = new Timer(_ => PostTick(), null, 100, 100);
    }

    public Task SetTextAsync(string text, object original)
    {
        Verify();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(original);
        // Validate and snapshot before touching the current selection, including unpaired surrogates.
        int length = Utf8.GetByteCount(text);
        if (length > MaximumBytes) throw new NotSupportedException("Clipboard text exceeds the 16 MiB transfer limit.");
        if (_claims.Count >= MaximumTransfers) throw new InvalidOperationException("Too many pending clipboard writes.");
        var claim = new Claim(new Snapshot(text, Utf8.GetBytes(text), original));
        _claims.Enqueue(claim);
        RequestTime(_window);
        return claim.Completion.Task;
    }

    public Task<string?> GetTextAsync()
    {
        Verify();
        nuint owner = X11ClipboardNative.XGetSelectionOwner(_connection.Display, _clipboard);
        if (owner == 0) return Task.FromResult<string?>(null);
        if (owner == _window && _current != null) return Task.FromResult<string?>(_current.Text);
        if (_incoming.Count >= MaximumTransfers) throw new InvalidOperationException("Too many pending clipboard reads.");
        var transfer = new Incoming(CreateWindow(), owner);
        _incoming.Add(transfer.Window, transfer);
        _connection.RegisterWindow(transfer.Window, ev => HandleIncoming(transfer, ev));
        RequestTime(transfer.Window);
        return transfer.Completion.Task;
    }

    public Task ClearAsync()
    {
        Verify();
        foreach (var claim in _claims) claim.Completion.TrySetCanceled();
        _current = null;
        // Clear has the same meaning whether another process or this connection owns CLIPBOARD.
        X11ClipboardNative.XSetSelectionOwner(_connection.Display, _clipboard, 0, 0);
        _connection.Flush();
        return Task.CompletedTask;
    }

    public Task FlushAsync()
    {
        Verify();
        // Linux copies live for this session. No clipboard manager is contacted or waited on.
        _connection.Flush();
        return Task.CompletedTask;
    }

    public Task<bool> IsCurrentAsync(object original)
    {
        Verify();
        if (X11ClipboardNative.XGetSelectionOwner(_connection.Display, _clipboard) != _window) _current = null;
        return Task.FromResult(_current != null && ReferenceEquals(_current.Original, original));
    }

    private nuint CreateWindow()
    {
        nuint window = X11ClipboardNative.XCreateSimpleWindow(_connection.Display, _connection.RootWindow, 0, 0, 1, 1, 0, 0, 0);
        if (window == 0) throw new InvalidOperationException("Unable to create the clipboard selection window.");
        X11ClipboardNative.XSelectInput(_connection.Display, window, X11ClipboardNative.PropertyChangeMask);
        return window;
    }

    private void RequestTime(nuint window)
    {
        X11ClipboardNative.XChangeProperty(_connection.Display, window, _clock, _string, 8, 0, Array.Empty<byte>(), 0);
        _connection.Flush();
    }

    private void HandleOwnerEvent(XEvent ev)
    {
        if (_disposed) return;
        if (ev.Type == 28 && ev.Property.Atom == _clock && ev.Property.State == 0 && _claims.TryDequeue(out var claim))
        {
            if (claim.Completion.Task.IsCompleted) return;
            X11ClipboardNative.XSetSelectionOwner(_connection.Display, _clipboard, _window, ev.Property.Time);
            if (X11ClipboardNative.XGetSelectionOwner(_connection.Display, _clipboard) != _window)
            {
                claim.Completion.TrySetException(new InvalidOperationException("Unable to acquire CLIPBOARD ownership."));
                return;
            }
            claim.Snapshot.Time = ev.Property.Time;
            _current = claim.Snapshot;
            claim.Completion.TrySetResult();
        }
        else if (ev.Type == 29 && ev.SelectionClear.Selection == _clipboard)
        {
            // A delayed clear from an older claim must not erase a newer snapshot.
            if (X11ClipboardNative.XGetSelectionOwner(_connection.Display, _clipboard) != _window) _current = null;
        }
        else if (ev.Type == 30) HandleRequest(ev.SelectionRequest);
    }

    private void HandleRequest(XSelectionRequestEvent request)
    {
        nuint property = request.Property == 0 ? request.Target : request.Property;
        var snapshot = _current;
        if (request.Selection != _clipboard || snapshot == null ||
            X11ClipboardNative.XGetSelectionOwner(_connection.Display, _clipboard) != _window ||
            (request.Time != 0 && IsEarlier(request.Time, snapshot.Time)))
        {
            Notify(request, 0);
            return;
        }
        if (request.Target == _targets)
        {
            nuint[] targets = snapshot.Latin1 ? [_targets, _timestamp, _utf8, _text, _string] : [_targets, _timestamp, _utf8, _text];
            Foreign(() => X11ClipboardNative.XChangePropertyLongs(_connection.Display, request.Requestor, property, 4, 32, 0, targets, targets.Length));
        }
        else if (request.Target == _timestamp)
        {
            Foreign(() => X11ClipboardNative.XChangePropertyLongs(_connection.Display, request.Requestor, property, 19, 32, 0, [snapshot.Time], 1));
        }
        else if (request.Target == _utf8 || request.Target == _text || (request.Target == _string && snapshot.Latin1))
        {
            byte[] bytes = request.Target == _string ? Encoding.Latin1.GetBytes(snapshot.Text) : snapshot.Bytes;
            nuint type = request.Target == _string ? _string : _utf8;
            if (bytes.Length <= ChunkBytes)
                Foreign(() => X11ClipboardNative.XChangeProperty(_connection.Display, request.Requestor, property, type, 8, 0, bytes, bytes.Length));
            else if (!StartOutgoing(request.Requestor, property, type, bytes)) property = 0;
        }
        else property = 0;
        Notify(request, property);
    }

    private void Notify(XSelectionRequestEvent request, nuint property)
    {
        var notify = new XEvent { Selection = new XSelectionEvent { Type = 31, Display = _connection.Display,
            Requestor = request.Requestor, Selection = request.Selection, Target = request.Target, Property = property, Time = request.Time } };
        Foreign(() => X11ClipboardNative.XSendEvent(_connection.Display, request.Requestor, 0, 0, ref notify));
        _connection.Flush();
    }

    private bool StartOutgoing(nuint window, nuint property, nuint type, byte[] bytes)
    {
        if (_outgoing.Count >= MaximumTransfers || _outgoing.ContainsKey((window, property))) return false;
        bool first = !_outgoing.Keys.Any(key => key.Window == window);
        nint originalMask;
        if (first)
        {
            XWindowAttributes attributes = default;
            int exists = 0;
            if (!Foreign(() => exists = X11Native.XGetWindowAttributes(_connection.Display, window, out attributes)) || exists == 0) return false;
            originalMask = attributes.YourEventMask;
            try { _connection.RegisterWindow(window, HandleOutgoing); }
            catch (ArgumentException) { return false; }
            if (!Foreign(() => X11ClipboardNative.XSelectInput(_connection.Display, window,
                attributes.YourEventMask | X11ClipboardNative.PropertyChangeMask | X11ClipboardNative.StructureNotifyMask)))
            {
                _connection.UnregisterWindow(window);
                return false;
            }
        }
        else originalMask = _outgoing.Values.First(t => t.Window == window).OriginalMask;
        var transfer = new Outgoing(window, property, type, bytes, originalMask);
        _outgoing.Add((window, property), transfer);
        if (Foreign(() => X11ClipboardNative.XChangePropertyLongs(_connection.Display, window, property, _incr, 32, 0, [(nuint)bytes.Length], 1))) return true;
        FinishOutgoing(transfer);
        return false;
    }

    private void HandleOutgoing(XEvent ev)
    {
        if (_disposed) return;
        if (ev.Type == 17)
        {
            foreach (var transfer in _outgoing.Values.Where(t => t.Window == ev.Any.Window).ToArray()) FinishOutgoing(transfer);
            return;
        }
        if (ev.Type != 28 || ev.Property.State != 1 || !_outgoing.TryGetValue((ev.Property.Window, ev.Property.Atom), out var current)) return;
        int count = Math.Min(ChunkBytes, current.Bytes.Length - current.Offset);
        byte[] bytes = count == 0 ? Array.Empty<byte>() : current.Bytes.AsSpan(current.Offset, count).ToArray();
        if (!Foreign(() => X11ClipboardNative.XChangeProperty(_connection.Display, current.Window, current.Property, current.Type, 8, 0, bytes, count)))
        {
            FinishOutgoing(current);
            return;
        }
        current.Offset += count;
        current.Progress = Stopwatch.GetTimestamp();
        _connection.Flush();
        if (count == 0) FinishOutgoing(current);
    }

    private void FinishOutgoing(Outgoing transfer)
    {
        _outgoing.Remove((transfer.Window, transfer.Property));
        if (_outgoing.Keys.Any(key => key.Window == transfer.Window)) return;
        _connection.UnregisterWindow(transfer.Window);
        Foreign(() => X11ClipboardNative.XSelectInput(_connection.Display, transfer.Window, transfer.OriginalMask));
    }

    private void HandleIncoming(Incoming transfer, XEvent ev)
    {
        if (_disposed || transfer.Completion.Task.IsCompleted) return;
        try
        {
            if (ev.Type == 28 && ev.Property.Atom == _clock && ev.Property.State == 0 && transfer.Time == 0)
            {
                transfer.Time = ev.Property.Time;
                Convert(transfer, _targets);
            }
            else if (ev.Type == 31 && ev.Selection.Selection == _clipboard && ev.Selection.Requestor == transfer.Window &&
                ev.Selection.Target == transfer.Target && ev.Selection.Time == transfer.Time)
            {
                if (ev.Selection.Property == 0)
                {
                    // Old owners sometimes implement text conversions but not TARGETS.
                    if (transfer.Target == _targets) transfer.FallbackTargets.AddRange([_utf8, _text, _string]);
                    ConvertNext(transfer);
                    return;
                }
                if (ev.Selection.Property != _property) throw new InvalidOperationException("Selection owner returned an unexpected clipboard property.");
                var property = ReadProperty(transfer.Window);
                if (property.Type == _incr)
                {
                    if (property.Format != 32 || property.Longs.Length != 1 || property.Longs[0] > MaximumBytes)
                        throw new NotSupportedException("Invalid or oversized incremental clipboard transfer.");
                    transfer.Incremental = true;
                    transfer.Progress = Stopwatch.GetTimestamp();
                }
                else Consume(transfer, property, true);
                _connection.Flush();
            }
            else if (ev.Type == 28 && ev.Property.Atom == _property && ev.Property.State == 0 && transfer.Incremental)
            {
                var property = ReadProperty(transfer.Window);
                Consume(transfer, property, property.Count == 0);
                _connection.Flush();
            }
        }
        catch (Exception exception) { CompleteIncoming(transfer, null, exception); }
    }

    private void Convert(Incoming transfer, nuint target)
    {
        if (X11ClipboardNative.XGetSelectionOwner(_connection.Display, _clipboard) != transfer.Owner)
            throw new OperationCanceledException("The clipboard owner changed during conversion.");
        transfer.Target = target;
        transfer.Incremental = false;
        transfer.Bytes.SetLength(0);
        transfer.Atoms.Clear();
        transfer.ActualType = 0;
        transfer.Progress = Stopwatch.GetTimestamp();
        X11ClipboardNative.XDeleteProperty(_connection.Display, transfer.Window, _property);
        X11ClipboardNative.XConvertSelection(_connection.Display, _clipboard, target, _property, transfer.Window, transfer.Time);
        _connection.Flush();
    }

    private void ConvertNext(Incoming transfer)
    {
        if (transfer.FallbackTargets.Count == 0) CompleteIncoming(transfer, null);
        else
        {
            nuint target = transfer.FallbackTargets[0];
            transfer.FallbackTargets.RemoveAt(0);
            Convert(transfer, target);
        }
    }

    private Property ReadProperty(nuint window)
    {
        int status = X11ClipboardNative.XGetWindowProperty(_connection.Display, window, _property, 0, MaximumBytes / 4 + 1, 1, 0,
            out var type, out var format, out var count, out var remaining, out var data);
        try
        {
            if (status != 0 || remaining != 0 || count > MaximumBytes || (format != 8 && format != 32))
                throw new NotSupportedException("Malformed or oversized clipboard property.");
            if (format == 32)
            {
                if (count > MaximumBytes / 8) throw new NotSupportedException("Clipboard atom list exceeds the transfer limit.");
                var values = new nuint[checked((int)count)];
                for (int i = 0; i < values.Length; i++) values[i] = unchecked((nuint)Marshal.ReadIntPtr(data, i * IntPtr.Size));
                return new Property(type, format, count, Array.Empty<byte>(), values);
            }
            var bytes = new byte[checked((int)count)];
            if (bytes.Length > 0) Marshal.Copy(data, bytes, 0, bytes.Length);
            return new Property(type, format, count, bytes, Array.Empty<nuint>());
        }
        finally { if (data != 0) X11Native.XFree(data); }
    }

    private void Consume(Incoming transfer, Property property, bool complete)
    {
        if (transfer.Target == _targets)
        {
            if (property.Type != 4 || property.Format != 32 || transfer.Atoms.Count + property.Longs.Length > 4096)
                throw new NotSupportedException("Clipboard TARGETS is not a bounded ATOM list.");
            transfer.Atoms.AddRange(property.Longs);
            if (complete)
            {
                foreach (nuint target in new[] { _utf8, _text, _string })
                    if (transfer.Atoms.Contains(target)) transfer.FallbackTargets.Add(target);
                ConvertNext(transfer);
            }
        }
        else
        {
            if (property.Format != 8 || (property.Type != _utf8 && property.Type != _string) ||
                (transfer.Target == _utf8 && property.Type != _utf8) || (transfer.Target == _string && property.Type != _string) ||
                (transfer.ActualType != 0 && transfer.ActualType != property.Type))
                throw new NotSupportedException("Clipboard owner returned an unsupported text encoding.");
            transfer.ActualType = property.Type;
            if (transfer.Bytes.Length + property.Bytes.Length > MaximumBytes) throw new NotSupportedException("Clipboard transfer exceeds 16 MiB.");
            transfer.Bytes.Write(property.Bytes);
            if (complete)
            {
                byte[] bytes = transfer.Bytes.ToArray();
                CompleteIncoming(transfer, property.Type == _utf8 ? Utf8.GetString(bytes) : Encoding.Latin1.GetString(bytes));
            }
        }
        transfer.Progress = Stopwatch.GetTimestamp();
    }

    private void CompleteIncoming(Incoming transfer, string? text, Exception? exception = null)
    {
        if (!_incoming.Remove(transfer.Window)) return;
        if (exception == null && X11ClipboardNative.XGetSelectionOwner(_connection.Display, _clipboard) != transfer.Owner)
            exception = new OperationCanceledException("The clipboard owner changed during conversion.");
        _connection.UnregisterWindow(transfer.Window);
        X11ClipboardNative.XDestroyWindow(_connection.Display, transfer.Window);
        transfer.Bytes.Dispose();
        if (exception is OperationCanceledException) transfer.Completion.TrySetCanceled();
        else if (exception != null) transfer.Completion.TrySetException(exception);
        else transfer.Completion.TrySetResult(text);
        _connection.Flush();
    }

    private void PostTick()
    {
        if (Interlocked.Exchange(ref _tickPosted, 1) != 0) return;
        try { _connection.Post(Tick); }
        catch (ObjectDisposedException) { Interlocked.Exchange(ref _tickPosted, 0); }
    }

    private void Tick()
    {
        Interlocked.Exchange(ref _tickPosted, 0);
        if (_disposed) return;
        foreach (var transfer in _incoming.Values.ToArray())
        {
            if (X11ClipboardNative.XGetSelectionOwner(_connection.Display, _clipboard) != transfer.Owner)
                CompleteIncoming(transfer, null, new OperationCanceledException("The clipboard owner disappeared or changed."));
            else if (Expired(transfer.Started, transfer.Progress))
                CompleteIncoming(transfer, null, new TimeoutException("The clipboard owner did not complete the selection transfer."));
        }
        foreach (var transfer in _outgoing.Values.ToArray())
            if (Expired(transfer.Started, transfer.Progress)) FinishOutgoing(transfer);
        foreach (var claim in _claims)
            if (!claim.Completion.Task.IsCompleted && Stopwatch.GetElapsedTime(claim.Started) > ProgressTimeout)
                claim.Completion.TrySetException(new TimeoutException("The X11 server did not timestamp clipboard ownership."));
    }

    private static bool Expired(long started, long progress) => Stopwatch.GetElapsedTime(started) > TransferTimeout || Stopwatch.GetElapsedTime(progress) > ProgressTimeout;
    private static bool IsEarlier(nuint left, nuint right) => unchecked((int)((uint)left - (uint)right)) < 0;

    private bool Foreign(Action action)
    {
        // The connection traps BadWindow from a requestor that exits during a transfer.
        return _connection.TryForeignRequest(action);
    }

    private void Verify() { _connection.VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this); }

    public void Dispose()
    {
        _connection.VerifyOwner();
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        foreach (var claim in _claims) claim.Completion.TrySetCanceled();
        _claims.Clear();
        foreach (var transfer in _incoming.Values.ToArray()) CompleteIncoming(transfer, null, new OperationCanceledException());
        foreach (var transfer in _outgoing.Values.ToArray()) FinishOutgoing(transfer);
        if (X11ClipboardNative.XGetSelectionOwner(_connection.Display, _clipboard) == _window)
            X11ClipboardNative.XSetSelectionOwner(_connection.Display, _clipboard, 0, 0);
        _current = null;
        _connection.UnregisterWindow(_window);
        X11ClipboardNative.XDestroyWindow(_connection.Display, _window);
        _connection.Flush();
    }

    private sealed class Snapshot(string text, byte[] bytes, object original)
    {
        internal readonly string Text = text;
        internal readonly byte[] Bytes = bytes;
        internal readonly object Original = original;
        internal readonly bool Latin1 = text.All(character => character <= 255);
        internal nuint Time;
    }
    private sealed class Claim(Snapshot snapshot)
    {
        internal readonly Snapshot Snapshot = snapshot;
        internal readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly long Started = Stopwatch.GetTimestamp();
    }
    private sealed class Incoming(nuint window, nuint owner)
    {
        internal readonly nuint Window = window, Owner = owner;
        internal readonly TaskCompletionSource<string?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly MemoryStream Bytes = new();
        internal readonly List<nuint> Atoms = new();
        internal readonly List<nuint> FallbackTargets = new();
        internal readonly long Started = Stopwatch.GetTimestamp();
        internal long Progress = Stopwatch.GetTimestamp();
        internal nuint Time, Target, ActualType;
        internal bool Incremental;
    }
    private sealed class Outgoing(nuint window, nuint property, nuint type, byte[] bytes, nint originalMask)
    {
        internal readonly nuint Window = window, Property = property, Type = type;
        internal readonly byte[] Bytes = bytes;
        internal readonly nint OriginalMask = originalMask;
        internal readonly long Started = Stopwatch.GetTimestamp();
        internal long Progress = Stopwatch.GetTimestamp();
        internal int Offset;
    }
    private readonly record struct Property(nuint Type, int Format, nuint Count, byte[] Bytes, nuint[] Longs);
}
