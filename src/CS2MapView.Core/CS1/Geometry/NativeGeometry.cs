using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.CS1.Geometry;

/// <summary>Owned native polygon operations shared by CS1 layer extensions.</summary>
public static class NativeGeometry
{
    public static bool Valid(XMLVector3? p) => p != null && float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    public static SKPath Polygon(IEnumerable<XMLVector3> source)
    {
        var points = source.Where(Valid).ToList();
        var result = new SKPath { FillType = SKPathFillType.Winding };
        if (points.Count < 3) return result;
        result.MoveTo(points[0].X, -points[0].Z);
        foreach (var p in points.Skip(1)) result.LineTo(p.X, -p.Z);
        result.Close(); return result;
    }
    public static double Area(IEnumerable<XMLVector3> source)
    {
        var p = source.ToList(); double area = 0;
        for (int i = 0; i < p.Count; i++) area += (double)p[i].X * p[(i + 1) % p.Count].Z - (double)p[(i + 1) % p.Count].X * p[i].Z;
        return Math.Abs(area) / 2;
    }
    public static SKPath Network(XMLSegment segment, double width)
    {
        using var centerline = new SKPath();
        var p = segment.Points.Where(Valid).ToList();
        if (p.Count < 2) return new SKPath();
        centerline.MoveTo(p[0].X, -p[0].Z);
        foreach (var point in p.Skip(1)) centerline.LineTo(point.X, -point.Z);
        using var paint = new SKPaint { IsStroke = true, StrokeWidth = (float)(double.IsFinite(width) && width > 0 ? width : 3),
            StrokeCap = SKStrokeCap.Butt, StrokeJoin = SKStrokeJoin.Round };
        var result = new SKPath();
        if (!paint.GetFillPath(centerline, result)) throw new InvalidOperationException("Unable to widen station network geometry.");
        return result;
    }
    /// <summary>Consumes all supplied paths and returns one owned union.</summary>
    public static SKPath Union(IEnumerable<SKPath> source)
    {
        var level = new List<SKPath>();
        foreach (var path in source)
            if (path.IsEmpty) path.Dispose(); else level.Add(path);
        level = level.OrderBy(p => p.Bounds.Left).ThenBy(p => p.Bounds.Top).ToList();
        while (level.Count > 1)
        {
            var next = new List<SKPath>();
            for (int i = 0; i < level.Count; i += 2)
            {
                if (i + 1 == level.Count) { next.Add(level[i]); continue; }
                var result = level[i].Op(level[i + 1], SKPathOp.Union);
                if (result == null) throw new InvalidOperationException("Unable to union native station polygons.");
                level[i].Dispose(); level[i + 1].Dispose(); next.Add(result);
            }
            level = next;
        }
        return level.Count == 0 ? new SKPath() : level[0];
    }
}
