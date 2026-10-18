// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using TextBuffer = HarfBuzzSharp.Buffer;

namespace MS.Internal.TextFormatting.Portable
{
    internal sealed class PortableTextLine : TextLine
    {
        internal sealed record Shaped(GlyphTypeface Typeface, ushort[] Glyphs, double[] Advances, Point[] Offsets, ushort[] Map, bool[] Stops, double Width);
        private sealed record LineRun(int Start, int Length, TextRun Source, GlyphRun Glyphs, double Offset, double Width);
        private readonly int _first;
        private readonly string _text;
        private readonly int _newline;
        private readonly int _dependent;
        private readonly List<LineRun> _runs = new List<LineRun>();
        private readonly IList<TextSpan<TextRun>> _spans;
        private readonly double _width;
        private readonly double _widthWithoutTrailing;
        private readonly int _trailing;
        private readonly double _height;
        private readonly double _baseline;
        private readonly Rect _ink;
        private readonly bool _overflow;
        private readonly bool _truncated;
        private bool _disposed;

        internal PortableTextLine(int first, string text, int newline, IList<PortableTextFormatter.Run> sourceRuns,
            TextParagraphProperties properties, double pixelsPerDip, TextFormattingMode mode, double paragraphWidth, bool truncated, IList<TextSpan<TextRun>> terminators, int dependent)
            : base(pixelsPerDip)
        {
            _first = first;
            _text = text;
            _newline = newline;
            _dependent = dependent;
            _truncated = truncated;
            var graphemes = StringInfo.ParseCombiningCharacters(text).ToHashSet();
            foreach (var run in sourceRuns)
                if (run.Start < text.Length && !graphemes.Contains(run.Start))
                    throw new NotSupportedException("A portable text run cannot split a grapheme cluster across formatting properties.");
            var shapedRuns = new List<(PortableTextFormatter.Run Source, int Length, Shaped Shape)>();
            foreach (var run in sourceRuns)
            {
                int count = Math.Min(run.Length, text.Length - run.Start);
                if (count <= 0)
                    continue;
                var shape = Shape(text.Substring(run.Start, count), run.Value.Properties, pixelsPerDip, mode);
                shapedRuns.Add((run, count, shape));
                double size = run.Value.Properties.FontRenderingEmSize;
                _baseline = Math.Max(_baseline, shape.Typeface.Baseline * size);
                _height = Math.Max(_height, (shape.Typeface.Height + shape.Typeface.PortableFontFace.Metrics.Leading) * size);
            }
            if (shapedRuns.Count == 0)
            {
                TextRunProperties defaultProperties = properties.DefaultTextRunProperties;
                if (!defaultProperties.Typeface.TryGetGlyphTypeface(out GlyphTypeface typeface))
                    throw new NotSupportedException("A physical font is required for portable empty-line metrics.");
                _baseline = typeface.Baseline * defaultProperties.FontRenderingEmSize;
                _height = typeface.Height * defaultProperties.FontRenderingEmSize;
            }
            double descent = shapedRuns.Count == 0 ? _height - _baseline : shapedRuns.Max(run =>
                (run.Shape.Typeface.Height - run.Shape.Typeface.Baseline) * run.Source.Value.Properties.FontRenderingEmSize);
            _height = Math.Max(_height, _baseline + descent);
            var spans = new List<TextSpan<TextRun>>();
            Rect ink = Rect.Empty;
            foreach (var item in shapedRuns)
            {
                var shape = item.Shape;
                TextRunProperties runProperties = item.Source.Value.Properties;
                var glyphs = GlyphRun.TryCreate(shape.Typeface, 0, false, runProperties.FontRenderingEmSize,
                    (float)pixelsPerDip, shape.Glyphs, new Point(_width, _baseline), shape.Advances, shape.Offsets,
                    text.Substring(item.Source.Start, item.Length).ToCharArray(), null, shape.Map, shape.Stops,
                    XmlLanguage.GetLanguage(runProperties.CultureInfo.IetfLanguageTag), mode);
                if (glyphs == null)
                    throw new InvalidOperationException("Portable shaped output is not a valid WPF GlyphRun.");
                _runs.Add(new LineRun(item.Source.Start, item.Length, item.Source.Value, glyphs, _width, shape.Width));
                Rect bounds = glyphs.ComputeInkBoundingBox();
                if (!bounds.IsEmpty)
                {
                    bounds.Offset(_width, _baseline);
                    ink.Union(bounds);
                }
                _width += shape.Width;
                spans.Add(new TextSpan<TextRun>(item.Length, item.Source.Value));
            }
            if (newline != 0)
                spans.AddRange(terminators);
            _spans = spans.AsReadOnly();
            _ink = ink;
            while (_trailing < text.Length && text[text.Length - _trailing - 1] == ' ')
                _trailing++;
            _widthWithoutTrailing = _trailing == 0 ? _width : Distance(text.Length - _trailing);
            _overflow = paragraphWidth > 0 && _widthWithoutTrailing > paragraphWidth;
        }

