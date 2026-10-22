using System.Runtime.InteropServices;

namespace Wpf.Portable.NativeHost.Linux;

// Xlib public ABI, Linux LP64. Derived from Xlib.h declarations, not another UI backend.
[StructLayout(LayoutKind.Explicit, Size = 192)]
internal struct XEvent
{
    [FieldOffset(0)] internal int Type;
    [FieldOffset(0)] internal XAnyEvent Any;
    [FieldOffset(0)] internal XKeyEvent Key;
    [FieldOffset(0)] internal XButtonEvent Button;
    [FieldOffset(0)] internal XMotionEvent Motion;
    [FieldOffset(0)] internal XCrossingEvent Crossing;
    [FieldOffset(0)] internal XFocusChangeEvent Focus;
    [FieldOffset(0)] internal XExposeEvent Expose;
    [FieldOffset(0)] internal XConfigureEvent Configure;
    [FieldOffset(0)] internal XClientMessageEvent ClientMessage;
    [FieldOffset(0)] internal XPropertyEvent Property;
    [FieldOffset(0)] internal XSelectionEvent Selection;
    [FieldOffset(0)] internal XSelectionRequestEvent SelectionRequest;
    [FieldOffset(0)] internal XSelectionClearEvent SelectionClear;
    [FieldOffset(0)] internal XGenericEventCookie GenericCookie;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XAnyEvent { internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display; internal nuint Window; }
[StructLayout(LayoutKind.Sequential)]
internal struct XKeyEvent
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal nuint Window, Root, Subwindow, Time;
    internal int X, Y, XRoot, YRoot; internal uint State, Keycode; internal int SameScreen;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XButtonEvent
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal nuint Window, Root, Subwindow, Time;
    internal int X, Y, XRoot, YRoot; internal uint State, Button; internal int SameScreen;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XMotionEvent
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal nuint Window, Root, Subwindow, Time;
    internal int X, Y, XRoot, YRoot; internal uint State; internal byte IsHint; internal int SameScreen;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XCrossingEvent
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal nuint Window, Root, Subwindow, Time;
    internal int X, Y, XRoot, YRoot, Mode, Detail, SameScreen, Focus; internal uint State;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XFocusChangeEvent { internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display; internal nuint Window; internal int Mode, Detail; }
[StructLayout(LayoutKind.Sequential)]
internal struct XExposeEvent { internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display; internal nuint Window; internal int X, Y, Width, Height, Count; }
[StructLayout(LayoutKind.Sequential)]
internal struct XConfigureEvent
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal nuint Event, Window; internal int X, Y, Width, Height, BorderWidth; internal nuint Above; internal int OverrideRedirect;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XClientMessageEvent
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal nuint Window, MessageType; internal int Format; internal nint Data0, Data1, Data2, Data3, Data4;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XPropertyEvent { internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display; internal nuint Window, Atom, Time; internal int State; }
[StructLayout(LayoutKind.Sequential)]
internal struct XSelectionEvent { internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display; internal nuint Requestor, Selection, Target, Property, Time; }
[StructLayout(LayoutKind.Sequential)]
internal struct XSelectionRequestEvent { internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display; internal nuint Owner, Requestor, Selection, Target, Property, Time; }
[StructLayout(LayoutKind.Sequential)]
internal struct XSelectionClearEvent { internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display; internal nuint Window, Selection, Time; }
[StructLayout(LayoutKind.Sequential)]
internal struct XGenericEventCookie
{
    internal int Type; internal nuint Serial; internal int SendEvent; internal nint Display;
    internal int Extension, EventType; internal uint Cookie; internal nint Data;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XErrorEvent
{
    internal int Type; internal nint Display; internal nuint ResourceId, Serial;
    internal byte ErrorCode, RequestCode, MinorCode;
}
[StructLayout(LayoutKind.Sequential)]
internal struct XWindowAttributes
{
    internal int X, Y, Width, Height, BorderWidth, Depth; internal nint Visual; internal nuint Root;
    internal int Class, BitGravity, WinGravity, BackingStore; internal nuint BackingPlanes, BackingPixel;
    internal int SaveUnder; internal nuint Colormap; internal int MapInstalled, MapState;
    internal nint AllEventMasks, YourEventMask, DoNotPropagateMask; internal int OverrideRedirect; internal nint Screen;
}
