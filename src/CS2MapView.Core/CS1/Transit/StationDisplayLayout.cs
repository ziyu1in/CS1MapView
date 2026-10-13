// Port of independently authored project station grouping rules; no recovered viewer drawing code.
using System;
using System.Collections.Generic;
using System.Linq;
using CS2MapView.CS1.Geometry;
using CS2MapView.CS1.Config;

namespace CS2MapView.CS1.Transit
{
    // All coordinates in this file are game metres (X,Z), independent of zoom/export size.
    public sealed class StationDisplayLayout
    {
        public const double StraightnessTolerance = 5;
        public const double SameModeToleranceMultiplier = 1.2;
        public const double OppositeDirectionToleranceMultiplier = 1.4;
        public const double MarkerWidthMetres = 3.6 * 17280 / 4096;
        public sealed class StopPoint
        {
            public int Id;
            public MapPoint Position;
            public List<TransportStop> Stops = [];
        }
        public sealed class Cluster
        {
            public int Id;
            public List<StopPoint> Points = [];
            public MapPoint A, B;
            public double Error;
            public double Tolerance = StraightnessTolerance;
            public int OppositePairs, SameModePairs;
            public double Span => (B - A).Length;
            public MergedStation Station = null!;
        }
        public sealed class PointLink
        {
            public StopPoint A = null!, B = null!;
            public double Distance;
        }
        public sealed class Link
        {
            public Cluster First = null!, Second = null!;
            public MapPoint A, B;
            public double Length => (B - A).Length;
        }

        public List<StopPoint> Points = new List<StopPoint>();
        public List<Cluster> Clusters = new List<Cluster>();
        public List<PointLink> PointLinks = new List<PointLink>();
        public List<Link> Links = new List<Link>();
        public List<MergedStation> Stations = new List<MergedStation>();
        public int CandidateCount;

