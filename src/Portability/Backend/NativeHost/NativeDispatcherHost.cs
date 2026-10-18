extern alias WpfFork;

using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Threading;
using Avalonia.X11;
using DispatcherPlatform = WpfFork::MS.Internal.Portability.DispatcherPlatform;
using WpfDispatcher = WpfFork::System.Windows.Threading.Dispatcher;

namespace Wpf.Portable.NativeHost;

public static class NativeDispatcherHost
{
    public static void InitializeForCurrentThread(AppBuilder builder)
        => InitializeForCurrentThread(builder, softwareFramebuffer: false);

    internal static void InitializeForCurrentThread(AppBuilder builder, bool softwareFramebuffer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("This pinned native dispatcher adapter supports Linux X11 with GLib only.");
        if (WpfDispatcher.FromThread(Thread.CurrentThread) is not null)
            throw new InvalidOperationException("Initialize the native host before creating this thread's WPF Dispatcher.");

        var exceptions = new NativeCallbackExceptions();
        var options = new X11PlatformOptions
        {
            UseGLibMainLoop = true,
            ExternalGLibMainLoopExceptionLogger = exceptions.Capture
        };
        if (softwareFramebuffer) options.RenderingMode = new[] { X11RenderingMode.Software };
        builder.UsePlatformDetect().With(options).SetupWithoutStarting();
        Dispatcher.UIThread.VerifyAccess();
        var implementation = Dispatcher.UIThread.PlatformImpl;
        if (implementation is not IControlledDispatcherImpl controlled ||
            implementation.GetType().FullName != "Avalonia.X11.Dispatching.GlibDispatcherImpl" ||
            implementation.GetType().Assembly.GetName().Version != new Version(12, 1, 4, 0))
            throw new PlatformNotSupportedException("Expected the pinned Avalonia.X11 12.1.4 GLib dispatcher implementation.");

        exceptions.ThrowPending();
        var input = ExternalDispatcherBridge.GetPendingInputProvider();
        DispatcherPlatform.RegisterPumpFactory(() => new GlibDispatcherPump(controlled, input, exceptions));
    }
}

internal sealed class NativeCallbackExceptions
{
    private Exception? _pending;

    internal void Capture(Exception exception) => Interlocked.CompareExchange(ref _pending, exception, null);

    internal void ThrowPending()
    {
        var exception = Interlocked.Exchange(ref _pending, null);
        if (exception is not null)
            ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
