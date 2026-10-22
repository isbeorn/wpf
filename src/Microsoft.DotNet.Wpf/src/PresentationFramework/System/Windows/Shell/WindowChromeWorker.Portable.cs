// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.ComponentModel;
using System.Windows.Input;
using Standard;

namespace System.Windows.Shell
{
    internal partial class WindowChromeWorker
    {
        private void PortableSetWindow(Window window)
        {
            _window = window;
            window.SourceInitialized += PortableSourceInitialized;
            window.Closing += PortableClosing;
            window.Closed += PortableClosed;
            window.IsVisibleChanged += PortableVisibilityChanged;
            window.PreviewMouseDown += PortableMouseDown;
            window.PreviewMouseUp += PortableMouseUp;
            window.QueryCursor += PortableQueryCursor;
            window.LostMouseCapture += PortableLostCapture;
        }

        private void PortableApplyChrome()
        {
            if (_window == null) return;
            _window.PortableCancelChromeOperation();
            _window.PortableUpdateChrome();
        }

        private void PortableSourceInitialized(object sender, EventArgs e) => PortableApplyChrome();
        private void PortableClosing(object sender, CancelEventArgs e) => _window.PortableCancelChromeOperation();
        private void PortableVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) _window.PortableCancelChromeOperation();
        }
        private void PortableLostCapture(object sender, MouseEventArgs e) => _window.PortableCancelChromeOperation();
        private void PortableMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) _window.PortableCancelChromeOperation();
        }
        private void PortableClosed(object sender, EventArgs e)
        {
            _chromeInfo?.PropertyChangedThatRequiresRepaint -= _OnChromePropertyChangedThatRequiresRepaint;
            _window.SourceInitialized -= PortableSourceInitialized;
            _window.Closing -= PortableClosing;
            _window.Closed -= PortableClosed;
            _window.IsVisibleChanged -= PortableVisibilityChanged;
            _window.PreviewMouseDown -= PortableMouseDown;
            _window.PreviewMouseUp -= PortableMouseUp;
            _window.QueryCursor -= PortableQueryCursor;
            _window.LostMouseCapture -= PortableLostCapture;
        }

        private bool PortableCanResize => _window.WindowState == WindowState.Normal &&
            _window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

        private HT PortableHitTest(Point point)
        {
            IInputElement input = _window.InputHitTest(point);
            if (input != null)
            {
                if (WindowChrome.GetIsHitTestVisibleInChrome(input)) return HT.CLIENT;
                ResizeGripDirection direction = WindowChrome.GetResizeGripDirection(input);
                if (direction != ResizeGripDirection.None) return _GetHTFromResizeGripDirection(direction);
            }
            return _HitTestNca(new Rect(0, 0, _window.ActualWidth, _window.ActualHeight), point);
        }

        private void PortableMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled || e.ChangedButton != MouseButton.Left || _chromeInfo == null || !_window.IsEnabled || !_window.IsVisible) return;
            Point point = e.GetPosition(_window);
            HT hit = PortableHitTest(point);
            if (hit == HT.CLIENT || hit == HT.NOWHERE || (hit != HT.CAPTION && !PortableCanResize)) return;
            Mouse.Capture(null);
            if (e.Handled || Mouse.Captured != null || _chromeInfo == null || !_window.IsEnabled || !_window.IsVisible) return;
            point = e.GetPosition(_window);
            hit = PortableHitTest(point);
            if (hit == HT.CLIENT || hit == HT.NOWHERE) return;
            if (hit == HT.CAPTION)
            {
                if (e.ClickCount == 2 && _window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
                {
                    _window.PortableCancelChromeOperation();
                    _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    e.Handled = true;
                    return;
                }
            }
            else if (!PortableCanResize) return;
            ResizeGripDirection direction = PortableDirection(hit);
            e.Handled = _window.PortableBeginChromeMoveResize(direction, _window.PointToScreen(point));
        }

        private void PortableQueryCursor(object sender, QueryCursorEventArgs e)
        {
            if (e.Handled || _chromeInfo == null || !_window.IsEnabled || !PortableCanResize) return;
            Cursor cursor = PortableHitTest(e.GetPosition(_window)) switch
            {
                HT.TOP or HT.BOTTOM => Cursors.SizeNS,
                HT.LEFT or HT.RIGHT => Cursors.SizeWE,
                HT.TOPLEFT or HT.BOTTOMRIGHT => Cursors.SizeNWSE,
                HT.TOPRIGHT or HT.BOTTOMLEFT => Cursors.SizeNESW,
                _ => null
            };
            if (cursor != null) { e.Cursor = cursor; e.Handled = true; }
        }

        private static ResizeGripDirection PortableDirection(HT hit) => hit switch
        {
            HT.TOPLEFT => ResizeGripDirection.TopLeft,
            HT.TOP => ResizeGripDirection.Top,
            HT.TOPRIGHT => ResizeGripDirection.TopRight,
            HT.RIGHT => ResizeGripDirection.Right,
            HT.BOTTOMRIGHT => ResizeGripDirection.BottomRight,
            HT.BOTTOM => ResizeGripDirection.Bottom,
            HT.BOTTOMLEFT => ResizeGripDirection.BottomLeft,
            HT.LEFT => ResizeGripDirection.Left,
            _ => ResizeGripDirection.None
        };
    }
}
#endif
