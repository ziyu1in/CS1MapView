using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CS2MapView.CS1.Config;
using CS2MapView.CS1.Transit;

using CSLMapView.XML;

internal static class StationClusterChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static TransportStop Stop(int route, int node, float x) => new TransportStop {
        TransportId = route, TransportType = "Monorail", NodeId = node,
        Node = new XMLNode { Id = node, Position = new XMLVector3(x, 0, 0) }
    };

    private static TransportInfo Route(int id, params TransportStop[] stops) => new TransportInfo {
        Id = id, TransportType = "Monorail", ViewerVisible = true, IsLineRenderingTarget = true,
        Stops = stops.ToList()
    };

    private static string Signature(List<MergedStation> groups) => string.Join("|", groups.Select(g =>
        string.Join(",", g.Stops.Select(s => s.TransportId + ":" + s.NodeId))));

    private static void UniqueServices(List<MergedStation> groups)
    {
        Check(groups.All(g => g.Stops.Select(s => s.TransportId).Distinct().Count() == g.Stops.Count),
            "A cluster contains more than one station from a line");
        Check(groups.SelectMany(g => g.Stops.Select(s => new { s.NodeId, Group = g.Id }))
            .GroupBy(x => x.NodeId).All(g => g.Select(x => x.Group).Distinct().Count() == 1),
            "A physical stop was split across transfer clusters");
    }

    internal static void Run()
    {
        var config = new TransportRouteMapConfig { RenderMonorailStation = true, StopsMergingRange = 10 };
        foreach (bool custom in new[] { false, true }) {
            config.UseCustomTransferModes = custom;
            var line = Route(1, Stop(1, 1, 0), Stop(1, 2, 18));
            var other = Route(2, Stop(2, 3, 9));
            var routes = new List<TransportInfo> { line, other };
            var groups = StationGrouping.MergeStations(routes, config);
            UniqueServices(groups);
            Check(groups.Count == 2 && groups.Sum(g => g.Stops.Count) == 3, "Indirect same-line chain was not split");

            // The nearest connection wins, even when node order favours the farther stop.
            line = Route(1, Stop(1, 1, 0), Stop(1, 2, 9));
            other = Route(2, Stop(2, 3, 8));
            routes = new List<TransportInfo> { line, other };
            groups = StationGrouping.MergeStations(routes, config);
            UniqueServices(groups);
            Check(groups.Single(g => g.Stops.Any(s => s.NodeId == 3)).Stops.Any(s => s.NodeId == 2), "Nearest eligible station did not win");
            string signature = Signature(groups);
            routes.Reverse(); line.Stops.Reverse();
            Check(Signature(StationGrouping.MergeStations(routes, config)) == signature, "Clustering depends on input order");

            // Keep a shared physical node atomic, including repeated visits on one line.
            line.Stops.Add(Stop(1, 1, 0));
            routes.Add(Route(3, Stop(3, 1, 0)));
            groups = StationGrouping.MergeStations(routes, config);
            UniqueServices(groups);
            Check(groups.Sum(g => g.Stops.Count) == 4, "A distinct service membership was lost or duplicated");
            Check(groups.Single(g => g.Stops.Any(s => s.NodeId == 1)).Stops.Select(s => s.TransportId).SequenceEqual(new[] { 1, 3 }),
                "Shared-node service membership was lost");
            var manager = new TransportManager { TransportList = routes };
            manager.MergedStations.AddRange(groups);
            manager.HighlightStation(groups.Single(g => g.Stops.Any(s => s.NodeId == 1)));
            Check(line.EffectiveVisible && routes.Single(r => r.Id == 3).EffectiveVisible && !other.EffectiveVisible,
                "Split cluster highlights an unrelated neighbouring service");
        }
        Console.WriteLine("PASS station clusters: one stop per line, indirect conflicts, nearest matches, stable ordering, shared nodes and repeated visits");
    }

    internal static void CheckReferenceMap(TransportManager manager, CSLExportXML raw, string outputDirectory)
    {
        UniqueServices(manager.MergedStations);
        var lines = new List<string>();
        foreach (var route in manager.TransportList.Where(t => t.TransportType == "Monorail"))
        for (int i = 0; i < route.Stops.Count; i++) {
            var stop = route.Stops[i]; var next = route.Stops[(i + 1) % route.Stops.Count];
            var path = raw.SegmentList.First(s => s.PathUnit != null && s.StartNodeId == stop.NodeId && s.EndNodeId == next.NodeId).PathUnit.SegmentIdList;
            var expected = path.Where((id, index) => index == 0 || id != path[index - 1]);
            Check(stop.DrawRoute.Select(s => s.SegmentId).SequenceEqual(expected), "Monorail path was truncated at " + stop.NodeId);
            double length = stop.PointsToNextStop.Sum(p => p.Points.Zip(p.Points.Skip(1), (a, b) => Math.Sqrt((Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2)))).Sum());
            Check(length > 1, "Monorail journey disappeared at " + stop.NodeId);
            var group = manager.MergedStations.Single(g => g.Stops.Any(s => s.TransportId == route.Id && s.NodeId == stop.NodeId));
            lines.Add(route.Name + " | " + stop.NodeId + " -> " + next.NodeId + " | cluster " + group.Id +
                " | assets " + stop.DrawRoute.Count + " | drawn metres " + length.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                " | position " + stop.PositionOnSegment.X + "," + stop.PositionOnSegment.Z);
        }
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllLines(Path.Combine(outputDirectory, "monorail-paths.txt"), lines);
        Console.WriteLine("PASS Reference map: all " + manager.MergedStations.Count + " clusters have one stop per line; all " + lines.Count + " monorail legs retain their full asset paths");
    }
}
