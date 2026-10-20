using System.Reflection;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.ViewModel;

namespace FixtureServices;

public class DisconnectedCameraHandler : DispatchProxy
{
    private readonly CameraInfo info = new() { Connected = false };
    public static ICameraVM Create() => DispatchProxy.Create<ICameraVM, DisconnectedCameraHandler>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.DeclaringType == typeof(IDeviceVM<CameraInfo>) && targetMethod.Name == nameof(IDeviceVM<CameraInfo>.GetDeviceInfo)) return info;
        throw new InvalidOperationException("FIXTURE_CAMERA_FORBIDDEN: " + targetMethod?.Name);
    }
}
