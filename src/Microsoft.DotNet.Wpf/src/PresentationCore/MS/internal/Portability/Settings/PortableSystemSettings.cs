// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace MS.Internal.Portability.Settings
{
    internal sealed class PortablePointerSettings
    {
        internal PortablePointerSettings(int doubleClickWidth, int doubleClickHeight, int doubleClickTimeMilliseconds)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(doubleClickWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(doubleClickHeight);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(doubleClickTimeMilliseconds);
            DoubleClickWidth = doubleClickWidth;
            DoubleClickHeight = doubleClickHeight;
            DoubleClickTimeMilliseconds = doubleClickTimeMilliseconds;
        }

        // Distances use raw mouse-report client pixels, matching MouseDevice.IsSameSpot.
        internal int DoubleClickWidth { get; }
        internal int DoubleClickHeight { get; }
        internal int DoubleClickTimeMilliseconds { get; }
    }

    internal sealed class PortableScrollBarMetrics
    {
        internal PortableScrollBarMetrics(double horizontalScrollBarButtonWidthDip, double horizontalScrollBarHeightDip,
            double verticalScrollBarWidthDip, double verticalScrollBarButtonHeightDip)
        {
            if (!double.IsFinite(horizontalScrollBarButtonWidthDip) || horizontalScrollBarButtonWidthDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(horizontalScrollBarButtonWidthDip), "Scroll bar button width must be finite and positive in DIPs.");
            if (!double.IsFinite(horizontalScrollBarHeightDip) || horizontalScrollBarHeightDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(horizontalScrollBarHeightDip), "Scroll bar height must be finite and positive in DIPs.");
            if (!double.IsFinite(verticalScrollBarWidthDip) || verticalScrollBarWidthDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(verticalScrollBarWidthDip), "Scroll bar width must be finite and positive in DIPs.");
            if (!double.IsFinite(verticalScrollBarButtonHeightDip) || verticalScrollBarButtonHeightDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(verticalScrollBarButtonHeightDip), "Scroll bar button height must be finite and positive in DIPs.");
            HorizontalScrollBarButtonWidthDip = horizontalScrollBarButtonWidthDip;
            HorizontalScrollBarHeightDip = horizontalScrollBarHeightDip;
            VerticalScrollBarWidthDip = verticalScrollBarWidthDip;
            VerticalScrollBarButtonHeightDip = verticalScrollBarButtonHeightDip;
        }

        // Declared portable Classic geometry in DIPs, independent of desktop preferences.
        internal static PortableScrollBarMetrics Classic { get; } = new PortableScrollBarMetrics(17, 17, 17, 17);
        internal double HorizontalScrollBarButtonWidthDip { get; }
        internal double HorizontalScrollBarHeightDip { get; }
        internal double VerticalScrollBarWidthDip { get; }
        internal double VerticalScrollBarButtonHeightDip { get; }
    }

    internal sealed class PortablePrimaryScreenMetrics
    {
        internal PortablePrimaryScreenMetrics(double widthDip, double heightDip, Int32Rect workAreaPixels, double scale)
        {
            if (!double.IsFinite(widthDip) || widthDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(widthDip), "Primary-screen width must be finite and positive in DIPs.");
            if (!double.IsFinite(heightDip) || heightDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(heightDip), "Primary-screen height must be finite and positive in DIPs.");
            ArgumentOutOfRangeException.ThrowIfNegative(workAreaPixels.Width);
            ArgumentOutOfRangeException.ThrowIfNegative(workAreaPixels.Height);
            if (!double.IsFinite(scale) || scale <= 0)
                throw new ArgumentOutOfRangeException(nameof(scale), "Primary-screen scale must be finite and positive.");
            _ = checked(workAreaPixels.X + workAreaPixels.Width);
            _ = checked(workAreaPixels.Y + workAreaPixels.Height);
            WidthDip = widthDip;
            HeightDip = heightDip;
            WorkAreaPixels = workAreaPixels;
            WorkAreaDip = new Rect(workAreaPixels.X / scale, workAreaPixels.Y / scale,
                workAreaPixels.Width / scale, workAreaPixels.Height / scale);
        }

        internal double WidthDip { get; }
        internal double HeightDip { get; }
        internal Int32Rect WorkAreaPixels { get; }
        internal Rect WorkAreaDip { get; }
    }

    internal sealed class PortableSystemSettings
    {
        private readonly PortablePointerSettings? _pointerSettings;
        private readonly PortablePrimaryScreenMetrics? _primaryScreenMetrics;

        internal PortableSystemSettings(string profileName, IReadOnlyDictionary<PortableSystemColor, uint> colors,
            bool keyboardCues, bool highContrast, PortablePointerSettings? pointerSettings = null,
            double caretWidthDip = 1.0, int caretBlinkTimeMilliseconds = 500, bool mouseVanish = false,
            double minimumHorizontalDragDistanceDip = 4.0, double minimumVerticalDragDistanceDip = 4.0,
            PortableScrollBarMetrics? scrollBarMetrics = null, PortablePrimaryScreenMetrics? primaryScreenMetrics = null,
            int mouseHoverTimeMilliseconds = 400, double mouseHoverWidthDip = 4.0, double mouseHoverHeightDip = 4.0)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
            ArgumentNullException.ThrowIfNull(colors);
            if (!double.IsFinite(caretWidthDip) || caretWidthDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(caretWidthDip), "Caret width must be finite and positive in DIPs.");
            if (caretBlinkTimeMilliseconds < -1 || caretBlinkTimeMilliseconds > int.MaxValue / 2)
                throw new ArgumentOutOfRangeException(nameof(caretBlinkTimeMilliseconds), "Caret blink time must be -1 or 0 for a steady caret, or a positive interval that can be doubled without overflow.");
            if (!double.IsFinite(minimumHorizontalDragDistanceDip) || minimumHorizontalDragDistanceDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimumHorizontalDragDistanceDip), "Horizontal drag distance must be finite and positive in DIPs.");
            if (!double.IsFinite(minimumVerticalDragDistanceDip) || minimumVerticalDragDistanceDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimumVerticalDragDistanceDip), "Vertical drag distance must be finite and positive in DIPs.");
            if (mouseHoverTimeMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(mouseHoverTimeMilliseconds), "Mouse hover time must be nonnegative in milliseconds.");
            if (!double.IsFinite(mouseHoverWidthDip) || mouseHoverWidthDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(mouseHoverWidthDip), "Mouse hover width must be finite and positive in DIPs.");
            if (!double.IsFinite(mouseHoverHeightDip) || mouseHoverHeightDip <= 0)
                throw new ArgumentOutOfRangeException(nameof(mouseHoverHeightDip), "Mouse hover height must be finite and positive in DIPs.");
            var palette = new Dictionary<PortableSystemColor, uint>();
            foreach (var role in Enum.GetValues<PortableSystemColor>())
            {
                if (!colors.TryGetValue(role, out uint color))
                    throw new ArgumentException($"Settings profile '{profileName}' is missing color role '{role}'.", nameof(colors));
                if ((color >> 24) != 255)
                    throw new ArgumentException($"System color role '{role}' must be opaque in profile '{profileName}'.", nameof(colors));
                palette.Add(role, color);
            }
            if (colors.Count != palette.Count)
                throw new ArgumentException($"Settings profile '{profileName}' contains an unsupported color role.", nameof(colors));

            ProfileName = profileName;
            Colors = new ReadOnlyDictionary<PortableSystemColor, uint>(palette);
            KeyboardCues = keyboardCues;
            HighContrast = highContrast;
            _pointerSettings = pointerSettings;
            _primaryScreenMetrics = primaryScreenMetrics;
            CaretWidthDip = caretWidthDip;
            CaretBlinkTimeMilliseconds = caretBlinkTimeMilliseconds;
            MouseVanish = mouseVanish;
            MinimumHorizontalDragDistanceDip = minimumHorizontalDragDistanceDip;
            MinimumVerticalDragDistanceDip = minimumVerticalDragDistanceDip;
            ScrollBarMetrics = scrollBarMetrics ?? PortableScrollBarMetrics.Classic;
            MouseHoverTimeMilliseconds = mouseHoverTimeMilliseconds;
            MouseHoverWidthDip = mouseHoverWidthDip;
            MouseHoverHeightDip = mouseHoverHeightDip;
        }

        internal string ProfileName { get; }
        internal IReadOnlyDictionary<PortableSystemColor, uint> Colors { get; }
        internal bool KeyboardCues { get; }
        internal bool HighContrast { get; }
        internal double CaretWidthDip { get; }
        internal int CaretBlinkTimeMilliseconds { get; }
        internal bool MouseVanish { get; }
        internal double MinimumHorizontalDragDistanceDip { get; }
        internal double MinimumVerticalDragDistanceDip { get; }
        internal PortableScrollBarMetrics ScrollBarMetrics { get; }
        internal int MouseHoverTimeMilliseconds { get; }
        internal double MouseHoverWidthDip { get; }
        internal double MouseHoverHeightDip { get; }
        // Declared custom-frame geometry in DIPs, independent of WM decoration measurements.
        internal double WindowCaptionHeightDip => 23;
        internal double WindowResizeBorderThicknessDip => 8;
        internal double WindowCornerRadiusDip => 0;
        internal string ThemeName => "Classic";
        internal string ThemedResourceName => "themes/classic";
        internal PortablePointerSettings PointerSettings => _pointerSettings ?? throw new PlatformNotSupportedException(
            $"Portable settings profile '{ProfileName}' does not supply native pointer double-click preferences.");

        internal PortablePrimaryScreenMetrics PrimaryScreenMetrics => _primaryScreenMetrics ?? throw new PlatformNotSupportedException(
            $"Portable settings profile '{ProfileName}' does not supply observed primary-screen metrics. Register native screen settings before constructing controls that require them.");

        internal uint GetColor(PortableSystemColor role) => Colors.TryGetValue(role, out uint color) ? color :
            throw new ArgumentOutOfRangeException(nameof(role), role, $"Color role is not supplied by settings profile '{ProfileName}'.");

        internal static PortableSystemSettings CreateInitialLightProfile(bool keyboardCues = false, PortablePointerSettings? pointerSettings = null,
            double caretWidthDip = 1.0, int caretBlinkTimeMilliseconds = 500, bool mouseVanish = false,
            double minimumHorizontalDragDistanceDip = 4.0, double minimumVerticalDragDistanceDip = 4.0,
            PortableScrollBarMetrics? scrollBarMetrics = null, PortablePrimaryScreenMetrics? primaryScreenMetrics = null,
            int mouseHoverTimeMilliseconds = 400, double mouseHoverWidthDip = 4.0, double mouseHoverHeightDip = 4.0) =>
            new PortableSystemSettings("portable-light-v1", new Dictionary<PortableSystemColor, uint>
            {
                [PortableSystemColor.ActiveBorder] = 0xFF6B7280,
                [PortableSystemColor.ActiveCaption] = 0xFF2463A6,
                [PortableSystemColor.ActiveCaptionText] = 0xFFFFFFFF,
                [PortableSystemColor.AppWorkspace] = 0xFF707781,
                [PortableSystemColor.Control] = 0xFFF0F0F0,
                [PortableSystemColor.ControlDark] = 0xFFA0A0A0,
                [PortableSystemColor.ControlDarkDark] = 0xFF696969,
                [PortableSystemColor.ControlLight] = 0xFFE3E3E3,
                [PortableSystemColor.ControlLightLight] = 0xFFFFFFFF,
                [PortableSystemColor.ControlText] = 0xFF1F2328,
                [PortableSystemColor.Desktop] = 0xFF3A4654,
                [PortableSystemColor.GradientActiveCaption] = 0xFF5B8CC0,
                [PortableSystemColor.GradientInactiveCaption] = 0xFFDCE5EE,
                [PortableSystemColor.GrayText] = 0xFF6B7280,
                [PortableSystemColor.Highlight] = 0xFF2463A6,
                [PortableSystemColor.HighlightText] = 0xFFFFFFFF,
                [PortableSystemColor.HotTrack] = 0xFF1B4A7D,
                [PortableSystemColor.InactiveBorder] = 0xFFB8C6D5,
                [PortableSystemColor.InactiveCaption] = 0xFFB8C6D5,
                [PortableSystemColor.InactiveCaptionText] = 0xFF3C4550,
                [PortableSystemColor.Info] = 0xFFFFF8D7,
                [PortableSystemColor.InfoText] = 0xFF1F2328,
                [PortableSystemColor.Menu] = 0xFFFFFFFF,
                [PortableSystemColor.MenuBar] = 0xFFF0F0F0,
                [PortableSystemColor.MenuHighlight] = 0xFFDDE8F3,
                [PortableSystemColor.MenuText] = 0xFF1F2328,
                [PortableSystemColor.ScrollBar] = 0xFFE0E0E0,
                [PortableSystemColor.Window] = 0xFFFFFFFF,
                [PortableSystemColor.WindowFrame] = 0xFF6B7280,
                [PortableSystemColor.WindowText] = 0xFF1F2328,
                [PortableSystemColor.Accent] = 0xFF2463A6,
                [PortableSystemColor.AccentLight1] = 0xFF5B8CC0,
                [PortableSystemColor.AccentLight2] = 0xFF9CB9D9,
                [PortableSystemColor.AccentLight3] = 0xFFDDE8F3,
                [PortableSystemColor.AccentDark1] = 0xFF1B4A7D,
                [PortableSystemColor.AccentDark2] = 0xFF123253,
                [PortableSystemColor.AccentDark3] = 0xFF09192A
            }, keyboardCues, highContrast: false, pointerSettings, caretWidthDip, caretBlinkTimeMilliseconds, mouseVanish,
                minimumHorizontalDragDistanceDip, minimumVerticalDragDistanceDip, scrollBarMetrics, primaryScreenMetrics,
                mouseHoverTimeMilliseconds, mouseHoverWidthDip, mouseHoverHeightDip);
    }
}
#endif
