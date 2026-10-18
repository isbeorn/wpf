extern alias WpfFork;

using System.Buffers.Binary;
using System.Text;
using Avalonia;
using Avalonia.Platform;
using Avalonia.X11;
using WpfFork::MS.Internal.Portability.Settings;
using WpfFork::MS.Internal.Portability.Windowing;

namespace Wpf.Portable.NativeHost;

public static class NativeWindowHost
{
    public static void InitializeForCurrentThread(AppBuilder builder)
    {
        NativeDispatcherHost.InitializeForCurrentThread(builder, softwareFramebuffer: true);
        var (data, _) = ExternalWindowBridge.ReadPointerSettingsProperty();
        var preferences = XSettingsPointerPreferences.Parse(data);
        PortableSystemSettingsProvider.Register(PortableSystemSettings.CreateInitialLightProfile(pointerSettings:
            new PortablePointerSettings(preferences.Distance, preferences.Distance, preferences.Time)));
        PortableWindowPlatform.RegisterFactory(new NativeWindowFactory(
            AvaloniaLocator.Current.GetRequiredService<IWindowingPlatform>()));
    }
}

internal readonly record struct XSettingsPointerPreferences(int Distance, int Time)
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
            string name = Encoding.ASCII.GetString(data.Slice(offset, length));
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
        if (distance is not > 0 || time is not > 0)
            throw new PlatformNotSupportedException("Native XSETTINGS must supply valid Net/DoubleClickDistance and Net/DoubleClickTime integer preferences.");
        if (offset != data.Length) throw new InvalidDataException("The native XSETTINGS property contains trailing or truncated bytes.");
        return new(distance.Value, time.Value);
    }

    private static void Assign(ref int? destination, int? value, string name)
    {
        if (destination.HasValue || !value.HasValue)
            throw new InvalidDataException("The native XSETTINGS preference '" + name + "' is duplicated or has an invalid type.");
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