        internal static Shaped Shape(string text, TextRunProperties properties, double pixelsPerDip, TextFormattingMode mode)
        {
            if (properties == null || properties.Typeface == null || properties.CultureInfo == null ||
                !double.IsFinite(properties.FontRenderingEmSize) || properties.FontRenderingEmSize <= 0)
                throw new ArgumentException("Portable text requires a typeface, culture and positive finite font size.");
            if ((properties.TextDecorations != null && properties.TextDecorations.Count != 0) ||
                (properties.TextEffects != null && properties.TextEffects.Count != 0) || properties.NumberSubstitution != null)
                throw new NotSupportedException("Portable run decorations, effects and number substitution are not implemented.");
            if (!properties.Typeface.TryGetGlyphTypeface(out GlyphTypeface typeface))
                throw new NotSupportedException("Portable text currently requires a physical font family.");
            HarfBuzzSharp.Script? script = null;
            foreach (Rune rune in text.EnumerateRunes())
            {
                HarfBuzzSharp.Script current = HarfBuzzSharp.UnicodeFunctions.Default.GetScript(rune.Value);
                if (current.HorizontalDirection == HarfBuzzSharp.Direction.RightToLeft || rune.Value == 0x200e || rune.Value == 0x200f || rune.Value == 0x061c)
                    throw new NotSupportedException("Portable bidi paragraph layout is not implemented.");
                if (current != HarfBuzzSharp.Script.Common && current != HarfBuzzSharp.Script.Inherited && current != HarfBuzzSharp.Script.Unknown)
                {
                    if (script.HasValue && script.Value != current)
                        throw new NotSupportedException("Portable mixed-script run itemization is not implemented.");
                    script = current;
                }
            }
            using (var font = new SKFont(typeface.PortableFontFace.Typeface, (float)properties.FontRenderingEmSize))
            using (var shaper = new SKShaper(typeface.PortableFontFace.Typeface))
            using (var buffer = new TextBuffer())
            {
                buffer.AddUtf16(text);
                buffer.Direction = HarfBuzzSharp.Direction.LeftToRight;
                buffer.Language = new HarfBuzzSharp.Language(properties.CultureInfo);
                buffer.GuessSegmentProperties();
                var result = shaper.Shape(buffer, font);
                if (result.Codepoints.Length == 0 || result.Codepoints.Any(glyph => glyph == 0 || glyph > ushort.MaxValue))
                    throw new NotSupportedException("The selected portable font does not cover this text; font fallback is not implemented.");
                var positions = buffer.GlyphPositions;
                double totalUnits = positions.Sum(position => (double)position.XAdvance);
                double scale = totalUnits == 0 ? 0 : result.Width / totalUnits;
                var advances = positions.Select(position => position.XAdvance * scale).ToArray();
                var offsets = new Point[advances.Length];
                double rawAdvance = 0;
                for (int index = 0; index < advances.Length; index++)
                {
                    offsets[index] = new Point(result.Points[index].X - rawAdvance, -result.Points[index].Y);
                    rawAdvance += advances[index];
                    if (mode == TextFormattingMode.Display)
                        advances[index] = Math.Round(advances[index] * pixelsPerDip) / pixelsPerDip;
                }
                var map = new ushort[text.Length];
                for (int glyph = 0; glyph < result.Clusters.Length; glyph++)
                {
                    if (glyph > 0 && result.Clusters[glyph] == result.Clusters[glyph - 1])
                        continue;
                    int start = checked((int)result.Clusters[glyph]);
                    int next = glyph + 1;
                    while (next < result.Clusters.Length && result.Clusters[next] == result.Clusters[glyph])
                        next++;
                    int end = next < result.Clusters.Length ? checked((int)result.Clusters[next]) : text.Length;
                    if (start < 0 || end < start || end > text.Length)
                        throw new NotSupportedException("Portable text requires monotonic LTR shaping clusters.");
                    for (int character = start; character < end; character++)
                        map[character] = checked((ushort)glyph);
                }
                var clusterStarts = result.Clusters.ToHashSet();
                var stops = new bool[text.Length + 1];
                foreach (int boundary in StringInfo.ParseCombiningCharacters(text))
                    stops[boundary] = clusterStarts.Contains((uint)boundary);
                stops[text.Length] = true;
                return new Shaped(typeface, result.Codepoints.Select(glyph => checked((ushort)glyph)).ToArray(), advances,
                    offsets, map, stops, advances.Sum());
            }
        }

