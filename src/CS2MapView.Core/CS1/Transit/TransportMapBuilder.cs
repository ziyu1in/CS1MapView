using CSLMapView.XML;
namespace CS2MapView.CS1.Transit;

/// <summary>Existing CS1 visual measurements and project direction marks, rendered natively.</summary>
public static class TransportMapBuilder
{
    public sealed class RouteArrow { public XMLVector3 Position = new(); public double Angle; }
    public static double GetLinePenWidth(int lanes, int position, bool widenByLanes, double scale)
    {
        if (!widenByLanes || lanes <= 1) return scale <= 1 ? 2 : 1 + scale;
        return scale <= 1 ? 1.2 + Math.Max(0, position) * 1.8 : scale * (1 + Math.Max(0, position) * 1.8);
    }
    public static List<RouteArrow> RouteArrows(IEnumerable<PointsWithWidth> parts, int routeId)
    {
        var arrows = new List<RouteArrow>(); XMLVector3? previousEnd = null; double next = 60;
        foreach (var part in parts)
        {
            if (part.Points.Count < 2) continue;
            if (!part.IsOneWayFor(routeId)) { next = 60; previousEnd = null; continue; }
            if (previousEnd != part.Points[0]) next = 60;
            for (int i = 1; i < part.Points.Count; i++)
            {
                var a = part.Points[i - 1]; var b = part.Points[i];
                double length = Math.Sqrt((double)(b.X-a.X)*(b.X-a.X)+(double)(b.Z-a.Z)*(b.Z-a.Z));
                if (length < .001) continue;
                while (next <= length)
                {
                    double t = next / length;
                    arrows.Add(new RouteArrow { Position = new((float)(a.X + (b.X-a.X)*t), (float)(a.Y + (b.Y-a.Y)*t), (float)(a.Z + (b.Z-a.Z)*t)),
                        Angle = Math.Atan2(a.Z - b.Z, b.X - a.X) }); next += 120;
                }
                next -= length;
            }
            previousEnd = part.Points[^1];
        }
        return arrows;
    }
}
