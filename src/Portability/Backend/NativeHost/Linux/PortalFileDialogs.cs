extern alias WpfFork;

using System.Text;
using WpfFork::MS.Internal.Portability.Windowing;

namespace Wpf.Portable.NativeHost.Linux;

internal sealed class PortalFileDialogs(X11Connection owner) : IDisposable
{
    private readonly X11Connection _owner = owner;
    private GioBusConnection? _bus;
    private readonly HashSet<Request> _requests = new();
    private bool _disposed;
    internal Task<PortableFileDialogResult> ShowAsync(nuint ownerXid, PortableFileDialogOptions options, CancellationToken cancellationToken)
    {
        _owner.VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (ownerXid == 0) throw new InvalidOperationException("A file chooser requires a live native owner.");
        _bus ??= GioBusConnection.ConnectSession(_owner);
        var request = new Request(this, _bus, ownerXid, options, cancellationToken);
        _requests.Add(request);
        try { request.Start(); }
        catch { request.Dispose(); _requests.Remove(request); throw; }
        return request.Completion.Task;
    }
    private sealed class Request(PortalFileDialogs service, GioBusConnection bus, nuint window, PortableFileDialogOptions options, CancellationToken token) : IDisposable
    {
        private const string Desktop = "org.freedesktop.portal.Desktop", DesktopPath = "/org/freedesktop/portal/desktop", RequestInterface = "org.freedesktop.portal.Request";
        internal readonly TaskCompletionSource<PortableFileDialogResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IDisposable? _response;
        private CancellationTokenRegistration _cancellation;
        private GioVariant? _earlyResponse;
        private string? _actualPath;
        private string _expectedPath = "";
        private bool _done, _closing;
        private readonly List<PendingOperation> _operations = new();
        private PendingOperation? _chooser, _close;
        private sealed class PendingOperation(Task<GioVariant> task) { internal readonly Task<GioVariant> Task = task; internal int Claimed; }
        internal void Start()
        {
            using var iface = GioVariant.String("org.freedesktop.portal.FileChooser"); using var property = GioVariant.String("version");
            using var parameters = GioVariant.Tuple(iface, property);
            Continue(bus.CallAsync(Desktop, DesktopPath, "org.freedesktop.DBus.Properties", "Get", parameters, "(v)", token), reply =>
            {
                using var boxed = reply.Child(0); using var version = boxed.Unbox();
                if (version.ReadUInt32() == 0) throw new PlatformNotSupportedException("The desktop file chooser portal is unavailable.");
                if (token.IsCancellationRequested) { CancelBeforeRequest(); return; }
                BeginChooser();
            }, availability: true);
        }
        private void BeginChooser()
        {
            string handle = "wpf_" + Guid.NewGuid().ToString("N");
            _expectedPath = "/org/freedesktop/portal/desktop/request/" + bus.UniqueName.TrimStart(':').Replace('.', '_') + "/" + handle;
            _response = bus.Subscribe(Desktop, _expectedPath, RequestInterface, "Response", (_, value) =>
            {
                if (_done || _closing || token.IsCancellationRequested) return;
                if (_actualPath is null) { _earlyResponse?.Dispose(); _earlyResponse = value.Ref(); }
                else ParseResponse(value);
            });
            bus.Closed += OnClosed;
            _cancellation = token.Register(() =>
            {
                try { service._owner.Post(Cancel); }
                catch (Exception error) { service._owner.CaptureCallbackException(error); }
            });
            using var chooserOptions = BuildOptions(handle);
            using var parent = GioVariant.String($"x11:{window:X}"); using var title = GioVariant.String(options.Title);
            using var parameters = GioVariant.Tuple(parent, title, chooserOptions);
            _chooser = Continue(bus.CallAsync(Desktop, DesktopPath, "org.freedesktop.portal.FileChooser", options.Save ? "SaveFile" : "OpenFile", parameters, "(o)"), reply =>
            {
                using var path = reply.Child(0); _actualPath = path.ReadString();
                if (token.IsCancellationRequested) { Cancel(); return; }
                if (_actualPath != _expectedPath)
                {
                    var failure = new InvalidDataException($"Portal returned unexpected request path '{_actualPath}', expected '{_expectedPath}'.");
                    _closing = true;
                    using var empty = GioVariant.Tuple();
                    _close = Continue(bus.CallAsync(Desktop, _actualPath, RequestInterface, "Close", empty, "()"), _ => Fail(failure));
                    return;
                }
                if (_earlyResponse is { } response) { _earlyResponse = null; using (response) ParseResponse(response); }
            });
        }
        private GioVariant BuildOptions(string handle)
        {
            var values = new Dictionary<string, GioVariant>(StringComparer.Ordinal);
            try
            {
                values.Add("handle_token", GioVariant.String(handle));
                values.Add("modal", GioVariant.Bool(true));
                values.Add("current_name", GioVariant.String(options.FileName));
                if (!options.Save) values.Add("multiple", GioVariant.Bool(options.AllowMultiple));
                if (!string.IsNullOrEmpty(options.Directory) && Directory.Exists(options.Directory))
                    values.Add("current_folder", GioVariant.Bytes(Encoding.UTF8.GetBytes(Path.GetFullPath(options.Directory) + "\0")));
                if (options.Filters.Count != 0)
                {
                    var filters = options.Filters.Select(Filter).ToArray();
                    try
                    {
                        values.Add("filters", GioVariant.Array("(sa(us))", filters));
                        values.Add("current_filter", filters[options.FilterIndex - 1].Ref());
                    }
                    finally { foreach (var filter in filters) filter.Dispose(); }
                }
                return GioVariant.Dictionary(values);
            }
            finally { foreach (var value in values.Values) value.Dispose(); }
        }
        private static GioVariant Filter(PortableFileDialogFilter filter)
        {
            var patterns = new List<GioVariant>();
            try
            {
                foreach (var pattern in filter.Patterns)
                {
                    using var kind = GioVariant.UInt32(0); using var text = GioVariant.String(pattern);
                    patterns.Add(GioVariant.Tuple(kind, text));
                }
                using var name = GioVariant.String(filter.Name); using var array = GioVariant.Array("(us)", patterns.ToArray());
                return GioVariant.Tuple(name, array);
            }
            finally { foreach (var pattern in patterns) pattern.Dispose(); }
        }
        private PendingOperation Continue(Task<GioVariant> operation, Action<GioVariant> consume, bool availability = false)
        {
            var pending = new PendingOperation(operation); _operations.Add(pending);
            _ = operation.ContinueWith(task =>
            {
                if (Volatile.Read(ref pending.Claimed) != 0) return;
                try
                {
                    service._owner.Post(() =>
                    {
                        if (Interlocked.CompareExchange(ref pending.Claimed, 1, 0) != 0) return;
                        if (task.IsCompletedSuccessfully)
                        {
                            using var reply = task.Result;
                            if (_done) return;
                            try { consume(reply); } catch (Exception error) { Fail(error); }
                        }
                        else if (!_done)
                        {
                            if (availability && token.IsCancellationRequested) CancelBeforeRequest();
                            else
                            {
                                var failure = task.Exception?.InnerException ?? new IOException("The portal operation was canceled unexpectedly.");
                                Fail(availability ? new PlatformNotSupportedException("The desktop file chooser portal is unavailable.", failure) : failure);
                            }
                        }
                    });
                }
                catch (Exception error) { if (task.IsCompletedSuccessfully && Interlocked.CompareExchange(ref pending.Claimed, 1, 0) == 0) task.Result.Dispose(); service._owner.CaptureCallbackException(error); }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return pending;
        }
        private void ParseResponse(GioVariant response)
        {
            try
            {
                response.Require("(ua{sv})");
                using var codeValue = response.Child(0); uint code = codeValue.ReadUInt32();
                if (code == 1) { Finish(new(false, Array.Empty<string>(), null)); return; }
                if (code != 0) throw new IOException("The native file chooser ended with response " + code + ".");
                using var results = response.Child(1);
                using var uris = results.Lookup("uris") ?? throw new InvalidDataException("The native file chooser success response has no uris.");
                uris.Require("as");
                if (uris.Count == 0) throw new InvalidDataException("The native file chooser accepted an empty selection.");
                if ((options.Save || !options.AllowMultiple) && uris.Count != 1) throw new InvalidDataException("The native file chooser returned multiple paths for a single selection.");
                var paths = new string[uris.Count];
                for (int index = 0; index < paths.Length; index++)
                {
                    using var item = uris.Child(index); string text = item.ReadString();
                    if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !uri.IsFile ||
                        (!string.IsNullOrEmpty(uri.Host) && !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) ||
                        !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.LocalPath.Contains('\0') || !Path.IsPathFullyQualified(uri.LocalPath))
                        throw new InvalidDataException("The native file chooser returned a nonlocal URI.");
                    paths[index] = uri.LocalPath;
                }
                int? filterIndex = null;
                using var filter = results.Lookup("current_filter");
                if (filter != null)
                {
                    filter.Require("(sa(us))"); using var nameValue = filter.Child(0); string name = nameValue.ReadString();
                    using var patternValues = filter.Child(1); var patterns = new string[patternValues.Count];
                    for (int index = 0; index < patterns.Length; index++)
                    {
                        using var entry = patternValues.Child(index); using var kind = entry.Child(0); using var pattern = entry.Child(1);
                        if (kind.ReadUInt32() != 0) throw new InvalidDataException("The desktop file chooser returned an unknown filter.");
                        patterns[index] = pattern.ReadString();
                    }
                    for (int index = 0; index < options.Filters.Count; index++)
                        if (options.Filters[index].Name == name && options.Filters[index].Patterns.SequenceEqual(patterns)) { filterIndex = index + 1; break; }
                    if (filterIndex is null) throw new InvalidDataException("The desktop file chooser returned an unknown filter.");
                }
                Finish(new(true, Array.AsReadOnly(paths), filterIndex));
            }
            catch (Exception error) { Fail(error); }
        }
        internal void Cancel()
        {
            if (_done || _closing || _actualPath is null) return;
            _closing = true;
            using var empty = GioVariant.Tuple();
            _close = Continue(bus.CallAsync(Desktop, _actualPath, RequestInterface, "Close", empty, "()"), _ =>
            {
                if (_done) return;
                _done = true; Dispose(); service._requests.Remove(this); Completion.TrySetCanceled(token);
            });
        }
        private void CancelBeforeRequest() { if (_done) return; _done = true; Dispose(); service._requests.Remove(this); Completion.TrySetCanceled(token); }
        internal void Shutdown()
        {
            if (_done) return;
            _done = true;
            foreach (var operation in _operations) Interlocked.Exchange(ref operation.Claimed, 1);
            Dispose();
            try
            {
                if (_chooser != null)
                {
                    if (_actualPath is null)
                    {
                        var reply = bus.WaitForShutdown(_chooser.Task);
                        using var path = reply.Child(0); _actualPath = path.ReadString();
                    }
                    if (_close != null) _ = bus.WaitForShutdown(_close.Task);
                    else
                    {
                        using var empty = GioVariant.Tuple();
                        using var reply = bus.WaitForShutdown(bus.CallAsync(Desktop, _actualPath, RequestInterface, "Close", empty, "()"));
                    }
                }
                Completion.TrySetCanceled(token.IsCancellationRequested ? token : new CancellationToken(true));
            }
            catch (Exception error) { Completion.TrySetException(error); throw; }
            finally
            {
                foreach (var operation in _operations)
                    if (operation.Task.IsCompletedSuccessfully) operation.Task.Result.Dispose();
                service._requests.Remove(this);
            }
        }
        private void OnClosed(Exception error) => Fail(error);
        private void Finish(PortableFileDialogResult result) { if (_done) return; _done = true; Dispose(); service._requests.Remove(this); Completion.TrySetResult(result); }
        private void Fail(Exception error) { if (_done) return; _done = true; Dispose(); service._requests.Remove(this); Completion.TrySetException(error); }
        public void Dispose() { _response?.Dispose(); _response = null; _cancellation.Dispose(); _earlyResponse?.Dispose(); _earlyResponse = null; bus.Closed -= OnClosed; }
    }
    public void Dispose()
    {
        _owner.VerifyOwner(); if (_disposed) return;
        Exception? failure = null;
        foreach (var request in _requests.ToArray())
            try { request.Shutdown(); } catch (Exception error) { failure ??= error; }
        try { _bus?.Dispose(); } catch (Exception error) { failure ??= error; }
        _bus = null; _disposed = true;
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
