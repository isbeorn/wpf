// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.Text;

namespace MS.Internal.Documents
{
    internal sealed partial class PortableDocumentLayout
    {
        private readonly List<Marker> _markers = new List<Marker>();

        private BlockResult FormatBlocks(BlockCollection blocks, TextElement owner, double x, double y, double width,
            ref double pendingMargin, bool suppressTopSpace, FlowDirection parentDirection, PortableDocumentLayout previous)
        {
            BlockResult result = new BlockResult(y, y, y);
            bool first = true;
            foreach (Block block in blocks)
            {
                BlockResult child = FormatBlock(block, x, y, width, ref pendingMargin, suppressTopSpace && first, parentDirection, previous);
                if (first) result = child;
                y = child.Bottom;
                first = false;
            }
            if (first && owner != null)
            {
                y += pendingMargin;
                pendingMargin = 0;
                int line = _lines.Count;
                double bottom = FormatParagraph(owner, x, y, width);
                return new BlockResult(y, bottom, y + _lines[line].Text.Baseline);
            }
            return new BlockResult(result.Top, y, result.Baseline);
        }

        private BlockResult FormatBlock(TextElement element, double x, double y, double width, ref double pendingMargin,
            bool suppressTopSpace, FlowDirection parentDirection, PortableDocumentLayout previous)
        {
            if (element is not Paragraph && element is not BlockUIContainer && element is not Section && element is not System.Windows.Documents.List && element is not ListItem)
                throw new PlatformNotSupportedException("Portable FlowDocument block type is not supported: " + element.GetType().Name);

            var list = element as System.Windows.Documents.List;
            List<Marker> markers = list == null ? null : CreateMarkers(list);
            Thickness? autoPadding = list == null || !HasAutoPadding(list.Padding) ? null : MarkerPadding(list, markers);
            var mbp = PtsHost.MbpInfo.FromElement(element, _document.PixelsPerDip, autoPadding);
            if (parentDirection == FlowDirection.RightToLeft) mbp.MirrorMargin();
            FlowDirection direction = (FlowDirection)element.GetValue(Block.FlowDirectionProperty);
            if (direction == FlowDirection.RightToLeft) mbp.MirrorBP();
            pendingMargin = Math.Max(pendingMargin, suppressTopSpace ? 0 : mbp.Margin.Top);
            x += mbp.Margin.Left;
            width = Math.Max(1, width - mbp.Margin.Left - mbp.Margin.Right);
            double boxX = x, boxWidth = width;
            double topInset = mbp.Border.Top + mbp.Padding.Top;
            double bottomInset = mbp.Border.Bottom + mbp.Padding.Bottom;
            double top = y + pendingMargin;
            if (topInset != 0) { y += pendingMargin + topInset; pendingMargin = 0; }
            x += mbp.Border.Left + mbp.Padding.Left;
            width = Math.Max(1, width - mbp.Border.Left - mbp.Padding.Left - mbp.Border.Right - mbp.Padding.Right);
            int backgroundIndex = Visual.Children.Count;
            BlockResult content;
            if (element is Paragraph)
            {
                y += pendingMargin;
                pendingMargin = 0;
                top = y - topInset;
                int firstLine = _lines.Count;
                double bottom = FormatParagraph(element, x, y, width);
                content = new BlockResult(y, bottom, y + _lines[firstLine].Text.Baseline);
            }
            else if (element is BlockUIContainer child)
            {
                y += pendingMargin;
                pendingMargin = 0;
                top = y - topInset;
                double bottom = FormatChild(child, x, y, width, previous);
                content = new BlockResult(y, bottom, bottom);
            }
            else if (list != null)
            {
                content = new BlockResult(y, y, y);
                bool first = true;
                int index = 0;
                foreach (ListItem item in list.ListItems)
                {
                    BlockResult formatted = FormatBlock(item, x, y, width, ref pendingMargin, suppressTopSpace && first, direction, previous);
                    if (first) content = formatted;
                    if (markers.Count != 0)
                    {
                        Marker marker = markers[index++];
                        double gap = double.IsNaN(list.MarkerOffset) ? DynamicPropertyReader.GetLineHeightValue(list) / 2 : list.MarkerOffset;
                        marker.Origin = new Point(direction == FlowDirection.RightToLeft ? x + width + gap + marker.Text.Width : x - gap - marker.Text.Width,
                            formatted.Baseline - marker.Text.Baseline);
                    }
                    y = formatted.Bottom;
                    first = false;
                }
                content = new BlockResult(content.Top, y, content.Baseline);
            }
            else
            {
                BlockCollection blocks = element is Section section ? section.Blocks : ((ListItem)element).Blocks;
                content = FormatBlocks(blocks, element, x, y, width, ref pendingMargin, suppressTopSpace, direction, previous);
            }
            if (topInset == 0) top = content.Top;
            y = content.Bottom;
            if (bottomInset != 0) { y += pendingMargin + bottomInset; pendingMargin = 0; }
            if (y > top && (element.Background != null || mbp.Border != new Thickness()))
            {
                var background = new PtsHost.ParagraphVisual();
                background.DrawBackgroundAndBorder(element.Background, mbp.BorderBrush, mbp.Border,
                    new Rect(boxX, top, boxWidth, y - top), true, true);
                Visual.Children.Insert(backgroundIndex, background);
            }
            pendingMargin = Math.Max(pendingMargin, mbp.Margin.Bottom);
            return new BlockResult(top, y, content.Baseline);
        }

