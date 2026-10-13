using CS2MapView.CS1.Config;
using CS2MapView.Util;
using CSLMapView.XML;
using Point = CS2MapView.CS1.Geometry.MapPoint;
using Vector = CS2MapView.CS1.Geometry.MapVector;

namespace CS2MapView.Drawing.Railways.CS1;

// Project-authored physical track geometry, expressed in game metres. This
// supplements the upstream Skia layer builder without using the former viewer.
    public sealed class RailTrackPath
    {
        public int SegmentId { get; set; }
        public WayType Mode { get; set; }
        public bool Tunnel { get; set; }
        public double Offset { get; set; }
        public double TieLength { get; set; }
        public List<Point> Points { get; set; }
        public int StartNodeId { get; set; }
        public int EndNodeId { get; set; }
        public List<double> Elevations { get; set; }
    }

    public sealed class CS1RailTrackGeometry
    {
        private readonly ApplicationConfig config;
        private readonly CS1SegmentManager manager;
        private readonly Dictionary<string, XMLSegmentType> types;
        public RailJunctionReport LastJoinReport { get; private set; } = new();
        public CS1RailTrackGeometry(CSLExportXML xml, ApplicationConfig config)
        {
            this.config = config; manager = new CS1SegmentManager(xml);
            types = xml.SegmentTypeList.ToDictionary(t => t.Name);
        }
        public List<RailTrackPath> BuildTrackPaths(CSLExportXML xml)
        {
            var result = new List<RailTrackPath>();
            if (xml == null) return result;
            var options = config.Railway;
            double unitsToPixels = 1;
            foreach (var segment in xml.SegmentList)
            {
                var way = manager.GetWayType(segment);
                if ((way & RailTrackMetadata.RailModes) == 0 || way.HasFlag(WayType.Transport)) continue;
                bool tunnel = way.HasFlag(WayType.Tunnel);
                // Solve the complete network before visibility filters so hiding a layer cannot move another.
                var tracks = RailTrackMetadata.Read(types.GetValueOrDefault(segment.Name)).ToList();
                if (tracks.Count == 0)
                {
                    // Old exports may have a known network type but no lane metadata.
                    var fallback = new[] { WayType.Rail, WayType.Metro, WayType.Tram, WayType.Monorail, WayType.CableCar }
                        .FirstOrDefault(mode => way.HasFlag(mode));
                    if (fallback == WayType.None) continue;
                    tracks.Add(new RailTrack { Mode = fallback, Position = 0 });
                }
                // Several permitted vehicle types or both directions can occupy one physical track.
                tracks = tracks.GroupBy(t => Math.Round(t.Position, 2)).Select(g => g.First()).OrderBy(t => t.Position).ToList();
                if (!options.SeparateTracks) tracks = new List<RailTrack> { new RailTrack { Mode = tracks[0].Mode, Position = 0 } };
                else
                {
                    // Simple networks always use one centered line, including offset one-way assets.
                    var simple = tracks.Where(t => t.Mode == WayType.Monorail || t.Mode == WayType.CableCar)
                        .Select(t => t.Mode).Distinct().Select(mode => new RailTrack { Mode = mode, Position = 0 });
                    tracks = tracks.Where(t => t.Mode != WayType.Monorail && t.Mode != WayType.CableCar)
                        .Concat(simple).OrderBy(t => t.Position).ToList();
                }
                var source = new List<XMLVector3>();
                foreach (var point in segment.Points)
                    if (Finite(point.X) && Finite(point.Y) && Finite(point.Z)
                        && (source.Count == 0 || (WorldPoint(point) - WorldPoint(source.Last())).Length > 0.0001)) source.Add(point);
                var points = source.Select(WorldPoint).ToList();
                if (points.Count < 2) continue;
                double spacing = double.PositiveInfinity;
                for (int i = 1; i < tracks.Count; i++) spacing = Math.Min(spacing, (tracks[i].Position - tracks[i - 1].Position) * unitsToPixels);
                var startDirection = EndpointDirection(segment, true, points[1] - points[0]);
                var endDirection = EndpointDirection(segment, false, points.Last() - points[points.Count - 2]);
                foreach (var track in tracks)
                    result.Add(new RailTrackPath {
                        SegmentId = segment.Id, Mode = track.Mode, Offset = track.Position,
                        StartNodeId = segment.StartNodeId, EndNodeId = segment.EndNodeId,
                        Elevations = source.Select(p => (double)p.Y).ToList(),
                        Tunnel = tunnel, TieLength = Math.Min(1.3 * 17280d / 4096d, spacing * 0.72),
                        Points = OffsetPath(points, track.Position, startDirection, endDirection)
                    });
            }
            LastJoinReport = options.SeparateTracks && options.ReconnectTracks ? RailJunctionSolver.Reconnect(result) : new RailJunctionReport();
            return result.Where(t => ModeEnabled(t.Mode) && (!t.Tunnel || options.ShowTunnels)).OrderBy(t => t.Tunnel ? 0 : 1).ToList();
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static Point WorldPoint(XMLVector3 p) => new Point(p.X, -p.Z);

        private bool ModeEnabled(WayType mode)
        {
            switch (mode)
            {
                case WayType.Rail: return config.RenderRailTrain;
                case WayType.Metro: return config.RenderRailMetro;
                case WayType.Tram: return config.RenderRailTram;
                case WayType.Monorail: return config.RenderRailMonorail;
                case WayType.CableCar: return config.RenderRailCableCar;
                default: return false;
            }
        }

        private Vector EndpointDirection(XMLSegment segment, bool start, Vector current)
        {
            var connected = start ? segment.StartConnections : segment.EndConnections;
            var neighbors = (connected ?? new List<XMLSegment>()).Where(s => s != segment && s.Name == segment.Name && s.PathUnit == null).ToList();
            if (neighbors.Count != 1) return current;
            var neighbor = neighbors[0];
            var points = CleanPoints(neighbor.Points.Select(WorldPoint));
            if (points.Count < 2) return current;
            var end = WorldPoint(start ? segment.Points.First() : segment.Points.Last());
            Vector outward;
            if ((points[0] - end).Length < 0.01) outward = points[1] - points[0];
            else if ((points.Last() - end).Length < 0.01) outward = points[points.Count - 2] - points.Last();
            else return current;
            current.Normalize(); outward.Normalize();
            var other = start ? -outward : outward;
            // Avoid extending a miter through a sharp turn or a reversal.
            return Vector.Multiply(current, other) > 0 ? current + other : current;
        }

        internal static List<Point> CleanPoints(IEnumerable<Point> source)
        {
            var points = new List<Point>();
            foreach (var point in source)
                if (!double.IsNaN(point.X) && !double.IsNaN(point.Y) && !double.IsInfinity(point.X) && !double.IsInfinity(point.Y)
                    && (points.Count == 0 || (point - points.Last()).Length > 0.0001)) points.Add(point);
            return points;
        }

        internal static List<Point> OffsetPath(List<Point> points, double offset, Vector? startDirection = null, Vector? endDirection = null)
        {
            var result = new List<Point>();
            if (points.Count < 2) return new List<Point>(points);
            for (int i = 0; i < points.Count; i++)
            {
                var before = i == 0 ? (startDirection ?? (points[1] - points[0])) : points[i] - points[i - 1];
                var after = i == points.Count - 1 ? (endDirection ?? before) : points[i + 1] - points[i];
                if (i == 0) after = before;
                if (i == points.Count - 1) before = after;
                before.Normalize(); after.Normalize();
                var normal = new Vector(-before.Y - after.Y, before.X + after.X);
                if (normal.Length < 0.001) normal = new Vector(-after.Y, after.X);
                normal.Normalize();
                double dot = Vector.Multiply(normal, new Vector(-after.Y, after.X));
                double miter = Math.Min(2.0, 1.0 / Math.Max(0.5, Math.Abs(dot)));
                result.Add(points[i] + normal * (offset * miter));
            }
            return result;
        }

    }
