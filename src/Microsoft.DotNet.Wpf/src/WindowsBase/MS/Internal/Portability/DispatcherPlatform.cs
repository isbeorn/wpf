// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Threading;
using System.Windows.Threading;

namespace MS.Internal.Portability
{
    internal interface IDispatcherPump : IDisposable
    {
        bool HasPendingInput { get; }
        // Wake is called under the dispatcher lock and may originate on another thread.
        void Wake();
        // Owner-thread native dispatch, bounded by timeout; -1 waits indefinitely and 0 never waits.
        void WaitAndDispatch(int timeoutMilliseconds);
    }

    internal static class DispatcherPlatform
    {
        [ThreadStatic]
        private static Func<IDispatcherPump> _pumpFactory;
        [ThreadStatic]
        private static bool _creatingPump;

        internal static void RegisterPumpFactory(Func<IDispatcherPump> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            if (_creatingPump || _pumpFactory != null || Dispatcher.FromThread(Thread.CurrentThread) != null)
            {
                throw new InvalidOperationException("A dispatcher pump factory must be registered once before this thread creates its Dispatcher.");
            }
            _pumpFactory = factory;
        }

        internal static IDispatcherPump CreatePump()
        {
            if (_creatingPump)
            {
                throw new InvalidOperationException("A dispatcher pump factory cannot create a Dispatcher recursively.");
            }
            Func<IDispatcherPump> factory = _pumpFactory;
            _pumpFactory = null;
            if (factory == null)
            {
                return new ManagedDispatcherPump();
            }
            _creatingPump = true;
            try
            {
                return factory() ?? throw new InvalidOperationException("The dispatcher pump factory returned null.");
            }
            finally
            {
                _creatingPump = false;
            }
        }
    }

    internal sealed class ManagedDispatcherPump : IDispatcherPump
    {
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        // This pump is for threads without a native window host or input queue.
        public bool HasPendingInput => false;

        public void Wake() => _wake.Set();

        public void WaitAndDispatch(int timeoutMilliseconds) => _wake.WaitOne(timeoutMilliseconds);

        public void Dispose() => _wake.Dispose();
    }
}
#endif
