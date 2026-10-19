// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace MS.Internal.TextFormatting.Portable
{
    internal sealed class PortableTextFormatter : TextFormatter
    {
        private readonly TextFormattingMode _mode;
        private bool _disposed;

        internal PortableTextFormatter(TextFormattingMode mode) { _mode = mode; }

        public override void Dispose() { _disposed = true; }

        public override TextLine FormatLine(TextSource source, int first, double width,
            TextParagraphProperties properties, TextLineBreak previous) =>
            FormatLine(source, first, width, properties, previous, null);

        public override TextLine FormatLine(TextSource source, int first, double width,
            TextParagraphProperties properties, TextLineBreak previous, TextRunCache cache)
        {
            Validate(source, first, width, properties, previous);
            PortableParagraph input;
            var continuation = previous?.PortableContinuation;
            if (continuation != null)
            {
                input = continuation.Paragraph;
                if (!ReferenceEquals(source, input.Source) || first != continuation.Next ||
                    input.BaseLevel != (properties.FlowDirection == FlowDirection.RightToLeft ? 1 : 0))
                    throw new ArgumentException("The portable line break belongs to a different source, source position or paragraph direction.", nameof(previous));
            }
            else
            {
                var read = Read(source, first, previous?.TextModifierScope);
                input = new PortableParagraph(source, first, read.Text, read.Runs, read.NewlineLength, read.Terminators, read.SourceLength, properties.FlowDirection, read.ContinuationScope);
            }
            int start = input.TextOffset(first - input.First);
            int length = input.Text.Length - start;
            bool truncated = false;
            if (properties.TextWrapping != TextWrapping.NoWrap && width > 0 && length > 0)
            {
                int low = 0, high = input.Breaks.Length;
                while (low < high)
                {
                    int middle = (low + high) / 2;
                    if (input.Breaks[middle].Wrap <= start) low = middle + 1;
                    else high = middle;
                }
                int firstBreak = low;
                int fitting = firstBreak - 1, failing = input.Breaks.Length;
                int candidate = firstBreak, step = 1;
                bool Fits(int index) => Measure(input, start, Math.Max(start, input.Breaks[index].Measure) - start, source.PixelsPerDip, width) <= width;
                while (true)
                {
                    if (!Fits(candidate)) { failing = candidate; break; }
                    fitting = candidate;
                    if (candidate == input.Breaks.Length - 1) break;
                    candidate = Math.Min(input.Breaks.Length - 1, firstBreak + step);
                    step *= 2;
                }
                while (fitting + 1 < failing)
                {
                    int middle = (fitting + failing) / 2;
                    if (Fits(middle)) fitting = middle;
                    else failing = middle;
                }
                int wrap = fitting < firstBreak ? start : input.Breaks[fitting].Wrap;
                if (wrap == start)
                {
                    if (properties.TextWrapping == TextWrapping.WrapWithOverflow)
                        wrap = input.Breaks[firstBreak].Wrap;
                    else
                    {
                        low = Array.BinarySearch(input.GraphemeStarts, start) + 1;
                        high = Array.BinarySearch(input.GraphemeStarts, input.Breaks[firstBreak].Wrap);
                        while (low < high)
                        {
                            int middle = (low + high + 1) / 2;
                            if (Measure(input, start, input.GraphemeStarts[middle] - start, source.PixelsPerDip) <= width) low = middle;
                            else high = middle - 1;
                        }
                        wrap = input.GraphemeStarts[low];
                        truncated = true;
                    }
                }
                length = wrap - start;
            }
            int newline = start + length == input.Text.Length ? input.NewlineLength : 0;
            if ((long)input.First + input.SourceOffset(start + length) + newline > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(first), "The formatted source range exceeds Int32 source positions.");
            return new PortableTextLine(input, start, length, newline, properties, source.PixelsPerDip, _mode, width, truncated);
        }

        internal override TextLine RecreateLine(TextSource source, int first, int length, double width,
            TextParagraphProperties properties, TextLineBreak previous, TextRunCache cache)
        {
            TextLine line = FormatLine(source, first, width, properties, previous, cache);
            if (line.Length != length)
            {
                line.Dispose();
                throw new NotSupportedException("The requested line length differs from portable greedy line breaking.");
            }
            return line;
        }

        internal override TextParagraphCache CreateParagraphCache(TextSource source, int first, double width,
            TextParagraphProperties properties, TextLineBreak previous, TextRunCache cache) =>
            throw new NotSupportedException("Portable optimal paragraph breaking is not implemented.");

        public override MinMaxParagraphWidth FormatMinMaxParagraphWidth(TextSource source, int first, TextParagraphProperties properties) =>
            FormatMinMaxParagraphWidth(source, first, properties, null);

        public override MinMaxParagraphWidth FormatMinMaxParagraphWidth(TextSource source, int first,
            TextParagraphProperties properties, TextRunCache cache)
        {
            Validate(source, first, double.MaxValue, properties, null);
            var read = Read(source, first);
            var input = new PortableParagraph(source, first, read.Text, read.Runs, read.NewlineLength, read.Terminators, read.SourceLength, properties.FlowDirection, read.ContinuationScope);
            double maximum = Measure(input, 0, input.Text.Length, source.PixelsPerDip);
            double minimum = 0;
            int start = 0;
            foreach (var item in input.Breaks)
            {
                minimum = Math.Max(minimum, Measure(input, start, Math.Max(start, item.Measure) - start, source.PixelsPerDip));
                start = item.Wrap;
            }
            return new MinMaxParagraphWidth(minimum, maximum);
        }

        private double Measure(PortableParagraph input, int first, int length, double pixelsPerDip, double available = double.PositiveInfinity)
        {
            double width = 0;
            foreach (var segment in input.Segments(first, length))
                width += PortableTextLine.Shape(input.Text, segment, pixelsPerDip, _mode, first, length, Math.Max(0, available - width)).Width;
            return width;
        }

        private void Validate(TextSource source, int first, double width, TextParagraphProperties properties, TextLineBreak previous)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(properties);
            ArgumentOutOfRangeException.ThrowIfNegative(first);
            if (double.IsNaN(width) || width < 0 || double.IsNaN(source.PixelsPerDip) || source.PixelsPerDip <= 0 || double.IsInfinity(source.PixelsPerDip))
                throw new ArgumentOutOfRangeException(nameof(width));
            if (properties.TextAlignment != TextAlignment.Left && !double.IsFinite(width))
                throw new ArgumentOutOfRangeException(nameof(width));
            if ((previous != null && previous.BreakRecord != IntPtr.Zero) ||
                (properties.TextAlignment != TextAlignment.Left && properties.TextAlignment != TextAlignment.Center && properties.TextAlignment != TextAlignment.Right) ||
                (properties.LineHeight != 0 && !double.IsNaN(properties.LineHeight)) || properties.Indent != 0 || properties.ParagraphIndent != 0 || properties.TextMarkerProperties != null ||
                (properties.Tabs != null && properties.Tabs.Count != 0) || (properties.TextDecorations != null && properties.TextDecorations.Count != 0) ||
                properties.AlwaysCollapsible)
                throw new NotSupportedException("Portable text supports horizontal Left, Center and Right greedy paragraphs without markers, tabs, decorations or custom line heights.");
        }

        private static Input Read(TextSource source, int first, TextModifierScope modifiers = null)
        {
            var text = new StringBuilder();
            var runs = new List<Run>();
            int consumed = 0;
            while (consumed < 32768)
            {
                TextRun run = source.GetTextRun(checked(first + consumed));
                if (run == null || run.Length <= 0)
                    throw new ArgumentException("TextSource must return a nonempty run or paragraph terminator.", nameof(source));
                if (run is TextEndOfLine)
                {
                    if (run.Length > 32767 - consumed)
                        throw new NotSupportedException("Portable text lines currently have a 32767 UTF-16 code-unit limit.");
                    return new Input(text.ToString(), runs, run.Length, consumed, new[] { new TextSpan<TextRun>(run.Length, run) }, run is TextEndOfParagraph ? null : modifiers);
                }
                if (run is TextHidden || run is TextModifier || run is TextEndOfSegment)
                {
                    if (run is TextModifier modifier)
                    {
                        if (modifier.HasDirectionalEmbedding)
                            throw new NotSupportedException("Portable text modifier directional embedding is not implemented.");
                        modifiers = new TextModifierScope(modifiers, modifier, checked(first + consumed));
                    }
                    else if (run is TextEndOfSegment && modifiers != null)
                        modifiers = modifiers.ParentScope;
                    if (run.Length > 32767 - consumed)
                        throw new NotSupportedException("Portable text lines currently have a 32767 source-symbol limit.");
                    runs.Add(new Run(text.Length, 0, run, consumed, run.Length));
                    consumed += run.Length;
                    continue;
                }
                if (run is TextEmbeddedObject embedded)
                {
                    if (embedded.Length != 1 || !embedded.HasFixedSize || embedded.BreakBefore != LineBreakCondition.BreakDesired || embedded.BreakAfter != LineBreakCondition.BreakDesired)
                        throw new NotSupportedException("Portable embedded text objects require one source symbol, fixed size and BreakDesired before and after the object.");
                    if (run.Length > 32767 - consumed)
                        throw new NotSupportedException("Portable text lines currently have a 32767 source-symbol limit.");
                    TextRunProperties objectProperties = modifiers == null ? run.Properties : modifiers.ModifyProperties(run.Properties);
                    runs.Add(new Run(text.Length, 1, run, consumed, run.Length, objectProperties));
                    text.Append('\ufffc');
                    consumed += run.Length;
                    continue;
                }
                if (!(run is TextCharacters))
                    throw new NotSupportedException("Portable text currently accepts text characters, hidden/modifier runs, fixed-size embedded objects and text line/paragraph terminators.");
                TextRunProperties effective = modifiers == null ? run.Properties : modifiers.ModifyProperties(run.Properties);
                if (effective == null) throw new ArgumentException("A text modifier must return text run properties.", nameof(source));
                CharacterBufferReference buffer = run.CharacterBufferReference;
                int start = text.Length;
                for (int index = 0; index < run.Length; index++)
                {
                    char character = buffer.CharacterBuffer[buffer.OffsetToFirstChar + index];
                    if (character == '\r' || character == '\n' || character == '\u0085' || character == '\u000b' ||
                        character == '\u000c' || character == '\u2028' || character == '\u2029')
                    {
                        int newline = character == '\r' && index + 1 < run.Length && buffer.CharacterBuffer[buffer.OffsetToFirstChar + index + 1] == '\n' ? 2 : 1;
                        var terminators = new List<TextSpan<TextRun>> { new TextSpan<TextRun>(newline, run) };
                        if (character == '\r' && index + 1 == run.Length)
                        {
                            TextRun next = source.GetTextRun(checked(first + consumed + index + 1));
                            if (next is TextCharacters && next.Length > 0)
                            {
                                CharacterBufferReference nextBuffer = next.CharacterBufferReference;
                                if (nextBuffer.CharacterBuffer[nextBuffer.OffsetToFirstChar] == '\n')
                                {
                                    newline = 2;
                                    terminators.Add(new TextSpan<TextRun>(1, next));
                                }
                            }
                        }
                        if (index != 0)
                            runs.Add(new Run(start, index, run, consumed, index, effective));
                        return new Input(text.ToString(), runs, newline, consumed + index, terminators, modifiers);
                    }
                    if (character == '\t')
                        throw new NotSupportedException("Portable paragraph tab stops are not implemented.");
                    if (character == '\u00ad')
                        throw new NotSupportedException("Portable discretionary hyphen insertion is not implemented.");
                    text.Append(character);
                    if (consumed + index + 1 >= 32768)
                        throw new NotSupportedException("Portable text lines currently have a 32767 UTF-16 code-unit limit.");
                }
                runs.Add(new Run(start, run.Length, run, consumed, run.Length, effective));
                consumed += run.Length;
            }
            throw new NotSupportedException("Portable paragraph is too long.");
        }

        internal sealed record Run(int Start, int Length, TextRun Value, int SourceStart, int SourceLength, TextRunProperties Properties = null)
        {
            internal TextRunProperties EffectiveProperties => Properties ?? Value.Properties;
        }
        private sealed record Input(string Text, List<Run> Runs, int NewlineLength, int SourceLength, IList<TextSpan<TextRun>> Terminators = null, TextModifierScope ContinuationScope = null);
    }
}
#endif
