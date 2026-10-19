using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Wpf.Portable.NativeHost.Linux;

internal sealed class GioBusConnection : IDisposable
{
    private readonly X11Connection _owner;
    private readonly List<Registration> _registrations = new();
    private readonly HashSet<PendingCall> _calls = new();
    private nint _connection;
    private int _nativeRegistrations;
    private bool _disposed;
    private readonly GioNative.Closed _closedCallback;
    private nuint _closedHandler;
    internal event Action<Exception>? Closed;
    internal string UniqueName { get; }
    private static readonly GioNative.Ready ReadyCallback = OnReady;
    private static readonly GioNative.Signal SignalCallback = OnSignal;
    private static readonly GioNative.Method MethodCallback = OnMethod;
    private static readonly GioNative.Destroy DestroyCallback = data =>
    {
        try
        {
            var handle = GCHandle.FromIntPtr(data);
            var registration = (Registration)handle.Target!;
            Interlocked.Decrement(ref registration.Bus._nativeRegistrations);
            handle.Free();
        }
        catch { }
    };

    private GioBusConnection(X11Connection owner, string address)
    {
        owner.VerifyOwner(); _owner = owner;
        _connection = GioNative.g_dbus_connection_new_for_address_sync(address, 1 | 8, 0, 0, out var error);
        if (_connection == 0) throw GioNative.TakeError(error);
        GioNative.g_dbus_connection_set_exit_on_close(_connection, 0);
        UniqueName = GioNative.Utf8(GioNative.g_dbus_connection_get_unique_name(_connection));
        _closedCallback = (_, _, errorPointer, _) =>
        {
            try
            {
                var error = new IOException(errorPointer == 0 ? "The D-Bus connection closed." : "The D-Bus connection closed unexpectedly.");
                _owner.Post(() => { if (!_disposed) Closed?.Invoke(error); });
            }
            catch (Exception error) { _owner.CaptureCallbackException(error); }
        };
        _closedHandler = GioNative.g_signal_connect_data(_connection, "closed", _closedCallback, 0, 0, 0);
    }
    internal static GioBusConnection ConnectSession(X11Connection owner)
    {
        owner.VerifyOwner();
        nint address = GioNative.g_dbus_address_get_for_bus_sync(2, 0, out var error);
        if (address == 0) throw GioNative.TakeError(error);
        try { return new(owner, GioNative.Utf8(address)); }
        finally { GioNative.g_free(address); }
    }
    internal static GioBusConnection ConnectAddress(X11Connection owner, string address) => new(owner, address);
    private void Verify() { _owner.VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this); }

    internal Task<GioVariant> CallAsync(string destination, string path, string iface, string method,
        GioVariant parameters, string? replySignature = null, CancellationToken cancellationToken = default)
    {
        Verify(); cancellationToken.ThrowIfCancellationRequested();
        var call = new PendingCall(this, cancellationToken);
        _calls.Add(call);
        nint type = replySignature is null ? 0 : GioNative.g_variant_type_new(replySignature);
        try
        {
            GioNative.g_dbus_connection_call(_connection, destination, path, iface, method, parameters.Handle, type,
                0, -1, call.Cancellable, ReadyCallback, GCHandle.ToIntPtr(call.Handle));
        }
        catch { _calls.Remove(call); call.ReleaseBeforeStart(); throw; }
        finally { if (type != 0) GioNative.g_variant_type_free(type); }
        return call.Completion.Task;
    }
    private sealed class PendingCall
    {
        internal readonly GioBusConnection Bus;
        internal readonly TaskCompletionSource<GioVariant> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly CancellationToken Token;
        internal readonly nint Cancellable;
        internal GCHandle Handle;
        internal int NativeCompleted;
        internal Action? Delivery;
        private CancellationTokenRegistration _cancellation;
        internal PendingCall(GioBusConnection bus, CancellationToken token)
        {
            Bus = bus; Token = token; Cancellable = GioNative.g_cancellable_new();
            Handle = GCHandle.Alloc(this);
            _cancellation = token.Register(() => GioNative.g_cancellable_cancel(Cancellable));
        }
        internal void ReleaseBeforeStart() { _cancellation.Dispose(); GioNative.g_object_unref(Cancellable); Handle.Free(); }
        internal void FinishNative() { _cancellation.Dispose(); GioNative.g_object_unref(Cancellable); Handle.Free(); Volatile.Write(ref NativeCompleted, 1); }
    }
    private static void OnReady(nint source, nint result, nint data)
    {
        PendingCall? call = null; GioVariant? reply = null;
        try
        {
            call = (PendingCall)GCHandle.FromIntPtr(data).Target!;
            var value = GioNative.g_dbus_connection_call_finish(source, result, out var error);
            Exception? failure = value == 0 ? GioNative.TakeError(error) : null;
            if (value != 0) reply = new(value);
            call.FinishNative();
            var completedCall = call; var completedReply = reply;
            call.Delivery = () =>
            {
                completedCall.Bus._calls.Remove(completedCall);
                if (completedCall.Bus._disposed || completedCall.Token.IsCancellationRequested)
                { completedReply?.Dispose(); completedCall.Completion.TrySetCanceled(completedCall.Token.IsCancellationRequested ? completedCall.Token : new CancellationToken(true)); }
                else if (failure != null) completedCall.Completion.TrySetException(failure);
                else completedCall.Completion.TrySetResult(completedReply!);
            };
            call.Bus._owner.Post(() => Interlocked.Exchange(ref completedCall.Delivery, null)?.Invoke());
            reply = null;
        }
        catch (Exception error) { reply?.Dispose(); call?.Bus._owner.CaptureCallbackException(error); }
    }

    internal IDisposable Subscribe(string? sender, string? path, string iface, string signal, Action<string, GioVariant> handler)
    {
        Verify();
        var registration = new Registration(this) { SignalHandler = handler };
        registration.Handle = GCHandle.Alloc(registration);
        registration.Ids.Add(GioNative.g_dbus_connection_signal_subscribe(_connection, sender, iface, signal, path, 0, 0,
            SignalCallback, GCHandle.ToIntPtr(registration.Handle), DestroyCallback));
        Interlocked.Increment(ref _nativeRegistrations);
        _registrations.Add(registration);
        return registration;
    }
    private static void OnSignal(nint connection, nint sender, nint path, nint iface, nint signal, nint parameters, nint data)
    {
        Registration? registration = null; GioVariant? value = null;
        try
        {
            registration = (Registration)GCHandle.FromIntPtr(data).Target!;
            string objectPath = GioNative.Utf8(path);
            value = new(GioNative.g_variant_ref(parameters));
            registration.PendingSignals.Add(value);
            var ownedValue = value; var subscriber = registration;
            registration.Bus._owner.Post(() =>
            {
                subscriber.PendingSignals.Remove(ownedValue);
                using (ownedValue)
                    if (!subscriber.Disposed && !subscriber.Bus._disposed) subscriber.SignalHandler!(objectPath, ownedValue);
            });
            value = null;
        }
        catch (Exception error) { value?.Dispose(); registration?.Bus._owner.CaptureCallbackException(error); }
    }

    internal IDisposable RegisterObject(string path, string xml, Func<string, string, GioVariant, Task<GioVariant>> handler)
    {
        Verify();
        var names = XDocument.Parse(xml).Root?.Elements("interface").Select(e => (string?)e.Attribute("name") ?? throw new ArgumentException("An interface name is missing.")).ToArray()
            ?? throw new ArgumentException("D-Bus introspection must contain a node.");
        nint node = GioNative.g_dbus_node_info_new_for_xml(xml, out var error);
        if (node == 0) throw GioNative.TakeError(error);
        var group = new List<IDisposable>();
        try
        {
            foreach (string name in names)
            {
                var registration = new Registration(this) { MethodHandler = handler, IsObject = true };
                registration.Handle = GCHandle.Alloc(registration);
                var table = new GioNative.VTable { Method = Marshal.GetFunctionPointerForDelegate(MethodCallback) };
                var id = GioNative.g_dbus_connection_register_object(_connection, path, GioNative.g_dbus_node_info_lookup_interface(node, name),
                    ref table, GCHandle.ToIntPtr(registration.Handle), DestroyCallback, out error);
                if (id == 0) { registration.Handle.Free(); throw GioNative.TakeError(error); }
                Interlocked.Increment(ref _nativeRegistrations);
                registration.Ids.Add(id); _registrations.Add(registration); group.Add(registration);
            }
            return new RegistrationGroup(group);
        }
        catch { foreach (var registration in group) registration.Dispose(); throw; }
        finally { GioNative.g_dbus_node_info_unref(node); }
    }
    private static void OnMethod(nint connection, nint sender, nint path, nint iface, nint method, nint parameters, nint invocation, nint data)
    {
        Registration? registration = null; GioVariant? value = null; nint retainedInvocation = invocation;
        try
        {
            registration = (Registration)GCHandle.FromIntPtr(data).Target!;
            string interfaceName = GioNative.Utf8(iface), methodName = GioNative.Utf8(method);
            value = new(GioNative.g_variant_ref(parameters));
            registration.PendingMethods.Add(retainedInvocation, value);
            var ownedValue = value; var ownedInvocation = retainedInvocation; var target = registration;
            registration.Bus._owner.Post(() => target.Dispatch(interfaceName, methodName, ownedValue, ownedInvocation));
            value = null; retainedInvocation = 0;
        }
        catch (Exception error)
        {
            value?.Dispose(); if (retainedInvocation != 0) { registration?.PendingMethods.Remove(retainedInvocation); AbandonInvocation(retainedInvocation); }
            registration?.Bus._owner.CaptureCallbackException(error);
        }
    }
    private static void AbandonInvocation(nint invocation)
        => GioNative.g_dbus_method_invocation_return_dbus_error(invocation, "org.freedesktop.DBus.Error.UnknownObject", "The exported D-Bus object is no longer available.");
    private sealed class Registration(GioBusConnection bus) : IDisposable
    {
        internal readonly GioBusConnection Bus = bus;
        internal readonly List<uint> Ids = new();
        internal readonly HashSet<GioVariant> PendingSignals = new();
        internal readonly Dictionary<nint, GioVariant> PendingMethods = new();
        private readonly HashSet<InvocationLease> _activeInvocations = new();
        private sealed class InvocationLease(nint invocation) : IDisposable
        {
            private nint _invocation = invocation;
            internal nint Take() => Interlocked.Exchange(ref _invocation, 0);
            public void Dispose() { var value = Take(); if (value != 0) AbandonInvocation(value); }
        }
        internal GCHandle Handle;
        internal bool IsObject, Disposed;
        internal Action<string, GioVariant>? SignalHandler;
        internal Func<string, string, GioVariant, Task<GioVariant>>? MethodHandler;
        internal async void Dispatch(string iface, string method, GioVariant parameters, nint invocation)
        {
            if (!PendingMethods.Remove(invocation)) return;
            var lease = new InvocationLease(invocation); _activeInvocations.Add(lease);
            GioVariant? result = null; Exception? failure = null;
            try
            {
                if (Disposed || Bus._disposed) return;
                result = await MethodHandler!(iface, method, parameters).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                parameters.Dispose();
                var reply = result; var error = failure;
                try
                {
                    Bus._owner.Post(() =>
                    {
                        try
                        {
                            if (Disposed || Bus._disposed) return;
                            if (error is null)
                            {
                                nint value = reply!.Handle, target = lease.Take();
                                if (target != 0) GioNative.g_dbus_method_invocation_return_value(target, value);
                            }
                            else
                            {
                                nint target = lease.Take();
                                if (target != 0) GioNative.g_dbus_method_invocation_return_dbus_error(target, "org.freedesktop.DBus.Error.Failed", error.Message);
                            }
                        }
                        finally { reply?.Dispose(); _activeInvocations.Remove(lease); lease.Dispose(); }
                    });
                }
                catch (Exception postError) { reply?.Dispose(); lease.Dispose(); Bus._owner.CaptureCallbackException(postError); }
            }
        }
        public void Dispose()
        {
            Bus._owner.VerifyOwner(); if (Disposed) return; Disposed = true;
            foreach (var id in Ids)
                if (IsObject) GioNative.g_dbus_connection_unregister_object(Bus._connection, id);
                else GioNative.g_dbus_connection_signal_unsubscribe(Bus._connection, id);
            foreach (var value in PendingSignals) value.Dispose();
            PendingSignals.Clear();
            foreach (var pair in PendingMethods) { pair.Value.Dispose(); AbandonInvocation(pair.Key); }
            PendingMethods.Clear();
            foreach (var invocation in _activeInvocations) invocation.Dispose();
            _activeInvocations.Clear();
            Bus._registrations.Remove(this);
        }
    }
    private sealed class RegistrationGroup(List<IDisposable> items) : IDisposable
    { public void Dispose() { foreach (var item in items) item.Dispose(); items.Clear(); } }
    internal void EmitSignal(string? destination, string path, string iface, string signal, GioVariant parameters)
    {
        Verify();
        if (GioNative.g_dbus_connection_emit_signal(_connection, destination, path, iface, signal, parameters.Handle, out var error) == 0)
            throw GioNative.TakeError(error);
    }
    // Shutdown may finish transport calls without dispatching window, signal or user callbacks.
    internal GioVariant WaitForShutdown(Task<GioVariant> operation)
    {
        _owner.VerifyOwner();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!operation.IsCompleted && timer.ElapsedMilliseconds < 2000)
        {
            GioShutdown.g_main_context_iteration(_owner.MainContext, 0);
            foreach (var call in _calls.ToArray()) Interlocked.Exchange(ref call.Delivery, null)?.Invoke();
            Thread.Yield();
        }
        if (!operation.IsCompleted) throw new IOException("The portal request did not finish during bounded shutdown.");
        return operation.GetAwaiter().GetResult();
    }
    public void Dispose()
    {
        _owner.VerifyOwner(); if (_connection == 0) return; _disposed = true;
        foreach (var registration in _registrations.ToArray()) registration.Dispose();
        foreach (var call in _calls) { if (Volatile.Read(ref call.NativeCompleted) == 0) GioNative.g_cancellable_cancel(call.Cancellable); call.Completion.TrySetCanceled(new CancellationToken(true)); }
        if (_closedHandler != 0) { GioNative.g_signal_handler_disconnect(_connection, _closedHandler); _closedHandler = 0; }
        if (GioNative.g_dbus_connection_close_sync(_connection, 0, out var error) == 0 && error != 0) GioNative.g_error_free(error);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while ((_calls.Any(c => Volatile.Read(ref c.NativeCompleted) == 0) || Volatile.Read(ref _nativeRegistrations) != 0) && timer.ElapsedMilliseconds < 2000)
        { GioShutdown.g_main_context_iteration(_owner.MainContext, 0); Thread.Yield(); }
        if (_calls.Any(c => Volatile.Read(ref c.NativeCompleted) == 0) || Volatile.Read(ref _nativeRegistrations) != 0) throw new IOException("D-Bus native completion cleanup did not finish before shutdown.");
        foreach (var call in _calls.ToArray()) Interlocked.Exchange(ref call.Delivery, null)?.Invoke();
        GioNative.g_object_unref(_connection); _connection = 0;
    }
    private static class GioShutdown
    { [DllImport("libglib-2.0.so.0")] internal static extern int g_main_context_iteration(nint context, int mayBlock); }
}
