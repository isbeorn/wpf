namespace Wpf.Portable.NativeHost.Linux;

// Every instance owns one nonfloating GVariant reference. Containers borrow their children.
internal sealed class GioVariant : IDisposable
{
    private nint _handle;
    internal GioVariant(nint owned) => _handle = owned != 0 ? owned : throw new InvalidDataException("A native GVariant was null.");
    internal nint Handle => _handle != 0 ? _handle : throw new ObjectDisposedException(nameof(GioVariant));
    internal string Signature => GioNative.Utf8(GioNative.g_variant_get_type_string(Handle));
    internal int Count => checked((int)GioNative.g_variant_n_children(Handle));
    internal GioVariant Ref() => new(GioNative.g_variant_ref(Handle));
    internal GioVariant Require(string signature) { if (Signature != signature) throw new InvalidDataException($"Expected GVariant '{signature}', received '{Signature}'."); return this; }
    internal GioVariant Child(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= Count) throw new InvalidDataException("A GVariant child is missing.");
        return new(GioNative.g_variant_get_child_value(Handle, (nuint)index));
    }
    internal GioVariant Unbox() => new(GioNative.g_variant_get_variant(Require("v").Handle));
    internal GioVariant? Lookup(string key) { Require("a{sv}"); var value = GioNative.g_variant_lookup_value(Handle, key, 0); return value == 0 ? null : new(value); }
    internal string ReadString()
    {
        if (Signature is not ("s" or "o" or "g")) throw new InvalidDataException("Expected a GVariant string.");
        return GioNative.Utf8(GioNative.g_variant_get_string(Handle, out _));
    }
    internal uint ReadUInt32() => GioNative.g_variant_get_uint32(Require("u").Handle);
    internal int ReadInt32() => GioNative.g_variant_get_int32(Require("i").Handle);
    internal bool ReadBool() => GioNative.g_variant_get_boolean(Require("b").Handle) != 0;
    internal double ReadDouble() => GioNative.g_variant_get_double(Require("d").Handle);
    private static GioVariant Sink(nint floating) => new(GioNative.g_variant_ref_sink(floating));
    internal static GioVariant String(string value) { ValidateString(value); return Sink(GioNative.g_variant_new_string(value)); }
    internal static GioVariant ObjectPath(string value)
    {
        ValidateString(value);
        if (value != "/" && (!value.StartsWith('/') || value.EndsWith('/') || value.Split('/').Skip(1).Any(s => s.Length == 0 || s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))))
            throw new ArgumentException("Invalid D-Bus object path.", nameof(value));
        return Sink(GioNative.g_variant_new_object_path(value));
    }
    internal static GioVariant Bool(bool value) => Sink(GioNative.g_variant_new_boolean(value ? 1 : 0));
    internal static GioVariant UInt32(uint value) => Sink(GioNative.g_variant_new_uint32(value));
    internal static GioVariant Int32(int value) => Sink(GioNative.g_variant_new_int32(value));
    internal static GioVariant Int16(short value) => Sink(GioNative.g_variant_new_int16(value));
    internal static GioVariant DictEntry(GioVariant key, GioVariant value) => Sink(GioNative.g_variant_new_dict_entry(key.Handle, value.Handle));
    internal static GioVariant Double(double value) => Sink(GioNative.g_variant_new_double(value));
    internal static GioVariant Byte(byte value) => Sink(GioNative.g_variant_new_byte(value));
    internal static GioVariant Box(GioVariant value) => Sink(GioNative.g_variant_new_variant(value.Handle));
    internal static GioVariant Tuple(params GioVariant[] children) => Sink(GioNative.g_variant_new_tuple(children.Select(c => c.Handle).ToArray(), (nuint)children.Length));
    internal static GioVariant Array(string childSignature, params GioVariant[] children)
    {
        foreach (var child in children) child.Require(childSignature);
        nint type = GioNative.g_variant_type_new(childSignature);
        try { return Sink(GioNative.g_variant_new_array(type, children.Select(c => c.Handle).ToArray(), (nuint)children.Length)); }
        finally { GioNative.g_variant_type_free(type); }
    }
    internal static GioVariant Bytes(byte[] bytes)
    {
        var children = bytes.Select(Byte).ToArray();
        try { return Array("y", children); }
        finally { foreach (var child in children) child.Dispose(); }
    }
    internal static GioVariant Dictionary(IReadOnlyDictionary<string, GioVariant> values)
    {
        var entries = new List<GioVariant>();
        try
        {
            foreach (var pair in values)
            {
                using var key = String(pair.Key); using var boxed = Box(pair.Value);
                entries.Add(Sink(GioNative.g_variant_new_dict_entry(key.Handle, boxed.Handle)));
            }
            return Array("{sv}", entries.ToArray());
        }
        finally { foreach (var entry in entries) entry.Dispose(); }
    }
    private static void ValidateString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0')) throw new ArgumentException("D-Bus strings cannot contain NUL.", nameof(value));
        // Reject lone surrogates instead of silently replacing them during UTF-8 marshalling.
        _ = new System.Text.UTF8Encoding(false, true).GetByteCount(value);
    }
    public void Dispose() { var value = Interlocked.Exchange(ref _handle, 0); if (value != 0) GioNative.g_variant_unref(value); }
}
