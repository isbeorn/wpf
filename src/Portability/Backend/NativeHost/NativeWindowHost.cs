extern alias WpfFork;

using System.Buffers.Binary;
using System.Text;
using Wpf.Portable.NativeHost.Linux;
using Wpf.Portable.NativeHost.Linux.Accessibility;
using WpfFork::MS.Internal.Portability.Settings;
using WpfFork::MS.Internal.Portability.Windowing;
using WpfRect = WpfFork::System.Windows.Rect;

namespace Wpf.Portable.NativeHost;

public static class NativeWindowHost
{
    private const int DeclaredDoubleClickDistancePixels = 5;
    private static readonly object s_settingsSync = new();
    private static bool s_settingsRegistered;
    internal static NativePointerPreferences? PointerPreferences { get; private set; }

    public static void InitializeForCurrentThread()
    {
        NativeDispatcherHost.InitializeForCurrentThread();
        var session = NativeDispatcherHost.Session;
        try
        {
            var screens = new X11Screens(session.Connection);
            session.Own(screens);
            var (data, origin) = screens.ReadPointerSettingsProperty();
            var preferences = ResolvePointerPreferences(XSettingsPointerPreferences.Parse(data), origin);
            var observedScreens = screens.ReadAll();
            var markedPrimary = observedScreens.Where(screen => screen.IsPrimary).ToArray();
            X11Screen? selectedPrimary = markedPrimary.Length == 1 ? markedPrimary[0] : observedScreens.Count == 1 ? observedScreens[0] : null;
            PortablePrimaryScreenMetrics? primaryMetrics = null;
            if (selectedPrimary is { } primary)
            {
                double scale = primary.Dpi / 96d;
                if (primary.Bounds.Width <= 0 || primary.Bounds.Height <= 0 || !double.IsFinite(scale) || scale <= 0)
                    throw new PlatformNotSupportedException("The native X11 screen service reported invalid primary-screen bounds or scaling.");
                var workArea = primary.WorkingArea;
                primaryMetrics = new PortablePrimaryScreenMetrics(primary.Bounds.Width / scale, primary.Bounds.Height / scale,
                    new WpfFork::System.Windows.Int32Rect(workArea.X, workArea.Y, workArea.Width, workArea.Height), scale);
            }

            lock (s_settingsSync)
            {
                if (!s_settingsRegistered)
                {
                    PortableSystemSettingsProvider.Register(PortableSystemSettings.CreateInitialLightProfile(pointerSettings:
                        new PortablePointerSettings(preferences.Distance, preferences.Distance, preferences.Time), primaryScreenMetrics: primaryMetrics,
                        virtualScreenBoundsDip: GetVirtualScreenBounds(observedScreens)));
                    PointerPreferences = preferences;
                    s_settingsRegistered = true;
                }
            }
            var dialogs = new PortalFileDialogs(session.Connection);
            session.Own(dialogs);
            var automation = new AtSpiServer(session.Connection);
            session.Own(automation);
            var clipboard = new X11Clipboard(session.Connection);
            session.Own(clipboard);
            var factory = new NativeWindowFactory(session.Connection, screens, dialogs, automation);
            session.Own(factory);
            PortableWindowPlatform.RegisterFactory(factory);
            PortableMessageBoxPlatform.RegisterExecutor(NativeMessageBoxHost.Instance);
            WpfFork::MS.Internal.Portability.Clipboard.PortableClipboard.Register(clipboard);
            WpfFork::MS.Internal.Portability.PortableSplashPlatform.Register(new NativeSplashFactory(session.Connection, screens));
        }
        catch (Exception startupFailure)
        {
            try { WpfFork::System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            catch (Exception cleanupFailure) { throw new AggregateException("Native host initialization and cleanup failed.", startupFailure, cleanupFailure); }
            throw;
        }
    }

    private static WpfRect GetVirtualScreenBounds(IReadOnlyList<X11Screen> screens)
    {
        WpfRect bounds = WpfRect.Empty;
        foreach (var screen in screens)
            bounds.Union(new WpfRect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height));
        double scale = screens[0].Dpi / 96d;
        return new WpfRect(bounds.X / scale, bounds.Y / scale, bounds.Width / scale, bounds.Height / scale);
    }

