// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            var input = Read(source, first);
            int length = input.Text.Length;
            bool truncated = false;
            if (properties.TextWrapping != TextWrapping.NoWrap && width > 0 && length > 0 &&
                Measure(input, length, source.PixelsPerDip) > width)
            {
                int[] boundaries = StringInfo.ParseCombiningCharacters(input.Text).Concat(new[] { length }).ToArray();
                int low = 1, high = boundaries.Length - 1;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    if (Measure(input, boundaries[middle], source.PixelsPerDip) <= width)
                        low = middle;
                    else
                        high = middle - 1;
                }
                length = boundaries[low];
                if (length < input.Text.Length && input.Text[length] != ' ' && input.Text[length - 1] != ' ')
                {
                    int wordEnd = input.Text.LastIndexOf(' ', length - 1) + 1;
                    if (wordEnd > 0)
                        length = wordEnd;
                    else if (properties.TextWrapping == TextWrapping.WrapWithOverflow)
                    {
                        int nextSpace = input.Text.IndexOf(' ', length);
                        length = nextSpace < 0 ? input.Text.Length : nextSpace;
                    }
                    else
                        truncated = true;
                }
                while (length < input.Text.Length && input.Text[length] == ' ')
                    length++;
            }
            int newline = length == input.Text.Length ? input.NewlineLength : 0;
            if ((long)first + length + newline > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(first), "The formatted source range exceeds Int32 source positions.");
            return new PortableTextLine(first, input.Text.Substring(0, length), newline,
                input.Runs, properties, source.PixelsPerDip, _mode, width, truncated, input.Terminators,
                input.Text.Length + input.NewlineLength - length - newline);
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
            var input = Read(source, first);
            double maximum = Measure(input, input.Text.Length, source.PixelsPerDip);
            double minimum = 0;
            int start = 0;
            for (int index = 0; index <= input.Text.Length; index++)
            {
                if (index == input.Text.Length || input.Text[index] == ' ')
                {
                    var segment = Slice(input, start, index - start);
                    minimum = Math.Max(minimum, Measure(segment, segment.Text.Length, source.PixelsPerDip));
                    start = index + 1;
                }
            }
            return new MinMaxParagraphWidth(minimum, maximum);
        }

        private double Measure(Input input, int length, double pixelsPerDip)
        {
            double width = 0;
            foreach (Run run in input.Runs)
            {
                int count = Math.Min(length - run.Start, run.Length);
                if (count > 0)
                    width += PortableTextLine.Shape(input.Text.Substring(run.Start, count), run.Value.Properties, pixelsPerDip, _mode).Width;
            }
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
                properties.FlowDirection != FlowDirection.LeftToRight || properties.TextAlignment != TextAlignment.Left ||
                properties.LineHeight != 0 || properties.Indent != 0 || properties.ParagraphIndent != 0 || properties.TextMarkerProperties != null ||
                (properties.Tabs != null && properties.Tabs.Count != 0) || (properties.TextDecorations != null && properties.TextDecorations.Count != 0) ||
                properties.AlwaysCollapsible)
                throw new NotSupportedException("Portable text currently supports horizontal LTR, left-aligned greedy paragraphs without markers, tabs, decorations or custom line heights.");
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
                    if (character == '\r' || character == '\n')
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
                    if (character == '\t' || (character >= '\u0590' && character <= '\u08ff') ||
                        (character >= '\u202a' && character <= '\u202e') || (character >= '\u2066' && character <= '\u2069'))
                        throw new NotSupportedException("Tabs, RTL scripts and bidi controls require the full portable paragraph adapter.");
                    text.Append(character);
                    if (text.Length >= 32768)
                        throw new NotSupportedException("Portable text lines currently have a 32767 UTF-16 code-unit limit.");
                }
                runs.Add(new Run(start, run.Length, run));
            }
            throw new NotSupportedException("Portable paragraph is too long.");
        }

        private static Input Slice(Input input, int first, int length) => new Input(input.Text.Substring(first, length),
            input.Runs.Where(run => run.Start < first + length && run.Start + run.Length > first)
                .Select(run => new Run(Math.Max(0, run.Start - first), Math.Min(run.Start + run.Length, first + length) - Math.Max(run.Start, first), run.Value)).ToList(), 0);

        internal sealed record Run(int Start, int Length, TextRun Value);
        private sealed record Input(string Text, List<Run> Runs, int NewlineLength, IList<TextSpan<TextRun>> Terminators = null);
    }
}
#endif
