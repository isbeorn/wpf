// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Windows;
using System.Windows.Input;

namespace MS.Internal.Portability.Windowing
{
    internal interface IPortablePresentationHost
    {
        Size ClientSize { get; }
        DpiScale DpiScale { get; }
        event Action MetricsChanged;
        event Action Exposed;
        event Action Closed;
        event Action<InputReport> RawInput;
        void Present(ReadOnlySpan<byte> premultipliedBgra, int pixelWidth, int pixelHeight, int stride);
    }
}
#endif
