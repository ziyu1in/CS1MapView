using CS2MapView.CS1.Config;
using CS2MapView.CS1.Geometry;
using SkiaSharp;
namespace CS2MapView.CS1.Transit;

// Independently authored project badge-placement rules, expressed with native Skia shapes.
// This class changes display placement only, never routes, clusters or transfer ownership.
public static class StationBadgeLayout
{
    public sealed class StationLabel : IDisposable
    {
        public required SKPath Path;
        public double Width;
        public void Dispose() => Path.Dispose();
    }
    public sealed class Badge : IDisposable
    {
        public required MergedStation Station;
        public required List<StationDisplayLayout.Cluster> Clusters;
        public required List<StationDisplayRenderer.BadgeEntry> Entries;
        public required List<StationLabel> Labels;
        public required List<double> Widths;
        public required Placement Position;
        public void Dispose() { foreach (var label in Labels) label.Dispose(); Position.LeaderGeometry.Dispose(); }
    }
    public sealed class Placement
    {
        public required StationDisplayLayout.Cluster Cluster;
        public MapRect Box;
        public MapPoint SymbolEnd, BadgeEnd;
        public required StationShape LeaderGeometry;
    }
    public static List<Badge> Build(List<StationDisplayLayout.Cluster> clusters,
        IList<TransportInfo> routes, ApplicationConfig config,
        Dictionary<StationDisplayLayout.Cluster, StationShape> symbols, IEnumerable<StationShape> connectors)
    {
        double s = config.CanvasSize / 4096;
        using var face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using var paint = new SKPaint { Typeface = face, TextSize = (float)(3.05*s), IsAntialias = true };
        IEnumerable<List<StationDisplayLayout.Cluster>> groups = config.RouteMapConfig.SplitStationBadgesByCluster
            ? clusters.OrderBy(c => c.Id).Select(c => new List<StationDisplayLayout.Cluster> { c })
            : clusters.GroupBy(c => c.Station).OrderBy(g => g.Key.Id).Select(g => g.OrderBy(c => c.Id).ToList());
        var result = new List<Badge>();
        var obstacles = symbols.Values.Concat(connectors).ToList();
        MapPoint Translate(MapPoint p) => new((p.X+8640)*config.CanvasSize/17280, (8640-p.Y)*config.CanvasSize/17280);
        foreach (var group in groups)
        {
            var ids = group.SelectMany(c => c.Points).SelectMany(p => p.Stops).Select(t => t.TransportId).ToHashSet();
            var entries = StationDisplayRenderer.BadgeEntries(routes.Where(r => ids.Contains(r.Id)), config);
            if (entries.Count == 0) continue;
            var labels = entries.Select(e => new StationLabel { Path = paint.GetTextPath(e.Text, 0, 0), Width = paint.MeasureText(e.Text) }).ToList();
            var widths = labels.Select(t => Math.Max(5*s, t.Width+2*s)).ToList();
            var size = new MapSize(widths.Sum()+2*s, 5.6*s);
            var candidates = group.SelectMany(c => Candidates(c, Translate(c.A), Translate(c.B), size, s)).ToList();
            var chosen = candidates.OrderBy(p => Score(p, symbols, obstacles, result, s, config.CanvasSize)).First();
            foreach (var discarded in candidates.Where(p => !ReferenceEquals(p, chosen))) discarded.LeaderGeometry.Dispose();
            result.Add(new Badge { Station = group[0].Station, Clusters = group, Entries = entries, Labels = labels, Widths = widths, Position = chosen });
        }
        return result;
    }
    public static IEnumerable<Placement> Candidates(StationDisplayLayout.Cluster cluster, MapPoint a, MapPoint b, MapSize size, double scale)
    {
        MapVector axis = b-a; bool dot = axis.Length < 1e-6; if (!dot) axis.Normalize();
        foreach (double gap in new[] { 2.8*scale, 5.6*scale })
        {
            if (dot)
            {
                for (int i = 0; i < 8; i++)
                {
                    double angle = i*Math.PI/4; var direction = new MapVector(Math.Cos(angle), Math.Sin(angle));
                    yield return Place(cluster, a+direction*(1.8*scale), direction, size, gap, scale);
                }
            }
            else for (int end = 0; end < 2; end++)
            {
                MapVector outward = end == 0 ? -axis : axis;
                // This explicit rounded capsule contour extends 3/8 width past its axis endpoint.
                MapPoint tip = (end == 0 ? a : b)+outward*(3.6*scale*.375);
                foreach (double angle in new[] { 0.0, Math.PI/4, -Math.PI/4, Math.PI*5/12, -Math.PI*5/12 })
                {
                    var direction = new MapVector(outward.X*Math.Cos(angle)-outward.Y*Math.Sin(angle), outward.X*Math.Sin(angle)+outward.Y*Math.Cos(angle));
                    yield return Place(cluster, tip, direction, size, gap, scale);
                }
            }
        }
    }
    private static Placement Place(StationDisplayLayout.Cluster cluster, MapPoint tip, MapVector direction, MapSize size, double gap, double scale)
    {
        double support = (Math.Abs(direction.X)*size.Width+Math.Abs(direction.Y)*size.Height)/2;
        MapPoint center = tip+direction*(gap+support);
        double edge = Math.Min(Math.Abs(direction.X)<1e-9 ? double.PositiveInfinity : size.Width/(2*Math.Abs(direction.X)),
            Math.Abs(direction.Y)<1e-9 ? double.PositiveInfinity : size.Height/(2*Math.Abs(direction.Y)));
        var box = new MapRect(center.X-size.Width/2, center.Y-size.Height/2, size.Width, size.Height);
        using var outline = StationShape.RoundedBox(box, scale);
        double inside = 0, outside = edge;
        for (int i = 0; i < 20; i++)
        {
            double middle = (inside+outside)/2;
            if (outline.Contains(center-direction*middle)) inside = middle; else outside = middle;
        }
        MapPoint badgeEnd = center-direction*inside;
        return new Placement { Cluster=cluster, SymbolEnd=tip, BadgeEnd=badgeEnd, Box=box,
            LeaderGeometry=StationShape.Stroke(tip,badgeEnd,.3*scale) };
    }
    private static double Score(Placement p, Dictionary<StationDisplayLayout.Cluster, StationShape> symbols,
        List<StationShape> obstacles, List<Badge> placed, double scale, double canvasSize)
    {
        MapRect padded = p.Box; padded.Inflate(.6*scale,.6*scale);
        using var box = StationShape.Box(padded); var line = p.LeaderGeometry;
        double score = (p.BadgeEnd-p.SymbolEnd).Length/scale;
        if (padded.Left<0 || padded.Top<0 || padded.Right>canvasSize || padded.Bottom>canvasSize) score+=100000;
        foreach (var obstacle in obstacles)
        {
            if (obstacle.Intersects(box)) score+=10000;
            if (obstacle!=symbols[p.Cluster] && obstacle.Intersects(line)) score+=1000;
        }
        foreach (var badge in placed)
        {
            if (badge.Position.Box.IntersectsWith(padded)) score+=10000;
            using var otherBox=StationShape.Box(badge.Position.Box);
            if (otherBox.Intersects(line)) score+=1000;
            if (box.Intersects(badge.Position.LeaderGeometry)) score+=1000;
            if (line.Intersects(badge.Position.LeaderGeometry)) score+=500;
        }
        return score;
    }
}
