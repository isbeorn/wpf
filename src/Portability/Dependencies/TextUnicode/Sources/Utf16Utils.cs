// Generated adaptation: WPF namespaces, internal top-level types and qualified utility references. Original notices follow.
#nullable enable annotations
using System;

namespace MS.Internal.Portability.Text.Unicode;

internal class Utf16Utils
{
    public static int CharacterOffsetToStringOffset(string s, int off, bool throwOnOutOfRange)
    {
        if (off == 0)
            return 0;
        var symbolOffset = 0;
        for (var c = 0; c < s.Length; c++)
        {
            if (symbolOffset == off)
                return c;
            
            if (!char.IsSurrogatePair(s, c))
                symbolOffset++;
        }

        if (throwOnOutOfRange)
            throw new IndexOutOfRangeException();
        return s.Length;
    }
}