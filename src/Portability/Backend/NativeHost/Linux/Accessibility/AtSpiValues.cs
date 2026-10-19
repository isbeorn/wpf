namespace Wpf.Portable.NativeHost.Linux.Accessibility;

internal static class AtSpiValues
{
    internal static GioVariant Tuple(params GioVariant[] values)
    {
        try { return GioVariant.Tuple(values); }
        finally { foreach (var value in values) value.Dispose(); }
    }
    internal static GioVariant Array(string signature, IEnumerable<GioVariant> values)
    {
        var children = values.ToArray();
        try { return GioVariant.Array(signature, children); }
        finally { foreach (var value in children) value.Dispose(); }
    }
    internal static GioVariant Strings(IEnumerable<string> values) => Array("s", values.Select(GioVariant.String));
    internal static GioVariant Reference(string bus, string path) => Tuple(GioVariant.String(bus), GioVariant.ObjectPath(path));
    internal static GioVariant Properties(IReadOnlyDictionary<string, GioVariant> values)
    {
        try { return GioVariant.Dictionary(values); }
        finally { foreach (var value in values.Values) value.Dispose(); }
    }
    internal static string String(GioVariant tuple, int index) { using var child = tuple.Child(index); return child.ReadString(); }
    internal static int Int(GioVariant tuple, int index) { using var child = tuple.Child(index); return child.ReadInt32(); }
    internal static uint UInt(GioVariant tuple, int index) { using var child = tuple.Child(index); return child.ReadUInt32(); }
}
