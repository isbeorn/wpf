// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#if PORTABLE_WPF
using System;
using System.IO;
using System.Windows.Threading;

namespace MS.Internal.Portability
{
    internal interface IPortableSplashWindow : IDisposable
    {
        void SetOpacity(double opacity);
    }

    internal interface IPortableSplashFactory
    {
        IPortableSplashWindow Create(Stream encodedImage, bool topMost, Dispatcher owner);
    }

    internal static class PortableSplashPlatform
    {
        [ThreadStatic]
        private static IPortableSplashFactory _factory;

        internal static void Register(IPortableSplashFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            if (_factory != null) throw new InvalidOperationException("A native splash factory is already registered on this thread.");
            _factory = factory;
        }

        internal static IPortableSplashWindow Create(Stream encodedImage, bool topMost, Dispatcher owner)
        {
            owner.VerifyAccess();
            if (owner.HasShutdownStarted) throw new InvalidOperationException("The splash owner Dispatcher has started shutdown.");
            return (_factory ?? throw new PlatformNotSupportedException("A native splash backend must be registered before showing a portable SplashScreen."))
                .Create(encodedImage, topMost, owner) ?? throw new InvalidOperationException("The native splash factory returned null.");
        }
    }
}
#endif
