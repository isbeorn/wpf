// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
namespace MS.Internal.Portability.Settings
{
    // Base role values retain SystemColors' existing native-index mapping.
    internal enum PortableSystemColor
    {
        ScrollBar = 0,
        Desktop = 1,
        ActiveCaption = 2,
        InactiveCaption = 3,
        Menu = 4,
        Window = 5,
        WindowFrame = 6,
        MenuText = 7,
        WindowText = 8,
        ActiveCaptionText = 9,
        ActiveBorder = 10,
        InactiveBorder = 11,
        AppWorkspace = 12,
        Highlight = 13,
        HighlightText = 14,
        Control = 15,
        ControlDark = 16,
        GrayText = 17,
        ControlText = 18,
        InactiveCaptionText = 19,
        ControlLightLight = 20,
        ControlDarkDark = 21,
        ControlLight = 22,
        InfoText = 23,
        Info = 24,
        HotTrack = 26,
        GradientActiveCaption = 27,
        GradientInactiveCaption = 28,
        MenuHighlight = 29,
        MenuBar = 30,
        Accent = 100,
        AccentLight1 = 101,
        AccentLight2 = 102,
        AccentLight3 = 103,
        AccentDark1 = 104,
        AccentDark2 = 105,
        AccentDark3 = 106
    }
}
#endif
