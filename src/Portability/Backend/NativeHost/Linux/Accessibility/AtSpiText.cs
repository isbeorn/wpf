extern alias WpfFork;

using System.Buffers;
using System.Text;
using System.Windows.Automation.Provider;
using WpfFork::MS.Internal.Automation;
using WpfFork::System.Windows;
using WpfFork::System.Windows.Automation.Peers;
using WpfFork::System.Windows.Controls;
using WpfFork::System.Windows.Controls.Primitives;
using WpfFork::System.Windows.Documents;

namespace Wpf.Portable.NativeHost.Linux.Accessibility;

internal sealed class AtSpiText : IDisposable
{
    private readonly AtSpiNode _node;
    private readonly TextBoxBase _editor;
    private readonly AutomationPeer _peer;
    private readonly ITextProvider _provider;
    private readonly TextAdaptor _adaptor;
    private string _previousText;
    private int _previousCaret;
    private (int Start, int End) _previousSelection;

    internal AtSpiText(AtSpiNode node, TextBoxBase editor, AutomationPeer peer, ITextProvider provider)
    {
        _node = node; _editor = editor; _peer = peer; _provider = provider;
        _adaptor = provider as TextAdaptor ?? throw new NotSupportedException("The WPF text pattern is not a TextAdaptor.");
        _previousText = Value; _previousCaret = Caret; _previousSelection = Selection;
        editor.TextChanged += TextChanged; editor.SelectionChanged += SelectionChanged;
    }
    private string Value => _provider.DocumentRange.GetText(-1);
    internal int CharacterCount => new Offsets(Value).Count;
    internal bool CanSetValue => _peer.GetPattern(PatternInterface.Value) is IValueProvider;
    internal bool IsReadOnly => _editor.IsReadOnly;
    internal bool IsMultiline => _editor is RichTextBox || _editor is TextBox { AcceptsReturn: true };
    private int PrefixLength(ITextPointer end) => ((ITextRangeProvider)new TextRangeAdaptor(_adaptor, _editor.TextContainer.Start, end, _peer)).GetText(-1).Length;
    internal int Caret => new Offsets(Value).Scalar(PrefixLength(_editor.TextSelectionInternal.MovingPosition));
    private (int Start, int End) Selection
    {
        get
        {
            var offsets = new Offsets(Value);
            return (offsets.Scalar(PrefixLength(_editor.TextSelectionInternal.Start)), offsets.Scalar(PrefixLength(_editor.TextSelectionInternal.End)));
        }
    }
    private TextPointer Position(Offsets offsets, int scalar)
    {
        int target = offsets.Utf16(scalar), low = 0, high = _editor.TextContainer.SymbolCount;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            var position = _editor.TextContainer.CreatePointerAtOffset(middle, LogicalDirection.Forward);
            if (PrefixLength(position) < target) low = middle + 1; else high = middle;
        }
        var result = _editor.TextContainer.CreatePointerAtOffset(low, LogicalDirection.Forward).GetInsertionPosition(LogicalDirection.Forward);
        if (PrefixLength(result) != target) throw new ArgumentException("The requested character offset is not an actual WPF insertion boundary.");
        return result;
    }
    private TextRangeAdaptor Range(int start, int end)
    {
        var offsets = new Offsets(Value);
        if (end == -1) end = offsets.Count;
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        return new TextRangeAdaptor(_adaptor, Position(offsets, start), Position(offsets, end), _peer);
    }
    private bool Select(int start, int end)
    {
        ((ITextRangeProvider)Range(start, end)).Select(); return true;
    }
    internal GioVariant Call(string method, GioVariant args)
    {
        switch (method)
        {
            case "GetText":
            {
                var offsets = new Offsets(Value); int end = AtSpiValues.Int(args, 1); if (end == -1) end = offsets.Count;
                return AtSpiValues.Tuple(GioVariant.String(offsets.Slice(AtSpiValues.Int(args, 0), end)));
            }
            case "GetCharacterAtOffset":
            {
                var offsets = new Offsets(Value); int index = AtSpiValues.Int(args, 0);
                return AtSpiValues.Tuple(GioVariant.Int32(index < 0 || index >= offsets.Count ? -1 : Rune.GetRuneAt(offsets.Text, offsets.Utf16(index)).Value));
            }
            case "SetCaretOffset": { int offset = AtSpiValues.Int(args, 0); return AtSpiValues.Tuple(GioVariant.Bool(Select(offset, offset))); }
            case "GetNSelections": { var range = Selection; return AtSpiValues.Tuple(GioVariant.Int32(range.Start == range.End ? 0 : 1)); }
            case "GetSelection":
            {
                var range = Selection; if (AtSpiValues.Int(args, 0) != 0 || range.Start == range.End) throw new ArgumentOutOfRangeException(nameof(args));
                return AtSpiValues.Tuple(GioVariant.Int32(range.Start), GioVariant.Int32(range.End));
            }
            case "AddSelection": return AtSpiValues.Tuple(GioVariant.Bool(Select(AtSpiValues.Int(args, 0), AtSpiValues.Int(args, 1))));
            case "SetSelection":
                if (AtSpiValues.Int(args, 0) != 0) return AtSpiValues.Tuple(GioVariant.Bool(false));
                return AtSpiValues.Tuple(GioVariant.Bool(Select(AtSpiValues.Int(args, 1), AtSpiValues.Int(args, 2))));
            case "RemoveSelection":
                return AtSpiValues.Tuple(GioVariant.Bool(AtSpiValues.Int(args, 0) == 0 && Select(Caret, Caret)));
            case "GetStringAtOffset": case "GetTextAtOffset": case "GetTextBeforeOffset": case "GetTextAfterOffset":
                return Unit(method, AtSpiValues.Int(args, 0), AtSpiValues.UInt(args, 1));
            case "GetCharacterExtents":
            {
                int index = AtSpiValues.Int(args, 0); return AtSpiNode.RectValues(Extents(index, index + 1, AtSpiValues.UInt(args, 1)), false);
            }
            case "GetRangeExtents": return AtSpiNode.RectValues(Extents(AtSpiValues.Int(args, 0), AtSpiValues.Int(args, 1), AtSpiValues.UInt(args, 2)), false);
            case "GetOffsetAtPoint":
            {
                var point = new Point(AtSpiValues.Int(args, 0), AtSpiValues.Int(args, 1)); var origin = _node.Origin(AtSpiValues.UInt(args, 2)); point.Offset(origin.X, origin.Y);
                var range = _provider.RangeFromPoint(point); var prefix = _provider.DocumentRange.Clone();
                prefix.MoveEndpointByRange(System.Windows.Automation.Text.TextPatternRangeEndpoint.End, range, System.Windows.Automation.Text.TextPatternRangeEndpoint.Start);
                return AtSpiValues.Tuple(GioVariant.Int32(new Offsets(Value).Scalar(prefix.GetText(-1).Length)));
            }
            case "ScrollSubstringTo": ((ITextRangeProvider)Range(AtSpiValues.Int(args, 0), AtSpiValues.Int(args, 1))).ScrollIntoView(true); return AtSpiValues.Tuple(GioVariant.Bool(true));
            case "GetAttributeRun": case "GetAttributes": return AtSpiValues.Tuple(GioVariant.Array("{ss}"), GioVariant.Int32(0), GioVariant.Int32(CharacterCount));
            case "GetDefaultAttributes": case "GetDefaultAttributeSet": return AtSpiValues.Tuple(GioVariant.Array("{ss}"));
            case "GetAttributeValue": return AtSpiValues.Tuple(GioVariant.String(""));
            default: throw new NotSupportedException("Unknown AT-SPI text method.");
        }
    }

    private GioVariant Unit(string method, int offset, uint granularity)
    {
        var offsets = new Offsets(Value);
        if (offset < 0 || offset > offsets.Count) throw new ArgumentOutOfRangeException(nameof(offset));
        GioVariant Empty(int at) => AtSpiValues.Tuple(GioVariant.String(""), GioVariant.Int32(at), GioVariant.Int32(at));
        uint unit = method == "GetStringAtOffset" ? granularity : granularity switch
        {
            0 => 0, 1 => 1, 5 => 3,
            _ => throw new NotSupportedException("The WPF provider does not supply this legacy AT-SPI boundary form.")
        };
        if (offsets.Count == 0) return Empty(0);
        if (unit == 0)
        {
            int character = method == "GetTextBeforeOffset" ? offset - 1 : method == "GetTextAfterOffset" ? offset + 1 : offset;
            if (character < 0 || character >= offsets.Count) return Empty(character < 0 ? 0 : offsets.Count);
            return AtSpiValues.Tuple(GioVariant.String(offsets.Slice(character, character + 1)), GioVariant.Int32(character), GioVariant.Int32(character + 1));
        }
        var textUnit = unit switch { 1 => System.Windows.Automation.Text.TextUnit.Word, 3 => System.Windows.Automation.Text.TextUnit.Line, 4 => System.Windows.Automation.Text.TextUnit.Paragraph,
            _ => throw new NotSupportedException("Sentence granularity is not supplied by the WPF text provider.") };
        bool before = method == "GetTextBeforeOffset", after = method == "GetTextAfterOffset";
        if (offset == offsets.Count && !before) return Empty(offset);
        if (offset == 0 && before) return Empty(0);
        ITextRangeProvider range = Range(Math.Min(offset, offsets.Count - 1), Math.Min(offset, offsets.Count - 1));
        range.ExpandToEnclosingUnit(textUnit);
        if (before && offset != offsets.Count && range.Move(textUnit, -1) == 0) return Empty(0);
        if (after && range.Move(textUnit, 1) == 0) return Empty(offsets.Count);
        var prefix = _provider.DocumentRange.Clone();
        prefix.MoveEndpointByRange(System.Windows.Automation.Text.TextPatternRangeEndpoint.End, range, System.Windows.Automation.Text.TextPatternRangeEndpoint.Start);
        int start = offsets.Scalar(prefix.GetText(-1).Length);
        string text = range.GetText(-1); int end = start + new Offsets(text).Count;
        return AtSpiValues.Tuple(GioVariant.String(text), GioVariant.Int32(start), GioVariant.Int32(end));
    }
    private Rect Extents(int start, int end, uint coordinates)
    {
        double[] rectangles = ((ITextRangeProvider)Range(start, end)).GetBoundingRectangles(); Rect bounds = Rect.Empty;
        for (int i = 0; i + 3 < rectangles.Length; i += 4) bounds.Union(new Rect(rectangles[i], rectangles[i + 1], rectangles[i + 2], rectangles[i + 3]));
        if (!bounds.IsEmpty) { var origin = _node.Origin(coordinates); bounds.Offset(-origin.X, -origin.Y); }
        return bounds;
    }
    internal GioVariant Edit(string method, GioVariant args)
    {
        if (_peer.GetPattern(PatternInterface.Value) is not IValueProvider provider) throw new NotSupportedException("The actual WPF peer does not supply a writable Value provider.");
        if (provider.IsReadOnly || !_editor.IsEnabled) return AtSpiValues.Tuple(GioVariant.Bool(false));
        string before = provider.Value; var offsets = new Offsets(before);
        switch (method)
        {
            case "SetTextContents": provider.SetValue(AtSpiValues.String(args, 0)); break;
            case "InsertText":
            {
                int at = offsets.Utf16(AtSpiValues.Int(args, 0)); string text = AtSpiValues.String(args, 1); int length = AtSpiValues.Int(args, 2);
                var utf8 = new UTF8Encoding(false, true); byte[] bytes = utf8.GetBytes(text);
                if (length >= 0 && length < bytes.Length) text = utf8.GetString(bytes, 0, length);
                provider.SetValue(before.Insert(at, text)); break;
            }
            case "DeleteText":
            {
                int start = offsets.Utf16(AtSpiValues.Int(args, 0)), end = offsets.Utf16(AtSpiValues.Int(args, 1));
                if (end < start) throw new ArgumentOutOfRangeException(nameof(args));
                provider.SetValue(before.Remove(start, end - start)); break;
            }
            case "CutText": case "PasteText": return AtSpiValues.Tuple(GioVariant.Bool(false));
            case "CopyText": throw new NotSupportedException("Accessibility clipboard operations have not been qualified.");
            default: throw new NotSupportedException("Unknown editable text method.");
        }
        return AtSpiValues.Tuple(GioVariant.Bool(true));
    }
    private void TextChanged(object sender, TextChangedEventArgs args)
    {
        if (!_node.IsAvailable) { _node.Dispose(); return; }
        string current = Value;
        if (current != _previousText)
        {
            var before = new Offsets(_previousText); var after = new Offsets(current); int prefix = 0, suffix = 0;
            while (prefix < before.Count && prefix < after.Count && before.Slice(prefix, prefix + 1) == after.Slice(prefix, prefix + 1)) prefix++;
            while (suffix < before.Count - prefix && suffix < after.Count - prefix && before.Slice(before.Count - suffix - 1, before.Count - suffix) == after.Slice(after.Count - suffix - 1, after.Count - suffix)) suffix++;
            int removed = before.Count - prefix - suffix, inserted = after.Count - prefix - suffix;
            _previousText = current;
            if (removed != 0) _node.Notify("TextChanged", "delete", prefix, removed, GioVariant.String(before.Slice(prefix, prefix + removed)));
            if (inserted != 0) _node.Notify("TextChanged", "insert", prefix, inserted, GioVariant.String(after.Slice(prefix, prefix + inserted)));
        }
        SelectionChanged(sender, args);
    }
    private void SelectionChanged(object sender, RoutedEventArgs args)
    {
        if (!_node.IsAvailable) { _node.Dispose(); return; }
        int caret = Caret; var selection = Selection;
        if (_previousSelection != selection) { _previousSelection = selection; _node.Notify("TextSelectionChanged", "", 0, 0, GioVariant.Int32(0)); }
        if (_previousCaret != caret) { _previousCaret = caret; _node.Notify("TextCaretMoved", "", caret, 0, GioVariant.Int32(0)); }
    }
    public void Dispose() { _editor.TextChanged -= TextChanged; _editor.SelectionChanged -= SelectionChanged; }

    private sealed class Offsets
    {
        internal readonly string Text;
        private readonly int[] _utf16;
        internal Offsets(string text)
        {
            Text = text; var offsets = new List<int> { 0 }; int position = 0;
            while (position < text.Length)
            {
                if (Rune.DecodeFromUtf16(text.AsSpan(position), out _, out int length) != OperationStatus.Done) throw new InvalidDataException("The actual WPF text contains an unpaired surrogate.");
                position += length; offsets.Add(position);
            }
            _utf16 = offsets.ToArray();
        }
        internal int Count => _utf16.Length - 1;
        internal int Utf16(int scalar) => scalar >= 0 && scalar <= Count ? _utf16[scalar] : throw new ArgumentOutOfRangeException(nameof(scalar));
        internal int Scalar(int utf16)
        {
            int scalar = Array.BinarySearch(_utf16, utf16);
            return scalar >= 0 ? scalar : throw new InvalidDataException("A WPF endpoint split a Unicode scalar.");
        }
        internal string Slice(int start, int end)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
            return Text.Substring(Utf16(start), Utf16(end) - Utf16(start));
        }
    }
}
