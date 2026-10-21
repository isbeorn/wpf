extern alias WpfFork;

using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Dispatcher = WpfFork::System.Windows.Threading.Dispatcher;
using DispatcherFrame = WpfFork::System.Windows.Threading.DispatcherFrame;
using DispatcherPriority = WpfFork::System.Windows.Threading.DispatcherPriority;
using PropertyChangedEventManager = WpfFork::System.ComponentModel.PropertyChangedEventManager;

internal static class WeakEventWaitCases
{
    internal static void PublicNotification()
    {
        using var source = new BlockingRegistrationSource();
        using var releaseOwner = new ManualResetEventSlim();
        using var notificationFinished = new ManualResetEventSlim();
        using var attempting = new ManualResetEventSlim();
        bool notificationStarted = false;
        Exception? ownerFailure = null, notificationFailure = null;
        int delivered = 0;
        EventHandler<PropertyChangedEventArgs> handler = (sender, args) =>
        {
            Require(ReferenceEquals(sender, source) && args.PropertyName == "Value", "The genuine property notification changed source or name.");
            Interlocked.Increment(ref delivered);
        };
        var owner = new Thread(() =>
        {
            try
            {
                PropertyChangedEventManager.AddHandler(source, handler, "Value");
                Require(releaseOwner.Wait(TimeSpan.FromSeconds(5)), "Registration owner was not released for cleanup.");
                PropertyChangedEventManager.RemoveHandler(source, handler, "Value");
            }
            catch (Exception exception) { ownerFailure = exception; }
            finally { try { Shutdown(); } catch (Exception exception) { ownerFailure ??= exception; } }
        }) { IsBackground = true };
        var notification = new Thread(() =>
        {
            try { WithParentAndQueuedCallback(() => { attempting.Set(); source.Raise(); }); }
            catch (Exception exception) { notificationFailure = exception; }
            finally { notificationFinished.Set(); try { Shutdown(); } catch (Exception exception) { notificationFailure ??= exception; } }
        }) { IsBackground = true };
        owner.Start();
        Exception? failure = null;
        try
        {
            Require(source.Registering.Wait(TimeSpan.FromSeconds(2)), "The original weak-event manager did not subscribe to the source.");
            notification.Start();
            notificationStarted = true;
            Require(attempting.Wait(TimeSpan.FromSeconds(2)), "The notification worker did not attempt the real source event.");
            Require(SpinWait.SpinUntil(() => notificationFinished.IsSet || source.Lock!.WaitingReadCount > 0, TimeSpan.FromSeconds(2)), "The property notification did not reach its contended read wait.");
            Require(Volatile.Read(ref delivered) == 0, "The property notification bypassed the held registration write lock.");
            source.ReleaseRegistration.Set();
            Require(notification.Join(TimeSpan.FromSeconds(3)), "The property notification stayed blocked after registration released.");
            Rethrow(notificationFailure);
            Require(delivered == 1, "The genuine worker notification was not delivered exactly once.");
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            source.ReleaseRegistration.Set();
            releaseOwner.Set();
            if (!owner.Join(TimeSpan.FromSeconds(3))) failure ??= new InvalidOperationException("The registration owner did not finish cleanup.");
            if (notificationStarted && !notification.Join(TimeSpan.FromSeconds(3))) failure ??= new InvalidOperationException("The notification worker did not finish cleanup.");
        }
        Rethrow(failure);
        Rethrow(ownerFailure);
        source.Raise();
        Require(delivered == 1 && source.Added == 1 && source.Removed == 1, "Weak-event removal retained or duplicated a subscription.");
        GC.KeepAlive(handler);
    }

    internal static void ReadAndWrite()
    {
        var failures = new List<Exception>();
        try { Contended(heldWrite: true, requestedWrite: false); } catch (Exception exception) { failures.Add(exception); }
        try { Contended(heldWrite: false, requestedWrite: true); } catch (Exception exception) { failures.Add(exception); }
        if (failures.Count != 0) throw new AggregateException("Contended read/write acquisition failed.", failures);
    }

