// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MS.Internal.Portability.Windowing;

namespace MS.Internal.Portability.Input
{
    internal sealed class PortableInputProvider : IKeyboardInputProvider, IMouseInputProvider, IDisposable
    {
        private readonly PresentationSource _source;
        private readonly IPortableInputHost _host;
        private readonly InputManager _manager;
        private readonly InputProviderSite _site;
        private bool _disposed;
        private bool _disposing;
        private bool _captured;
        private bool _releasingCapture;
        private uint _captureGeneration;
        private bool _cursorHidden;

        internal PortableInputProvider(PresentationSource source, IPortableInputHost host)
        {
            ArgumentNullException.ThrowIfNull(source);
            source.VerifyAccess();
            ObjectDisposedException.ThrowIf(source.IsDisposed, source);
            _source = source;
            _host = host;
            _manager = InputManager.UnsecureCurrent;
            _site = _manager.RegisterInputProvider(this);
        }

        internal bool ReportInput(InputReport report)
        {
            return ReportInput(report, null);
        }

        private bool ReportInput(InputReport report, PortablePointerSnapshot? pointerSnapshot)
        {
            VerifyActive();
            ArgumentNullException.ThrowIfNull(report);
            if (!ReferenceEquals(report.InputSource, _source))
                throw new ArgumentException("The input report belongs to a different presentation source.", nameof(report));
            if (report is RawKeyboardInputReport keyboard)
            {
                if ((keyboard.Actions & RawKeyboardActions.Activate) == 0 && _manager.PortableInputState.KeyboardSource != _source)
                    return false;
                if ((keyboard.Actions & RawKeyboardActions.Deactivate) != 0)
                {
                    PortableTextCompositionSession.Current.CancelSource(_source);
                    if (_disposed || _source.IsDisposed) return false;
                }
                _manager.PortableInputState.Update(keyboard, _host);
            }
            else if (report is RawMouseInputReport mouse)
            {
                if ((mouse.Actions & RawMouseActions.CancelCapture) != 0)
                {
                    _captured = false;
                    unchecked { _captureGeneration++; }
                }
                if ((mouse.Actions & RawMouseActions.Activate) == 0 && _manager.PortableInputState.MouseSource != _source)
                    return false;
                if ((mouse.Actions & RawMouseActions.Activate) != 0 && pointerSnapshot == null)
                    pointerSnapshot = _host?.GetPointerState();
                _manager.PortableInputState.Update(mouse, pointerSnapshot);
                if ((mouse.Actions & RawMouseActions.Deactivate) != 0)
                {
                    bool handled = false;
                    try
                    {
                        if (_captured) ReleaseNativeCapture();
                    }
                    finally
                    {
                        try { RestoreCursorVisibility(); }
                        finally { handled = _site.ReportInput(report); }
                    }
                    return handled;
                }
            }
            else if (report is PortableTextInputReport committedText)
            {
                if (committedText.Text.Length == 0 || _manager.PortableInputState.KeyboardSource != _source) return false;
            }
            else if (report is RawTextInputReport text)
            {
                if (char.IsSurrogate(text.CharacterCode) || text.IsDeadCharacter)
                    throw new PlatformNotSupportedException("Portable text input currently supports committed BMP characters without dead keys.");
                if (_manager.PortableInputState.KeyboardSource != _source) return false;
            }
            else
                throw new PlatformNotSupportedException($"Portable input does not support report type '{report.GetType().Name}'.");
            return _site.ReportInput(report);
        }

        private bool CanAcceptPreedit { get { _source.VerifyAccess(); return !_disposed && !_disposing && !_source.IsDisposed; } }
        internal long BeginPreedit(string text, int caret) => CanAcceptPreedit ? PortableTextCompositionSession.Current.Begin(_source, text, caret) : 0;
        internal bool UpdatePreedit(long generation, string text, int caret) => CanAcceptPreedit && PortableTextCompositionSession.Current.Update(_source, generation, text, caret);
        internal bool CommitPreedit(long generation, string text) => CanAcceptPreedit && PortableTextCompositionSession.Current.Commit(_source, generation, text);
        internal bool CancelPreedit(long generation) => CanAcceptPreedit && PortableTextCompositionSession.Current.Cancel(_source, generation);
        public bool AcquireFocus(bool checkOnly)
        {
            VerifyActive();
            if (_disposing) return false;
            bool acquired = _host?.AcquireFocus(checkOnly) == true;
            if (_disposed || _disposing || _source.IsDisposed) return false;
            if (acquired && !checkOnly && _manager.PortableInputState.KeyboardSource != _source)
                ReportInput(new RawKeyboardInputReport(_source, InputMode.Foreground, Environment.TickCount,
                    RawKeyboardActions.Activate, 0, false, false, 0, IntPtr.Zero));
            return acquired && !_disposed && !_disposing && !_source.IsDisposed &&
                (checkOnly || _manager.PortableInputState.KeyboardSource == _source);
        }

