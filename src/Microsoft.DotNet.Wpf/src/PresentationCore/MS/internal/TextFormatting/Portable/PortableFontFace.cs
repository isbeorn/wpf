// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if PORTABLE_WPF
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.FontCache;
using MS.Internal.FontFace;
using SkiaSharp;

namespace MS.Internal.TextFormatting.Portable
{
    // The owning GlyphTypeface keeps the native immutable face alive. Fonts and shapers
    // are per-operation objects, so callers do not share their mutable size/buffer state.
    internal sealed class PortableFontFace
    {
        internal SKTypeface Typeface { get; }
        internal Uri Uri { get; }
        internal SKFontMetrics Metrics { get; }
        private readonly Lazy<IDictionary<int, ushort>> _characterMap;
        internal IDictionary<int, ushort> CharacterMap => _characterMap.Value;

        internal PortableFontFace(Uri uri)
        {
            if (!uri.IsAbsoluteUri || !uri.IsFile || !string.IsNullOrEmpty(uri.Fragment))
                throw new NotSupportedException("Portable font faces currently require a local font file without a collection index.");
            Uri = uri;
            Typeface = SKTypeface.FromFile(uri.LocalPath) ?? throw new FileFormatException(uri);
            _characterMap = new Lazy<IDictionary<int, ushort>>(ReadCharacterMap);
            using (var font = new SKFont(Typeface, 1))
                Metrics = font.Metrics;
        }

        internal ushort GetGlyph(int codepoint)
        {
            using (var font = new SKFont(Typeface, 1))
                return font.GetGlyph(codepoint);
        }

        internal double GetAdvance(ushort glyph)
        {
            using (var font = new SKFont(Typeface, 1))
                return font.GetGlyphWidths(new ushort[] { glyph })[0];
        }

        internal Rect GetInkBounds(GlyphRun run)
        {
            if (run.IsSideways || (run.BidiLevel & 1) != 0)
                throw new NotSupportedException("Portable glyph bounds currently support horizontal LTR runs.");
            using (var font = new SKFont(Typeface, (float)run.FontRenderingEmSize))
            {
                ushort[] glyphs = run.GlyphIndices.ToArray();
                SKRect[] bounds;
                font.GetGlyphWidths(glyphs, out bounds);
                Rect result = Rect.Empty;
                double advance = 0;
                for (int index = 0; index < glyphs.Length; index++)
                {
                    SKRect bound = bounds[index];
                    Point offset = run.GlyphOffsets == null || run.GlyphOffsets.Count == 0 ? default : run.GlyphOffsets[index];
                    if (bound.Width > 0 && bound.Height > 0)
                        result.Union(new Rect(advance + offset.X + bound.Left, bound.Top - offset.Y, bound.Width, bound.Height));
                    advance += run.AdvanceWidths[index];
                }
                GC.KeepAlive(run);
                return result;
            }
        }

        internal Geometry GetOutline(ushort glyph, double emSize)
        {
            using (var font = new SKFont(Typeface, (float)emSize))
            using (var path = font.GetGlyphPath(glyph))
            {
                if (path == null)
                    return Geometry.Empty;
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                using (var iterator = path.CreateRawIterator())
                {
                    var points = new SKPoint[4];
                    SKPathVerb verb;
                    bool open = false;
                    while ((verb = iterator.Next(points)) != SKPathVerb.Done)
                    {
                        switch (verb)
                        {
                            case SKPathVerb.Move:
                                context.BeginFigure(ToPoint(points[0]), true, true);
                                open = true;
                                break;
                            case SKPathVerb.Line:
                                context.LineTo(ToPoint(points[1]), true, false);
                                break;
                            case SKPathVerb.Quad:
                                context.QuadraticBezierTo(ToPoint(points[1]), ToPoint(points[2]), true, false);
                                break;
                            case SKPathVerb.Cubic:
                                context.BezierTo(ToPoint(points[1]), ToPoint(points[2]), ToPoint(points[3]), true, false);
                                break;
                            case SKPathVerb.Conic:
                                throw new NotSupportedException("Conic glyph outlines are not implemented by the portable backend.");
                            case SKPathVerb.Close:
                                open = false;
                                break;
                        }
                    }
                    if (open)
                        throw new NotSupportedException("Open glyph contours are not supported.");
                }
                geometry.Freeze();
                return geometry;
            }
        }

