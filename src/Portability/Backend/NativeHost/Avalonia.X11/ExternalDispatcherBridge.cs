using System;
using System.Threading;
using Avalonia.Controls.Platform;
using Avalonia.Metadata;
using Avalonia.Threading;
using Avalonia.X11.Dispatching;

namespace Avalonia.X11;

[PrivateApi]
public static class ExternalDispatcherBridge
{
    public static ManagedDispatcherImpl.IManagedDispatcherInputProvider GetPendingInputProvider()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (Dispatcher.UIThread.PlatformImpl is not GlibDispatcherImpl)
            throw new PlatformNotSupportedException("The external input provider requires the pinned GLib X11 backend.");
        var platform = AvaloniaLocator.Current.GetRequiredService<AvaloniaX11Platform>();
        return new InputProvider(platform);
    }

    private sealed class InputProvider : ManagedDispatcherImpl.IManagedDispatcherInputProvider
    {
        private readonly X11EventDispatcher _native;
        private readonly ManualRawEventGrouperDispatchQueueDispatcherInputProvider _grouped;

        internal InputProvider(AvaloniaX11Platform platform)
        {
            _native = platform.DispatcherImpl.EventDispatcher;
            _grouped = new ManualRawEventGrouperDispatchQueueDispatcherInputProvider(platform.EventGrouperDispatchQueue);
        }

        public bool HasInput => _native.IsPending || _grouped.HasInput;

        public void DispatchNextInputEvent()
        {
            if (_native.IsPending)
                _native.DispatchX11Events(CancellationToken.None);
            if (_grouped.HasInput)
                _grouped.DispatchNextInputEvent();
        }
    }
}
