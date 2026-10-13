using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Point = CS2MapView.CS1.Geometry.MapPoint;
using Colors = SkiaSharp.SKColors;
using System.Xml.Serialization;
using CS2MapView.CS1.Config;
using CS2MapView.CS1.Transit;

using CSLMapView.XML;

internal static class CollinearStationChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static TransportInfo Route(int id, double x, double y, string mode = "Metro") => new TransportInfo {
        Id = id, Name = "Unnamed", TransportType = mode, ViewerVisible = true, UserDefinedColor = id % 2 == 0 ? Colors.Red : Colors.Blue,
        Stops = new List<TransportStop> { new TransportStop { NodeId = id, TransportId = id, TransportType = mode,
            Node = new XMLNode { Id = id, Position = new XMLVector3((float)x, 0, (float)y) } } }
    };
    private static string Signature(StationDisplayLayout layout) => string.Join("|", layout.Clusters.Select(c => string.Join(",", c.Points.SelectMany(p => p.Stops).Select(s => s.NodeId))));
    private static void Heading(TransportStop stop, float x, float z)
    {
        var p = stop.Node.Position;
        stop.PointsToNextStop.Add(new PointsWithWidth { Points = new List<XMLVector3> {
            p, new XMLVector3(p.X + x * 30, 0, p.Z + z * 30) } });
    }
    private static void PriorityChecks(TransportRouteMapConfig config)
    {
        // Vertical same-mode pairs are longer than the competing mixed horizontal pairs.
        var routes = new List<TransportInfo> { Route(1, 0, 0, "Train"), Route(2, 0, 40, "Train"), Route(3, 30, 0), Route(4, 30, 40) };
        var layout = StationDisplayLayout.Build(routes, config);
        Check(layout.Clusters.Count == 2 && layout.Clusters.All(c => c.Points.SelectMany(p => p.Stops).Select(s => s.TransportType).Distinct().Count() == 1),
            "Shorter mixed-mode pairs beat same-mode clusters");
        routes = new List<TransportInfo> { Route(1, 0, 0), Route(2, 0, 40), Route(3, 30, 0), Route(4, 30, 40) };
        var inbound = routes[1].Stops[0]; inbound.TransportId = 1;
        routes[0].Stops.Add(inbound); routes.RemoveAt(1);
        Heading(routes[0].Stops[0], 1, 0); Heading(inbound, -1, 0);
        layout = StationDisplayLayout.Build(routes, config);
        Check(layout.Clusters.Any(c => c.OppositePairs == 1 && c.Points.SelectMany(p => p.Stops).Select(s => s.NodeId).OrderBy(i => i).SequenceEqual(new[] { 1, 2 })),
            "Opposite platforms on one line lost priority to shorter same-mode pairs");
        Check(StationDisplayLayout.DepartureHeading(Route(8, 0, 0).Stops[0]) == null, "Missing direction was guessed");

        routes = new List<TransportInfo> { Route(1, 0, 0), Route(2, 40, 8.5), Route(3, 80, 0) };
        layout = StationDisplayLayout.Build(routes, config);
        Check(layout.Clusters.Count == 1 && layout.Clusters[0].Error > 5 && layout.Clusters[0].Tolerance == 6, "Same-mode coefficient was not applied");
        routes[1].TransportType = routes[1].Stops[0].TransportType = "Train";
        Check(StationDisplayLayout.Build(routes, config).Clusters.Count > 1, "Same-mode tolerance leaked into a mixed-mode group");

        routes = new List<TransportInfo> { Route(1, 0, 0), Route(2, 40, 10), Route(3, 80, 0) };
        var route = routes[0];
        foreach (var other in routes.Skip(1)) { other.Stops[0].TransportId = 1; route.Stops.Add(other.Stops[0]); }
        Heading(route.Stops[0], 1, 0); Heading(route.Stops[1], -1, 0); Heading(route.Stops[2], 1, 0);
        layout = StationDisplayLayout.Build(new List<TransportInfo> { route }, config);
        Check(layout.Clusters.Count == 1 && layout.Clusters[0].Error > 6 && layout.Clusters[0].Tolerance == 7, "Opposite-direction coefficient was not applied");
        route.Stops[1].PointsToNextStop.Clear(); Heading(route.Stops[1], 1, 0);
        Check(StationDisplayLayout.Build(new List<TransportInfo> { route }, config).Clusters.Count > 1, "One shared line ID was mistaken for opposite directions");
        Console.WriteLine("PASS cluster priority: opposite directions before same mode before mixed; observed headings; distinct 5/6/7 m tolerances; no mixed-group tolerance leakage");
    }
    private static string LinkSignature(StationDisplayLayout layout) => string.Join("|", layout.Links.Select(l => Math.Min(l.First.Id,l.Second.Id) + ":" + Math.Max(l.First.Id,l.Second.Id)));
    private static void ForestChecks(StationDisplayLayout layout)
    {
        var parents = Enumerable.Range(0,layout.Clusters.Count).ToArray();
        Func<int,int> root = i => { while(parents[i] != i) i=parents[i]; return i; };
        foreach (var link in layout.Links) {
            int a=root(link.First.Id), b=root(link.Second.Id);
            Check(a != b, "Station links contain a cycle"); parents[b]=a;
        }
        var owners=layout.Clusters.SelectMany(c=>c.Points.Select(p=>new {p.Id,Owner=c.Id})).ToDictionary(x=>x.Id,x=>x.Owner);
        foreach (var edge in layout.PointLinks) {
            int a=owners[edge.A.Id], b=owners[edge.B.Id];
            Check(root(a)==root(b), "Reachable station was disconnected by loop removal");
            if (a==b) continue;
            var first=layout.Clusters[a]; var second=layout.Clusters[b];
            double candidate=StationDisplayLayout.Nearest(first.A,first.B,second.A,second.B,out _,out _);
            // Cycle optimality: any discarded side is at least as long as every side
            // on the retained path between its endpoints. This proves minimum length,
            // rather than merely checking that an arbitrary tree has no loops.
            var queue=new Queue<Tuple<int,double>>(); var seen=new HashSet<int>();
            queue.Enqueue(Tuple.Create(a,0.0)); seen.Add(a); bool reached=false;
            while(queue.Count>0) {
                var node=queue.Dequeue();
                if(node.Item1==b) { Check(node.Item2<=candidate+1e-6,"A loop kept a longer side and discarded a shorter one"); reached=true; break; }
                foreach(var link in layout.Links.Where(l=>l.First.Id==node.Item1 || l.Second.Id==node.Item1)) {
                    int next=link.First.Id==node.Item1?link.Second.Id:link.First.Id;
                    if(seen.Add(next)) queue.Enqueue(Tuple.Create(next,Math.Max(node.Item2,link.Length)));
                }
            }
            Check(reached,"No retained path for eligible link");
        }
    }
    internal static void Run()
    {
        var config = new TransportRouteMapConfig { UseCustomTransferModes = true, RenderMetroStation = true, RenderTrainStation = true };
        PriorityChecks(config);
        var routes = new List<TransportInfo> { Route(1, 0, 0), Route(2, 25, 1), Route(3, 50, -1), Route(4, 75, 0), Route(5, 35, 35) };
        var layout = StationDisplayLayout.Build(routes, config);
        Check(layout.Clusters.Any(c => c.Points.Count == 4) && layout.Clusters.Count == 2, "Aligned quartet/off-axis dot not partitioned");
        // 15 subsets of the quartet, plus 5 involving the off-axis point (four pairs and its singleton).
        Check(layout.CandidateCount >= 20, "Pairs/trios/quartets were not all considered");
        Check(layout.Clusters.All(c => c.Error <= c.Tolerance), "Cluster exceeds maximum perpendicular deviation");
        string signature = Signature(layout); routes.Reverse();
        Check(Signature(StationDisplayLayout.Build(routes, config)) == signature, "Display partition depends on input order");

        routes = new List<TransportInfo> { Route(1, 0, 0), Route(2, 30, 0), Route(3, 0, 40), Route(4, 30, 40) };
        layout = StationDisplayLayout.Build(routes, config);
        Check(layout.Clusters.Count == 2 && layout.Clusters.All(c => c.Points.Count == 2), "Multiple clusters in one radius were lost");
        Check(layout.Links.Count == 1, "Duplicate cluster connectors not collapsed");
        StationDisplayLayout.Nearest(new Point(0, 0), new Point(100, 0), new Point(30, 40), new Point(30, 40), out Point a, out Point b);
        Check(a == new Point(30, 0) && b == new Point(30, 40), "Link did not attach to nearest point on cluster");

        // Shortest links connect every reachable station without loops or a compass rule.
        routes.Clear();
        for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++) routes.Add(Route(y * 5 + x + 1, 50 * x, 50 * y));
        config.StationClusterRadius = 0; config.StationLinkDistance = Math.Sqrt(5000);
        layout = StationDisplayLayout.Build(routes, config);
        Check(layout.PointLinks.Count == 72 && layout.Links.Count == 24, "Lattice forest must connect 25 stations with 24 links");
        Check(layout.Links.All(l => Math.Abs(l.Length - 50) < .00001), "Lattice retained a longer diagonal instead of a cardinal edge");
        ForestChecks(layout);
        string gridSignature = LinkSignature(layout); routes.Reverse();
        Check(LinkSignature(StationDisplayLayout.Build(routes, config)) == gridSignature, "Equal-length links depend on input order");
        config.StationLinkDistance = 100;
        layout = StationDisplayLayout.Build(routes, config);
        Check(layout.PointLinks.Count == 102 && layout.Links.Count == 24 && layout.Links.All(l => Math.Abs(l.Length - 50) < .00001), "Increasing link radius added redundant long lattice edges");
        ForestChecks(layout);
        config.StationClusterRadius = 90;
        layout = StationDisplayLayout.Build(routes, config);
        Check(layout.PointLinks.Count == 102, "Visual clustering removed physical candidates");
        ForestChecks(layout);
        Check(layout.Clusters.All(c => layout.Points.Any(anchor => c.Points.All(p => (p.Position - anchor.Position).Length <= 90 + 1e-6))), "Cluster grew beyond every allowed search circle");
        config.StationClusterRadius = 0; config.StationLinkDistance = 0;
        Check(StationDisplayLayout.Build(routes, config).Links.Count == 0, "Zero failed to disable transfer links");
        config.StationLinkDistance = 60;
        var triangle = StationDisplayLayout.Build(new List<TransportInfo> { Route(1,0,0),Route(2,30,0),Route(3,30,40) },config);
        Check(triangle.Links.Select(l=>l.Length).OrderBy(x=>x).SequenceEqual(new[] {30.0,40.0}), "Triangle must keep the 30/40 m sides and drop the 50 m side");
        ForestChecks(triangle);
        config.StationLinkDistance = 60; config.TransferTrain = false;
        Check(StationDisplayLayout.Build(new List<TransportInfo> { Route(1, 0, 0), Route(2, 20, 0, "Train") }, config).Links.Count == 0, "Excluded mode still links");

        var app = new ApplicationConfig();
        routes = new List<TransportInfo> { Route(1, 0, 0), Route(2, 10, 0) };
        var entries = StationDisplayRenderer.BadgeEntries(routes.Concat(routes), app);
        Check(entries.Count == 1 && entries[0].Text == "M" && entries[0].Routes.Count == 2, "Mode badge did not deduplicate actual lines");
        app.RouteMapConfig.ShowStationModeBadges = false;
        Check(StationDisplayRenderer.BadgeEntries(routes, app).Count == 0, "Mode badge toggle ignored");
        routes[0].ViewerDefinedLineNo = "X1";
        Check(StationDisplayRenderer.BadgeEntries(routes, app).Single().Text == "X1", "Mode toggle removed explicit line shorthand");
        var serializer = new XmlSerializer(typeof(TransportRouteMapConfig));
        config.StationClusterRadius = 123.5; config.StationLinkDistance = 77; config.ShowStationModeBadges = false;
        using (var writer = new StringWriter()) {
            serializer.Serialize(writer, config);
            var copy = (TransportRouteMapConfig)serializer.Deserialize(new StringReader(writer.ToString()));
            Check(copy.StationClusterRadius == 123.5 && copy.StationLinkDistance == 77 && !copy.ShowStationModeBadges, "Station display settings did not persist independently");
        }
        config.StationClusterRadius = double.NaN;
        Check(config.SafeStationClusterRadius == 90, "Non-finite station distance lost safe fallback");
        config.StationClusterRadius = -1;
        Check(config.SafeStationClusterRadius == 0, "Negative station distance bypassed safe bounds");
        config.StationClusterRadius = 513;
        Check(config.SafeStationClusterRadius == 512, "Station distance bypassed maximum bound");
        config.StationClusterRadius = 90; config.StationLinkDistance = 100;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        layout = StationDisplayLayout.Build(Enumerable.Range(1, 32).Select(i => Route(i, i * 2, 0)).ToList(), config);
        Check(layout.Clusters.Count == 1 && timer.Elapsed.TotalSeconds < 5, "Dense straight row did not complete in bounded time");
        Console.WriteLine("PASS collinear clusters: pairs/trios/quartets, multiple groups, order independence, radius bounds, nearest edges, minimum spanning lattice forest (24 links, no diagonals or loops), transfer policy, badge toggle and persistence");
    }

    internal static void Springdale(TransportManager manager, string outputDirectory)
    {
        var layout = manager.StationLayout;
        ForestChecks(layout);
        var original = manager.TransportList.Where(r => r.ViewerVisible && !r.HadLoadError).SelectMany(r => r.Stops).Select(s => s.TransportId + ":" + s.NodeId).Distinct().OrderBy(s => s);
        var displayed = layout.Clusters.SelectMany(c => c.Points).SelectMany(p => p.Stops).Select(s => s.TransportId + ":" + s.NodeId).OrderBy(s => s);
        Check(original.SequenceEqual(displayed), "Display lost or duplicated a Springdale service/stop");
        Check(layout.Clusters.All(c => c.Error <= c.Tolerance + .00001), "Springdale collinearity constraint violated");
        var lines = layout.Clusters.Select(c => {
            var members = string.Join(";", c.Points.Select(p => string.Format(CultureInfo.InvariantCulture,
                "{0:F3},{1:F3}[{2}]", p.Position.X, p.Position.Y, string.Join(",", p.Stops.Select(s => s.TransportId + ":" + s.NodeId)))));
            return string.Format(CultureInfo.InvariantCulture,
                "cluster {0} selection {1}: ({2:F3},{3:F3}) -> ({4:F3},{5:F3}); error {6:F3}; stops {7}; tolerance {8}; opposite {9}; same mode {10}",
                c.Id, c.Station.Id, c.A.X, c.A.Y, c.B.X, c.B.Y, c.Error, members, c.Tolerance, c.OppositePairs, c.SameModePairs);
        });
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllLines(Path.Combine(outputDirectory, "collinear-stations.txt"), lines);
        Console.WriteLine("PASS Springdale station display: " + layout.Points.Count + " physical positions, " + layout.CandidateCount + " candidates, " + layout.Clusters.Count + " symbols, " + layout.PointLinks.Count + " stop links / " + layout.Links.Count + " symbol links");
    }
}
