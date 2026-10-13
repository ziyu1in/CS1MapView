namespace CS2MapView.CS1.Geometry;

/// <summary>Double precision planar coordinates used for metre-level fitting.</summary>
public readonly record struct MapPoint(double X, double Y)
{
    public static MapVector operator -(MapPoint a, MapPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static MapPoint operator +(MapPoint p, MapVector v) => new(p.X + v.X, p.Y + v.Y);
    public static MapPoint operator -(MapPoint p, MapVector v) => new(p.X - v.X, p.Y - v.Y);
}

public struct MapVector(double x, double y)
{
    public double X = x, Y = y;
    public readonly double LengthSquared => X * X + Y * Y;
    public readonly double Length => Math.Sqrt(LengthSquared);
    public void Normalize() { double length = Length; if (length > 0) { X /= length; Y /= length; } }
    public static double Multiply(MapVector a, MapVector b) => a.X * b.X + a.Y * b.Y;
    public static double CrossProduct(MapVector a, MapVector b) => a.X * b.Y - a.Y * b.X;
    public static double operator *(MapVector a, MapVector b) => Multiply(a,b);
    public static MapVector operator +(MapVector a, MapVector b) => new(a.X + b.X, a.Y + b.Y);
    public static MapVector operator -(MapVector a, MapVector b) => new(a.X - b.X, a.Y - b.Y);
    public static MapVector operator -(MapVector a) => new(-a.X, -a.Y);
    public static MapVector operator *(MapVector a, double scale) => new(a.X * scale, a.Y * scale);
    public static MapVector operator *(double scale, MapVector a) => a * scale;
    public static MapVector operator /(MapVector a, double scale) => new(a.X / scale, a.Y / scale);
}
