extern alias WpfFork;

using System.Runtime.ExceptionServices;
using Wpf.Portable.NativeHost.Linux;
using DispatcherPlatform = WpfFork::MS.Internal.Portability.DispatcherPlatform;
using IDispatcherPump = WpfFork::MS.Internal.Portability.IDispatcherPump;
using WpfDispatcher = WpfFork::System.Windows.Threading.Dispatcher;

namespace Wpf.Portable.NativeHost;

public static class NativeDispatcherHost
{
    [ThreadStatic] private static NativeHostSession? s_session;

    public static void InitializeForCurrentThread()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The owned native dispatcher supports Linux X11 only.");
        if (s_session is not null || WpfDispatcher.FromThread(Thread.CurrentThread) is not null)
            throw new InvalidOperationException("Initialize the native host before creating this thread's WPF Dispatcher.");

        var session = new NativeHostSession(new X11Connection());
        try
        {
            DispatcherPlatform.RegisterPumpFactory(() => new HostDispatcherPump(session));
            s_session = session;
            WpfDispatcher.CurrentDispatcher.ShutdownFinished += (_, _) => session.CloseNativeResources();
        }
        catch
        {
            s_session = null;
            session.Dispose();
            throw;
        }
    }

    internal static NativeHostSession Session
    {
        get
        {
            var session = s_session ?? throw new InvalidOperationException("Initialize the native dispatcher before registering window services.");
            session.Connection.VerifyOwner();
            return session;
        }
    }

    private sealed class HostDispatcherPump : IDispatcherPump
    {
        private readonly NativeHostSession _session;
        private readonly OwnedGlibDispatcherPump _pump;
        private bool _disposed;

        internal HostDispatcherPump(NativeHostSession session)
        {
            _session = session;
            _pump = new OwnedGlibDispatcherPump(session.Connection);
        }

        public bool HasPendingInput => _pump.HasPendingInput;
        public void Wake() => _pump.Wake();
        public void WaitAndDispatch(int timeoutMilliseconds) => _pump.WaitAndDispatch(timeoutMilliseconds);
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ExceptionDispatchInfo? failure = null;
            try { _session.CloseNativeResources(); }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
            try { _pump.Dispose(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            try { _session.Dispose(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            failure?.Throw();
        }
    }
}

internal sealed class NativeHostSession(X11Connection connection) : IDisposable
{
    private readonly List<IDisposable> _resources = new();
    private bool _closing, _closed, _disposed;
    private ExceptionDispatchInfo? _shutdownFailure;
    internal X11Connection Connection { get; } = connection;

    internal void Own(IDisposable resource)
    {
        Connection.VerifyOwner();
        ArgumentNullException.ThrowIfNull(resource);
        if (_closing || _closed) throw new InvalidOperationException("The native host is shutting down.");
        _resources.Add(resource);
    }

    internal void CloseNativeResources()
    {
        Connection.VerifyOwner();
        if (_closing || _closed) return;
        _closing = true;
        try
        {
            for (int index = _resources.Count - 1; index >= 0; index--)
            {
                try { _resources[index].Dispose(); }
                catch (Exception exception) { _shutdownFailure ??= ExceptionDispatchInfo.Capture(exception); }
            }
            _resources.Clear();
            _closed = true;
        }
        finally { _closing = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { CloseNativeResources(); }
        finally
        {
            Connection.Dispose();
            _disposed = true;
        }
        _shutdownFailure?.Throw();
    }
}
