// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Windows;
using System.Windows.Input;

namespace MS.Internal.Portability.Input
{
    internal sealed class PortableInputState
    {
        private readonly KeyStates[] _keys = new KeyStates[(int)Key.DeadCharProcessed + 1];
        private readonly MouseButtonState[] _buttons = new MouseButtonState[5];
        internal PresentationSource KeyboardSource { get; private set; }
        internal PresentationSource MouseSource { get; private set; }

        internal KeyStates GetKeyStates(Key key) => _keys[(int)key];
        internal MouseButtonState GetButtonState(MouseButton button) => _buttons[(int)button];

        internal void Update(RawKeyboardInputReport report, IPortableInputHost host)
        {
            if ((report.Actions & RawKeyboardActions.Activate) != 0)
            {
                if (host != null)
                {
                    var snapshot = new KeyStates[_keys.Length];
                    for (int index = 0; index < _keys.Length; index++)
                        snapshot[index] = host.GetKeyStates((Key)index) & (KeyStates.Down | KeyStates.Toggled);
                    Array.Copy(snapshot, _keys, snapshot.Length);
                }
                else ClearKeys();
                KeyboardSource = report.InputSource;
            }
            if (KeyboardSource != report.InputSource) return;
            if ((report.Actions & RawKeyboardActions.Deactivate) != 0)
            {
                ClearKeys();
                KeyboardSource = null;
                return;
            }
            Key key = KeyInterop.KeyFromVirtualKey(report.VirtualKey);
            if (key == Key.None) return;
            if ((report.Actions & RawKeyboardActions.KeyDown) != 0)
            {
                if ((_keys[(int)key] & KeyStates.Down) == 0 &&
                    (key == Key.CapsLock || key == Key.NumLock || key == Key.Scroll))
                    _keys[(int)key] ^= KeyStates.Toggled;
                _keys[(int)key] |= KeyStates.Down;
            }
            if ((report.Actions & RawKeyboardActions.KeyUp) != 0)
                _keys[(int)key] &= ~KeyStates.Down;
        }

        internal void Update(RawMouseInputReport report, PortablePointerSnapshot? snapshot)
        {
            if ((report.Actions & RawMouseActions.Activate) != 0)
            {
                for (int index = 0; index < _buttons.Length; index++)
                    _buttons[index] = snapshot?.GetButtonState((MouseButton)index) ?? MouseButtonState.Released;
                MouseSource = report.InputSource;
            }
            if (MouseSource != report.InputSource) return;
            if ((report.Actions & RawMouseActions.Deactivate) != 0)
                Array.Clear(_buttons);
            if ((report.Actions & RawMouseActions.Deactivate) != 0)
            {
                MouseSource = null;
                return;
            }
            UpdateButton(report.Actions, MouseButton.Left, RawMouseActions.Button1Press, RawMouseActions.Button1Release);
            UpdateButton(report.Actions, MouseButton.Right, RawMouseActions.Button2Press, RawMouseActions.Button2Release);
            UpdateButton(report.Actions, MouseButton.Middle, RawMouseActions.Button3Press, RawMouseActions.Button3Release);
            UpdateButton(report.Actions, MouseButton.XButton1, RawMouseActions.Button4Press, RawMouseActions.Button4Release);
            UpdateButton(report.Actions, MouseButton.XButton2, RawMouseActions.Button5Press, RawMouseActions.Button5Release);
        }

        private void UpdateButton(RawMouseActions actions, MouseButton button, RawMouseActions press, RawMouseActions release)
        {
            if ((actions & press) != 0) _buttons[(int)button] = MouseButtonState.Pressed;
            if ((actions & release) != 0) _buttons[(int)button] = MouseButtonState.Released;
        }

        private void ClearKeys()
        {
            for (int index = 0; index < _keys.Length; index++) _keys[index] &= KeyStates.Toggled;
        }
    }

    internal sealed class PortableKeyboardDevice : KeyboardDevice
    {
        private readonly PortableInputState _state;
        internal PortableKeyboardDevice(InputManager manager, PortableInputState state) : base(manager) => _state = state;
        protected override KeyStates GetKeyStatesFromSystem(Key key) => _state.GetKeyStates(key);
    }

    internal sealed class PortableMouseDevice : MouseDevice
    {
        private readonly PortableInputState _state;
        internal PortableMouseDevice(InputManager manager, PortableInputState state) : base(manager) => _state = state;
        internal override MouseButtonState GetButtonStateFromSystem(MouseButton button) => IsActive ? _state.GetButtonState(button) : MouseButtonState.Released;
    }
}
#endif
