using System;
using System.Collections.Generic;
using System.Linq;
using CSLMapView.XML;
using Point = CS2MapView.CS1.Geometry.MapPoint;
using Vector = CS2MapView.CS1.Geometry.MapVector;

namespace CS2MapView.Drawing.Railways.CS1
{
    // Coordinates and tolerances are in game metres, before zoom/layer filtering.
    // Every segment is a rigid body, including ALL its physical tracks.
    public sealed class RailJunctionReport
    {
        public int CandidateNodes, AcceptedGroups, MovedSegments, FallbackGroups, SearchStates;
        public readonly List<string> Details = new List<string>();
        public override string ToString() => "junctions=" + CandidateNodes + ", accepted groups=" + AcceptedGroups
            + ", moved segments=" + MovedSegments + ", unchanged groups=" + FallbackGroups + ", search states=" + SearchStates;
    }

    public static class RailJunctionSolver
    {
        public const double PositionTolerance = 0.005;
        public const double TangentTolerance = 0.0001; // radians; numerical tolerance, not a visible corner
        private const double MaxRotation = Math.PI / 9, MaxMovement = 24;
        private const int MaxSegments = 128, MaxNodes = 128, MaxEnds = 8, MaxStates = 20000;

        private sealed class Network
        {
            internal int Id, Start, End;
            internal List<RailTrackPath> Tracks;
        }
        private sealed class End
        {
            internal Network Network;
            internal RailTrackPath Track;
            internal bool Start;
            internal Point Point => Start ? Track.Points[0] : Track.Points.Last();
            internal Vector Direction {
                get {
                    var p = Track.Points;
                    var v = Start ? p[1] - p[0] : p[p.Count - 2] - p.Last();
                    v.Normalize(); return v;
                }
            }
        }
        private sealed class Pair { internal End A, B; }
        private struct Pose
        {
            internal double Angle, X, Y;
            internal Vector Rotate(Vector v) => new Vector(Math.Cos(Angle) * v.X - Math.Sin(Angle) * v.Y,
                Math.Sin(Angle) * v.X + Math.Cos(Angle) * v.Y);
            internal Point Apply(Point p) { var v = Rotate(new Vector(p.X, p.Y)); return new Point(v.X + X, v.Y + Y); }
        }
        private sealed class Search
        {
            internal Dictionary<int, List<End>> Nodes;
            internal List<Network> Networks;
            internal List<RailTrackPath> AllTracks;
            internal Dictionary<int, Pose> Best;
            internal double BestCost = double.PositiveInfinity;
            internal int States;
            internal bool Exhausted;
            internal string Conflict;

            internal void Run(List<int> remaining, List<Pair> pairs, Dictionary<int, Pose> poses)
            {
                if (++States > MaxStates) { Exhausted = true; return; }
                if (!Propagate(pairs, poses)) return;
                if (remaining.Count == 0)
                {
                    // Independent through paths at a mixed node stay anchored to their existing placement.
                    foreach (var network in Networks)
                        if (!poses.ContainsKey(network.Id)) {
                            poses[network.Id] = new Pose();
                            if (!Propagate(pairs, poses)) return;
                        }
                    double cost = Networks.Sum(n => n.Tracks.Sum(t => t.Points.Sum(p => (poses[n.Id].Apply(p) - p).LengthSquared)));
                    if (cost >= BestCost) return;
                    if (!ClearGeometry(Networks, AllTracks, poses, pairs)) { Conflict = "prohibited intersection"; return; }
                    BestCost = cost; Best = new Dictionary<int, Pose>(poses); return;
                }
                int node = remaining.OrderByDescending(n => Nodes[n].Count(e => poses.ContainsKey(e.Network.Id))).ThenBy(n => n).First();
                var rest = remaining.Where(n => n != node).ToList();
                foreach (var assignment in Match(Nodes[node]))
                {
                    Run(rest, pairs.Concat(assignment).ToList(), new Dictionary<int, Pose>(poses));
                    if (Exhausted) return;
                }
            }

