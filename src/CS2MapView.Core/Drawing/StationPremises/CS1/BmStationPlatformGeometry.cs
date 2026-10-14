using CS2MapView.CS1.Geometry;
using CS2MapView.Drawing.Railways.CS1;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Drawing.StationPremises.CS1;

/// <summary>Platform projections for the four supported BM underground station layouts.</summary>
internal static class BmStationPlatformGeometry
{
    private static readonly (string Prefix, bool CenterPlatform)[] Layouts =
    [
        ("2463160214.", false), // Two tracks and two side platforms.
        ("2469645459.", true),  // Two tracks and one center platform.
        ("2621140416.", false), // Four tracks and two outer platforms, with bypass tracks.
        ("2908783312.", true)   // Four tracks, two outer platforms and one center platform.
    ];

    internal static bool IsStationTrack(string? name) => Layouts.Any(l => Matches(name, l.Prefix));

    internal static SKPath Build(XMLSegment segment, XMLSegmentType? type, double networkWidth)
    {
        var layout = Layouts.FirstOrDefault(l => Matches(segment.Name, l.Prefix));
        // Retain the established footprint for other station assets and exports without path metadata.
        if (layout.Prefix == null) return NativeGeometry.Network(segment, networkWidth);
        var paths = (type?.Lanes ?? []).Where(l => (l.Type ?? "").Split(',').Any(f => f.Trim() == "Pedestrian")
            && float.IsFinite(l.Position) && float.IsFinite(l.Width) && l.Width > 0)
            .DistinctBy(l => (l.Position, l.Width)).ToList();
        if (paths.Count == 0) return NativeGeometry.Network(segment, networkWidth);

        var bands = new List<(double Min, double Max)>();
        if (layout.CenterPlatform)
        {
            // Both center-platform assets have inner tracks at +/-6 m (4 m wide).
            // Their two pedestrian paths occupy one island between the inner track edges at +/-4 m.
            var center = paths.Where(l => Math.Abs(l.Position) < 4).ToList();
            if (center.Count == 2 && center.Any(l => l.Position < 0) && center.Any(l => l.Position > 0))
            {
                bands.Add((center.Min(l => (double)l.Position - l.Width / 2d),
                    center.Max(l => (double)l.Position + l.Width / 2d)));
                paths.RemoveAll(center.Contains);
            }
        }
        bands.AddRange(paths.Select(l => ((double)l.Position - l.Width / 2d, (double)l.Position + l.Width / 2d)));
        var points = CS1RailTrackGeometry.CleanPoints(segment.Points.Where(NativeGeometry.Valid)
            .Select(p => new MapPoint(p.X, -p.Z)));
        using var platforms = NativeGeometry.Union(bands.Select(b => Sweep(points, b.Min, b.Max)));
        using var footprint = NativeGeometry.Network(segment, networkWidth);
        return platforms.Op(footprint, SKPathOp.Intersect)
            ?? throw new InvalidOperationException("Unable to clip BM station platforms to their station footprint.");
    }

    private static bool Matches(string? name, string prefix) => (name ?? "").StartsWith(prefix, StringComparison.Ordinal);

    private static SKPath Sweep(List<MapPoint> points, double min, double max)
    {
        var result = new SKPath();
        if (points.Count < 2) return result;
        var left = CS1RailTrackGeometry.OffsetPath(points, min);
        var right = CS1RailTrackGeometry.OffsetPath(points, max);
        result.MoveTo((float)left[0].X, (float)left[0].Y);
        foreach (var p in left.Skip(1)) result.LineTo((float)p.X, (float)p.Y);
        for (int i = right.Count - 1; i >= 0; i--) result.LineTo((float)right[i].X, (float)right[i].Y);
        result.Close();
        return result;
    }
}
