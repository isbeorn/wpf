extern alias WpfFork;

using System.Runtime.ExceptionServices;
using WpfFork::MS.Internal.Portability.Windowing;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Threading;

namespace Wpf.Portable.NativeHost;

internal sealed class NativeMessageBoxHost : IPortableMessageBoxExecutor
{
    internal static NativeMessageBoxHost Instance { get; } = new();

    public MessageBoxResult Execute(Func<MessageBoxResult> showDialog)
    {
        MessageBoxResult result = MessageBoxResult.None;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            bool initialized = false;
            try
            {
                NativeWindowHost.InitializeForCurrentThread();
                initialized = true;
                result = showDialog();
            }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
            finally
            {
                if (initialized)
                    try { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
                    catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
            }
        }) { IsBackground = true, Name = "WPF MessageBox" };
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }
}