        private void Verify() { ObjectDisposedException.ThrowIf(_disposed, this); }
        public override void Dispose() { _disposed = true; }
        public override void Draw(DrawingContext context, Point origin, InvertAxes inversion)
        {
            Verify();
            ArgumentNullException.ThrowIfNull(context);
            if (inversion != InvertAxes.None)
                throw new NotSupportedException("Portable inverted text drawing is not implemented.");
            context.PushTransform(new TranslateTransform(origin.X, origin.Y));
            try
            {
                foreach (LineRun run in _runs)
                {
                    if (run.Source.Properties.BackgroundBrush != null)
                        context.DrawRectangle(run.Source.Properties.BackgroundBrush, null, new Rect(run.Offset, 0, run.Width, _height));
                    context.DrawGlyphRun(run.Source.Properties.ForegroundBrush, run.Glyphs);
                }
            }
            finally { context.Pop(); }
        }
        public override TextLine Collapse(params TextCollapsingProperties[] properties)
        {
            Verify();
            if (properties == null || properties.Length == 0)
                return this;
            throw new NotSupportedException("Portable text collapsing is not implemented.");
        }
        public override IList<TextCollapsedRange> GetTextCollapsedRanges() { Verify(); return null; }
        public override TextLineBreak GetTextLineBreak() { Verify(); return _newline == 0 ? new TextLineBreak(null, IntPtr.Zero) : null; }
        public override IList<TextSpan<TextRun>> GetTextRunSpans() { Verify(); return _spans; }
        public override IEnumerable<IndexedGlyphRun> GetIndexedGlyphRuns()
        {
            Verify();
            return _runs.Select(run => new IndexedGlyphRun(_first + run.Start, run.Length, run.Glyphs)).ToArray();
        }