    private static NativePointerPreferences ResolvePointerPreferences(XSettingsPointerPreferences parsed, string origin)
    {
        if (parsed.Time is null)
            throw new PlatformNotSupportedException("Native XSETTINGS must supply Net/DoubleClickTime as a positive integer preference.");
        return new(parsed.Distance ?? DeclaredDoubleClickDistancePixels, parsed.Time.Value, origin,
            parsed.Distance.HasValue ? PointerPreferenceSource.XSettings : PointerPreferenceSource.DeclaredFallback,
            PointerPreferenceSource.XSettings);
    }
}

internal enum PointerPreferenceSource { XSettings, DeclaredFallback }

internal readonly record struct NativePointerPreferences(int Distance, int Time, string Origin,
    PointerPreferenceSource DistanceSource, PointerPreferenceSource TimeSource);

internal readonly record struct XSettingsPointerPreferences(int? Distance, int? Time)
{
    internal static XSettingsPointerPreferences Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || data.Length > 65536 || (data[0] != 0 && data[0] != 1))
            throw new InvalidDataException("The native XSETTINGS header or byte order is invalid.");
        bool little = data[0] == 0;
        int offset = 4;
        Read32(data, ref offset, little);
        uint count = Read32(data, ref offset, little);
        if (count > 4096) throw new InvalidDataException("The native XSETTINGS entry count is too large.");
        int? distance = null, time = null;
        for (uint index = 0; index < count; index++)
        {
            Need(data, offset, 4);
            byte type = data[offset];
            offset += 2;
            int length = Read16(data, ref offset, little);
            Need(data, offset, length);
            ReadOnlySpan<byte> nameBytes = data.Slice(offset, length);
            ValidateName(nameBytes);
            string name = Encoding.ASCII.GetString(nameBytes);
            offset = Align(checked(offset + length));
            Read32(data, ref offset, little);
            int? value = null;
            switch (type)
            {
                case 0: value = unchecked((int)Read32(data, ref offset, little)); break;
                case 1:
                    uint bytes = Read32(data, ref offset, little);
                    if (bytes > 65536) throw new InvalidDataException("The native XSETTINGS string is too large.");
                    Need(data, offset, (int)bytes);
                    offset = Align(checked(offset + (int)bytes));
                    break;
                case 2: Need(data, offset, 8); offset += 8; break;
                default: throw new InvalidDataException("The native XSETTINGS entry has an unsupported type.");
            }
            if (name == "Net/DoubleClickDistance") Assign(ref distance, value, name);
            if (name == "Net/DoubleClickTime") Assign(ref time, value, name);
        }
        if (offset != data.Length) throw new InvalidDataException("The native XSETTINGS property contains trailing or truncated bytes.");
        return new(distance, time);
    }

    private static void ValidateName(ReadOnlySpan<byte> name)
    {
        bool segmentStart = true;
        foreach (byte character in name)
        {
            if (character == (byte)'/' && !segmentStart) segmentStart = true;
            else if (character is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or (byte)'_' ||
                (!segmentStart && character is >= (byte)'0' and <= (byte)'9')) segmentStart = false;
            else throw new InvalidDataException("The native XSETTINGS entry name is invalid.");
        }
        if (segmentStart) throw new InvalidDataException("The native XSETTINGS entry name is empty or has an empty segment.");
    }

    private static void Assign(ref int? destination, int? value, string name)
    {
        if (destination.HasValue || !value.HasValue)
            throw new InvalidDataException("The native XSETTINGS preference '" + name + "' is duplicated or has an invalid type.");
        if (value <= 0)
            throw new InvalidDataException("The native XSETTINGS preference '" + name + "' must be a positive integer.");
        destination = value;
    }

    private static int Align(int offset) => checked((offset + 3) & ~3);
    private static void Need(ReadOnlySpan<byte> data, int offset, int bytes)
    {
        if (offset < 0 || bytes < 0 || offset > data.Length - bytes)
            throw new InvalidDataException("The native XSETTINGS property is truncated.");
    }
    private static ushort Read16(ReadOnlySpan<byte> data, ref int offset, bool little)
    {
        Need(data, offset, 2);
        ushort value = little ? BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]) : BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;
        return value;
    }
    private static uint Read32(ReadOnlySpan<byte> data, ref int offset, bool little)
    {
        Need(data, offset, 4);
        uint value = little ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) : BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        offset += 4;
        return value;
    }
}
