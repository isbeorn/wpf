using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using static Wpf.Portable.NativeHost.Linux.X11WindowNative;

namespace Wpf.Portable.NativeHost.Linux;

internal sealed class X11Screens : IDisposable
{
    private readonly X11Connection _connection;
    private readonly int _randrEvent, _fixesEvent;
    private nuint _settingsOwner;
    private bool _disposed;
    public event Action? Changed;
    public X11Screens(X11Connection connection)
    {
        _connection = connection;
        connection.VerifyOwner();
        if (XRRQueryExtension(connection.Display, out _randrEvent, out _) == 0)
            throw new PlatformNotSupportedException("Observed X11 screens require RandR.");
        if (XFixesQueryExtension(connection.Display, out _fixesEvent, out _) == 0)
            throw new PlatformNotSupportedException("Observed X11 settings ownership requires XFixes.");
        connection.RegisterEvent(_randrEvent, OnMonitorEvent);
        connection.RegisterEvent(_randrEvent + 1, OnMonitorEvent);
        connection.RegisterEvent(_fixesEvent, OnSettingsOwnerEvent);
        connection.RegisterWindow(connection.RootWindow, OnRootEvent);
        SelectPropertyEvents(connection.RootWindow);
        XRRSelectInput(connection.Display, connection.RootWindow, 1 | 2 | 4 | 8 | 64);
        XFixesSelectSelectionInput(connection.Display, connection.RootWindow,
            connection.GetAtom("_XSETTINGS_S" + XDefaultScreen(connection.Display)), 1 | 2 | 4);
        ObserveSettingsOwner();
        connection.Flush();
    }
    private bool SelectPropertyEvents(nuint window)
    {
        WindowAttributes attributes = default;
        int status = 0;
        if (!_connection.TryForeignRequest(() => status = XGetWindowAttributes(_connection.Display, window, out attributes)) || status == 0) return false;
        return _connection.TryForeignRequest(() => XSelectInput(_connection.Display, window, attributes.YourEventMask | (1 << 22)));
    }
    private void ObserveSettingsOwner()
    {
        nuint owner = XGetSelectionOwner(_connection.Display, _connection.GetAtom("_XSETTINGS_S" + XDefaultScreen(_connection.Display)));
        if (_settingsOwner == owner) return;
        if (_settingsOwner != 0) _connection.UnregisterWindow(_settingsOwner, OnSettingsProperty);
        _settingsOwner = owner;
        if (owner == 0) return;
        if (SelectPropertyEvents(owner)) _connection.RegisterWindow(owner, OnSettingsProperty);
    }
    private void OnSettingsProperty(XEvent value)
    {
        if (!_disposed && value.Type == 28 && value.Property.Atom == _connection.GetAtom("_XSETTINGS_SETTINGS")) Changed?.Invoke();
    }
    private void OnSettingsOwnerEvent(XEvent value)
    {
        if (_disposed) return;
        ObserveSettingsOwner();
        Changed?.Invoke();
    }
    private void OnRootEvent(XEvent value)
    {
        if (_disposed || value.Type != 28) return;
        nuint atom = value.Property.Atom;
        if (atom == _connection.GetAtom("RESOURCE_MANAGER") || atom == _connection.GetAtom("_NET_WORKAREA") ||
            atom == _connection.GetAtom("_NET_CURRENT_DESKTOP")) Changed?.Invoke();
    }
    private void OnMonitorEvent(XEvent value)
    {
        if (_disposed) return;
        if (value.Type == _randrEvent) XRRUpdateConfiguration(ref value);
        Changed?.Invoke();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _connection.VerifyOwner();
        _disposed = true;
        _connection.UnregisterEvent(_randrEvent, OnMonitorEvent);
        _connection.UnregisterEvent(_randrEvent + 1, OnMonitorEvent);
        _connection.UnregisterEvent(_fixesEvent, OnSettingsOwnerEvent);
        _connection.UnregisterWindow(_connection.RootWindow, OnRootEvent);
        if (_settingsOwner != 0) _connection.UnregisterWindow(_settingsOwner, OnSettingsProperty);
    }
    public IReadOnlyList<X11Screen> ReadAll()
    {
        Verify();
        if (XRRQueryVersion(_connection.Display, out int major, out int minor) == 0 || major < 1 || (major == 1 && minor < 5))
            throw new PlatformNotSupportedException("Observed X11 monitor enumeration requires RandR 1.5.");
        nint monitors = XRRGetMonitors(_connection.Display, _connection.RootWindow, 1, out int count);
        try
        {
            if (monitors == 0 || count is < 1 or > 256) throw new PlatformNotSupportedException("RandR exposes no bounded active monitor inventory.");
            var result = new List<X11Screen>(count);
            var work = ReadDesktopWorkArea();
            double dpi = ReadDpi();
            int size = Marshal.SizeOf<MonitorInfo>();
            for (int index = 0; index < count; index++)
            {
                var monitor = Marshal.PtrToStructure<MonitorInfo>(monitors + checked(index * size));
                if (monitor.Width <= 0 || monitor.Height <= 0 || monitor.OutputCount is < 0 or > 4096)
                    throw new InvalidOperationException("RandR returned invalid monitor geometry.");
                var bounds = new X11PixelRect(monitor.X, monitor.Y, monitor.Width, monitor.Height);
                string name = "";
                nint atomName = XGetAtomName(_connection.Display, monitor.Name);
                try { if (atomName != 0) name = Marshal.PtrToStringUTF8(atomName) ?? ""; }
                finally { if (atomName != 0) XFree(atomName); }
                result.Add(new(monitor.Name, name, monitor.Primary != 0, bounds, work is { } area ? Intersect(bounds, area) : bounds, dpi));
            }
            return result.AsReadOnly();
        }
        finally { if (monitors != 0) XRRFreeMonitors(monitors); }
    }
    public X11Screen? ReadPrimary()
    {
        var screens = ReadAll();
        var primary = screens.Where(screen => screen.IsPrimary).ToArray();
        return primary.Length == 1 ? primary[0] : screens.Count == 1 ? screens[0] : null;
    }
    public X11Screen ReadForRect(X11PixelRect rectangle)
    {
        var screens = ReadAll();
        return screens.OrderByDescending(screen => Area(Intersect(screen.Bounds, rectangle)))
            .ThenBy(screen => Distance(screen.Bounds, rectangle.X, rectangle.Y)).First();
    }
    public double ReadDpi() { Verify(); return ReadDpi(_connection); }
    internal static double ReadDpi(X11Connection connection)
    {
        connection.VerifyOwner();
        nuint selection = connection.GetAtom("_XSETTINGS_S" + XDefaultScreen(connection.Display));
        nuint owner = XGetSelectionOwner(connection.Display, selection);
        if (owner != 0)
        {
            nuint property = connection.GetAtom("_XSETTINGS_SETTINGS");
            var data = ReadByteProperty(connection, owner, "_XSETTINGS_SETTINGS", property, 65536);
            if (data.Length > 0 && ReadSettingsDpi(data) is { } settingsDpi) return ValidateDpi(settingsDpi);
        }
        var resources = Encoding.UTF8.GetString(ReadByteProperty(connection, connection.RootWindow, "RESOURCE_MANAGER", 31, 1024 * 1024));
        double? configured = null;
        foreach (string line in resources.Split('\n'))
        {
            int separator = line.IndexOf(':');
            if (separator < 0 || line[..separator].Trim() != "Xft.dpi") continue;
            if (configured.HasValue || !double.TryParse(line[(separator + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double dpi))
                throw new InvalidOperationException("The observed Xft.dpi resource is malformed or repeated.");
            configured = ValidateDpi(dpi);
        }
        if (configured.HasValue) return configured.Value;
        // Physical monitor density does not define the desktop's logical scale.
        // When no desktop preference is published, retain WPF's declared 96-DPI fallback.
        return 96;
    }
    public (byte[] Data, string Origin) ReadPointerSettingsProperty()
    {
        Verify();
        string selection = "_XSETTINGS_S" + XDefaultScreen(_connection.Display);
        nuint owner = XGetSelectionOwner(_connection.Display, _connection.GetAtom(selection));
        if (owner == 0) throw new PlatformNotSupportedException("No XSETTINGS owner supplies pointer double-click preferences.");
        nuint property = _connection.GetAtom("_XSETTINGS_SETTINGS");
        var data = ReadByteProperty(_connection, owner, "_XSETTINGS_SETTINGS", property, 65536);
        if (data.Length == 0) throw new InvalidOperationException("The observed XSETTINGS preference property is absent or empty.");
        return (data, selection + "/owner=0x" + owner.ToString("x") + "/_XSETTINGS_SETTINGS");
    }
    private X11PixelRect? ReadDesktopWorkArea()
    {
        var work = ReadLongProperty(_connection, _connection.RootWindow, "_NET_WORKAREA", 6, 4096);
        if (work.Length == 0) return null;
        var desktops = ReadLongProperty(_connection, _connection.RootWindow, "_NET_CURRENT_DESKTOP", 6, 1);
        if (work.Length % 4 != 0 || desktops.Length > 1 || (desktops.Length == 1 && desktops[0] >= (nuint)(work.Length / 4)))
            throw new InvalidOperationException("The EWMH desktop work area is malformed.");
        int offset = desktops.Length == 1 ? checked((int)desktops[0] * 4) : 0;
        if (desktops.Length == 0)
        {
            for (int index = 4; index < work.Length; index++)
                if (work[index] != work[index % 4])
                    throw new InvalidOperationException("The EWMH desktop work area is ambiguous without a current desktop.");
        }
        if (work.Skip(offset).Take(4).Any(value => value > uint.MaxValue) || work[offset + 2] is 0 or > int.MaxValue || work[offset + 3] is 0 or > int.MaxValue)
            throw new InvalidOperationException("The EWMH desktop work area is outside supported pixel bounds.");
        return new(unchecked((int)(uint)work[offset]), unchecked((int)(uint)work[offset + 1]), (int)work[offset + 2], (int)work[offset + 3]);
    }
    private static double? ReadSettingsDpi(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || data[0] is not (0 or 1)) throw new InvalidOperationException("The XSETTINGS header is malformed.");
        bool little = data[0] == 0;
        uint count = UInt32(data[8..], little);
        if (count > 4096) throw new InvalidOperationException("The XSETTINGS preference count exceeds its bound.");
        int offset = 12;
        double? dpi = null;
        for (uint entry = 0; entry < count; entry++)
        {
            Require(data, offset, 4);
            byte type = data[offset];
            int length = little ? BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]) : BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
            offset += 4;
            Require(data, offset, length);
            string name = Encoding.UTF8.GetString(data.Slice(offset, length));
            offset = checked((offset + length + 3) & ~3);
            Require(data, offset, 4);
            offset += 4;
            switch (type)
            {
                case 0:
                    Require(data, offset, 4);
                    if (name == "Xft/DPI")
                    {
                        if (dpi.HasValue) throw new InvalidOperationException("The XSETTINGS DPI preference is repeated.");
                        dpi = unchecked((int)UInt32(data[offset..], little)) / 1024.0;
                    }
                    offset += 4;
                    break;
                case 1:
                    Require(data, offset, 4);
                    uint stringLength = UInt32(data[offset..], little);
                    offset += 4;
                    if (stringLength > 65536) throw new InvalidOperationException("An XSETTINGS string exceeds its bound.");
                    Require(data, offset, (int)stringLength);
                    offset = checked((offset + (int)stringLength + 3) & ~3);
                    break;
                case 2: Require(data, offset, 8); offset += 8; break;
                default: throw new InvalidOperationException("An XSETTINGS preference has an unsupported wire type.");
            }
        }
        if (offset != data.Length) throw new InvalidOperationException("The XSETTINGS property has trailing data.");
        return dpi;
    }
    private void Verify() { _connection.VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this); }
    private static void Require(ReadOnlySpan<byte> data, int offset, int count)
    { if (offset < 0 || count < 0 || offset > data.Length - count) throw new InvalidOperationException("The XSETTINGS preference data is truncated."); }
    private static uint UInt32(ReadOnlySpan<byte> data, bool little) => little ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);
    private static double ValidateDpi(double dpi)
    { if (!double.IsFinite(dpi) || dpi <= 0 || dpi > 1536) throw new InvalidOperationException("The observed X11 DPI is outside supported positive bounds."); return dpi; }
    private static long Area(X11PixelRect rectangle) => (long)rectangle.Width * rectangle.Height;
    private static double Distance(X11PixelRect rectangle, int x, int y)
    { double dx = x - Math.Clamp((long)x, rectangle.X, rectangle.Right), dy = y - Math.Clamp((long)y, rectangle.Y, rectangle.Bottom); return dx * dx + dy * dy; }
    private static X11PixelRect Intersect(X11PixelRect first, X11PixelRect second)
    {
        long x = Math.Max(first.X, second.X), y = Math.Max(first.Y, second.Y);
        long right = Math.Min(first.Right, second.Right), bottom = Math.Min(first.Bottom, second.Bottom);
        return new(checked((int)x), checked((int)y), checked((int)Math.Max(0, right - x)), checked((int)Math.Max(0, bottom - y)));
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    {
        public nuint Name;
        public int Primary, Automatic, OutputCount, X, Y, Width, Height, MillimeterWidth, MillimeterHeight;
        public nint Outputs;
    }
    [DllImport(Xlib)] private static extern int XSelectInput(nint display, nuint window, nint mask);
    [DllImport("libXfixes.so.3")] private static extern void XFixesSelectSelectionInput(nint display, nuint window, nuint selection, nuint mask);
    [DllImport("libXrandr.so.2")] private static extern int XRRQueryExtension(nint display, out int eventBase, out int errorBase);
    [DllImport("libXrandr.so.2")] private static extern void XRRSelectInput(nint display, nuint window, int mask);
    [DllImport("libXrandr.so.2")] private static extern int XRRUpdateConfiguration(ref XEvent value);
    [DllImport("libXrandr.so.2")] private static extern int XRRQueryVersion(nint display, out int major, out int minor);
    [DllImport("libXrandr.so.2")] private static extern nint XRRGetMonitors(nint display, nuint window, int activeOnly, out int count);
    [DllImport("libXrandr.so.2")] private static extern void XRRFreeMonitors(nint monitors);
    [DllImport(Xlib)] private static extern nint XGetAtomName(nint display, nuint atom);
}
