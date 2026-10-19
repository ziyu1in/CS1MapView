using CS2MapView.CS1.Config;
using CS2MapView.Drawing.Terrain.CS1;
using SkiaSharp;

internal static class ContourPresetChecks
{
    internal static void Run()
    {
        const int n = 6;
        var plane = Enumerable.Range(0, n * n).Select(i => (float)(i % n)).ToArray();
        var dry = new bool[n * n];
        var contour = CS1ContourBuilder.TraceLevel(plane, dry, n, 2.5f, 1, new SKPoint(0, 5)).Single();
        Require(!contour.Closed && contour.Points.Length == n && contour.Points.All(p => Math.Abs(p.X - 2.5f) < .0001f),
            "Contours did not interpolate a planar slope between grid samples");
        Require(!CS1ContourBuilder.TraceLevel(new float[n * n], dry, n, 0, 1, new SKPoint()).Any(),
            "An exactly level surface produced contour noise");
        var mask = new bool[n * n]; mask[2 * n + 2] = true;
        var split = CS1ContourBuilder.TraceLevel(plane, mask, n, 2.5f, 1, new SKPoint(0, 5)).ToArray();
        Require(split.Length == 2 && split.All(c => !c.Closed), "Water masking connected contours across a gap");
        const int hn = 9;
        var hill = Enumerable.Range(0, hn * hn).Select(i => 20f - MathF.Pow(i % hn - 4, 2) - MathF.Pow(i / hn - 4, 2)).ToArray();
        var ring = CS1ContourBuilder.TraceLevel(hill, new bool[hill.Length], hn, 13.5f, 1, new SKPoint(0, 8)).Single();
        using var path = CS1ContourBuilder.CreatePath(ring);
        Require(ring.Closed && ring.Points[0] == ring.Points[^1] && path.Contains(4, 4) && path.PointCount > ring.Points.Length,
            "Hill contours were not closed, smoothly joined curves");
        foreach (var saddle in new[] { new[] { 3f, -1, -1, 3 }, new[] { 1f, -3, -3, 1 }, new[] { -3f, 1, 1, -3 } })
        {
            var branches = CS1ContourBuilder.TraceLevel(saddle, new bool[4], 2, 0, 1, new SKPoint(0, 1)).ToArray();
            Require(branches.Length == 2 && branches.All(c => !c.Closed && c.Points.Length == 2 &&
                SKPoint.Distance(c.Points[0], c.Points[1]) < .5f), "Ambiguous saddle contours crossed or joined the wrong sides");
        }
        Require(CS1ContourBuilder.GetPreset(ContourDetail.Fine).Passes == 1 &&
                CS1ContourBuilder.GetPreset(ContourDetail.Balanced).Passes == 3 &&
                CS1ContourBuilder.GetPreset(ContourDetail.Sparse).Interval == 50,
            "The approved A/B/C smoothing/detail presets changed");
        Console.WriteLine("PASS smooth contour geometry: interpolated planes, flat plateaus, open water gaps, closed hill curves, saddle topology and A/B/C profiles");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
