extern alias WpfFork;

using System.Reflection;
using System.Runtime.ExceptionServices;
using Wpf.Portable.NativeHost;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Threading;

internal static class OwnedNativeAccess
{
    internal const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static object Get(object instance, string name) =>
        instance.GetType().GetProperty(name, Members)?.GetValue(instance) ??
        instance.GetType().GetField(name, Members)?.GetValue(instance) ??
        throw new MissingMemberException(instance.GetType().FullName, name);

    internal static object? Call(object instance, string name, params object?[] arguments)
    {
        try { return instance.GetType().GetMethod(name, Members)!.Invoke(instance, arguments); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    internal static object Session => typeof(NativeDispatcherHost).GetProperty("Session", Members)!.GetValue(null)!;
    internal static object Connection => Get(Session, "Connection");
    internal static object Peer(PresentationSource source) => Get(source, "_host");
    internal static object Window(PresentationSource source) => Get(Peer(source), "_window");
    internal static object Input(PresentationSource source) => Get(Peer(source), "_input");
    internal static IntPtr Handle(PresentationSource source) => unchecked((IntPtr)(nint)(nuint)Get(Window(source), "Handle"));
    internal static object Pump => Get(Dispatcher.CurrentDispatcher, "_pump");
    internal static bool HasPendingInput => (bool)Get(Pump, "HasPendingInput");
    internal static void Post(Action action) => Call(Connection, "Post", action);
}
