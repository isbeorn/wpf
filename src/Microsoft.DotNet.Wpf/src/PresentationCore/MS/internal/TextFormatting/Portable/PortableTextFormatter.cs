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
                var read = Read(source, first);
                input = new PortableParagraph(source, first, read.Text, read.Runs, read.NewlineLength, read.Terminators, properties.FlowDirection);
            }
            int start = first - input.First;
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
                bool Fits(int index) => Measure(input, start, Math.Max(start, input.Breaks[index].Measure) - start, source.PixelsPerDip) <= width;
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
            if ((long)first + length + newline > int.MaxValue)
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
            var input = new PortableParagraph(source, first, read.Text, read.Runs, read.NewlineLength, read.Terminators, properties.FlowDirection);
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

        private double Measure(PortableParagraph input, int first, int length, double pixelsPerDip)
        {
            double width = 0;
            foreach (var segment in input.Segments(first, length))
                width += PortableTextLine.Shape(input.Text, segment, pixelsPerDip, _mode, first, length).Width;
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
            if ((previous != null && (previous.BreakRecord != IntPtr.Zero || previous.TextModifierScope != null)) ||
                properties.TextAlignment != TextAlignment.Left ||
                properties.LineHeight != 0 || properties.Indent != 0 || properties.ParagraphIndent != 0 || properties.TextMarkerProperties != null ||
                (properties.Tabs != null && properties.Tabs.Count != 0) || (properties.TextDecorations != null && properties.TextDecorations.Count != 0) ||
                properties.AlwaysCollapsible)
                throw new NotSupportedException("Portable text supports horizontal left-aligned greedy paragraphs without markers, tabs, decorations or custom line heights.");
        }

        private static Input Read(TextSource source, int first)
        {
            var text = new StringBuilder();
            var runs = new List<Run>();
            while (text.Length < 32768)
            {
                TextRun run = source.GetTextRun(checked(first + text.Length));
                if (run == null || run.Length <= 0)
                    throw new ArgumentException("TextSource must return a nonempty run or paragraph terminator.", nameof(source));
                if (run is TextEndOfLine)
                {
                    if (run.Length > 32767 - text.Length)
                        throw new NotSupportedException("Portable text lines currently have a 32767 UTF-16 code-unit limit.");
                    return new Input(text.ToString(), runs, run.Length, new[] { new TextSpan<TextRun>(run.Length, run) });
                }
                if (!(run is TextCharacters))
                    throw new NotSupportedException("Portable text currently accepts TextCharacters and text line/paragraph terminators.");
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
                            TextRun next = source.GetTextRun(checked(first + text.Length + 1));
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
                            runs.Add(new Run(start, index, run));
                        return new Input(text.ToString(), runs, newline, terminators);
                    }
                    if (character == '\t')
                        throw new NotSupportedException("Portable paragraph tab stops are not implemented.");
                    if (character == '\u00ad')
                        throw new NotSupportedException("Portable discretionary hyphen insertion is not implemented.");
                    text.Append(character);
                    if (text.Length >= 32768)
                        throw new NotSupportedException("Portable text lines currently have a 32767 UTF-16 code-unit limit.");
                }
                runs.Add(new Run(start, run.Length, run));
            }
            throw new NotSupportedException("Portable paragraph is too long.");
        }

        internal sealed record Run(int Start, int Length, TextRun Value);
        private sealed record Input(string Text, List<Run> Runs, int NewlineLength, IList<TextSpan<TextRun>> Terminators = null);
    }
}
#endif
