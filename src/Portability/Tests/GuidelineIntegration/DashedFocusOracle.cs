using System.Windows;

internal static class DashedFocusOracle
{
    internal sealed record Observation(int excludedSamples, int strictContourSamples, int strictInkSamples, int strictMismatches, int rawMismatches, int rawMaximum);

    internal static Observation Compare(byte[] actual, byte[] stock, double dpi)
    {
        int width = (int)(192 * dpi);
        var corners = new[] { new Point(0.5, 0.5), new Point(177.5, 0.5), new Point(177.5, 13.46875), new Point(0.5, 13.46875) };
        var endpoints = new List<(Point Position, bool Horizontal, double Margin)>();
        double traveled = 0;
        for (int edge = 0; edge < 4; edge++)
        {
            Point start = corners[edge], end = corners[(edge + 1) % 4];
            bool horizontal = start.Y == end.Y;
            double length = Math.Abs(horizontal ? end.X - start.X : end.Y - start.Y);
            for (int cycle = (int)Math.Floor(traveled / 3) - 1; cycle <= Math.Ceiling((traveled + length) / 3); cycle++)
                foreach (int transition in new[] { 0, 1 })
                {
                    double distance = cycle * 3 + transition - traveled;
                    if (distance < 0 || distance > length) continue;
                    Point point = start + (end - start) * (distance / length);
                    // CDasher::Flush merges ends within .1 local units. Allow one such adjustment per edge.
                    endpoints.Add((Device(point), horizontal, .1 * (edge + 1) * dpi));
                }
            traveled += length;
        }
        double[] horizontalEdges = [SnapY(0), SnapY(1), SnapY(12.96875), SnapY(13.96875)];
        double[] verticalEdges = [0, dpi, 177 * dpi, 178 * dpi];
        int excluded = 0, contour = 0, ink = 0, mismatch = 0, rawMismatch = 0, maximum = 0;
        for (int index = 0; index < actual.Length; index++)
        {
            double x = index % width + .5, y = index / width + .5;
            int difference = Math.Abs(actual[index] - stock[index]);
            if (difference != 0) rawMismatch++;
            maximum = Math.Max(maximum, difference);
            bool masked = corners.Any(corner =>
            {
                Point device = Device(corner);
                return Math.Abs(x - device.X) <= dpi / 2 + 1 && Math.Abs(y - device.Y) <= dpi / 2 + 1;
            });
            masked |= x >= 0 && x <= 178 * dpi && horizontalEdges.Any(edge => Straddles(y, edge));
            masked |= y >= 0 && y <= SnapY(13.96875) && verticalEdges.Any(edge => Straddles(x, edge));
            masked |= endpoints.Any(endpoint => endpoint.Horizontal
                ? Math.Abs(x - endpoint.Position.X) <= endpoint.Margin + .5 && Math.Abs(y - endpoint.Position.Y) <= dpi / 2 + .5
                : Math.Abs(y - endpoint.Position.Y) <= endpoint.Margin + .5 && Math.Abs(x - endpoint.Position.X) <= dpi / 2 + .5);
            if (masked) { excluded++; continue; }
            if (difference != 0) mismatch++;
            bool nearContour = x >= 0 && x <= 178 * dpi && (y <= dpi || y >= SnapY(12.96875) && y <= SnapY(13.96875)) ||
                y >= 0 && y <= SnapY(13.96875) && (x <= dpi || x >= 177 * dpi && x <= 178 * dpi);
            if (nearContour) { contour++; if (stock[index] != 0) ink++; }
        }
        return new(excluded, contour, ink, mismatch, rawMismatch, maximum);

        Point Device(Point point) => new(point.X * dpi, SnapY(point.Y));
        double SnapY(double coordinate) => coordinate * dpi + (coordinate > 13.96875 / 2 ? Math.Floor(13.96875 * dpi + .5) - 13.96875 * dpi : 0);
        static bool Straddles(double center, double edge) => edge != Math.Floor(edge) && Math.Abs(center - edge) < .5;
    }
}
