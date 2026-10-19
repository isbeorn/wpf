// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.Text;

namespace MS.Internal.Documents
{
    internal sealed partial class PortableDocumentLayout : IDisposable
    {
        private readonly FlowDocument _document;
        private readonly TextFormatter _formatter;
        private readonly List<Line> _lines = new List<Line>();
        private readonly List<NavigationLine> _navigationLines = new List<NavigationLine>();
        internal ContainerVisual Visual { get; } = new ContainerVisual();
        internal Size Size { get; private set; }
        internal ReadOnlyCollection<TextSegment> Segments { get; }

        private PortableDocumentLayout(FlowDocument document)
        {
            _document = document;
            _formatter = TextFormatter.Create(TextOptions.GetTextFormattingMode(document));
            Segments = new List<TextSegment> { new TextSegment(document.ContentStart, document.ContentEnd, true) }.AsReadOnly();
        }

        internal static PortableDocumentLayout Format(FlowDocument document, double width, Thickness margin, PortableDocumentLayout previous)
        {
            var layout = new PortableDocumentLayout(document);
            try
            {
                double y = margin.Top;
                double previousBottom = 0;
                foreach (Block block in document.Blocks)
                {
                    var spacing = PtsHost.MbpInfo.FromElement(block, document.PixelsPerDip).Margin;
                    double top = spacing.Top;
                    double bottom = spacing.Bottom;
                    double left = spacing.Left;
                    double right = spacing.Right;
                    y += Math.Max(previousBottom, top);
                    double available = Math.Max(1, width - margin.Left - margin.Right - left - right);
                    if (block is Paragraph paragraph)
                        y = layout.FormatParagraph(paragraph, margin.Left + left, y, available);
                    else if (block is BlockUIContainer container)
                        y = layout.FormatChild(container, margin.Left + left, y, available, previous);
                    else
                        throw new PlatformNotSupportedException("Portable FlowDocument block type is not supported: " + block.GetType().Name);
                    previousBottom = bottom;
                }
                y += previousBottom + margin.Bottom;
                layout.Size = new Size(width, y);
                var drawing = new DrawingVisual();
                using (DrawingContext dc = drawing.RenderOpen())
                    foreach (Line line in layout._lines)
                        line.Text.Draw(dc, line.Origin, InvertAxes.None);
                layout.Visual.Children.Add(drawing);
                return layout;
            }
            catch (Exception failure)
            {
                try { layout.RestorePreviousChildLayout(failure); }
                finally { layout.Dispose(); }
                throw;
            }
        }

        private double FormatParagraph(Paragraph paragraph, double x, double y, double width)
        {
            var source = new ParagraphSource(paragraph, _document.PixelsPerDip);
            if (!double.IsNaN(paragraph.LineHeight) || paragraph.Padding != new Thickness() || paragraph.BorderThickness != new Thickness())
                throw new PlatformNotSupportedException("Portable paragraph custom line height, padding and borders are not yet supported.");
            var properties = new LineProperties(paragraph, _document, source.DefaultProperties, null);
            int offset = 0;
            TextLineBreak previous = null;
            try
            {
                while (offset < source.Length)
                {
                    TextLine text = _formatter.FormatLine(source, offset, width, offset == 0 ? properties.FirstLineProps : properties, previous);
                    var line = new Line(source, text, offset, new Point(x, y), width, properties.FlowDirection);
                    _lines.Add(line);
                    _navigationLines.Add(new NavigationLine(line));
                    if (text.Length <= 0) throw new InvalidOperationException("Document formatter did not consume source symbols.");
                    previous?.Dispose();
                    previous = text.GetTextLineBreak();
                    offset += text.Length;
                    y += text.Height;
                }
            }
            finally { previous?.Dispose(); }
            return y;
        }

        private Line FindLine(ITextPointer position)
        {
            Line previous = null;
            foreach (Line line in _lines)
            {
                int offset = line.Source.Start.GetOffsetToPosition((TextPointer)position);
                if (offset < line.Offset || (offset == line.Offset && position.LogicalDirection == LogicalDirection.Backward))
                    return previous ?? line;
                if (offset < line.Offset + line.Text.Length ||
                    (offset == line.Offset + line.Text.Length && position.LogicalDirection == LogicalDirection.Backward)) return line;
                previous = line;
            }
            return previous;
        }

