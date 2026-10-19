// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Windows;
using System.Windows.Media;

namespace MS.Internal.Portability.Windowing
{
    internal interface IPortableWindowPeer : IPortablePresentationHost, IDisposable
    {
        Size OuterSize { get; }
        Point DesktopPosition { get; }
        bool IsVisible { get; }
        bool IsEnabled { get; }
        bool IsActive { get; }
        // State associated with the current metrics snapshot, including before StateChanged notification.
        WindowState WindowState { get; }
        event Action CloseRequested;
        event Action<bool> ActivationChanged;
        event Action<WindowState> StateChanged;
        void SetTitle(string title);
        void SetIcon(ImageSource icon) =>
            throw new PlatformNotSupportedException("The registered window peer does not support bitmap icons.");
        void SetShowInTaskbar(bool show);
        void SetWindowStyle(WindowStyle style) =>
            throw new PlatformNotSupportedException("The registered window peer does not support decoration changes.");
        void SetTopmost(bool topmost) =>
            throw new PlatformNotSupportedException("The registered window peer does not support topmost changes.");
        void SetClientSize(Size size);
        void SetDesktopPosition(Point position);
        void SetClientSizeConstraints(Size minimum, Size maximum);
        void SetOwner(IPortableWindowPeer owner);
        void SetEnabled(bool enabled);
        void SetWindowState(WindowState state);
        void Show(bool activate);
        void Hide();
        bool Activate();
        bool CaptureMouse();
        void ReleaseMouseCapture();
        // Complete native peer disposal synchronously, then emit Closed exactly once.
        // This acknowledges disposal, not a round trip confirming X server destruction.
        void Close();
    }

    internal interface IPortablePopupPeer : IPortablePresentationHost, IDisposable
    {
        Point DesktopPosition { get; }
        event Action OwnerDeactivated;
        event Action OwnerClosing;
        void SetClientSize(Size size);
        void SetDesktopPosition(Point position);
        void SetHitTestable(bool value);
        void Show();
        void Hide();
    }

    internal sealed class PortableWindowOptions
    {
        internal string Title { get; init; }
        internal bool ShowInTaskbar { get; init; } = true;
        internal WindowStyle WindowStyle { get; init; } = WindowStyle.SingleBorderWindow;
        internal bool AllowsTransparency { get; init; }
        internal bool Topmost { get; init; }
        internal Size? ClientSize { get; init; }
        internal Point? DesktopPosition { get; init; }
        internal IPortableWindowPeer Owner { get; init; }
        internal bool IsEnabled { get; init; }
        internal WindowState WindowState { get; init; }
    }

    internal interface IPortableWindowFactory
    {
        IPortableWindowPeer CreateWindow(PresentationSource source, PortableWindowOptions options);
        IPortablePopupPeer CreatePopup(PresentationSource source, PresentationSource owner, bool transparent) =>
            throw new PlatformNotSupportedException("The registered backend does not support native Popups.");
        Rect GetPopupScreenBounds(PresentationSource owner, Rect targetBounds, Point targetPoint, bool useWorkArea) =>
            throw new PlatformNotSupportedException("The registered backend does not supply Popup screen bounds.");
    }

    internal static class PortableWindowPlatform
    {
        [ThreadStatic]
        private static IPortableWindowFactory _factory;

        internal static void RegisterFactory(IPortableWindowFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            if (_factory != null)
                throw new InvalidOperationException("A portable window factory is already registered on this thread.");
            _factory = factory;
        }

        internal static IPortableWindowFactory Factory => _factory ??
            throw new PlatformNotSupportedException("A portable window backend must be registered before showing a Window.");
    }
}
#endif