        public override CharacterHit GetCharacterHitFromDistance(double distance)
        {
            Verify();
            if (!double.IsFinite(distance))
                throw new ArgumentOutOfRangeException(nameof(distance));
            if (_runs.Count == 0)
                return new CharacterHit(_first, 0);
            LineRun run = _runs.Last();
            foreach (LineRun candidate in _runs)
                if (distance < candidate.Offset + candidate.Width) { run = candidate; break; }
            CharacterHit hit = run.Glyphs.GetCaretCharacterHitFromDistance(distance - run.Offset, out _);
            return new CharacterHit(_first + run.Start + hit.FirstCharacterIndex, hit.TrailingLength);
        }
        public override double GetDistanceFromCharacterHit(CharacterHit hit)
        {
            Verify();
            ValidateHit(hit);
            return Distance(hit.FirstCharacterIndex - _first, hit.TrailingLength);
        }
        private double Distance(int index, int trailing = 0)
        {
            foreach (LineRun run in _runs)
                if (index < run.Start + run.Length || (index == run.Start + run.Length && trailing == 0))
                    return run.Offset + run.Glyphs.GetDistanceFromCaretCharacterHit(new CharacterHit(index - run.Start, trailing));
            return _width;
        }
        private void ValidateHit(CharacterHit hit)
        {
            if (hit.FirstCharacterIndex < _first || hit.TrailingLength < 0 || (long)hit.FirstCharacterIndex + hit.TrailingLength > (long)_first + Length)
                throw new ArgumentOutOfRangeException(nameof(hit));
        }
        private int[] Stops() => _runs.SelectMany(run => run.Glyphs.CaretStops.Select((stop, index) => (stop, index))
            .Where(item => item.stop).Select(item => _first + run.Start + item.index)).Append(_first + _text.Length).Distinct().OrderBy(value => value).ToArray();
        public override CharacterHit GetNextCaretCharacterHit(CharacterHit hit)
        {
            Verify(); ValidateHit(hit);
            int position = hit.FirstCharacterIndex + hit.TrailingLength;
            int previous = _first;
            foreach (int stop in Stops())
            {
                if (stop > position)
                    return new CharacterHit(previous, stop - previous);
                previous = stop;
            }
            return hit;
        }
        public override CharacterHit GetPreviousCaretCharacterHit(CharacterHit hit)
        {
            Verify(); ValidateHit(hit);
            int position = hit.FirstCharacterIndex + hit.TrailingLength;
            int[] stops = Stops();
            for (int index = stops.Length - 1; index >= 0; index--)
                if (stops[index] < position)
                    return new CharacterHit(stops[index], 0);
            return hit;
        }
        public override CharacterHit GetBackspaceCaretCharacterHit(CharacterHit hit) => GetPreviousCaretCharacterHit(hit);
        public override IList<TextBounds> GetTextBounds(int first, int length)
        {
            Verify();
            if (length < 0 || first < _first || first > _first + Length || (long)first + length > _first + Length)
                throw new ArgumentOutOfRangeException(nameof(length));
            var ranges = new List<TextRunBounds>();
            foreach (LineRun run in _runs)
            {
                int start = Math.Max(first - _first, run.Start), end = Math.Min(first - _first + length, run.Start + run.Length);
                if (end > start)
                    ranges.Add(new TextRunBounds(new Rect(Distance(start), 0, Math.Max(0, Distance(end) - Distance(start)), _height),
                        _first + start, _first + end, run.Source));
            }
            double left = Distance(Math.Min(first - _first, _text.Length));
            double right = Distance(Math.Min(first - _first + length, _text.Length));
            return new[] { new TextBounds(new Rect(left, 0, Math.Max(0, right - left), _height), FlowDirection.LeftToRight, ranges) };
        }

        public override bool HasOverflowed => _overflow;
        public override bool HasCollapsed => false;
        public override bool IsTruncated => _truncated;
        public override int Length => _text.Length + _newline;
        public override int TrailingWhitespaceLength => _trailing + _newline;
        public override int DependentLength => _dependent;
        public override int NewlineLength => _newline;
        public override double Start => 0;
        public override double Width => _widthWithoutTrailing;
        public override double WidthIncludingTrailingWhitespace => _width;
        public override double Height => _height;
        public override double TextHeight => _height;
        public override double Extent => _ink.IsEmpty ? 0 : _ink.Height;
        public override double Baseline => _baseline;
        public override double TextBaseline => _baseline;
        public override double MarkerBaseline => 0;
        public override double MarkerHeight => 0;
        public override double OverhangLeading => _ink.IsEmpty ? 0 : _ink.Left - Start;
        public override double OverhangTrailing => _ink.IsEmpty ? 0 : Start + Width - _ink.Right;
        public override double OverhangAfter => _ink.IsEmpty ? 0 : _ink.Bottom - Height;
    }
}
#endif
