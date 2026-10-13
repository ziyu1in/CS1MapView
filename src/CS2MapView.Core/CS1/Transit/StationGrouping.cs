// Independently authored project transfer membership policy, ported without WPF.
using System.Collections.Generic;
using System.Linq;
using CS2MapView.CS1.Config;


namespace CS2MapView.CS1.Transit
{
    public static class StationGrouping
    {
        private sealed class Connection
        {
            public int A, B;
            public double DistanceSquared;
        }

        public static List<MergedStation> MergeStations(List<TransportInfo> transportList, TransportRouteMapConfig config)
        {
            // A physical stop shared by several services must stay together. Repeated visits
            // to the same node by one service represent one station membership.
            var groups = transportList.Where(t => !t.HadLoadError && t.ViewerVisible && ShowStops(t.TransportType, config))
                .SelectMany(t => t.Stops).Where(s => s.Node != null)
                .GroupBy(s => s.NodeId).OrderBy(g => g.Key)
                .Select(g => g.GroupBy(s => s.TransportId).OrderBy(r => r.Key).Select(r => r.First()).ToList()).ToList();
            var parents = Enumerable.Range(0, groups.Count).ToArray();
            var services = groups.Select(g => new HashSet<int>(g.Select(s => s.TransportId))).ToArray();
            var connections = new List<Connection>();
            double rangeSquared = config.StopsMergingRange * config.StopsMergingRange;
            for (int a = 0; a < groups.Count; a++)
            for (int b = a + 1; b < groups.Count; b++)
            {
                double distance = groups[a].SelectMany(s => groups[b]
                    .Where(t => config.CanConnectTransfers(s.TransportType, t.TransportType))
                    .Select(t => DistanceSquared(s.PositionOnSegment, t.PositionOnSegment)))
                    .DefaultIfEmpty(double.PositiveInfinity).Min();
                if (distance <= rangeSquared)
                    connections.Add(new Connection { A = a, B = b, DistanceSquared = distance });
            }
            // Closest eligible connections win. Check the whole component so a chain
            // through other lines cannot absorb a second station of an existing line.
            foreach (var connection in connections.OrderBy(c => c.DistanceSquared).ThenBy(c => c.A).ThenBy(c => c.B))
            {
                int a = Find(parents, connection.A), b = Find(parents, connection.B);
                if (a == b || services[a].Overlaps(services[b])) continue;
                if (a > b) { int swap = a; a = b; b = swap; }
                parents[b] = a;
                groups[a].AddRange(groups[b]);
                groups[b].Clear();
                services[a].UnionWith(services[b]);
            }
            return groups.Where(g => g.Count > 0).Select((g, index) => new MergedStation {
                Id = index + 1,
                Stops = g.OrderBy(s => s.NodeId).ThenBy(s => s.TransportId).ToList()
            }).ToList();
        }

        private static double DistanceSquared(CSLMapView.XML.XMLVector3 a, CSLMapView.XML.XMLVector3 b) => (double)(a.X-b.X)*(a.X-b.X) + (double)(a.Z-b.Z)*(a.Z-b.Z);
        private static int Find(int[] parents, int index)
        {
            while (parents[index] != index) {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }

        public static bool ShowStops(string mode, TransportRouteMapConfig c)
        {
            switch (mode)
            {
                case "Bus": case "Trolleybus": return c.RenderBusStop;
                case "Tram": return c.RenderTramStop;
                case "Metro": return c.RenderMetroStation;
                case "Train": return c.RenderTrainStation;
                case "Monorail": return c.RenderMonorailStation;
                case "Airplane": return c.RenderBlimpStop;
                case "Ship": return c.RenderFerryHarbor;
                default: return false;
            }
        }
    }
}
