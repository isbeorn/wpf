// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.Portability.Text.Unicode;

namespace MS.Internal.TextFormatting.Portable
{
    internal sealed class PortableParagraph
    {
        internal sealed record Segment(int Start, int Length, TextRun Source, GlyphTypeface Typeface, HarfBuzzSharp.Script Script, int Level);
        internal TextSource Source { get; }
        internal int First { get; }
        internal string Text { get; }
        internal IList<PortableTextFormatter.Run> Runs { get; }
        internal int NewlineLength { get; }
        internal IList<TextSpan<TextRun>> Terminators { get; }
        internal int BaseLevel { get; }
        internal int[] GraphemeStarts { get; }
        internal (int Measure, int Wrap)[] Breaks { get; }
        internal int[] Levels { get; }
        private readonly BidiClass[] _classes;
        private readonly HarfBuzzSharp.Script[] _scripts;

        internal PortableParagraph(TextSource source, int first, string text, IList<PortableTextFormatter.Run> runs,
            int newline, IList<TextSpan<TextRun>> terminators, FlowDirection direction)
        {
            Source = source;
            First = first;
            Text = text;
            Runs = runs;
            NewlineLength = newline;
            Terminators = terminators;
            BaseLevel = direction == FlowDirection.RightToLeft ? 1 : 0;
            var data = new BidiData { ParagraphEmbeddingLevel = (sbyte)BaseLevel };
            var algorithm = new BidiAlgorithm();
            if (text.Length != 0)
            {
                data.Append(text);
                algorithm.Process(data);
            }
            Levels = new int[text.Length];
            _classes = new BidiClass[text.Length];
            int scalar = 0, offset = 0;
            foreach (Rune rune in text.EnumerateRunes())
            {
                for (int index = 0; index < rune.Utf16SequenceLength; index++)
                {
                    Levels[offset + index] = algorithm.ResolvedLevels[scalar];
                    _classes[offset + index] = data.Classes[scalar];
                }
                offset += rune.Utf16SequenceLength;
                scalar++;
            }
            var graphemes = new List<int>();
            var graphemeEnumerator = new GraphemeEnumerator(text);
            while (graphemeEnumerator.MoveNext(out Grapheme grapheme)) graphemes.Add(grapheme.Offset);
            graphemes.Add(text.Length);
            GraphemeStarts = graphemes.ToArray();
            _scripts = new HarfBuzzSharp.Script[text.Length];
            HarfBuzzSharp.Script previousScript = HarfBuzzSharp.Script.Common;
            for (int index = 0; index + 1 < GraphemeStarts.Length; index++)
            {
                int position = GraphemeStarts[index];
                var script = HarfBuzzSharp.Script.Common;
                foreach (Rune rune in text.AsSpan(position, GraphemeStarts[index + 1] - position).EnumerateRunes())
                {
                    var candidate = HarfBuzzSharp.UnicodeFunctions.Default.GetScript(rune.Value);
                    if (IsStrongScript(candidate)) { script = candidate; break; }
                }
                if (!IsStrongScript(script)) script = previousScript;
                _scripts[position] = script;
                previousScript = script;
            }
            HarfBuzzSharp.Script followingScript = HarfBuzzSharp.Script.Common;
            for (int index = GraphemeStarts.Length - 2; index >= 0; index--)
            {
                int position = GraphemeStarts[index];
                if (IsStrongScript(_scripts[position])) followingScript = _scripts[position];
                else _scripts[position] = followingScript;
            }
            for (int index = 1; index < runs.Count; index++)
                if (Array.BinarySearch(GraphemeStarts, runs[index].Start) < 0 &&
                    !CompatibleProperties(runs[index - 1].Value.Properties, runs[index].Value.Properties))
                    throw new NotSupportedException("A portable text run cannot split a grapheme cluster across formatting properties.");
            var breaks = new List<(int Measure, int Wrap)>();
            var breakEnumerator = new LineBreakEnumerator(text);
            while (breakEnumerator.MoveNext(out LineBreak item))
                if (Array.BinarySearch(GraphemeStarts, item.PositionWrap) >= 0)
                    breaks.Add((item.PositionMeasure, item.PositionWrap));
            if (breaks.Count == 0 || breaks[breaks.Count - 1].Wrap != text.Length)
                breaks.Add((text.Length, text.Length));
            Breaks = breaks.ToArray();
        }

