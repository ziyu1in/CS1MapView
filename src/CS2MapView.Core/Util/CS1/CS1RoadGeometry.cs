using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Util.CS1;

/// <summary>Rendering-only road curves, recovered before display-network joining.</summary>
internal sealed class CS1RoadGeometry
{
    internal const double SampleToleranceMetres = 0.01;
    private readonly Dictionary<XMLSegment, List<RoadCurvePiece>> _pieces = new(ReferenceEqualityComparer.Instance);

    internal void Initialize(CSLExportXML xml)
    {
        _pieces.Clear();
        var manager = new CS1SegmentManager(xml);
        foreach (var segment in xml.SegmentList)
        {
            if (!manager.GetWayType(segment).HasBit(WayType.Road)) continue;
            _pieces[segment] = OriginalPieces(segment.Points);
        }
    }

    internal IReadOnlyList<RoadCurvePiece> Pieces(XMLSegment segment)
        => _pieces.TryGetValue(segment, out var pieces) ? pieces : [];

    internal static List<RoadCurvePiece> OriginalPieces(IReadOnlyList<XMLVector3> points)
        => TryRecover(points, out var curve) ? [curve] : Lines(points);

    // The simplifier reverses copied segments while assembling display chains.
    // Keep each original piece and its endpoint tangents in that same order.
    internal void Reverse(XMLSegment segment)
    {
        if (!_pieces.TryGetValue(segment, out var pieces)) return;
        pieces.Reverse();
        for (int i = 0; i < pieces.Count; i++) pieces[i] = pieces[i].Reversed();
    }

    internal void Append(XMLSegment first, XMLSegment next)
    {
        if (_pieces.TryGetValue(first, out var pieces) && _pieces.Remove(next, out var continuation))
            pieces.AddRange(continuation);
    }

    internal SKPath CreatePath(XMLSegment segment)
    {
        var path = new SKPath();
        var pieces = _pieces.TryGetValue(segment, out var stored) ? stored : Lines(segment.Points);
        SKPoint? previous = null;
        foreach (var piece in pieces)
        {
            if (previous != piece.Start) path.MoveTo(piece.Start);
            if (piece.IsCubic) path.CubicTo(piece.Control1, piece.Control2, piece.End);
            else path.LineTo(piece.End);
            previous = piece.End;
        }
        return path;
    }

    internal SKPath CreateJunctionPath(XMLSegment segment, bool fromStart)
    {
        var result = new SKPath();
        if (segment.Points.Count < 2) return result;
        int first = fromStart ? 0 : segment.Points.Count - 1;
        int step = fromStart ? 1 : -1;
        float distance = MapDistance(segment.Points[first], segment.Points[first + step]);
        // Retain the established 30 m first-leg / 20 m second-leg extent,
        // measuring the replacement patch along the actual rendered curve.
        distance = distance > 30 ? 30 : distance + (segment.Points.Count > 2
            ? Math.Min(20, MapDistance(segment.Points[first + step], segment.Points[first + 2 * step])) : 0);
        using var path = CreatePath(segment);
        using var measure = new SKPathMeasure(path, false, 4);
        distance = Math.Min(distance, measure.Length);
        if (float.IsFinite(distance) && distance > 0)
            measure.GetSegment(fromStart ? 0 : measure.Length - distance,
                fromStart ? distance : measure.Length, result, true);
        return result;
    }

    private static float MapDistance(XMLVector3 a, XMLVector3 b)
        => (float)Math.Sqrt(Math.Pow((double)a.X - b.X, 2) + Math.Pow((double)a.Z - b.Z, 2));

    private static List<RoadCurvePiece> Lines(IReadOnlyList<XMLVector3> points)
    {
        var result = new List<RoadCurvePiece>();
        for (int i = 1; i < points.Count; i++)
        {
            var start = points[i - 1].ToMapSpace();
            var end = points[i].ToMapSpace();
            result.Add(new(start, start, end, end, false, points[i - 1].Y, points[i - 1].Y, points[i].Y, points[i].Y));
        }
        return result;
    }

    internal static bool TryRecover(IReadOnlyList<XMLVector3> points, out RoadCurvePiece curve)
    {
        curve = default;
        if (points.Count != 5 || points.Any(p => p is null
            || !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))) return false;
        var a = points[0]; var q = points[1]; var m = points[2]; var r = points[3]; var d = points[4];
        // The exporter samples one cubic at t=0, 1/4, 1/2, 3/4, 1.
        // Solve the quarter samples for both handles; reserve the midpoint as
        // an independent check, including elevation, against unrelated polylines.
        static double Residual(double a, double q, double m, double r, double d)
            => (4 * q + 4 * r - a - d) / 6 - m;
        double dx = Residual(a.X, q.X, m.X, r.X, d.X);
        double dy = Residual(a.Y, q.Y, m.Y, r.Y, d.Y);
        double dz = Residual(a.Z, q.Z, m.Z, r.Z, d.Z);
        if (dx * dx + dy * dy + dz * dz > SampleToleranceMetres * SampleToleranceMetres) return false;
        static double First(double a, double q, double r, double d) => (-10 * a + 24 * q - 8 * r + 3 * d) / 9;
        static double Second(double a, double q, double r, double d) => (3 * a - 8 * q + 24 * r - 10 * d) / 9;
        var c1 = new SKPoint((float)First(a.X, q.X, r.X, d.X), (float)-First(a.Z, q.Z, r.Z, d.Z));
        var c2 = new SKPoint((float)Second(a.X, q.X, r.X, d.X), (float)-Second(a.Z, q.Z, r.Z, d.Z));
        if (!float.IsFinite(c1.X) || !float.IsFinite(c1.Y) || !float.IsFinite(c2.X) || !float.IsFinite(c2.Y)) return false;
        curve = new(a.ToMapSpace(), c1, c2, d.ToMapSpace(), true, a.Y, First(a.Y, q.Y, r.Y, d.Y), Second(a.Y, q.Y, r.Y, d.Y), d.Y);
        return true;
    }
}

internal readonly record struct RoadCurvePiece(SKPoint Start, SKPoint Control1, SKPoint Control2, SKPoint End, bool IsCubic,
    double StartHeight = 0, double Control1Height = 0, double Control2Height = 0, double EndHeight = 0)
{
    internal RoadCurvePiece Reversed() => new(End, Control2, Control1, Start, IsCubic, EndHeight, Control2Height, Control1Height, StartHeight);
}