            private bool Propagate(List<Pair> pairs, Dictionary<int, Pose> poses)
            {
                bool changed;
                do {
                    changed = false;
                    foreach (var pair in pairs)
                    {
                        Pose a, b;
                        bool hasA = poses.TryGetValue(pair.A.Network.Id, out a), hasB = poses.TryGetValue(pair.B.Network.Id, out b);
                        if (!hasA && !hasB) continue;
                        if (!hasA) {
                            a = Align(pair.A, b.Apply(pair.B.Point), -b.Rotate(pair.B.Direction));
                            if (!Bounded(pair.A.Network, a)) { Conflict = "movement bound"; return false; }
                            poses.Add(pair.A.Network.Id, a); changed = true;
                        }
                        if (!hasB) {
                            b = Align(pair.B, a.Apply(pair.A.Point), -a.Rotate(pair.A.Direction));
                            if (!Bounded(pair.B.Network, b)) { Conflict = "movement bound"; return false; }
                            poses.Add(pair.B.Network.Id, b); changed = true;
                        }
                        double distance = (a.Apply(pair.A.Point) - b.Apply(pair.B.Point)).Length;
                        double angle = (a.Rotate(pair.A.Direction) + b.Rotate(pair.B.Direction)).Length;
                        if (distance > PositionTolerance || angle > TangentTolerance) {
                            Conflict = "endpoint/tangent conflict at node " + (pair.A.Start ? pair.A.Network.Start : pair.A.Network.End)
                                + " (gap " + distance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " m)";
                            return false;
                        }
                    }
                } while (changed);
                return true;
            }
        }

        public static RailJunctionReport Reconnect(List<RailTrackPath> paths)
        {
            var report = new RailJunctionReport();
            var networks = paths.Where(t => t.Mode == WayType.Rail || t.Mode == WayType.Metro || t.Mode == WayType.Tram)
                .GroupBy(t => t.SegmentId).Select(g => new Network { Id = g.Key, Tracks = g.OrderBy(t => t.Offset).ToList(),
                    Start = g.First().StartNodeId, End = g.First().EndNodeId }).OrderBy(n => n.Id).ToList();
            var nodes = new Dictionary<int, List<End>>();
            foreach (var n in networks)
                foreach (bool start in new[] { true, false }) {
                    int id = start ? n.Start : n.End;
                    if (!nodes.ContainsKey(id)) nodes[id] = new List<End>();
                    nodes[id].AddRange(n.Tracks.Select(t => new End { Network = n, Track = t, Start = start }));
                }
            var candidates = nodes.Where(p => p.Key != 0 && p.Value.Select(e => e.Network.Tracks.Count).Distinct().Count() > 1)
                .Select(p => p.Key).OrderBy(n => n).ToList();
            report.CandidateNodes = candidates.Count;
            var anchors = new HashSet<int>();
            foreach (int id in candidates) {
                int largest = nodes[id].Max(e => e.Network.Tracks.Count);
                foreach (var e in nodes[id].Where(e => e.Network.Tracks.Count == largest)) anchors.Add(e.Network.Id);
            }
            // Existing turnouts and unsupported junctions are fixed boundaries, not invitations
            // to propagate a local repair through the city's entire single-track network.
            foreach (var node in nodes.Values) {
                int degree = node.Select(e => e.Network.Id).Distinct().Count();
                if (degree > 1 && (node.Count % 2 != 0 || node.Count > MaxEnds
                    || degree > 2 && node.Select(e => e.Network.Tracks.Count).Distinct().Count() == 1))
                    foreach (var e in node) anchors.Add(e.Network.Id);
            }
            var visited = new HashSet<int>();
            foreach (int seed in candidates)
            {
                if (visited.Contains(seed)) continue;
                var groupNodes = new HashSet<int>();
                var groupNetworks = new Dictionary<int, Network>();
                var queue = new Queue<int>(); queue.Enqueue(seed);
                bool tooLarge = false;
                while (queue.Count > 0)
                {
                    int id = queue.Dequeue();
                    if (!groupNodes.Add(id)) continue;
                    visited.Add(id);
                    if (groupNodes.Count > MaxNodes || groupNetworks.Count > MaxSegments) tooLarge = true;
                    foreach (var network in nodes[id].Select(e => e.Network).Distinct())
                    {
                        groupNetworks[network.Id] = network;
                        if (anchors.Contains(network.Id)) continue;
                        int other = network.Start == id ? network.End : network.Start;
                        // Track ends at true dead ends are free. Every other boundary is included.
                        if (nodes[other].Select(e => e.Network.Id).Distinct().Count() > 1 && !groupNodes.Contains(other)) queue.Enqueue(other);
                    }
                }
                var localNodes = groupNodes.OrderBy(n => n).ToDictionary(n => n, n => nodes[n]);
                string reason = null;
                if (localNodes.Any(p => p.Key == 0 || p.Value.Count > MaxEnds || p.Value.Count % 2 != 0)) reason = "unpairable endpoints";
                else if (tooLarge || groupNetworks.Count > MaxSegments) reason = "component limit";
                else if (groupNetworks.Values.Any(n => n.Start == n.End || n.Tracks.Select(t => t.Mode).Distinct().Count() != 1)) reason = "unsupported network";
                string size = " [" + groupNetworks.Count + " segments, " + groupNodes.Count + " nodes]";
                if (reason != null) { Fallback(report, seed, reason + size); continue; }
                var search = new Search { Nodes = localNodes, Networks = groupNetworks.Values.OrderBy(n => n.Id).ToList(), AllTracks = paths };
                var poses = groupNetworks.Keys.Where(anchors.Contains).ToDictionary(n => n, n => new Pose());
                search.Run(localNodes.Keys.ToList(), new List<Pair>(), poses);
                report.SearchStates += search.States;
                if (search.Exhausted || search.Best == null) { Fallback(report, seed, (search.Exhausted ? "search limit" : "no solution; " + (search.Conflict ?? "no compatible pairing")) + size); continue; }
                int moved = 0;
                foreach (var network in search.Networks) {
                    var pose = search.Best[network.Id];
                    if (!Changed(network, pose)) continue;
                    foreach (var track in network.Tracks) track.Points = track.Points.Select(pose.Apply).ToList();
                    moved++;
                }
                if (moved > 0) {
                    report.AcceptedGroups++; report.MovedSegments += moved;
                    report.Details.Add("ACCEPT node " + seed + ": " + moved + " segments");
                } else Fallback(report, seed, "already aligned");
            }
            return report;
        }