        private List<Marker> CreateMarkers(System.Windows.Documents.List list)
        {
            var result = new List<Marker>();
            if (list.MarkerStyle == TextMarkerStyle.None) return result;
            foreach (ListItem item in list.ListItems)
            {
                var marker = new Marker(FormatMarker(list, list.MarkerStyle, list.GetListItemIndex(item)));
                _markers.Add(marker);
                result.Add(marker);
            }
            return result;
        }

        private TextLine FormatMarker(System.Windows.Documents.List list, TextMarkerStyle style, int index)
        {
            var text = new TextProperties(list, StaticTextPointer.Null, false, false, _document.PixelsPerDip);
            var properties = new LineProperties(list, _document, text, null, TextAlignment.Left);
            string symbol = style switch
            {
                TextMarkerStyle.Disc => "\u2022",
                TextMarkerStyle.Circle => "\u25cb",
                TextMarkerStyle.Square => "\u25a1",
                TextMarkerStyle.Box => "\u25a0",
                _ => null
            };
            TextSource source = symbol == null ? new TextSimpleMarkerProperties(style, 0, index, properties).TextSource :
                new SymbolMarkerSource(symbol, text, _document.PixelsPerDip);
            return _formatter.FormatLine(source, 0, 0, properties, null);
        }

        private Thickness MarkerPadding(System.Windows.Documents.List list, List<Marker> markers)
        {
            double width = 0;
            foreach (Marker marker in markers) width = Math.Max(width, marker.Text.Width);
            if (markers.Count == 0)
            {
                using TextLine marker = FormatMarker(list, list.MarkerStyle == TextMarkerStyle.None ? TextMarkerStyle.Disc : list.MarkerStyle, list.StartIndex);
                width = marker.Width;
            }
            double height = DynamicPropertyReader.GetLineHeightValue(list);
            double padding = ((int)((width + 1.5 * height) / height) + 1) * height;
            return new Thickness(padding, 0, 0, 0);
        }

        private static bool HasAutoPadding(Thickness padding) => double.IsNaN(padding.Left) || double.IsNaN(padding.Top) || double.IsNaN(padding.Right) || double.IsNaN(padding.Bottom);

        private readonly struct BlockResult
        {
            internal readonly double Top, Bottom, Baseline;
            internal BlockResult(double top, double bottom, double baseline) { Top = top; Bottom = bottom; Baseline = baseline; }
        }

        private sealed class Marker
        {
            internal readonly TextLine Text;
            internal Point Origin;
            internal Marker(TextLine text) { Text = text; }
        }

        private sealed class SymbolMarkerSource : TextSource
        {
            private readonly string _symbol;
            private readonly TextRunProperties _properties;
            internal SymbolMarkerSource(string symbol, TextRunProperties properties, double pixelsPerDip)
            { _symbol = symbol; _properties = properties; PixelsPerDip = pixelsPerDip; }
            public override TextRun GetTextRun(int index) => index == 0 ? new TextCharacters(_symbol, _properties) : new TextEndOfParagraph(1, _properties);
            public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) => new TextSpan<CultureSpecificCharacterBufferRange>(limit,
                new CultureSpecificCharacterBufferRange(_properties.CultureInfo, new CharacterBufferRange(_symbol, 0, Math.Min(limit, _symbol.Length))));
            public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
        }
    }
}