    internal static void RecursionAndFailure()
    {
        object wrapper = NewWrapper();
        var parent = new ParentContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(parent);
        try
        {
            using (Acquire(wrapper, write: true))
            using (Acquire(wrapper, write: true))
            using (Acquire(wrapper, write: false))
                Require(ReferenceEquals(SynchronizationContext.Current, parent), "Recursive lock acquisition lost its parent context.");
            using (Acquire(wrapper, write: false))
            using (Acquire(wrapper, write: false)) { }
            var released = Acquire(wrapper, write: false);
            released.Dispose();
            bool rejected = false;
            try { released.Dispose(); }
            catch (SynchronizationLockException) { rejected = true; }
            Require(rejected && ReferenceEquals(SynchronizationContext.Current, parent), "A failed release did not restore the parent synchronization context.");
            using (Acquire(wrapper, write: true)) { }
            Require(parent.Calls == 0 && ReferenceEquals(SynchronizationContext.Current, parent), "Lock acquisition/release called or replaced the parent context.");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void Contended(bool heldWrite, bool requestedWrite)
    {
        object wrapper = NewWrapper();
        var slim = GetSlim(wrapper);
        IDisposable? held = Acquire(wrapper, heldWrite);
        using var finished = new ManualResetEventSlim();
        using var attempting = new ManualResetEventSlim();
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { WithParentAndQueuedCallback(() => { attempting.Set(); using (Acquire(wrapper, requestedWrite)) { } }); }
            catch (Exception exception) { failure = exception; }
            finally { finished.Set(); try { Shutdown(); } catch (Exception exception) { failure ??= exception; } }
        }) { IsBackground = true };
        worker.Start();
        try
        {
            Require(attempting.Wait(TimeSpan.FromSeconds(2)), "The lock worker did not attempt acquisition.");
            Require(SpinWait.SpinUntil(() => finished.IsSet || (requestedWrite ? slim.WaitingWriteCount : slim.WaitingReadCount) > 0, TimeSpan.FromSeconds(2)), "The lock worker did not reach its contended wait.");
            held.Dispose();
            held = null;
            Require(worker.Join(TimeSpan.FromSeconds(3)), "The contended lock stayed blocked after the owner released it.");
        }
        catch (Exception exception) { failure ??= exception; }
        finally
        {
            held?.Dispose();
            if (!worker.Join(TimeSpan.FromSeconds(3))) failure ??= new InvalidOperationException("The lock worker did not finish cleanup.");
        }
        Rethrow(failure);
    }

    private static void WithParentAndQueuedCallback(Action action)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        bool callbackRan = false;
        dispatcher.BeginInvoke(new Action(() => callbackRan = true));
        var parent = new ParentContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(parent);
        try
        {
            Exception? failure = null;
            try { action(); } catch (Exception exception) { failure = exception; }
            if (!ReferenceEquals(SynchronizationContext.Current, parent)) failure ??= new InvalidOperationException("The non-pumping lock wait did not restore its parent context.");
            Rethrow(failure);
            Require(!callbackRan && parent.Calls == 0, "The contended wait pumped queued WPF work or delegated to the parent context.");
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Require(callbackRan, "Queued WPF work did not recover after the lock wait.");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static object NewWrapper() => Activator.CreateInstance(typeof(Dispatcher).Assembly.GetType("MS.Internal.ReaderWriterLockWrapper", throwOnError: true)!, nonPublic: true)!;
    private static ReaderWriterLockSlim GetSlim(object wrapper) => (ReaderWriterLockSlim)wrapper.GetType().GetField("_rwLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wrapper)!;
    private static ReaderWriterLockSlim CurrentTableLock()
    {
        var type = typeof(Dispatcher).Assembly.GetType("MS.Internal.WeakEventTable", throwOnError: true)!;
        object table = type.GetProperty("CurrentWeakEventTable", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        return GetSlim(type.GetField("_lock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(table)!);
    }
    private static IDisposable Acquire(object wrapper, bool write)
    {
        try { return (IDisposable)wrapper.GetType().GetProperty(write ? "WriteLock" : "ReadLock", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(wrapper)!; }
        catch (TargetInvocationException exception) when (exception.InnerException != null) { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
    private static void Rethrow(Exception? exception) { if (exception != null) ExceptionDispatchInfo.Capture(exception).Throw(); }
    private static void Shutdown()
    {
        var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        if (dispatcher != null && !dispatcher.HasShutdownFinished) dispatcher.InvokeShutdown();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ParentContext : SynchronizationContext
    {
        internal int Calls;
        public ParentContext() { SetWaitNotificationRequired(); }
        public override int Wait(IntPtr[] handles, bool all, int timeout) { Calls++; throw new InvalidOperationException("The lock delegated a blocking wait to its parent context."); }
        public override void Send(SendOrPostCallback callback, object? state) { Calls++; throw new InvalidOperationException("The lock delegated Send to its parent context."); }
        public override void Post(SendOrPostCallback callback, object? state) { Calls++; throw new InvalidOperationException("The lock delegated Post to its parent context."); }
    }

    private sealed class BlockingRegistrationSource : INotifyPropertyChanged, IDisposable
    {
        private PropertyChangedEventHandler? _changed;
        internal readonly ManualResetEventSlim Registering = new();
        internal readonly ManualResetEventSlim ReleaseRegistration = new();
        internal int Added, Removed;
        internal ReaderWriterLockSlim? Lock;
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
                _changed += value;
                Added++;
                Lock = CurrentTableLock();
                Registering.Set();
                Require(ReleaseRegistration.Wait(TimeSpan.FromSeconds(5)), "The controlled event registration was not released.");
            }
            remove { _changed -= value; Removed++; }
        }
        internal void Raise() => _changed?.Invoke(this, new PropertyChangedEventArgs("Value"));
        public void Dispose() { Registering.Dispose(); ReleaseRegistration.Dispose(); }
    }
}
