using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

static class WordSelectionCases
{
    internal sealed record State(int Start, int Length);
    internal sealed record Result(string Name, string Text, string Flow, State[] MoveForward, State[] MoveBackward, State[] SelectForward, State[] SelectBackward);

    internal static Result[] Capture()
    {
        var results = new List<Result>();
        foreach (var item in new[] {
            ("words", "alpha beta"), ("digits", "12 34"), ("punctuation", "a-b.c!"), ("apostrophe", "can't won't"),
            ("curly-apostrophe", "a\u2019b c"), ("soft-hyphen", "a\u00adb c"), ("line-whitespace", "a\tb\r\nc"),
            ("unicode-whitespace", "\u2003a\u00a0b\u3000c"), ("combining", "q\u0301x y"),
            ("vedic-diacritic", "a\u1cd3b"), ("kayah-li-diacritic", "a\ua92eb"),
            ("hebrew", "\u05d0\u05d1\u05f3\u05d2 \u05d3"), ("hebrew-quote", "\u05d0\u05d1\"\u05d2 \u05d3"),
            ("arabic", "\u0633\u064e\u0644\u0627\u0645 \u0661\u0662"), ("kana-cjk", "\u3042\u3044\u30a2\u30a4\uff71\uff72\u6f22\u5b57"),
            ("kana-punctuation", "\u30a2\u30fb\u30a4 \uff71\uff65\uff72"), ("kana-prolonged", "\u3042\u30fc\u30a2 \uff71\uff70\uff72"),
            ("cjk-numeric", "\u6f22\u3007\u5b57"), ("emoji-surrogate", "a\U0001f600b c"),
            ("cjk-surrogate", "a\U00020000b c"), ("kana-surrogate", "a\U0001b000b c"),
            ("unicode17-punctuation", "a\u061db"), ("unicode17-symbol", "a\u166db"),
            ("unicode17-format", "a\u180eb"), ("unicode17-kana", "a\u3300b"), ("unicode17-ideograph", "a\u4db6b") })
        foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
        {
            var box = new TextBox { Text = item.Item2, FlowDirection = flow };
            bool rtl = flow == FlowDirection.RightToLeft;
            results.Add(new Result(item.Item1, item.Item2, flow.ToString(),
                Trace(box, rtl ? EditingCommands.MoveLeftByWord : EditingCommands.MoveRightByWord, 0),
                Trace(box, rtl ? EditingCommands.MoveRightByWord : EditingCommands.MoveLeftByWord, box.Text.Length),
                Trace(box, rtl ? EditingCommands.SelectLeftByWord : EditingCommands.SelectRightByWord, 0),
                Trace(box, rtl ? EditingCommands.SelectRightByWord : EditingCommands.SelectLeftByWord, box.Text.Length)));
        }
        return results.ToArray();
    }

    private static State[] Trace(TextBox box, RoutedUICommand command, int first)
    {
        box.Select(first, 0);
        var states = new List<State> { new(box.SelectionStart, box.SelectionLength) };
        for (int step = 0; step < box.Text.Length + 3; step++)
        {
            if (!command.CanExecute(null, box)) throw new InvalidOperationException("The real TextBox word command is unavailable.");
            command.Execute(null, box);
            var next = new State(box.SelectionStart, box.SelectionLength);
            if (box.SelectedText != box.Text.Substring(next.Start, next.Length)) throw new InvalidOperationException("Word selection lost its source range.");
            foreach (int position in new[] { next.Start, next.Start + next.Length })
                if (position > 0 && position < box.Text.Length && char.IsHighSurrogate(box.Text[position - 1]) && char.IsLowSurrogate(box.Text[position]))
                    throw new InvalidOperationException("A real TextBox word command stopped inside a surrogate pair.");
            states.Add(next);
            if (next == states[^2]) return states.ToArray();
        }
        throw new InvalidOperationException("The real TextBox word command did not reach its source boundary.");
    }

    internal static IEnumerable<object> Verify()
    {
        var reference = JsonSerializer.Deserialize<Result[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WordSelectionReference.json")))!;
        Result[] actual = Capture();
        if (reference.Length != actual.Length) throw new InvalidOperationException("The word selection reference count changed.");
        for (int index = 0; index < actual.Length; index++)
        {
            Result item = actual[index], expected = reference[index];
            if (item.Name != expected.Name || item.Text != expected.Text || item.Flow != expected.Flow)
                throw new InvalidOperationException("The word selection reference identity changed.");
            if (item.Name.StartsWith("unicode17-", StringComparison.Ordinal))
            {
                bool split = item.Name is "unicode17-punctuation" or "unicode17-kana" or "unicode17-ideograph";
                int[] forward = split ? [0, 1, 2, 3, 3] : [0, 3, 3];
                int[] backward = split ? [3, 2, 1, 0, 0] : [3, 0, 0];
                expected = expected with { MoveForward = forward.Select(position => new State(position, 0)).ToArray(),
                    MoveBackward = backward.Select(position => new State(position, 0)).ToArray(),
                    SelectForward = forward.Select(position => new State(0, position)).ToArray(),
                    SelectBackward = backward.Select(position => new State(position, 3 - position)).ToArray() };
            }
            foreach (var comparison in new[] { ("move-forward", item.MoveForward, expected.MoveForward), ("move-backward", item.MoveBackward, expected.MoveBackward),
                ("select-forward", item.SelectForward, expected.SelectForward), ("select-backward", item.SelectBackward, expected.SelectBackward) })
            {
                if (!comparison.Item2.SequenceEqual(comparison.Item3))
                    throw new InvalidOperationException($"TextBox {item.Name}/{item.Flow}/{comparison.Item1}: expected {JsonSerializer.Serialize(comparison.Item3)}, actual {JsonSerializer.Serialize(comparison.Item2)}.");
                yield return new { name = $"word-{item.Name}-{item.Flow}-{comparison.Item1}", status = "pass" };
            }
        }
    }
}
