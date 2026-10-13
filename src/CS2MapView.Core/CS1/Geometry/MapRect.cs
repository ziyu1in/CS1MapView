using SkiaSharp;
namespace CS2MapView.CS1.Geometry;

public readonly record struct MapSize(double Width, double Height);
public struct MapRect(double x, double y, double width, double height)
{
    public double X = x, Y = y, Width = width, Height = height;
    public readonly double Left => X;
    public readonly double Top => Y;
    public readonly double Right => X + Width;
    public readonly double Bottom => Y + Height;
    public readonly bool IsEmpty => Width <= 0 || Height <= 0;
    public void Inflate(double x, double y) { X -= x; Y -= y; Width += 2*x; Height += 2*y; }
    public readonly bool IntersectsWith(MapRect other) => Left <= other.Right && Right >= other.Left && Top <= other.Bottom && Bottom >= other.Top;
    public readonly bool Contains(MapPoint point) => point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
    public readonly SKRect ToSKRect() => new((float)Left, (float)Top, (float)Right, (float)Bottom);
    public static MapRect FromSKRect(SKRect r) => new(r.Left, r.Top, r.Width, r.Height);
}
