// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Windows;
using System.Windows.Input;

namespace MS.Internal.Portability.Input
{
    internal interface IPortableInputHost
    {
        // checkOnly queries native focus without acquiring it.
        bool AcquireFocus(bool checkOnly);
        bool CaptureMouse();
        void ReleaseMouseCapture();
        bool SetCursor(Cursor cursor);
        bool SetCursorVisibility(bool visible) => false;
        KeyStates GetKeyStates(Key key);
        PortablePointerSnapshot GetPointerState();

        // Both coordinate spaces use physical pixels. Root DIPs are converted
        // separately by the presentation target's TransformTo/FromDevice.
        Point ClientToScreen(Point physicalClientPoint);
        Point ScreenToClient(Point physicalScreenPoint);
        PresentationSource SourceFromScreenPoint(Point physicalScreenPoint);
    }
}
#endif