        private static Point ToPoint(SKPoint point) => new Point(point.X, point.Y);

        private IDictionary<int, ushort> ReadCharacterMap()
        {
            byte[] table = Typeface.GetTableData(0x636d6170);
            if (table == null || table.Length < 4)
                throw new FileFormatException(Uri);
            int U16(int offset)
            {
                if (offset < 0 || offset > table.Length - 2) throw new FileFormatException(Uri);
                return BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(offset, 2));
            }
            uint U32(int offset)
            {
                if (offset < 0 || offset > table.Length - 4) throw new FileFormatException(Uri);
                return BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(offset, 4));
            }
            int selected = -1, priority = -1;
            for (int index = 0; index < U16(2); index++)
            {
                int record = 4 + index * 8;
                int platform = U16(record), encoding = U16(record + 2);
                if (platform != 0 && !(platform == 3 && (encoding == 1 || encoding == 10)))
                    continue;
                int offset = checked((int)U32(record + 4));
                int format = U16(offset);
                int score = format == 12 ? 2 : format == 4 ? 1 : 0;
                if (score > priority) { selected = offset; priority = score; }
            }
            var map = new Dictionary<int, ushort>();
            if (priority == 2)
            {
                uint count = U32(selected + 12);
                if (count > (uint)table.Length / 12) throw new FileFormatException(Uri);
                for (int index = 0; index < count; index++)
                {
                    int group = checked(selected + 16 + index * 12);
                    uint first = U32(group), last = U32(group + 4), glyph = U32(group + 8);
                    if (first > last || last > 0x10ffff || (ulong)glyph + last - first > ushort.MaxValue)
                        throw new FileFormatException(Uri);
                    for (uint codepoint = first; codepoint <= last; codepoint++)
                    {
                        uint value = glyph + codepoint - first;
                        if (value != 0 && !(codepoint >= 0xd800 && codepoint <= 0xdfff))
                            map[(int)codepoint] = (ushort)value;
                    }
                }
            }
            else if (priority == 1)
            {
                int segments = U16(selected + 6) / 2;
                int starts = selected + 16 + 2 * segments;
                int deltas = starts + 2 * segments;
                int ranges = deltas + 2 * segments;
                for (int segment = 0; segment < segments; segment++)
                {
                    int first = U16(starts + 2 * segment), last = U16(selected + 14 + 2 * segment);
                    int delta = U16(deltas + 2 * segment), range = U16(ranges + 2 * segment);
                    if (first > last) throw new FileFormatException(Uri);
                    for (int codepoint = first; codepoint <= last && codepoint != 0xffff; codepoint++)
                    {
                        int value = range == 0 ? codepoint : U16(ranges + 2 * segment + range + 2 * (codepoint - first));
                        if (range == 0 || value != 0) value = (value + delta) & 0xffff;
                        if (value != 0 && !(codepoint >= 0xd800 && codepoint <= 0xdfff))
                            map[codepoint] = (ushort)value;
                    }
                }
            }
            else
                throw new NotSupportedException("Portable character maps currently support Unicode OpenType cmap formats 4 and 12.");
            return new ReadOnlyDictionary<int, ushort>(map);
        }
    }

    internal sealed class PortableFontFamily : IFontFamily
    {
        private sealed record FontFile(string Path, string Family, int Weight, int Width, bool Italic);
        private static readonly Lazy<FontFile[]> SystemFonts = new Lazy<FontFile[]>(() => ReadFonts(FontDirectories()).ToArray());
        private readonly FontFile[] _files;
        private readonly FontStyle _style;
        private readonly FontWeight _weight;
        private readonly FontStretch _stretch;
        private readonly GlyphTypeface _metrics;

        private PortableFontFamily(FontFile[] files, FontStyle style, FontWeight weight, FontStretch stretch)
        {
            _files = files;
            _style = style;
            _weight = weight;
            _stretch = stretch;
            _metrics = OpenFace(style, weight, stretch);
        }

        internal static IFontFamily Resolve(Uri location, string name, FontStyle style, FontWeight weight, FontStretch stretch)
        {
            FontFile[] catalog;
            if (location != null)
            {
                if (!location.IsFile)
                    throw new NotSupportedException("Portable font families currently require local font sources.");
                catalog = ReadFonts(new[] { location.LocalPath }).ToArray();
            }
            else
                catalog = SystemFonts.Value;

            FontFile[] matches = catalog.Where(file => string.Equals(file.Family, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0 && (string.Equals(name, "GLOBAL USER INTERFACE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "ARIAL", StringComparison.OrdinalIgnoreCase)))
            {
                string[] defaults = { "DejaVu Sans", "Liberation Sans", "Arial" };
                foreach (string candidate in defaults)
                {
                    matches = catalog.Where(file => string.Equals(file.Family, candidate, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length != 0)
                        break;
                }
            }
            return matches.Length == 0 ? null : new PortableFontFamily(matches, style, weight, stretch);
        }

        private GlyphTypeface OpenFace(FontStyle style, FontWeight weight, FontStretch stretch)
        {
            FontFile file = _files.OrderBy(candidate =>
                Math.Abs(candidate.Weight - weight.ToOpenTypeWeight()) +
                Math.Abs(candidate.Width - stretch.ToOpenTypeStretch()) * 100 +
                (candidate.Italic == (style != FontStyles.Normal) ? 0 : 1000)).First();
            return new GlyphTypeface(new Uri(file.Path, UriKind.Absolute));
        }

        public IDictionary<XmlLanguage, string> Names => new Dictionary<XmlLanguage, string>
        {
            [XmlLanguage.GetLanguage("en-US")] = _metrics.PortableFontFace.Typeface.FamilyName
        };
        public double BaselineDesign => _metrics.Baseline;
        public double LineSpacingDesign => _metrics.Height + _metrics.PortableFontFace.Metrics.Leading;
        public double Baseline(double emSize, double toReal, double pixelsPerDip, TextFormattingMode mode) => Round(BaselineDesign * emSize, pixelsPerDip, mode);
        public double LineSpacing(double emSize, double toReal, double pixelsPerDip, TextFormattingMode mode) => Round(LineSpacingDesign * emSize, pixelsPerDip, mode);
        public ITypefaceMetrics GetTypefaceMetrics(FontStyle style, FontWeight weight, FontStretch stretch) =>
            style == _style && weight == _weight && stretch == _stretch ? _metrics : OpenFace(style, weight, stretch);
        public IDeviceFont GetDeviceFont(FontStyle style, FontWeight weight, FontStretch stretch) => null;
        public bool GetMapTargetFamilyNameAndScale(CharacterBufferRange text, CultureInfo culture, CultureInfo digitCulture,
            double defaultSizeInEm, out int length, out string targetFamilyName, out double scaleInEm)
        {
            length = text.Length;
            targetFamilyName = null;
            scaleInEm = 1;
            return false;
        }
        public ICollection<Typeface> GetTypefaces(FontFamilyIdentifier identifier) =>
            new[] { new Typeface(new FontFamily(_files[0].Family), _style, _weight, _stretch) };

        private static double Round(double value, double pixelsPerDip, TextFormattingMode mode) =>
            mode == TextFormattingMode.Display ? Math.Round(value * pixelsPerDip) / pixelsPerDip : value;

        private static IEnumerable<string> FontDirectories()
        {
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(fonts))
                yield return fonts;
            yield return "/usr/share/fonts";
            yield return "/usr/local/share/fonts";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "fonts");
            yield return "/System/Library/Fonts";
            yield return "/Library/Fonts";
        }

        private static IEnumerable<FontFile> ReadFonts(IEnumerable<string> roots)
        {
            foreach (string root in roots)
            {
                IEnumerable<string> files = File.Exists(root) ? new[] { root } : Directory.Exists(root)
                    ? Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    : Array.Empty<string>();
                foreach (string file in files)
                {
                    string extension = Path.GetExtension(file);
                    if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                        continue;
                    using (SKTypeface face = SKTypeface.FromFile(file))
                    {
                        if (face != null)
                            yield return new FontFile(Path.GetFullPath(file), face.FamilyName, face.FontWeight, face.FontWidth, face.IsItalic);
                    }
                }
            }
        }
    }
}
#endif