        public bool CaptureMouse()
        {
            VerifyActive();
            if (_disposing) return false;
            bool captured = _host?.CaptureMouse() == true;
            if (_disposed || _source.IsDisposed)
            {
                if (captured) _host.ReleaseMouseCapture();
                return false;
            }
            if (captured)
            {
                _captured = true;
                unchecked { _captureGeneration++; }
                try
                {
                    if (_manager.PortableInputState.MouseSource != _source)
                    {
                        PortablePointerSnapshot snapshot = _host.GetPointerState();
                        ReportInput(new RawMouseInputReport(InputMode.Foreground, Environment.TickCount, _source,
                            RawMouseActions.Activate | RawMouseActions.AbsoluteMove,
                            (int)snapshot.PhysicalClientPosition.X, (int)snapshot.PhysicalClientPosition.Y, 0, IntPtr.Zero), snapshot);
                    }
                    captured = !_disposed && !_source.IsDisposed && _captured;
                }
                catch
                {
                    try
                    {
                        ReleaseNativeCapture();
                    }
                    finally
                    {
                        if (_manager.PortableInputState.MouseSource == _source)
                            ReportInput(new RawMouseInputReport(InputMode.Foreground, Environment.TickCount, _source,
                                RawMouseActions.Deactivate, 0, 0, 0, IntPtr.Zero));
                    }
                    throw;
                }
            }
            return captured;
        }

        public void ReleaseMouseCapture()
        {
            VerifyActive();
            if (!_captured) return;
            if (!ReleaseNativeCapture() || _disposed || _source.IsDisposed) return;
            ReportInput(new RawMouseInputReport(InputMode.Foreground, Environment.TickCount, _source,
                RawMouseActions.CancelCapture, 0, 0, 0, IntPtr.Zero));
        }

        private bool ReleaseNativeCapture()
        {
            if (!_captured) return true;
            if (_releasingCapture) return false;
            _releasingCapture = true;
            uint generation = _captureGeneration;
            try
            {
                _host.ReleaseMouseCapture();
                // Native loss can route a handler that acquires a newer capture.
                if (generation != _captureGeneration) return false;
                _captured = false;
                return true;
            }
            finally
            {
                _releasingCapture = false;
            }
        }

        public bool SetCursor(Cursor cursor)
        {
            VerifyActive();
            return _host?.SetCursor(cursor) == true;
        }

        internal bool IsCursorHidden => !_disposed && _cursorHidden;

        internal bool SetCursorVisibility(bool visible)
        {
            VerifyActive();
            if (_disposing && !visible) return false;
            bool accepted = _host?.SetCursorVisibility(visible) == true;
            if (_disposed || _source.IsDisposed)
            {
                if (accepted && !visible && !_host.SetCursorVisibility(true))
                    throw new InvalidOperationException("The presentation host rejected restoring cursor visibility after source closure.");
                return false;
            }
            if (accepted) _cursorHidden = !visible;
            return accepted;
        }

        private void RestoreCursorVisibility()
        {
            if (_cursorHidden && !SetCursorVisibility(true))
                throw new InvalidOperationException("The presentation host rejected restoring cursor visibility.");
        }

        public int GetIntermediatePoints(IInputElement relativeTo, Point[] points)
        {
            VerifyActive();
            return -1;
        }

        public bool ProvidesInputForRootVisual(Visual rootVisual) => !_disposed && ReferenceEquals(_source.RootVisual, rootVisual);

        public void NotifyDeactivate()
        {
            VerifyActive();
            RestoreCursorVisibility();
        }

        internal Point ClientToScreen(Point point)
        {
            VerifyActive();
            return RequireHost().ClientToScreen(point);
        }

        internal Point ScreenToClient(Point point)
        {
            VerifyActive();
            return RequireHost().ScreenToClient(point);
        }

        internal PresentationSource SourceFromScreenPoint(Point point)
        {
            VerifyActive();
            PresentationSource hit = RequireHost().SourceFromScreenPoint(point);
            if (hit == null) return null;
            if (hit is not PortablePresentationSource || hit.IsDisposed || hit.Dispatcher != _source.Dispatcher)
                throw new InvalidOperationException("The input host returned a disposed or foreign presentation source.");
            foreach (PresentationSource attached in PresentationSource.CurrentSources)
                if (ReferenceEquals(attached, hit)) return hit;
            throw new InvalidOperationException("The input host returned an unattached presentation source.");
        }

        public void Dispose()
        {
            _source.VerifyAccess();
            if (_disposed || _disposing) return;
            _disposing = true;
            try
            {
                try
                {
                    PortableTextCompositionSession.Current.CancelSource(_source);
                    if (_captured) ReleaseMouseCapture();
                }
                finally
                {
                    try
                    {
                        if (_manager.PortableInputState.KeyboardSource == _source)
                            ReportInput(new RawKeyboardInputReport(_source, InputMode.Foreground, Environment.TickCount,
                                RawKeyboardActions.Deactivate, 0, false, false, 0, IntPtr.Zero));
                    }
                    finally
                    {
                        try { RestoreCursorVisibility(); }
                        finally
                        {
                            if (_manager.PortableInputState.MouseSource == _source)
                                ReportInput(new RawMouseInputReport(InputMode.Foreground, Environment.TickCount, _source,
                                    RawMouseActions.Deactivate, 0, 0, 0, IntPtr.Zero));
                        }
                    }
                }
            }
            finally
            {
                _disposed = true;
                _site.Dispose();
                _disposing = false;
            }
        }

        private IPortableInputHost RequireHost() => _host ?? throw new PlatformNotSupportedException(
            "The presentation host does not provide physical client/screen coordinate conversion.");

        private void VerifyActive()
        {
            _source.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed || _source.IsDisposed, this);
        }
    }
}
#endif
