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
        private readonly PortableParagraph _paragraph;
        private readonly double _drawingOffset;
        private readonly double _alignmentOffset;
        private readonly int _first;
        private readonly int _textStart;
        private readonly int _length;
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

        internal PortableTextLine(PortableParagraph paragraph, int start, int length, int newline,
            TextParagraphProperties properties, double pixelsPerDip, TextFormattingMode mode, double paragraphWidth, bool truncated)
            : base(pixelsPerDip)
        {
            _paragraph = paragraph;
            string text = paragraph.Text.Substring(start, length);
            _textStart = start;
            _first = paragraph.First + (start == 0 ? 0 : paragraph.SourceOffset(start));
            _length = paragraph.First + paragraph.SourceOffset(start + length) - _first;
            _text = text;
            _newline = newline;
            _dependent = paragraph.First + paragraph.SourceLength + paragraph.NewlineLength - _first - _length - newline;
            _truncated = truncated;
            var shapedRuns = new List<(PortableParagraph.Segment Source, Shaped Shape)>();
            foreach (var run in paragraph.Segments(start, length))
            {
                var shape = Shape(paragraph.Text, run, pixelsPerDip, mode, start, length);
                shapedRuns.Add((run, shape));
                double size = run.Source.Properties.FontRenderingEmSize;
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
                (run.Shape.Typeface.Height - run.Shape.Typeface.Baseline) * run.Source.Source.Properties.FontRenderingEmSize);
            _height = Math.Max(_height, _baseline + descent);
            var spans = new List<TextSpan<TextRun>>();
            foreach (var run in paragraph.Runs)
            {
                int count = Math.Min(paragraph.First + run.SourceStart + run.SourceLength, _first + _length) -
                    Math.Max(paragraph.First + run.SourceStart, _first);
                if (count > 0) spans.Add(new TextSpan<TextRun>(count, run.Value));
            }
            PortableParagraph.Reorder(shapedRuns, run => run.Source.Level);
            double measuredWidth = shapedRuns.Sum(run => run.Shape.Width);
            _drawingOffset = paragraph.BaseLevel == 1 && double.IsFinite(paragraphWidth) ? paragraphWidth - measuredWidth : 0;
            Rect ink = Rect.Empty;
            foreach (var item in shapedRuns)
            {
                var shape = item.Shape;
                TextRunProperties runProperties = item.Source.Source.Properties;
                bool rtl = (item.Source.Level & 1) != 0;
                double origin = _width + (rtl ? shape.Width : 0);
                var glyphs = GlyphRun.TryCreate(shape.Typeface, item.Source.Level, false, runProperties.FontRenderingEmSize,
                    (float)pixelsPerDip, shape.Glyphs, new Point(_drawingOffset + origin, _baseline), shape.Advances, shape.Offsets,
                    paragraph.Text.Substring(item.Source.Start, item.Source.Length).ToCharArray(), null, shape.Map, shape.Stops,
                    XmlLanguage.GetLanguage(runProperties.CultureInfo.IetfLanguageTag), mode);
                if (glyphs == null)
                    throw new InvalidOperationException("Portable shaped output is not a valid WPF GlyphRun.");
                _runs.Add(new LineRun(item.Source.Start - start, item.Source.Length, item.Source.Source, glyphs, _width, shape.Width));
                Rect bounds = glyphs.ComputeInkBoundingBox();
                if (!bounds.IsEmpty)
                {
                    bounds.Offset(origin, _baseline);
                    ink.Union(bounds);
                }
                _width += shape.Width;
            }
            if (newline != 0)
                spans.AddRange(paragraph.Terminators);
            _spans = spans.AsReadOnly();
            _ink = ink;
            while (_trailing < text.Length && char.IsWhiteSpace(text[text.Length - _trailing - 1]))
                _trailing++;
            double whitespaceWidth = 0;
            foreach (var run in _runs)
            {
                int trailingStart = Math.Max(run.Start, text.Length - _trailing);
                if (trailingStart < run.Start + run.Length)
                    whitespaceWidth += run.Width - run.Glyphs.GetDistanceFromCaretCharacterHit(new CharacterHit(trailingStart - run.Start, 0));
            }
            _widthWithoutTrailing = _width - whitespaceWidth;
            if (properties.TextAlignment != TextAlignment.Left)
            {
                _alignmentOffset = (paragraphWidth - _widthWithoutTrailing) / (properties.TextAlignment == TextAlignment.Center ? 2 : 1);
                if (mode == TextFormattingMode.Display)
                    _alignmentOffset = Math.Round(_alignmentOffset * pixelsPerDip) / pixelsPerDip;
            }
            _overflow = paragraphWidth > 0 && _widthWithoutTrailing > paragraphWidth;
        }

        internal static Shaped Shape(string paragraph, PortableParagraph.Segment segment, double pixelsPerDip, TextFormattingMode mode, int lineStart, int lineLength)
        {
            TextRunProperties properties = segment.Source.Properties;
            if ((properties.TextDecorations != null && properties.TextDecorations.Count != 0) ||
                (properties.TextEffects != null && properties.TextEffects.Count != 0))
                throw new NotSupportedException("Portable run decorations and effects are not implemented.");
            if (properties.TypographyProperties != null && !IsDefaultTypography(properties.TypographyProperties))
                throw new NotSupportedException("Portable custom OpenType typography features are not implemented.");
            if (properties.BaselineAlignment != BaselineAlignment.Baseline)
                throw new NotSupportedException("Portable inline baseline alignment outside Baseline is not implemented.");
            var digits = new DigitState();
            digits.SetTextRunProperties(properties);
            if (digits.RequiresNumberSubstitution)
                throw new NotSupportedException("Portable contextual, native-national and traditional digit substitution are not implemented.");
            GlyphTypeface typeface = segment.Typeface;
            string text = paragraph.Substring(segment.Start, segment.Length);
            bool rtl = (segment.Level & 1) != 0;
            using (var font = new SKFont(typeface.PortableFontFace.Typeface, (float)properties.FontRenderingEmSize))
            using (var shaper = new SKShaper(typeface.PortableFontFace.Typeface))
            using (var buffer = new TextBuffer())
            {
                int contextOffset = segment.Start - lineStart;
                buffer.AddUtf16(paragraph.AsSpan(lineStart, lineLength), contextOffset, segment.Length);
                buffer.Flags = (contextOffset == 0 ? HarfBuzzSharp.BufferFlags.BeginningOfText : 0) |
                    (contextOffset + segment.Length == lineLength ? HarfBuzzSharp.BufferFlags.EndOfText : 0);
                buffer.Direction = rtl ? HarfBuzzSharp.Direction.RightToLeft : HarfBuzzSharp.Direction.LeftToRight;
                buffer.Script = segment.Script;
                buffer.Language = new HarfBuzzSharp.Language(properties.CultureInfo);
                buffer.GuessSegmentProperties();
                var result = shaper.Shape(buffer, font);
                if (result.Codepoints.Length == 0 || result.Codepoints.Length > ushort.MaxValue || result.Codepoints.Any(glyph => glyph > ushort.MaxValue))
                    throw new NotSupportedException("The portable font returned unsupported glyph identifiers.");
                var positions = buffer.GlyphPositions;
                double totalUnits = positions.Sum(position => (double)position.XAdvance);
                double scale = totalUnits == 0 ? 0 : result.Width / totalUnits;
                int count = result.Codepoints.Length;
                var glyphs = new ushort[count];
                var advances = new double[count];
                var offsets = new Point[count];
                var clusters = new int[count];
                double visualPen = 0;
                for (int original = 0; original < count; original++)
                {
                    int index = rtl ? count - original - 1 : original;
                    double advance = positions[original].XAdvance * scale;
                    double xOffset = result.Points[original].X - visualPen;
                    visualPen += advance;
                    if (mode == TextFormattingMode.Display) advance = Math.Round(advance * pixelsPerDip) / pixelsPerDip;
                    glyphs[index] = checked((ushort)result.Codepoints[original]);
                    clusters[index] = checked((int)result.Clusters[original]) - contextOffset;
                    if (glyphs[index] == 0)
                    {
                        int scalar = char.ConvertToUtf32(text, clusters[index]);
                        if (!MS.Internal.Portability.Text.Unicode.UnicodeData.GetIsDefaultIgnorable((uint)scalar))
                            throw new NotSupportedException("The selected portable font cannot shape this grapheme cluster.");
                        glyphs[index] = typeface.PortableFontFace.GetGlyph(' ');
                    }
                    advances[index] = advance;
                    double nominal = typeface.PortableFontFace.GetAdvance(glyphs[index]) * properties.FontRenderingEmSize;
                    if (mode == TextFormattingMode.Display) nominal = Math.Round(nominal * pixelsPerDip) / pixelsPerDip;
                    offsets[index] = new Point(rtl ? advance - nominal - xOffset : xOffset, -result.Points[original].Y);
                }
                var map = new ushort[text.Length];
                for (int glyph = 0; glyph < count; glyph++)
                {
                    if (glyph != 0 && clusters[glyph] == clusters[glyph - 1]) continue;
                    int next = glyph + 1;
                    while (next < count && clusters[next] == clusters[glyph]) next++;
                    int end = next < count ? clusters[next] : text.Length;
                    if (clusters[glyph] < 0 || end < clusters[glyph] || end > text.Length)
                        throw new InvalidOperationException("HarfBuzz returned nonmonotonic paragraph clusters.");
                    for (int character = clusters[glyph]; character < end; character++) map[character] = checked((ushort)glyph);
                }
                var stops = new bool[text.Length + 1];
                var enumerator = new MS.Internal.Portability.Text.Unicode.GraphemeEnumerator(text);
                while (enumerator.MoveNext(out var grapheme)) stops[grapheme.Offset] = true;
                stops[text.Length] = true;
                return new Shaped(typeface, glyphs, advances, offsets, map, stops, advances.Sum());
            }
        }

        internal static bool IsDefaultTypography(TextRunTypographyProperties value) =>
            value.StandardLigatures && value.ContextualLigatures && value.ContextualAlternates && value.Kerning &&
            !value.DiscretionaryLigatures && !value.HistoricalLigatures && !value.HistoricalForms && !value.CapitalSpacing &&
            !value.CaseSensitiveForms && !value.SlashedZero && !value.MathematicalGreek && !value.EastAsianExpertForms &&
            !value.StylisticSet1 && !value.StylisticSet2 && !value.StylisticSet3 && !value.StylisticSet4 && !value.StylisticSet5 &&
            !value.StylisticSet6 && !value.StylisticSet7 && !value.StylisticSet8 && !value.StylisticSet9 && !value.StylisticSet10 &&
            !value.StylisticSet11 && !value.StylisticSet12 && !value.StylisticSet13 && !value.StylisticSet14 && !value.StylisticSet15 &&
            !value.StylisticSet16 && !value.StylisticSet17 && !value.StylisticSet18 && !value.StylisticSet19 && !value.StylisticSet20 &&
            value.Variants == FontVariants.Normal && value.Capitals == FontCapitals.Normal && value.Fraction == FontFraction.Normal &&
            value.NumeralStyle == FontNumeralStyle.Normal && value.NumeralAlignment == FontNumeralAlignment.Normal &&
            value.EastAsianWidths == FontEastAsianWidths.Normal && value.EastAsianLanguage == FontEastAsianLanguage.Normal &&
            value.StandardSwashes == 0 && value.ContextualSwashes == 0 && value.StylisticAlternates == 0 && value.AnnotationAlternates == 0;
        private void Verify() { ObjectDisposedException.ThrowIf(_disposed, this); }
        public override void Dispose() { _disposed = true; }
        public override void Draw(DrawingContext context, Point origin, InvertAxes inversion)
        {
            Verify();
            ArgumentNullException.ThrowIfNull(context);
            if (inversion != InvertAxes.None)
                throw new NotSupportedException("Portable inverted text drawing is not implemented.");
            context.PushTransform(new TranslateTransform(origin.X + (_paragraph.BaseLevel == 0 ? _alignmentOffset : -_alignmentOffset), origin.Y));
            try
            {
                foreach (LineRun run in _runs)
                {
                    if (run.Source.Properties.BackgroundBrush != null)
                        context.DrawRectangle(run.Source.Properties.BackgroundBrush, null, new Rect(_drawingOffset + run.Offset, 0, run.Width, _height));
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
        public override TextLineBreak GetTextLineBreak() { Verify(); return _newline == 0 ?
            new TextLineBreak(new PortableParagraphContinuation(_paragraph, _first + _length)) : null; }
        public override IList<TextSpan<TextRun>> GetTextRunSpans() { Verify(); return _spans; }
        public override IEnumerable<IndexedGlyphRun> GetIndexedGlyphRuns()
        {
            Verify();
            return _runs.OrderBy(run => run.Start).Select(run => new IndexedGlyphRun(SourcePosition(run.Start), SourcePosition(run.Start + run.Length, end: true) - SourcePosition(run.Start), run.Glyphs)).ToArray();
        }

        public override CharacterHit GetCharacterHitFromDistance(double distance)
        {
            Verify();
            if (!double.IsFinite(distance))
                throw new ArgumentOutOfRangeException(nameof(distance));
            if (_runs.Count == 0)
                return new CharacterHit(_first, 0);
            distance -= _alignmentOffset;
            if (_paragraph.BaseLevel == 1) distance = _width - distance;
            LineRun run = _runs.Last();
            foreach (LineRun candidate in _runs)
                if (distance < candidate.Offset + candidate.Width) { run = candidate; break; }
            double local = (run.Glyphs.BidiLevel & 1) == 0 ? distance - run.Offset : run.Offset + run.Width - distance;
            CharacterHit hit = run.Glyphs.GetCaretCharacterHitFromDistance(local, out _);
            int first = SourcePosition(run.Start + hit.FirstCharacterIndex);
            return new CharacterHit(first, hit.TrailingLength == 0 ? 0 :
                SourcePosition(run.Start + hit.FirstCharacterIndex + hit.TrailingLength, end: true) - first);
        }
        public override double GetDistanceFromCharacterHit(CharacterHit hit)
        {
            Verify();
            ValidateHit(hit);
            int index = _paragraph.TextOffset(hit.FirstCharacterIndex - _paragraph.First) - _textStart;
            if (index >= _text.Length) return _width + _alignmentOffset;
            int end = _paragraph.TextOffset(hit.FirstCharacterIndex + hit.TrailingLength - _paragraph.First) - _textStart;
            return ParagraphDistance(Distance(index, end - index));
        }
        private int SourcePosition(int textOffset, bool end = false) => _paragraph.First +
            (end ? _paragraph.SourceEnd(_textStart + textOffset) : _paragraph.SourceOffset(_textStart + textOffset));
        private double ParagraphDistance(double physical) => _alignmentOffset + (_paragraph.BaseLevel == 0 ? physical : _width - physical);
        private double Distance(int index, int trailing = 0)
        {
            foreach (LineRun run in _runs)
                if (index >= run.Start && index < run.Start + run.Length)
                    return RunDistance(run, index, trailing);
            LineRun end = _runs.FirstOrDefault(run => run.Start + run.Length == _text.Length);
            if (end != null) return RunDistance(end, _text.Length);
            return _width;
        }
        private static double RunDistance(LineRun run, int index, int trailing = 0)
        {
            double local = run.Glyphs.GetDistanceFromCaretCharacterHit(new CharacterHit(index - run.Start, trailing));
            return (run.Glyphs.BidiLevel & 1) == 0 ? run.Offset + local : run.Offset + run.Width - local;
        }
        private static double SelectionDistance(LineRun run, int index)
        {
            int local = index - run.Start;
            // A grapheme's selection advance belongs to its first source code unit.
            while (local < run.Length && !run.Glyphs.CaretStops[local]) local++;
            return RunDistance(run, run.Start + local);
        }
        private void ValidateHit(CharacterHit hit)
        {
            if (hit.FirstCharacterIndex < _first || hit.TrailingLength < 0 || (long)hit.FirstCharacterIndex + hit.TrailingLength > (long)_first + Length)
                throw new ArgumentOutOfRangeException(nameof(hit));
        }
        private int[] Stops() => _runs.SelectMany(run => run.Glyphs.CaretStops.Select((stop, index) => (stop, index))
            .Where(item => item.stop).Select(item => run.Start + item.index)).Distinct().OrderBy(value => value).ToArray();
        public override CharacterHit GetNextCaretCharacterHit(CharacterHit hit)
        {
            Verify(); ValidateHit(hit);
            int position = _paragraph.TextOffset(hit.FirstCharacterIndex + hit.TrailingLength - _paragraph.First) - _textStart;
            int previous = 0;
            foreach (int stop in Stops())
            {
                if (stop > position)
                {
                    int first = SourcePosition(previous);
                    return new CharacterHit(first, SourcePosition(stop, end: true) - first);
                }
                previous = stop;
            }
            return hit;
        }
        public override CharacterHit GetPreviousCaretCharacterHit(CharacterHit hit)
        {
            Verify(); ValidateHit(hit);
            int position = _paragraph.TextOffset(hit.FirstCharacterIndex + hit.TrailingLength - _paragraph.First) - _textStart;
            int[] stops = Stops();
            for (int index = stops.Length - 1; index >= 0; index--)
                if (stops[index] < position)
                    return new CharacterHit(SourcePosition(stops[index]), 0);
            return hit;
        }
        public override CharacterHit GetBackspaceCaretCharacterHit(CharacterHit hit) => GetPreviousCaretCharacterHit(hit);
        public override IList<TextBounds> GetTextBounds(int first, int length)
        {
            Verify();
            if (length < 0)
            {
                long start = (long)first + length;
                if (start < int.MinValue || length == int.MinValue) throw new ArgumentOutOfRangeException(nameof(length));
                first = (int)start;
                length = -length;
            }
            if (length < 0 || first < _first || first > _first + Length || (long)first + length > _first + Length)
                throw new ArgumentOutOfRangeException(nameof(length));
            var bounds = new List<TextBounds>();
            foreach (LineRun run in _runs)
            {
                int start = Math.Max(_paragraph.TextOffset(first - _paragraph.First) - _textStart, run.Start),
                    end = Math.Min(_paragraph.TextOffset(first + length - _paragraph.First) - _textStart, run.Start + run.Length);
                if (end > start)
                {
                    double left = ParagraphDistance(SelectionDistance(run, start)), right = ParagraphDistance(SelectionDistance(run, end));
                    var rectangle = new Rect(Math.Min(left, right), 0, Math.Abs(right - left), _height);
                    int paragraphOffset = _textStart;
                    var sourceBounds = new List<TextRunBounds>();
                    for (int index = _paragraph.FindRun(paragraphOffset + start);
                        index < _paragraph.Runs.Count && _paragraph.Runs[index].Start < paragraphOffset + end; index++)
                    {
                        var source = _paragraph.Runs[index];
                        if (source.Length == 0) continue;
                        int sourceStart = Math.Max(start, source.Start - paragraphOffset);
                        int sourceEnd = Math.Min(end, source.Start + source.Length - paragraphOffset);
                        double sourceLeft = ParagraphDistance(SelectionDistance(run, sourceStart));
                        double sourceRight = ParagraphDistance(SelectionDistance(run, sourceEnd));
                        sourceBounds.Add(new TextRunBounds(new Rect(Math.Min(sourceLeft, sourceRight), 0, Math.Abs(sourceRight - sourceLeft), _height),
                            SourcePosition(sourceStart), SourcePosition(sourceEnd, end: true), source.Value));
                    }
                    bounds.Add(new TextBounds(rectangle, (run.Glyphs.BidiLevel & 1) == 0 ? FlowDirection.LeftToRight : FlowDirection.RightToLeft,
                        sourceBounds));
                }
            }
            if (bounds.Count == 0)
                bounds.Add(new TextBounds(new Rect(GetDistanceFromCharacterHit(new CharacterHit(first, 0)), 0, 0, _height),
                    _paragraph.BaseLevel == 0 ? FlowDirection.LeftToRight : FlowDirection.RightToLeft, null));
            return bounds;
        }

        public override bool HasOverflowed => _overflow;
        public override bool HasCollapsed => false;
        public override bool IsTruncated => _truncated;
        public override int Length => _length + _newline;
        public override int TrailingWhitespaceLength => _text.Length == 0 ? _length + _newline :
            _trailing == 0 ? _newline : _first + _length - SourcePosition(_text.Length - _trailing) + _newline;
        public override int DependentLength => _dependent;
        public override int NewlineLength => _newline;
        public override double Start => _alignmentOffset;
        public override double Width => _widthWithoutTrailing;
        public override double WidthIncludingTrailingWhitespace => _width;
        public override double Height => _height;
        public override double TextHeight => _height;
        public override double Extent => _ink.IsEmpty ? 0 : _ink.Height;
        public override double Baseline => _baseline;
        public override double TextBaseline => _baseline;
        public override double MarkerBaseline => 0;
        public override double MarkerHeight => 0;
        public override double OverhangLeading => _ink.IsEmpty ? 0 : _paragraph.BaseLevel == 0 ? _ink.Left : _width - _ink.Right;
        public override double OverhangTrailing => _ink.IsEmpty ? 0 : _paragraph.BaseLevel == 0 ? Width - _ink.Right : _ink.Left - (_width - Width);
        public override double OverhangAfter => _ink.IsEmpty ? 0 : _ink.Bottom - Height;
    }
}
#endif