        public static StationDisplayLayout Build(List<TransportInfo> routes, TransportRouteMapConfig config)
        {
            var result = new StationDisplayLayout();
            // Shared nodes and coincident physical platforms are atomic. Route memberships
            // are retained even when several services use the same position.
            var stops = routes.Where(r => r.ViewerVisible && !r.HadLoadError && StationGrouping.ShowStops(r.TransportType, config))
                .SelectMany(r => r.Stops).Where(s => s.Node != null)
                .GroupBy(s => new { s.NodeId, s.TransportId }).Select(g => g.First())
                .OrderBy(s => s.NodeId).ThenBy(s => s.TransportId);
            foreach (var stop in stops)
            {
                var pos = new MapPoint(stop.PositionOnSegment.X, stop.PositionOnSegment.Z);
                var point = result.Points.FirstOrDefault(p => (p.Position - pos).Length < .01
                    && p.Stops.All(s => config.CanConnectTransfers(s.TransportType, stop.TransportType)));
                if (point == null) {
                    point = new StopPoint { Id = result.Points.Count, Position = pos, Stops = new List<TransportStop>() };
                    result.Points.Add(point);
                }
                point.Stops.Add(stop);
            }
            int count = result.Points.Count;
            var compatible = new bool[count, count];
            var opposite = new bool[count, count];
            var sameMode = new bool[count, count];
            var headings = result.Points.SelectMany(p => p.Stops).ToDictionary(s => s, DepartureHeading);
            for (int i = 0; i < count; i++)
            for (int j = i; j < count; j++)
            {
                compatible[i, j] = compatible[j, i] = result.Points[i].Stops.All(a => result.Points[j].Stops.All(b =>
                    config.CanConnectTransfers(a.TransportType, b.TransportType)));
                sameMode[i, j] = sameMode[j, i] = result.Points[i].Stops.Any(a => result.Points[j].Stops.Any(b => a.TransportType == b.TransportType));
                opposite[i, j] = opposite[j, i] = result.Points[i].Stops.Any(a => result.Points[j].Stops.Any(b =>
                    a.TransportId == b.TransportId && headings[a].HasValue && headings[b].HasValue
                    && MapVector.Multiply(headings[a].GetValueOrDefault(), headings[b].GetValueOrDefault()) <= -Math.Cos(Math.PI / 6)));
            }

            // Search around EVERY stop, not just a chosen seed. Enumerate pairs, trios,
            // quartets and larger subsets; a stop can occur in several candidates.
            var seen = new HashSet<string>();
            var candidates = new List<Cluster>();
            double radius = config.SafeStationClusterRadius;
            foreach (var anchor in result.Points)
            {
                var nearby = result.Points.Where(p => (p.Position - anchor.Position).Length <= radius + 1e-7).ToList();
                if (nearby.Count > 12 && Fit(nearby).Error < 1e-7 && nearby.All(p => nearby.All(q => compatible[p.Id, q.Id])))
                {
                    // On an exact straight row, a non-contiguous subset would run through
                    // an omitted marker and fail the overlap rule. Enumerate all intervals
                    // instead of materialising exponentially many impossible partitions.
                    var axis = Fit(nearby);
                    var row = nearby.OrderBy(p => MapVector.Multiply(p.Position - axis.A, axis.B - axis.A)).ToList();
                    for (int a = 0; a < row.Count; a++)
                    for (int b = a; b < row.Count; b++) {
                        var members = row.Skip(a).Take(b - a + 1).OrderBy(p => p.Id).ToList();
                        if (seen.Add(string.Join(",", members.Select(p => p.Id)))) candidates.Add(DescribeCandidate(members, opposite, sameMode));
                    }
                    continue;
                }
                Enumerate(nearby, 0, new List<StopPoint>(), compatible, opposite, sameMode, seen, candidates);
            }
            result.CandidateCount = candidates.Count;
            result.Clusters = SelectPartition(result.Points, candidates, opposite, sameMode);
            for (int i = 0; i < result.Clusters.Count; i++) result.Clusters[i].Id = i;

            // Retain all legal candidates for distance/policy checks. The final drawing
            // is a minimum spanning forest: shortest links, full reachable coverage,
            // branches allowed, no loops and one connector per pair of symbols.
            double limit = config.SafeStationLinkDistance;
            for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
            {
                double distance = (result.Points[i].Position - result.Points[j].Position).Length;
                if (compatible[i, j] && distance <= limit + 1e-7)
                    result.PointLinks.Add(new PointLink { A = result.Points[i], B = result.Points[j], Distance = distance });
            }
            var owner = result.Clusters.SelectMany(c => c.Points.Select(p => new { p.Id, Cluster = c })).ToDictionary(x => x.Id, x => x.Cluster);
            var drawn = new HashSet<string>();
            var possibleLinks = new List<Link>();
            foreach (var edge in result.PointLinks)
            {
                Cluster a = owner[edge.A.Id], b = owner[edge.B.Id];
                if (a == b) continue; // Internal adjacency is already represented by the straight cluster.
                string key = Math.Min(a.Id, b.Id) + ":" + Math.Max(a.Id, b.Id);
                if (!drawn.Add(key)) continue; // One visible connector per pair of cluster symbols.
                Nearest(a.A, a.B, b.A, b.B, out MapPoint p, out MapPoint q);
                possibleLinks.Add(new Link { First = a, Second = b, A = p, B = q });
            }
            var parents = Enumerable.Range(0, result.Clusters.Count).ToArray();
            foreach (var link in possibleLinks.OrderBy(l => l.Length)
                .ThenBy(l => Math.Min(l.First.Id, l.Second.Id)).ThenBy(l => Math.Max(l.First.Id, l.Second.Id))) {
                int a = Find(parents, link.First.Id), b = Find(parents, link.Second.Id);
                if (a == b) continue;
                parents[b] = a;
                result.Links.Add(link);
            }
            result.BuildSelections(Math.Max(radius, limit), compatible);
            return result;
        }

        private static void Enumerate(List<StopPoint> nearby, int start, List<StopPoint> chosen,
            bool[,] compatible, bool[,] opposite, bool[,] sameMode, HashSet<string> seen, List<Cluster> candidates)
        {
            for (int i = start; i < nearby.Count; i++)
            {
                var p = nearby[i];
                if (chosen.Any(q => !compatible[p.Id, q.Id])) continue;
                // A subset fitting a strip of width 2*tolerance cannot contain a triangle
                // whose minimum strip width exceeds that. This safely prunes all supersets.
                bool impossible = false;
                for (int a = 0; a < chosen.Count && !impossible; a++)
                for (int b = a + 1; b < chosen.Count; b++)
                {
                    var x = chosen[a].Position; var y = chosen[b].Position;
                    double longest = Math.Max((x - y).Length, Math.Max((x - p.Position).Length, (y - p.Position).Length));
                    if (longest > 0 && Math.Abs(Cross(y - x, p.Position - x)) / longest > 2 * StraightnessTolerance * OppositeDirectionToleranceMultiplier + 1e-7)
                    { impossible = true; break; }
                }
                if (impossible) continue;
                chosen.Add(p);
                string key = string.Join(",", chosen.Select(q => q.Id));
                if (seen.Add(key)) {
                    var candidate = DescribeCandidate(chosen, opposite, sameMode);
                    if (candidate.Error <= candidate.Tolerance + 1e-7) candidates.Add(candidate);
                }
                Enumerate(nearby, i + 1, chosen, compatible, opposite, sameMode, seen, candidates);
                chosen.RemoveAt(chosen.Count - 1);
            }
        }

