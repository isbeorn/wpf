// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MS.Internal.Portability.Input
{
    internal interface IPortableTextCompositionSink
    {
        bool CanStart { get; }
        bool Begin(PortableTextCompositionSession session, long generation, string text, int caret, bool visible);
        bool Update(string text, int caret, bool? visible);
        bool SetVisibility(bool visible);
        bool Commit(string text);
        void Cancel();
    }

    internal sealed class PortableTextCompositionSession
    {
        private static readonly ConditionalWeakTable<Dispatcher, PortableTextCompositionSession> Sessions = new();
        private static readonly DependencyProperty SinkProperty = DependencyProperty.RegisterAttached(
            "PortableTextCompositionSink", typeof(IPortableTextCompositionSink), typeof(PortableTextCompositionSession));
        private readonly Dispatcher _dispatcher;
        private readonly InputManager _manager;
        private PresentationSource _source;
        private IInputElement _focus;
        private IPortableTextCompositionSink _sink;
        private long _generation;

        private PortableTextCompositionSession(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _manager = InputManager.UnsecureCurrent;
            _manager.PostProcessInput += OnPostProcessInput;
            dispatcher.ShutdownStarted += (_, _) => CancelCurrent();
        }

        internal static PortableTextCompositionSession Current => Sessions.GetValue(
            Dispatcher.CurrentDispatcher, dispatcher => new PortableTextCompositionSession(dispatcher));

        internal static void RegisterEditor(DependencyObject element, IPortableTextCompositionSink sink)
        {
            element.VerifyAccess();
            if (sink == null) element.ClearValue(SinkProperty);
            else element.SetValue(SinkProperty, sink);
        }

        internal long Begin(PresentationSource source, string text, int caret, bool visible = true)
        {
            _dispatcher.VerifyAccess();
            Validate(text, caret);
            if (!CanReceive(source)) return 0;
            IInputElement focus = Keyboard.FocusedElement;
            var sink = (focus as DependencyObject)?.GetValue(SinkProperty) as IPortableTextCompositionSink;
            if (sink == null || !sink.CanStart) return 0;
            long previous = _generation;
            if (_sink != null)
            {
                CancelCurrent();
                previous = Next(previous);
                if (_generation != previous || _sink != null || !CanReceive(source) || Keyboard.FocusedElement != focus) return 0;
            }
            _source = source;
            _focus = focus;
            _sink = sink;
            _generation = Next(_generation);
            long generation = _generation;
            try
            {
                if (sink.Begin(this, generation, text, caret, visible) && IsCurrent(generation, sink)) return generation;
                if (IsCurrent(generation, sink)) CancelCurrent();
                return 0;
            }
            catch
            {
                if (IsCurrent(generation, sink)) CancelCurrent();
                throw;
            }
        }

        internal bool Update(PresentationSource source, long generation, string text, int caret, bool? visible = null)
        {
            _dispatcher.VerifyAccess();
            if (!Matches(source, generation)) return false;
            Validate(text, caret);
            IPortableTextCompositionSink sink = _sink;
            try { return sink.Update(text, caret, visible); }
            catch
            {
                if (IsCurrent(generation, sink)) CancelCurrent();
                throw;
            }
        }

        internal bool SetVisibility(PresentationSource source, long generation, bool visible)
        {
            _dispatcher.VerifyAccess();
            if (!Matches(source, generation)) return false;
            IPortableTextCompositionSink sink = _sink;
            try { return sink.SetVisibility(visible); }
            catch
            {
                if (IsCurrent(generation, sink)) CancelCurrent();
                throw;
            }
        }

        internal bool Commit(PresentationSource source, long generation, string text)
        {
            _dispatcher.VerifyAccess();
            if (!Matches(source, generation)) return false;
            PortableTextInputReport.ValidateUtf16(text);
            IPortableTextCompositionSink sink = _sink;
            try { return sink.Commit(text); }
            catch
            {
                if (IsCurrent(generation, sink)) CancelCurrent();
                throw;
            }
            finally { Release(generation, sink); }
        }

        internal bool Cancel(PresentationSource source, long generation)
        {
            _dispatcher.VerifyAccess();
            if (_source != source || _sink == null || generation != _generation) return false;
            CancelCurrent();
            return true;
        }

        internal void CancelSource(PresentationSource source)
        {
            _dispatcher.VerifyAccess();
            if (_source == source) CancelCurrent();
        }

        internal bool IsCurrent(long generation, IPortableTextCompositionSink sink) =>
            _generation == generation && ReferenceEquals(_sink, sink);

        internal void Release(long generation, IPortableTextCompositionSink sink)
        {
            _dispatcher.VerifyAccess();
            if (IsCurrent(generation, sink)) Clear();
        }

        private bool Matches(PresentationSource source, long generation)
        {
            if (_source != source || _sink == null || generation != _generation) return false;
            if (CanReceive(source) && Keyboard.FocusedElement == _focus) return true;
            CancelCurrent();
            return false;
        }

        private bool CanReceive(PresentationSource source) => source != null && !source.IsDisposed &&
            source.Dispatcher == _dispatcher && _manager.PortableInputState.KeyboardSource == source &&
            Keyboard.FocusedElement is Visual visual && PresentationSource.FromVisual(visual) == source;

        private void OnPostProcessInput(object sender, ProcessInputEventArgs args)
        {
            if (_sink != null && (!CanReceive(_source) || Keyboard.FocusedElement != _focus)) CancelCurrent();
        }

        private void CancelCurrent()
        {
            IPortableTextCompositionSink sink = _sink;
            Clear();
            sink?.Cancel();
        }

        private void Clear()
        {
            _source = null;
            _focus = null;
            _sink = null;
            _generation = Next(_generation);
        }

        private static long Next(long value)
        {
            unchecked { value++; if (value == 0) value++; }
            return value;
        }

        private static void Validate(string text, int caret)
        {
            PortableTextInputReport.ValidateUtf16(text);
            ArgumentOutOfRangeException.ThrowIfNegative(caret);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(caret, text.Length);
            if (caret > 0 && caret < text.Length && char.IsHighSurrogate(text[caret - 1]) && char.IsLowSurrogate(text[caret]))
                throw new ArgumentException("A portable preedit caret cannot split a UTF-16 surrogate pair.", nameof(caret));
        }
    }
}
#endif
