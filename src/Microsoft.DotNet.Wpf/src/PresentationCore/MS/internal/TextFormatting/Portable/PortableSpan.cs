// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
namespace MS.Internal.TextFormatting.Portable
{
    // Managed form of DirectWriteForwarder/CPP/DWriteWrapper/ItemSpan.{h,cpp}.
    internal sealed class PortableSpan
    {
        internal PortableSpan(object element, int length)
        {
            this.element = element;
            this.length = length;
        }

        internal object element;
        internal int length;
    }
}
#endif