        private static void Fallback(RailJunctionReport report, int seed, string reason)
        { report.FallbackGroups++; report.Details.Add("UNCHANGED node " + seed + ": " + reason); }

        private static IEnumerable<List<Pair>> Match(List<End> ends)
        {
            if (ends.Count == 0) { yield return new List<Pair>(); yield break; }
            var a = ends[0];
            for (int i = 1; i < ends.Count; i++)
            {
                var b = ends[i];
                if (a.Network == b.Network || a.Track.Mode != b.Track.Mode) continue;
                if (a.Track.Elevations != null && b.Track.Elevations != null
                    && Math.Abs((a.Start ? a.Track.Elevations[0] : a.Track.Elevations.Last())
                        - (b.Start ? b.Track.Elevations[0] : b.Track.Elevations.Last())) > PositionTolerance) continue;
                // Both bodies have a bounded rotation. This cannot remove any admissible pairing.
                if (Vector.Multiply(a.Direction, b.Direction) > -Math.Cos(2 * MaxRotation + TangentTolerance)) continue;
                foreach (var rest in Match(ends.Where((e, j) => j != 0 && j != i).ToList()))
                    yield return new[] { new Pair { A = a, B = b } }.Concat(rest).ToList();
            }
        }

        private static Pose Align(End end, Point target, Vector direction)
        {
            double angle = Math.Atan2(Vector.CrossProduct(end.Direction, direction), Vector.Multiply(end.Direction, direction));
            var pose = new Pose { Angle = angle };
            var rotated = pose.Apply(end.Point);
            pose.X = target.X - rotated.X; pose.Y = target.Y - rotated.Y; return pose;
        }
        private static bool Bounded(Network n, Pose pose) => Math.Abs(pose.Angle) <= MaxRotation
            && n.Tracks.All(t => t.Points.All(p => (pose.Apply(p) - p).Length <= MaxMovement));
        private static bool Changed(Network n, Pose pose) => n.Tracks.Any(t => t.Points.Any(p => (pose.Apply(p) - p).Length > 1e-7));