        internal List<Segment> Segments(int start, int length)
        {
            int end = start + length;
            int trailing = end;
            while (trailing > start && IsLineReset(_classes[trailing - 1])) trailing--;
            var segments = new List<Segment>();
            int first = Array.BinarySearch(GraphemeStarts, start);
            int sourceIndex = FindRun(start);
            for (int index = first; index + 1 < GraphemeStarts.Length && GraphemeStarts[index] < end; index++)
            {
                int position = GraphemeStarts[index];
                int next = GraphemeStarts[index + 1];
                while (position >= Runs[sourceIndex].Start + Runs[sourceIndex].Length) sourceIndex++;
                var source = Runs[sourceIndex].Value;
                HarfBuzzSharp.Script script = _scripts[position];
                int level = position >= trailing ? BaseLevel : Levels[position];
                GlyphTypeface face = PortableFontFamily.ResolveGrapheme(source.Properties, Text.Substring(position, next - position));
                Segment last = segments.Count == 0 ? null : segments[segments.Count - 1];
                if (last != null && last.Level == level && last.Script == script && CompatibleProperties(last.Source.Properties, source.Properties) &&
                    ReferenceEquals(last.Typeface, face))
                    segments[segments.Count - 1] = last with { Length = next - last.Start };
                else
                    segments.Add(new Segment(position, next - position, source, face, script, level));
            }
            return segments;
        }

        internal int FindRun(int position)
        {
            int low = 0, high = Runs.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (Runs[middle].Start + Runs[middle].Length <= position) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        internal static bool IsStrongScript(HarfBuzzSharp.Script script) => script != HarfBuzzSharp.Script.Common &&
            script != HarfBuzzSharp.Script.Inherited && script != HarfBuzzSharp.Script.Unknown;

        private static bool CompatibleProperties(TextRunProperties left, TextRunProperties right) => ReferenceEquals(left, right) ||
            Equals(left.Typeface, right.Typeface) && left.FontRenderingEmSize == right.FontRenderingEmSize &&
            left.FontHintingEmSize == right.FontHintingEmSize && Equals(left.CultureInfo, right.CultureInfo) &&
            left.BaselineAlignment == right.BaselineAlignment && left.PixelsPerDip == right.PixelsPerDip &&
            CompatibleBrush(left.ForegroundBrush, right.ForegroundBrush) && CompatibleBrush(left.BackgroundBrush, right.BackgroundBrush) &&
            (ReferenceEquals(left.TextDecorations, right.TextDecorations) ||
                (left.TextDecorations == null || left.TextDecorations.Count == 0) && (right.TextDecorations == null || right.TextDecorations.Count == 0)) &&
            (ReferenceEquals(left.TextEffects, right.TextEffects) ||
                (left.TextEffects == null || left.TextEffects.Count == 0) && (right.TextEffects == null || right.TextEffects.Count == 0)) &&
            (ReferenceEquals(left.TypographyProperties, right.TypographyProperties) ||
                (left.TypographyProperties == null || PortableTextLine.IsDefaultTypography(left.TypographyProperties)) &&
                (right.TypographyProperties == null || PortableTextLine.IsDefaultTypography(right.TypographyProperties))) &&
            (ReferenceEquals(left.NumberSubstitution, right.NumberSubstitution) || left.NumberSubstitution != null && right.NumberSubstitution != null &&
                left.NumberSubstitution.CultureSource == right.NumberSubstitution.CultureSource &&
                left.NumberSubstitution.Substitution == right.NumberSubstitution.Substitution &&
                Equals(left.NumberSubstitution.CultureOverride, right.NumberSubstitution.CultureOverride));

        private static bool CompatibleBrush(Brush left, Brush right) => ReferenceEquals(left, right) ||
            left is SolidColorBrush a && right is SolidColorBrush b && a.IsFrozen && b.IsFrozen &&
            a.Color == b.Color && a.Opacity == b.Opacity && a.Transform.Value == b.Transform.Value &&
            a.RelativeTransform.Value == b.RelativeTransform.Value;

        private static bool IsLineReset(BidiClass value) => value == BidiClass.WhiteSpace || value == BidiClass.BoundaryNeutral ||
            value == BidiClass.SegmentSeparator || value == BidiClass.LeftToRightEmbedding || value == BidiClass.RightToLeftEmbedding ||
            value == BidiClass.LeftToRightOverride || value == BidiClass.RightToLeftOverride || value == BidiClass.PopDirectionalFormat ||
            value == BidiClass.LeftToRightIsolate || value == BidiClass.RightToLeftIsolate || value == BidiClass.FirstStrongIsolate || value == BidiClass.PopDirectionalIsolate;

        internal static void Reorder<T>(List<T> runs, Func<T, int> level)
        {
            if (runs.Count == 0) return;
            int maximum = runs.Max(level), minimum = runs.Where(item => (level(item) & 1) != 0).Select(level).DefaultIfEmpty(maximum + 1).Min();
            for (int current = maximum; current >= minimum; current--)
            {
                for (int first = 0; first < runs.Count; first++)
                {
                    if (level(runs[first]) < current) continue;
                    int end = first + 1;
                    while (end < runs.Count && level(runs[end]) >= current) end++;
                    runs.Reverse(first, end - first);
                    first = end - 1;
                }
            }
        }
    }

    internal sealed record PortableParagraphContinuation(PortableParagraph Paragraph, int Next);
}
#endif