        private static int Offset(Line line, ITextPointer position) => Math.Clamp(
            line.Source.Start.GetOffsetToPosition((TextPointer)position), line.Offset, line.Offset + line.Text.Length);

        internal TextPointer HitTest(Point point, bool snap)
        {
            foreach (Child child in _children)
                if (child.Bounds.Contains(point)) return child.Container.ContentStart.GetPositionAtOffset(point.X < child.Bounds.Left + child.Bounds.Width / 2 ? 0 : 1);
            Line nearest = null;
            double distance = double.PositiveInfinity;
            foreach (Line line in _lines)
            {
                var bounds = line.ToPageRectangle(new Rect(line.Text.Start, 0,
                    line.Text.WidthIncludingTrailingWhitespace, line.Text.Height));
                double dy = point.Y < bounds.Top ? bounds.Top - point.Y : Math.Max(0, point.Y - bounds.Bottom);
                if (bounds.Contains(point)) return Hit(line, point.X);
                if (dy < distance) { distance = dy; nearest = line; }
            }
            return snap && nearest != null ? Hit(nearest, point.X) : null;
        }

        private static TextPointer Hit(Line line, double x)
        {
            CharacterHit hit = line.Text.GetCharacterHitFromDistance(line.FromPageX(x));
            int offset = Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, 0, line.Source.Length - 1);
            return new TextPointer(line.Source.Start, offset,
                hit.TrailingLength == 0 ? LogicalDirection.Forward : LogicalDirection.Backward);
        }

        internal Rect GetCaretRectangle(ITextPointer position)
        {
            foreach (Child child in _children)
                if (position.CompareTo(child.Container.ContentStart) >= 0 && position.CompareTo(child.Container.ContentEnd) <= 0)
                    return new Rect(position.CompareTo(child.Container.ContentStart) == 0 ? child.Bounds.Left : child.Bounds.Right, child.Bounds.Top, 0, child.Bounds.Height);
            Line line = FindLine(position);
            if (line == null) return Rect.Empty;
            int offset = Offset(line, position);
            double x = line.Text.GetDistanceFromCharacterHit(new CharacterHit(offset, 0));
            return new Rect(line.ToPageX(x), line.Origin.Y, 0, line.Text.Height);
        }

        internal List<Rect> GetRectangles(ITextPointer start, ITextPointer end)
        {
            var result = new List<Rect>();
            if (start.CompareTo(end) > 0) (start, end) = (end, start);
            foreach (Line line in _lines)
            {
                int first = Offset(line, start), last = Offset(line, end);
                if (last <= first) continue;
                foreach (TextBounds bounds in line.Text.GetTextBounds(first, last - first))
                {
                    result.Add(line.ToPageRectangle(bounds.Rectangle));
                }
            }
            foreach (Child child in _children)
                if (start.CompareTo(child.Container.ContentEnd) < 0 && end.CompareTo(child.Container.ContentStart) > 0)
                    result.Add(child.Bounds);
            return result;
        }

        internal Geometry GetSelectionGeometry(ITextPointer start, ITextPointer end)
        {
            var geometry = new GeometryGroup();
            foreach (Rect rect in GetRectangles(start, end)) geometry.Children.Add(new RectangleGeometry(rect));
            return geometry.Children.Count == 0 ? null : geometry;
        }

        internal bool IsCaretBoundary(ITextPointer position)
        {
            if (FindChild(position) != null) return false;
            Line line = FindLine(position);
            if (line == null) return false;
            var hit = new CharacterHit(Offset(line, position), 0);
            var next = line.Text.GetNextCaretCharacterHit(hit);
            if (next == hit) next = new CharacterHit(line.Offset + line.Text.Length - 1, 1);
            return line.Text.GetPreviousCaretCharacterHit(next) == hit;
        }