        private static bool ClearGeometry(List<Network> networks, List<RailTrackPath> all, Dictionary<int, Pose> poses, List<Pair> pairs)
        {
            var moving = new HashSet<int>(networks.Where(n => Changed(n, poses[n.Id])).Select(n => n.Id));
            var transformed = all.ToDictionary(t => t, t => moving.Contains(t.SegmentId) ? t.Points.Select(poses[t.SegmentId].Apply).ToList() : t.Points);
            var bounds = transformed.ToDictionary(p => p.Key, p => Bounds(p.Value));
            foreach (var a in all.Where(t => moving.Contains(t.SegmentId)))
            {
                var pa = transformed[a];
                for (int i = 0; i < pa.Count - 1; i++)
                    for (int j = i + 2; j < pa.Count - 1; j++) {
                        double u, v; bool overlap;
                        if (Intersection(pa[i], pa[i + 1], pa[j], pa[j + 1], out u, out v, out overlap)) return false;
                    }
                foreach (var b in all)
                {
                    if (a == b || !bounds[a].IntersectsWith(bounds[b])) continue;
                    var pb = transformed[b];
                    for (int i = 0; i < pa.Count - 1; i++)
                        for (int j = 0; j < pb.Count - 1; j++)
                        {
                            double u, v; bool overlap;
                            if (!Intersection(pa[i], pa[i + 1], pb[j], pb[j + 1], out u, out v, out overlap)) continue;
                            // Only verified separation over the entire intersecting edges permits a projected crossing.
                            if (Separated(a, i, b, j)) continue;
                            if (!overlap && AllowedContact(a, i, u, b, j, v, pairs)) continue;
                            return false;
                        }
                }
            }
            return true;
        }
        private readonly record struct Bounds2(double Left, double Top, double Right, double Bottom)
        {
            internal bool IntersectsWith(Bounds2 other) => Right >= other.Left && Bottom >= other.Top && other.Right >= Left && other.Bottom >= Top;
        }
        private static Bounds2 Bounds(List<Point> p) => new(p.Min(v => v.X) - PositionTolerance, p.Min(v => v.Y) - PositionTolerance,
            p.Max(v => v.X) + PositionTolerance, p.Max(v => v.Y) + PositionTolerance);
        private static bool Separated(RailTrackPath a, int i, RailTrackPath b, int j)
        {
            if (a.Elevations == null || b.Elevations == null) return false;
            double amin = Math.Min(a.Elevations[i], a.Elevations[i + 1]), amax = Math.Max(a.Elevations[i], a.Elevations[i + 1]);
            double bmin = Math.Min(b.Elevations[j], b.Elevations[j + 1]), bmax = Math.Max(b.Elevations[j], b.Elevations[j + 1]);
            return amin - bmax >= 3 || bmin - amax >= 3;
        }
        private static bool AllowedContact(RailTrackPath a, int i, double u, RailTrackPath b, int j, double v, List<Pair> pairs)
        {
            bool ast = i == 0 && u <= 1e-7, aen = i == a.Points.Count - 2 && u >= 1 - 1e-7;
            bool bst = j == 0 && v <= 1e-7, ben = j == b.Points.Count - 2 && v >= 1 - 1e-7;
            if (!(ast || aen) || !(bst || ben)) return false;
            return pairs.Any(p => p.A.Track == a && p.A.Start == ast && p.B.Track == b && p.B.Start == bst
                || p.A.Track == b && p.A.Start == bst && p.B.Track == a && p.B.Start == ast);
        }

        // Tests actual output edges, including collinear overlaps and tangential contact.
        public static bool Intersection(Point a, Point b, Point c, Point d, out double u, out double v, out bool overlap)
        {
            u = v = 0; overlap = false;
            var r = b - a; var s = d - c; var q = c - a;
            double det = Vector.CrossProduct(r, s), scale = Math.Max(1, r.Length * s.Length);
            if (Math.Abs(det) > 1e-12 * scale) {
                u = Vector.CrossProduct(q, s) / det; v = Vector.CrossProduct(q, r) / det;
                return u >= -1e-9 && u <= 1 + 1e-9 && v >= -1e-9 && v <= 1 + 1e-9;
            }
            if (Math.Abs(Vector.CrossProduct(q, r)) > 1e-9 * Math.Max(1, r.Length) || r.LengthSquared < 1e-16 || s.LengthSquared < 1e-16) return false;
            double lo = Vector.Multiply(q, r) / r.LengthSquared, hi = Vector.Multiply(d - a, r) / r.LengthSquared;
            double first = Math.Max(0, Math.Min(lo, hi)), last = Math.Min(1, Math.Max(lo, hi));
            if (first > last + 1e-9) return false;
            overlap = (last - first) * r.Length > 1e-7;
            u = first; v = Vector.Multiply(a + r * u - c, s) / s.LengthSquared; return true;
        }
    }
}
