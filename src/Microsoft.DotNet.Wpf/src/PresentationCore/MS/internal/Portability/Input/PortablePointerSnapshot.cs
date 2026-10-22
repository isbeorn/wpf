// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System.Windows;
using System.Windows.Input;

namespace MS.Internal.Portability.Input
{
    internal readonly struct PortablePointerSnapshot
    {
        private readonly MouseButtonState _left;
        private readonly MouseButtonState _right;
        private readonly MouseButtonState _middle;
        private readonly MouseButtonState _xButton1;
        private readonly MouseButtonState _xButton2;

        internal PortablePointerSnapshot(Point physicalClientPosition, MouseButtonState left, MouseButtonState right,
            MouseButtonState middle, MouseButtonState xButton1, MouseButtonState xButton2)
        {
            if (!double.IsFinite(physicalClientPosition.X) || !double.IsFinite(physicalClientPosition.Y) ||
                physicalClientPosition.X < int.MinValue || physicalClientPosition.X > int.MaxValue ||
                physicalClientPosition.Y < int.MinValue || physicalClientPosition.Y > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(physicalClientPosition));
            PhysicalClientPosition = physicalClientPosition;
            _left = left;
            _right = right;
            _middle = middle;
            _xButton1 = xButton1;
            _xButton2 = xButton2;
        }

        internal Point PhysicalClientPosition { get; }
        internal MouseButtonState GetButtonState(MouseButton button) => button switch
        {
            MouseButton.Left => _left,
            MouseButton.Right => _right,
            MouseButton.Middle => _middle,
            MouseButton.XButton1 => _xButton1,
            MouseButton.XButton2 => _xButton2,
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };
    }
}
#endif