        internal ITextPointer MoveCaret(ITextPointer position, LogicalDirection direction, bool backspace = false)
        {
            if (FindChild(position) != null) return position;
            Line line = FindLine(position);
            if (line == null) return position;
            var hit = new CharacterHit(Offset(line, position), 0);
            var next = direction == LogicalDirection.Forward ? line.Text.GetNextCaretCharacterHit(hit) :
                backspace ? line.Text.GetBackspaceCaretCharacterHit(hit) : line.Text.GetPreviousCaretCharacterHit(hit);
            int offset = Math.Clamp(next.FirstCharacterIndex + next.TrailingLength, 0, line.Source.Length - 1);
            var result = new TextPointer(line.Source.Start, offset, direction);
            return result.CompareTo((TextPointer)position) == 0 ? new TextPointer(((TextPointer)position).GetNextInsertionPosition(direction) ?? result) : result;
        }

        internal TextSegment GetLineRange(ITextPointer position)
        {
            Child child = FindChild(position);
            if (child != null) return new TextSegment(child.Container.ContentStart, child.Container.ContentEnd, true);
            Line line = FindLine(position);
            if (line == null) return new TextSegment(position, position, true);
            return new TextSegment(line.Source.Start.GetPositionAtOffset(line.Offset),
                line.Source.Start.GetPositionAtOffset(Math.Min(line.Source.Length - 1, line.Offset + line.Text.Length)), true);
        }

        internal ITextPointer MoveLine(ITextPointer position, double x, int count, out double newX, out int moved)
        {
            newX = x;
            moved = 0;
            if (_navigationLines.Count == 0) return position;
            Child child = FindChild(position);
            Line line = child == null ? FindLine(position) : null;
            int index = _navigationLines.FindIndex(entry => child != null ? entry.Child == child : entry.Text == line);
            if (index < 0) return position;
            int target = (int)Math.Clamp((long)index + count, 0, _navigationLines.Count - 1);
            moved = target - index;
            NavigationLine targetLine = _navigationLines[target];
            if (targetLine.Child != null)
            {
                Child targetChild = targetLine.Child;
                return double.IsNaN(x) || x <= targetChild.Bounds.Left + targetChild.Bounds.Width / 2
                    ? new TextPointer(targetChild.Container.ContentStart, LogicalDirection.Forward)
                    : new TextPointer(targetChild.Container.ContentEnd, LogicalDirection.Backward);
            }
            Line targetText = targetLine.Text;
            return double.IsNaN(x) ? new TextPointer(targetText.Source.Start, targetText.Offset, LogicalDirection.Forward) : Hit(targetText, x);
        }

        internal ReadOnlyCollection<GlyphRun> GetGlyphRuns(ITextPointer start, ITextPointer end)
        {
            var result = new List<GlyphRun>();
            foreach (Line line in _lines)
            {
                int first = Offset(line, start), last = Offset(line, end);
                if (last <= first) continue;
                foreach (IndexedGlyphRun run in line.Text.GetIndexedGlyphRuns())
                    if (run.TextSourceCharacterIndex < last && run.TextSourceCharacterIndex + run.TextSourceLength > first)
                        result.Add(run.GlyphRun);
            }
            return result.AsReadOnly();
        }

        public void Dispose()
        {
            Visual.Children.Clear();
            DisposeChildren();
            foreach (Line line in _lines) line.Text.Dispose();
            _lines.Clear();
            _navigationLines.Clear();
            _formatter.Dispose();
        }

        private sealed class NavigationLine
        {
            internal readonly Line Text;
            internal readonly Child Child;
            internal NavigationLine(Line text) { Text = text; }
            internal NavigationLine(Child child) { Child = child; }
        }

        private sealed class Line
        {
            internal readonly ParagraphSource Source;
            internal readonly TextLine Text;
            internal readonly int Offset;
            internal readonly Point Origin;
            private readonly double _width;
            private readonly bool _rightToLeft;
            internal Line(ParagraphSource source, TextLine text, int offset, Point origin, double width, FlowDirection direction)
            { Source = source; Text = text; Offset = offset; Origin = origin; _width = width; _rightToLeft = direction == FlowDirection.RightToLeft; }
            internal double ToPageX(double distance) => Origin.X + (_rightToLeft ? _width - distance : distance);
            internal double FromPageX(double x) => _rightToLeft ? _width - (x - Origin.X) : x - Origin.X;
            internal Rect ToPageRectangle(Rect rectangle) => new Rect(
                ToPageX(_rightToLeft ? rectangle.Right : rectangle.Left), Origin.Y + rectangle.Top, rectangle.Width, rectangle.Height);
        }

