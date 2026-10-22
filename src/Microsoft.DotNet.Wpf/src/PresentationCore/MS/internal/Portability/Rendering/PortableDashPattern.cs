// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Windows.Media;
using SkiaSharp;

namespace MS.Internal.Portability.Rendering
{
    internal static class PortableDashPattern
    {
        internal static SKPathEffect Create(Pen pen)
        {
            DashStyle style = pen.DashStyle;
            if (style == null || style.Dashes == null || style.Dashes.Count == 0) return null;
            if (pen.StartLineCap != pen.DashCap || pen.EndLineCap != pen.DashCap || pen.DashCap == PenLineCap.Triangle)
                throw Unsupported("different outer and dash caps or triangle dash caps");
            double width = Math.Abs(pen.Thickness);
            if (!float.IsFinite((float)width) || width > 0 && (float)width == 0)
                throw Unsupported("unrepresentable dashed stroke widths");
            int count = style.Dashes.Count;
            var intervals = new float[checked((count & 1) == 0 ? count : count * 2)];
            double length = 0;
            for (int index = 0; index < intervals.Length; index++)
            {
                double interval = Math.Abs(style.Dashes[index % count]) * width;
                if (!float.IsFinite((float)interval) || interval > 0 && (float)interval == 0)
                    throw Unsupported("unrepresentable dash intervals");
                if ((index & 1) == 0 && interval == 0 && pen.DashCap != PenLineCap.Flat)
                    throw Unsupported("zero-length dash caps at contour endpoints");
                intervals[index] = (float)interval;
                length += intervals[index];
            }
            // Native CDashSequence rescales cycles below .1; that profile is not calibrated here.
            if (length < .1f || !float.IsFinite((float)length))
                throw Unsupported("zero, tiny or overflowing dash cycles");
            double phase = style.Offset * width;
            if (!float.IsFinite((float)phase))
                throw Unsupported("non-finite or overflowing dash offsets");
            phase %= length;
            if (phase < 0) phase += length;
            return SKPathEffect.CreateDash(intervals, (float)phase) ?? throw Unsupported("dash intervals rejected by the raster backend");
        }

        private static NotSupportedException Unsupported(string operation) => new NotSupportedException($"Portable rendering does not support {operation}.");
    }
}
#endif
