using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Point = CS2MapView.CS1.Geometry.MapPoint;
using CS2MapView.Drawing.Railways.CS1;
using CSLMapView.XML;

internal static class RailJunctionChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static RailTrackPath Track(int id, int start, int end, double offset, params Point[] points)
        => new RailTrackPath { SegmentId = id, StartNodeId = start, EndNodeId = end, Offset = offset,
            Mode = WayType.Rail, Points = points.ToList(), Elevations = points.Select(p => 0.0).ToList() };
    private static List<RailTrackPath> Split(bool curved = false)
    {
        return new List<RailTrackPath> {
            Track(1, 1, 2, -2, new Point(-20, -2), new Point(0, -2)),
            Track(1, 1, 2, 2, new Point(-20, 2), new Point(0, 2)),
            Track(2, 2, 3, 0, new Point(0, 0), new Point(10, -1), new Point(20, curved ? -4 : -2)),
            Track(3, 2, 4, 0, new Point(0, 0), new Point(10, 1), new Point(20, curved ? 4 : 2))
        };
    }
    private static Dictionary<RailTrackPath, List<Point>> Snapshot(List<RailTrackPath> paths) => paths.ToDictionary(p => p, p => p.Points.ToList());
    private static bool Unchanged(Dictionary<RailTrackPath, List<Point>> original) => original.All(p => p.Key.Points.SequenceEqual(p.Value));
    private static void Rigid(List<RailTrackPath> paths, Dictionary<RailTrackPath, List<Point>> original)
    {
        foreach (var network in paths.GroupBy(p => p.SegmentId)) {
            var before = network.SelectMany(p => original[p]).ToList();
            var after = network.SelectMany(p => p.Points).ToList();
            for (int i = 0; i < before.Count; i++)
                for (int j = i + 1; j < before.Count; j++)
                    Check(Math.Abs((before[i] - before[j]).Length - (after[i] - after[j]).Length) < 1e-8, "A track bundle was deformed");
        }
    }
    internal static void Run()
    {
        foreach (bool curved in new[] { false, true }) {
            var paths = Split(curved); var original = Snapshot(paths);
            var report = RailJunctionSolver.Reconnect(paths);
            Check(report.AcceptedGroups == 1 && report.MovedSegments == 2, "Simple split not reconnected");
            Check(paths.Take(2).All(p => p.Points.SequenceEqual(original[p])), "Higher-count parent moved");
            foreach (var branch in paths.Skip(2)) {
                Check(paths.Take(2).Any(p => (p.Points.Last() - branch.Points[0]).Length < 1e-8), "Endpoint gap remains");
                Check(Math.Abs((branch.Points[1] - branch.Points[0]).Y) < 1e-8, "Join has an abrupt tangent");
            }
            Rigid(paths, original);
            var accepted = Snapshot(paths);
            RailJunctionSolver.Reconnect(paths);
            Check(Unchanged(accepted), "Repeated solver drifted");
        }
        var bundle = new List<RailTrackPath>();
        foreach (double y in new[] { -6.0, -2, 2, 6 }) bundle.Add(Track(1, 1, 2, y, new Point(-30, y), new Point(0, y)));
        for (int id = 2; id <= 3; id++)
            foreach (double y in new[] { -2.0, 2 }) {
                double angle = id == 2 ? -.1 : .1;
                Func<Point, Point> rotate = p => new Point(Math.Cos(angle) * p.X - Math.Sin(angle) * p.Y, Math.Sin(angle) * p.X + Math.Cos(angle) * p.Y);
                bundle.Add(Track(id, 2, id + 1, y, rotate(new Point(0, y)), rotate(new Point(15, y)), rotate(new Point(30, y))));
            }
        var bundleBefore = Snapshot(bundle);
        Check(RailJunctionSolver.Reconnect(bundle).AcceptedGroups == 1, "Quad to double bundles failed");
        Rigid(bundle, bundleBefore);

        // Both ends must satisfy fixed parents. Unequal target spacing makes rigid placement impossible.
        foreach (double rightSpacing in new[] { 2.0, 3 }) {
            var paths = new List<RailTrackPath>();
            foreach (double sign in new[] { -1.0, 1 }) {
                paths.Add(Track(1, 1, 2, sign * 2, new Point(-20, sign * 2), new Point(0, sign * 2)));
                paths.Add(Track(4, 5, 6, sign * rightSpacing, new Point(40, sign * rightSpacing), new Point(60, sign * rightSpacing)));
            }
            paths.Add(Track(2, 2, 5, 0, new Point(0, 0), new Point(40, 0)));
            paths.Add(Track(3, 2, 5, 0, new Point(0, 0), new Point(40, 0)));
            var original = Snapshot(paths); var report = RailJunctionSolver.Reconnect(paths);
            Check(rightSpacing == 2 ? report.AcceptedGroups == 1 : report.AcceptedGroups == 0 && Unchanged(original), "Back-to-back junction transaction failed");
            Rigid(paths, original);
        }
        // Validate against unrelated tracks, retaining verified overpasses.
        foreach (double height in new[] { 0.0, 10 }) {
            var paths = Split();
            var crossing = Track(10, 10, 11, 0, new Point(5, -10), new Point(5, 10));
            crossing.Elevations = new List<double> { height, height }; paths.Add(crossing);
            var original = Snapshot(paths); var report = RailJunctionSolver.Reconnect(paths);
            Check(height == 0 ? report.AcceptedGroups == 0 && Unchanged(original) : report.AcceptedGroups == 1, "Crossing/overpass validation failed");
        }
        var odd = Split(); odd.Add(Track(5, 2, 9, 0, new Point(0, 0), new Point(0, 10)));
        var oddBefore = Snapshot(odd);
        Check(RailJunctionSolver.Reconnect(odd).AcceptedGroups == 0 && Unchanged(oddBefore), "Unpairable junction changed");
        // The user's split-plus-diamond example: sharing a game node must not whitelist
        // the two interior crossings created at the offset parent-track endpoints.
        var diamond = Split();
        diamond.Add(Track(5, 2, 9, 0, new Point(0, 0), new Point(0, -10)));
        diamond.Add(Track(6, 2, 10, 0, new Point(0, 0), new Point(0, 10)));
        var diamondBefore = Snapshot(diamond);
        Check(RailJunctionSolver.Reconnect(diamond).AcceptedGroups == 0 && Unchanged(diamondBefore), "Mixed diamond was improperly exempted");
        // A long, otherwise feasible chain must fall back atomically when its component exceeds the cap.
        var longChain = Split();
        for (int id = 10; id < 140; id++) {
            int from = id == 10 ? 3 : id + 1000;
            longChain.Add(Track(id, from, id + 1001, 0, new Point(20 + (id - 10) * 10, -2 - (id - 10)), new Point(30 + (id - 10) * 10, -3 - (id - 10))));
        }
        var chainBefore = Snapshot(longChain);
        Check(RailJunctionSolver.Reconnect(longChain).AcceptedGroups == 0 && Unchanged(chainBefore), "Oversize component partially changed");
        var many = Split();
        for (int id = 4; id <= 9; id++) many.Add(Track(id, 2, id + 10, 0, new Point(0, 0), new Point(10, id)));
        var manyBefore = Snapshot(many);
        Check(RailJunctionSolver.Reconnect(many).AcceptedGroups == 0 && Unchanged(manyBefore), "Endpoint limit failed");
        double u, v; bool overlap;
        Check(RailJunctionSolver.Intersection(new Point(0,0), new Point(10,0), new Point(5,-1), new Point(5,1), out u, out v, out overlap), "Interior intersection missed");
        Check(RailJunctionSolver.Intersection(new Point(0,0), new Point(10,0), new Point(5,0), new Point(15,0), out u, out v, out overlap) && overlap, "Collinear overlap missed");
        Check(!RailJunctionSolver.Intersection(new Point(0,0), new Point(10,0), new Point(0,.01), new Point(10,.01), out u, out v, out overlap), "Separate parallel tracks intersected");
        var ordered = Split(true); var reversed = Split(true); reversed.Reverse();
        RailJunctionSolver.Reconnect(ordered); RailJunctionSolver.Reconnect(reversed);
        Check(ordered.All(a => a.Points.SequenceEqual(reversed.Single(b => b.SegmentId == a.SegmentId && b.Offset == a.Offset).Points)), "Input ordering changed the result");
        Console.WriteLine("PASS rigid curved/straight splits, fixed parents, double bundles, both-end constraints, atomic fallback, crossings, overpasses, limits and determinism");
    }
}