        private sealed class ParagraphSource : TextSource
        {
            internal readonly TextPointer Start;
            internal readonly int Length;
            internal readonly TextProperties DefaultProperties;
            private readonly double _pixelsPerDip;
            internal ParagraphSource(Paragraph paragraph, double pixelsPerDip)
            {
                Start = paragraph.ContentStart;
                Length = Start.GetOffsetToPosition(paragraph.ContentEnd) + 1;
                _pixelsPerDip = PixelsPerDip = pixelsPerDip;
                DefaultProperties = new TextProperties(paragraph, StaticTextPointer.Null, false, true, pixelsPerDip);
            }
            public override TextRun GetTextRun(int index)
            {
                if (index >= Length - 1) return new TextEndOfParagraph(1, DefaultProperties);
                TextPointer pointer = Start.GetPositionAtOffset(index);
                switch (pointer.GetPointerContext(LogicalDirection.Forward))
                {
                    case TextPointerContext.Text:
                        string text = pointer.GetTextInRun(LogicalDirection.Forward);
                        return new TextCharacters(text, new TextProperties(pointer.Parent, ((ITextPointer)pointer).CreateStaticPointer(), false, true, _pixelsPerDip));
                    case TextPointerContext.ElementStart:
                        Inline inline = GetInline(pointer);
                        if (inline is LineBreak) return new TextEndOfLine(2);
                        if (inline.IsEmpty)
                            return new TextCharacters("\u200b\u200b", new TextProperties(inline, ((ITextPointer)pointer).CreateStaticPointer(), false, true, _pixelsPerDip));
                        TextDecorationCollection decorations = DynamicPropertyReader.GetTextDecorations(inline);
                        return decorations == null || decorations.Count == 0 ? new TextHidden(1) :
                            new TextSpanModifier(1, decorations, inline.Foreground);
                    case TextPointerContext.ElementEnd:
                        TextDecorationCollection endingDecorations = DynamicPropertyReader.GetTextDecorations(GetInline(pointer));
                        return endingDecorations == null || endingDecorations.Count == 0 ? new TextHidden(1) : new TextEndOfSegment(1);
                    default:
                        throw new PlatformNotSupportedException("Portable FlowDocument embedded content is not yet supported.");
                }
            }
            private static Inline GetInline(TextPointer pointer)
            {
                object element = pointer.GetAdjacentElement(LogicalDirection.Forward);
                if (element is not Inline inline || (inline is not Run && inline is not Span && inline is not LineBreak))
                    throw new PlatformNotSupportedException("Portable FlowDocument inline type is not supported: " + element?.GetType().Name);
                if (inline.Parent != null && inline.FlowDirection != (FlowDirection)inline.Parent.GetValue(FrameworkElement.FlowDirectionProperty))
                    throw new NotSupportedException("Portable FlowDocument inline directional embedding is not supported.");
                return inline;
            }
            public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int index)
            {
                int nonTextLength = 0;
                string text = string.Empty;
                CultureInfo culture = null;
                if (index > 0)
                {
                    TextPointer pointer = Start.GetPositionAtOffset(Math.Min(index, Length - 1));
                    while (pointer.CompareTo(Start) > 0 && pointer.GetPointerContext(LogicalDirection.Backward) != TextPointerContext.Text)
                    {
                        pointer = pointer.GetPositionAtOffset(-1);
                        nonTextLength++;
                    }
                    text = pointer.GetTextInRun(LogicalDirection.Backward);
                    culture = DynamicPropertyReader.GetCultureInfo(pointer.Parent ?? Start.Parent);
                }
                return new TextSpan<CultureSpecificCharacterBufferRange>(nonTextLength + text.Length,
                    new CultureSpecificCharacterBufferRange(culture, new CharacterBufferRange(text, 0, text.Length)));
            }
            public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => Start.Offset + index;
        }

    }
}
