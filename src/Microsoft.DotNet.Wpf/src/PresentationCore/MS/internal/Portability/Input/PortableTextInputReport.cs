// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Windows;
using System.Windows.Input;

namespace MS.Internal.Portability.Input
{
    internal sealed class PortableTextInputReport : InputReport
    {
        internal PortableTextInputReport(PresentationSource source, InputMode mode, int timestamp, string text)
            : base(source, InputType.Text, mode, timestamp)
        {
            ArgumentNullException.ThrowIfNull(text);
            for (int index = 0; index < text.Length; index++)
            {
                if (!char.IsSurrogate(text[index])) continue;
                if (!char.IsHighSurrogate(text[index]) || index + 1 == text.Length || !char.IsLowSurrogate(text[index + 1]))
                    throw new ArgumentException("Native committed text must contain well-formed UTF-16.", nameof(text));
                index++;
            }
            Text = text;
        }

        internal string Text { get; }
    }
}
#endif
