using System.Diagnostics;
using System.Windows;
using System.Windows.Media.TextFormatting;

static class ContinuationBenchmark
{
    internal static object Capture()
    {
        var properties = BidiTests.Properties("DejaVuSans.ttf", "DejaVu Sans", 20);
        using var formatter = TextFormatter.Create();
        var paragraph = new Paragraph(properties, TextWrapping.Wrap);
        Measure(600, 100);
        return new { os = Environment.OSVersion.Platform.ToString(), fontSize = 20,
            narrow = new[] { Measure(7998, 100), Measure(15996, 100), Measure(32760, 100) },
            wide = new[] { Measure(7998, 1000000), Measure(15996, 1000000), Measure(32760, 1000000) },
            characterChunksNarrow = new[] { Measure(7998, 100, 1), Measure(15996, 100, 1), Measure(32760, 100, 1) },
            characterChunksWide = new[] { Measure(7998, 1000000, 1), Measure(15996, 1000000, 1), Measure(32760, 1000000, 1) } };

        object Measure(int length, double width, int chunkSize = 0)
        {
            var source = new Source(string.Concat(Enumerable.Repeat("hello ", length / 6)), properties, chunkSize);
            int position = 0, lines = 0;
            TextLineBreak? continuation = null;
            var timer = Stopwatch.StartNew();
            try
            {
                while (position < source.Length)
                {
                    using var line = formatter.FormatLine(source, position, width, paragraph, continuation!);
                    continuation?.Dispose();
                    continuation = line.GetTextLineBreak();
                    if (line.Length <= 0 || line.GetTextRunSpans().Sum(span => span.Length) != line.Length)
                        throw new InvalidOperationException("Wrapped continuation failed to consume its source spans.");
                    position += line.Length;
                    lines++;
                }
            }
            finally { continuation?.Dispose(); }
            timer.Stop();
            if (position != length + 1 || lines != (width == 100 ? length / 6 : 1))
                throw new InvalidOperationException("The continuation benchmark changed the expected one-word-per-line result.");
            return new { characters = length, width, chunkSize, lines, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds };
        }
    }
}