        public static MapVector? DepartureHeading(TransportStop stop)
        {
            // PointsToNextStop is ordered in this service's travel direction. A shared
            // node or segment's orientation alone cannot identify inbound/outbound stops.
            var path = stop.PointsToNextStop.SelectMany(p => p.Points).ToList();
            if (path.Count < 2) return null;
            var origin = new MapPoint(path[0].X, path[0].Z);
            var previous = origin; double distance = 0;
            var end = origin;
            foreach (var p in path.Skip(1)) {
                var next = new MapPoint(p.X, p.Z); var edge = next - previous;
                if (edge.Length < 1e-7) continue;
                if (distance + edge.Length >= 20) { end = previous + edge * ((20 - distance) / edge.Length); break; }
                distance += edge.Length; previous = end = next;
            }
            var direction = end - origin;
            if (direction.Length < 1) return null;
            direction.Normalize(); return direction;
        }

        private static Cluster DescribeCandidate(IEnumerable<StopPoint> members, bool[,] opposite, bool[,] sameMode)
        {
            var c = Fit(members);
            for (int i = 0; i < c.Points.Count; i++)
            for (int j = i + 1; j < c.Points.Count; j++) {
                if (opposite[c.Points[i].Id, c.Points[j].Id]) c.OppositePairs++;
                if (sameMode[c.Points[i].Id, c.Points[j].Id]) c.SameModePairs++;
            }
            bool oneMode = c.Points.SelectMany(p => p.Stops).Select(s => s.TransportType).Distinct().Count() == 1;
            var commonLines = new HashSet<int>(c.Points[0].Stops.Select(s => s.TransportId));
            foreach (var p in c.Points.Skip(1)) commonLines.IntersectWith(p.Stops.Select(s => s.TransportId));
            // An opposite-direction pair must not relax every unrelated member of a mixed hub.
            c.Tolerance = StraightnessTolerance * (oneMode && commonLines.Count > 0 && c.OppositePairs > 0
                ? OppositeDirectionToleranceMultiplier : oneMode ? SameModeToleranceMultiplier : 1);
            return c;
        }

        public static Cluster Fit(IEnumerable<StopPoint> members)
        {
            var points = members.OrderBy(p => p.Id).ToList();
            var center = new MapPoint(points.Average(p => p.Position.X), points.Average(p => p.Position.Y));
            double xx = 0, yy = 0, xy = 0;
            foreach (var p in points) { var v = p.Position - center; xx += v.X * v.X; yy += v.Y * v.Y; xy += v.X * v.Y; }
            double angle = .5 * Math.Atan2(2 * xy, xx - yy);
            var axis = new MapVector(Math.Cos(angle), Math.Sin(angle));
            var positions = points.Select(p => MapVector.Multiply(p.Position - center, axis)).ToList();
            return new Cluster { Points = points, A = center + axis * positions.Min(), B = center + axis * positions.Max(),
                Error = points.Max(p => Math.Abs(Cross(p.Position - center, axis))) };
        }

        private static List<Cluster> SelectPartition(List<StopPoint> points, List<Cluster> candidates, bool[,] opposite, bool[,] sameMode)
        {
            var parents = Enumerable.Range(0, points.Count).ToArray();
            foreach (var c in candidates)
                foreach (var p in c.Points.Skip(1)) parents[Find(parents, p.Id)] = Find(parents, c.Points[0].Id);
            var output = new List<Cluster>();
            foreach (var component in points.GroupBy(p => Find(parents, p.Id)))
            {
                var ids = new HashSet<int>(component.Select(p => p.Id));
                var options = candidates.Where(c => ids.Contains(c.Points[0].Id)).OrderByDescending(c => c.OppositePairs)
                    .ThenByDescending(c => c.SameModePairs).ThenByDescending(c => c.Points.Count)
                    .ThenBy(c => c.Span).ThenBy(c => c.Error).ThenBy(c => string.Join(",", c.Points.Select(p => p.Id))).ToList();
                var best = component.Select(p => Fit(new[] { p })).ToList();
                // Exact cover for normal interchanges. A deterministic work budget limits
                // partition optimisation on huge synthetic sites; all candidates still qualify.
                int visits = 0;
                SearchPartition(ids, new List<Cluster>(), options, opposite, sameMode, ref best, ref visits);
                output.AddRange(best);
            }
            return output.OrderBy(c => c.Points.Min(p => p.Id)).ToList();
        }

