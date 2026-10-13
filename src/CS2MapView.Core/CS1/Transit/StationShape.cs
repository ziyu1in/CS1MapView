using CS2MapView.CS1.Geometry;
using SkiaSharp;
namespace CS2MapView.CS1.Transit;

/// <summary>Native vector shape shared by station rendering, badge placement and hits.</summary>
public sealed class StationShape(SKPath path) : IDisposable
{
    public SKPath Path { get; } = path;
    public MapRect Bounds => MapRect.FromSKRect(Path.Bounds);
    public bool Contains(MapPoint p) => Path.Contains((float)p.X, (float)p.Y);
    public bool Contains(MapPoint p, double tolerance)
    {
        if (Contains(p)) return true;
        using var paint = new SKPaint {Style=SKPaintStyle.Stroke,StrokeWidth=(float)(2*tolerance)};
        using var border = paint.GetFillPath(Path); return border.Contains((float)p.X,(float)p.Y);
    }
    public bool Intersects(StationShape other)
    {
        if (!Bounds.IntersectsWith(other.Bounds)) return false;
        using var intersection = Path.Op(other.Path, SKPathOp.Intersect);
        return intersection != null && !intersection.IsEmpty;
    }
    public StationShape Clone() => new(new SKPath(Path));
    public void Dispose() => Path.Dispose();
    public static StationShape RoundedBox(MapRect box, double radius)
    { var path = new SKPath(); path.AddRoundRect(box.ToSKRect(), (float)radius, (float)radius); return new(path); }
    public static StationShape Box(MapRect box)
    { var path = new SKPath(); path.AddRect(box.ToSKRect()); return new(path); }
    public static StationShape Stroke(MapPoint a, MapPoint b, double width)
    {
        var path = new SKPath(); var axis = b - a;
        if (axis.Length < 1e-6) { path.AddCircle((float)a.X, (float)a.Y, (float)(width/2)); return new(path); }
        axis.Normalize(); var side = new MapVector(-axis.Y, axis.X) * (width/2);
        var p = a + side; var q = b + side; var r = b - side; var t = a - side;
        static SKPoint Sk(MapPoint point) => new((float)point.X, (float)point.Y);
        path.MoveTo(Sk(p)); path.LineTo(Sk(q));
        path.CubicTo(Sk(q + axis*(width/2)), Sk(r + axis*(width/2)), Sk(r));
        path.LineTo(Sk(t)); path.CubicTo(Sk(t - axis*(width/2)), Sk(p - axis*(width/2)), Sk(p)); path.Close();
        return new(path);
    }
}

public sealed class StationHitTarget : IDisposable
{
    public required MergedStation Station { get; init; }
    // Individual paths form a union. They never cancel one another's winding.
    public List<StationShape> Shapes { get; } = [];
    public MapRect Bounds
    {
        get
        {
            if (Shapes.Count == 0) return new();
            var rect = Shapes[0].Path.Bounds;
            foreach (var shape in Shapes.Skip(1)) rect = SKRect.Union(rect, shape.Path.Bounds);
            return MapRect.FromSKRect(rect);
        }
    }
    public bool Contains(MapPoint p, double padding)
    {
        if (Shapes.Any(shape => shape.Contains(p))) return true;
        if (padding <= 0) return false;
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)(padding*2), StrokeJoin = SKStrokeJoin.Round };
        foreach (var shape in Shapes)
        {
            using var outline = paint.GetFillPath(shape.Path);
            if (outline.Contains((float)p.X, (float)p.Y)) return true;
        }
        return false;
    }
    public void Dispose() { foreach (var shape in Shapes) shape.Dispose(); Shapes.Clear(); }
}
