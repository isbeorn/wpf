using System.Runtime.InteropServices;

namespace Wpf.Portable.NativeHost.Linux;

// Public XInput2 ABI. Device and crossing payloads are borrowed only during the event handler.
internal static class Xi2Native
{
    internal const int AllMasterDevices = 1, ButtonPress = 4, ButtonRelease = 5, Motion = 6, Enter = 7, Leave = 8;
    [DllImport("libXi.so.6")] internal static extern int XIQueryVersion(nint display, ref int major, ref int minor);
    [DllImport("libXi.so.6")] internal static extern int XISelectEvents(nint display, nuint window, ref XIEventMask masks, int count);
    [DllImport("libXi.so.6")] internal static extern int XIGetClientPointer(nint display, nuint window, out int device);
    [DllImport("libXi.so.6")] internal static extern int XIGrabDevice(nint display, int device, nuint window, nuint time, nuint cursor, int grabMode, int pairedDeviceMode, int ownerEvents, ref XIEventMask mask);
    [DllImport("libXi.so.6")] internal static extern int XIUngrabDevice(nint display, int device, nuint time);
    [DllImport("libXi.so.6")] internal static extern int XIQueryPointer(nint display, int device, nuint window, out nuint root, out nuint child, out double rootX, out double rootY, out double clientX, out double clientY, out XIButtonState buttons, out XIModifierState modifiers, out XIModifierState group);
}
[StructLayout(LayoutKind.Sequential)]
internal struct XIEventMask { internal int DeviceId, MaskLength; internal nint Mask; }
[StructLayout(LayoutKind.Sequential)]
internal struct XIButtonState { internal int MaskLength; internal nint Mask; }
[StructLayout(LayoutKind.Sequential)]
internal struct XIValuatorState { internal int MaskLength; internal nint Mask, Values; }
[StructLayout(LayoutKind.Sequential)]
internal struct XIModifierState { internal int Base, Latched, Locked, Effective; }
[StructLayout(LayoutKind.Sequential)]
internal struct XIEvent
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal int Extension, EventType; internal nuint Time;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XIDeviceEvent
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal int Extension, EventType; internal nuint Time; internal int DeviceId, SourceId, Detail;
    internal nuint Root, Event, Child; internal double RootX, RootY, EventX, EventY;
    internal int Flags; internal XIButtonState Buttons; internal XIValuatorState Valuators;
    internal XIModifierState Modifiers, Group;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XIEnterLeaveEvent
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal int Extension, EventType; internal nuint Time; internal int DeviceId, SourceId, Detail;
    internal nuint Root, Event, Child; internal double RootX, RootY, EventX, EventY;
    internal int Mode, Focus, SameScreen; internal XIButtonState Buttons; internal XIModifierState Modifiers, Group;
}