        private static void SearchPartition(HashSet<int> remaining, List<Cluster> chosen, List<Cluster> options,
            bool[,] opposite, bool[,] sameMode, ref List<Cluster> best, ref int visits)
        {
            if (++visits > 100000) return;
            if (remaining.Count == 0) {
                if (BetterPartition(chosen, best)) best = chosen.ToList();
                return;
            }
            int upperOpposite = chosen.Sum(c => c.OppositePairs), upperMode = chosen.Sum(c => c.SameModePairs);
            foreach (int a in remaining) foreach (int b in remaining.Where(b => b > a)) {
                if (opposite[a, b]) upperOpposite++;
                if (sameMode[a, b]) upperMode++;
            }
            int bestOpposite = best.Sum(c => c.OppositePairs), bestMode = best.Sum(c => c.SameModePairs);
            if (upperOpposite < bestOpposite || upperOpposite == bestOpposite &&
                (upperMode < bestMode || upperMode == bestMode && chosen.Count >= best.Count)) return;
            int first = remaining.Min();
            foreach (var c in options.Where(c => c.Points.Any(p => p.Id == first) && c.Points.All(p => remaining.Contains(p.Id))))
            {
                if (chosen.Any(h => (c.Points.Count > 1 || h.Points.Count > 1) &&
                    Nearest(c.A, c.B, h.A, h.B, out _, out _) < MarkerWidthMetres + .5)) continue;
                chosen.Add(c);
                SearchPartition(new HashSet<int>(remaining.Except(c.Points.Select(p => p.Id))), chosen, options, opposite, sameMode, ref best, ref visits);
                chosen.RemoveAt(chosen.Count - 1);
            }
        }

        private static bool BetterPartition(List<Cluster> candidate, List<Cluster> best)
        {
            int opposite = candidate.Sum(c => c.OppositePairs).CompareTo(best.Sum(c => c.OppositePairs));
            if (opposite != 0) return opposite > 0;
            int mode = candidate.Sum(c => c.SameModePairs).CompareTo(best.Sum(c => c.SameModePairs));
            if (mode != 0) return mode > 0;
            if (candidate.Count != best.Count) return candidate.Count < best.Count;
            double span = candidate.Sum(c => c.Span) - best.Sum(c => c.Span);
            return span < -1e-6 || Math.Abs(span) < 1e-6 && candidate.Sum(c => c.Error) < best.Sum(c => c.Error);
        }

        private void BuildSelections(double radius, bool[,] compatible)
        {
            var parents = Enumerable.Range(0, Clusters.Count).ToArray();
            var members = Clusters.Select(c => c.Points.ToList()).ToArray();
            // This bound concerns badge/highlight ownership only. It NEVER removes links.
            // It prevents a city-wide chain from becoming a single selectable station.
            foreach (var link in Links.OrderBy(l => (l.B - l.A).Length).ThenBy(l => l.First.Id).ThenBy(l => l.Second.Id))
            {
                int a = Find(parents, link.First.Id), b = Find(parents, link.Second.Id);
                if (a == b) continue;
                if (members[a].Any(p => members[b].Any(q => !compatible[p.Id, q.Id] || (p.Position - q.Position).Length > 2 * radius + 1e-7))) continue;
                parents[b] = a; members[a].AddRange(members[b]);
            }
            foreach (var group in Clusters.GroupBy(c => Find(parents, c.Id)))
            {
                var station = new MergedStation { Id = Stations.Count + 1,
                    Stops = group.SelectMany(c => c.Points).SelectMany(p => p.Stops)
                        .OrderBy(s => s.NodeId).ThenBy(s => s.TransportId).ToList() };
                Stations.Add(station);
                foreach (var c in group) c.Station = station;
            }
        }

        private static int Find(int[] parents, int i)
        { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
        private static double Cross(MapVector a, MapVector b) => a.X * b.Y - a.Y * b.X;
        public static MapPoint Project(MapPoint p, MapPoint a, MapPoint b)
        {
            var v = b - a;
            double t = v.LengthSquared < 1e-12 ? 0 : Math.Max(0, Math.Min(1, MapVector.Multiply(p - a, v) / v.LengthSquared));
            return a + v * t;
        }
        public static double Nearest(MapPoint a, MapPoint b, MapPoint c, MapPoint d, out MapPoint first, out MapPoint second)
        {
            var v = b - a; var w = d - c; double den = Cross(v, w);
            if (Math.Abs(den) > 1e-10) {
                double t = Cross(c - a, w) / den, u = Cross(c - a, v) / den;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1) { first = second = a + v * t; return 0; }
            }
            var pairs = new[] { Tuple.Create(a, Project(a, c, d)), Tuple.Create(b, Project(b, c, d)),
                Tuple.Create(Project(c, a, b), c), Tuple.Create(Project(d, a, b), d) };
            var best = pairs.OrderBy(p => (p.Item1 - p.Item2).LengthSquared).First();
            first = best.Item1; second = best.Item2; return (second - first).Length;
        }
    }
}
