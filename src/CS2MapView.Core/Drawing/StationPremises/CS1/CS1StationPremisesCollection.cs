using System.Text.RegularExpressions;
using CS2MapView.CS1.Geometry;
using CS2MapView.Util;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Drawing.StationPremises.CS1;

/// <summary>Original asset identities and native Skia unions, collected before any label simplification.</summary>
public sealed class CS1StationPremisesCollection : IDisposable
{
    public HashSet<int> BuildingIds { get; } = [];
    public HashSet<int> ExcludedBuildingIds { get; } = [];
    public HashSet<int> StopTrackIds { get; } = [];
    public HashSet<int> StationTrackIds { get; } = [];
    public HashSet<int> PassageIds { get; } = [];
    public HashSet<int> PlatformIds { get; } = [];
    public HashSet<int> UndergroundMetroStationIds { get; } = [];
    public SKPath Geometry { get; internal set; } = new();
    public SKPath WithoutStopTracks { get; internal set; } = new();
    public SKPath Platforms { get; internal set; } = new();
    public SKPath UndergroundMetroStations { get; internal set; } = new();
    public SKPath UndergroundMetroStationsWithoutStopTracks { get; internal set; } = new();
    public void Dispose()
    {
        Geometry.Dispose(); WithoutStopTracks.Dispose(); Platforms.Dispose();
        UndergroundMetroStations.Dispose(); UndergroundMetroStationsWithoutStopTracks.Dispose();
    }
    public static CS1StationPremisesCollection Collect(CSLExportXML raw, CS2MapView.CS1.Config.ApplicationConfig? config = null) => CS1StationPremisesBuilder.Collect(raw);
}

/// <summary>Project-authored station classification, now using upstream native Skia polygon operations.</summary>
public static class CS1StationPremisesBuilder
{
        private static readonly string[] RailModes = { "Train", "Metro", "Tram", "Monorail", "CableCar" };
        private static readonly string[] FramedRailModes = { "Train", "Metro", "Monorail", "CableCar" };
        private static readonly string[] StationServices = {
            "PublicTransportTrain", "PublicTransportMetro",
            "PublicTransportMonorail", "PublicTransportCableCar", "PublicTransportBus",
            "PublicTransportShip"
        };
        private static readonly string[] BmStationTracks = { "2469645459.", "2463160214.", "2621140416.", "2908783312." };
        private static readonly string[] BmPassages = { "2372093501.", "2372093901.", "2372094372.", "2372094811.", "2372095863.", "2870803975." };

        private static bool Contains(string text, string value) => (text ?? "").IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        private static bool Valid(XMLVector3 p) => p != null && Finite(p.X) && Finite(p.Y) && Finite(p.Z);
        private static bool Flag(string flags, string value) => (flags ?? "").Split(',').Any(f => f.Trim() == value);
        private static bool HasPrefix(string name, IEnumerable<string> prefixes) => prefixes.Any(p => (name ?? "").StartsWith(p, StringComparison.Ordinal));

        internal static bool ExcludedName(XMLBuilding b) => Contains(b.Name, "yard") || Contains(b.Name, "depot")
            || Contains(b.MyName, "yard") || Contains(b.MyName, "depot");

        internal static bool IsDecorativePlatform(XMLSegment s, XMLSegmentType type)
        {
            return Contains(s.Name, "Platform") && (s.ItemClass == "Beautification Item" || s.ItemClass == "Pedestrian Path")
                && (type == null || !type.Lanes.Any(l => RailModes.Any(m => Flag(l.VehicleType, m)) || Flag(l.VehicleType, "Car")));
        }

        private static bool StationBuilding(XMLBuilding b) => b.Service == "PublicTransport"
            && StationServices.Contains(b.SubService) && !Contains(b.ItemClass, "Path") && !Contains(b.ItemClass, "Decoration");

        private static int RootId(XMLBuilding b, Dictionary<int, XMLBuilding> buildings)
        {
            var visited = new HashSet<int>();
            int parent;
            while (visited.Add(b.Id) && int.TryParse(b.ParentBuildingId, out parent) && buildings.ContainsKey(parent)) b = buildings[parent];
            return b.Id;
        }

        private static bool Placeholder(XMLBuilding b) => b.SubService == "PublicTransportMetro"
            && Contains(b.Name, "FTP - Station") && NativeGeometry.Area(b.Points) <= 65;

        public static CS1StationPremisesCollection Collect(CSLExportXML xml)
        {
            var result = new CS1StationPremisesCollection();
            var manager = new CS1SegmentManager(xml);
            var figures = new List<SKPath>();
            var buildings = xml.BuildingList.ToDictionary(b => b.Id);
            var stationBuildings = new List<XMLBuilding>();
            foreach (var family in xml.BuildingList.GroupBy(b => RootId(b, buildings)).OrderBy(g => g.Key))
            {
                // Test the entire building family so a depot's unnamed sub-buildings cannot leak through.
                if (family.Any(ExcludedName)) { foreach (var b in family) result.ExcludedBuildingIds.Add(b.Id); continue; }
                // Classify the parent facility: some train-station roofs export as airplane sub-buildings.
                if (!StationBuilding(buildings[family.Key])) continue;
                foreach (var b in family)
                {
                    if (b.Points.Count < 3 || !b.Points.All(Valid)) continue;
                    stationBuildings.Add(b);
                    // The invisible FTP building is an anchor, not the station's physical extent.
                    if (Placeholder(b)) continue;
                    result.BuildingIds.Add(b.Id);
                    figures.Add(NativeGeometry.Polygon(b.Points));
                }
            }

            var segments = xml.SegmentList.ToDictionary(s => s.Id);
            var types = xml.SegmentTypeList.ToDictionary(t => t.Name);
            var physical = xml.SegmentList.Where(s => s.PathUnit == null && !Regex.IsMatch(s.ItemClass ?? "", @"^(Train|Tram|Metro|CableCar|Monorail) Line$")
                && s.Points.Count >= 2 && s.Points.All(Valid)).ToList();
            var framedModes = WayType.Rail | WayType.Metro | WayType.Monorail | WayType.CableCar;
            var railIds = new HashSet<int>(physical.Where(s => ((manager.GetWayType(s) & (WayType.Rail | WayType.Metro | WayType.Tram | WayType.Monorail | WayType.CableCar)) != 0 && !manager.GetWayType(s).HasFlag(WayType.Transport))
                && (manager.GetWayType(s) & framedModes) != 0).Select(s => s.Id));
            var railStopNodes = new HashSet<int>(xml.TransportList.Where(t => FramedRailModes.Contains(t.TransportType)).SelectMany(t => t.Stops).Select(s => s.Node));
            // The first path segment is the actual departure platform, not the whole leg to the next stop.
            // Use the arrival segment only for stops that have no usable outgoing path (incomplete exports).
            var resolvedStops = new HashSet<int>();
            foreach (var route in xml.SegmentList.Where(s => s.PathUnit != null && s.PathUnit.SegmentIdList.Count != 0 && railStopNodes.Contains(s.StartNodeId)))
            {
                int id = route.PathUnit.SegmentIdList.FirstOrDefault();
                if (railIds.Contains(id)) { result.StopTrackIds.Add(id); resolvedStops.Add(route.StartNodeId); }
            }
            foreach (var route in xml.SegmentList.Where(s => s.PathUnit != null && s.PathUnit.SegmentIdList.Count != 0 && railStopNodes.Contains(s.EndNodeId) && !resolvedStops.Contains(s.EndNodeId)))
            {
                int id = route.PathUnit.SegmentIdList.LastOrDefault();
                if (railIds.Contains(id)) result.StopTrackIds.Add(id);
            }
            foreach (var s in physical)
            {
                XMLSegmentType type;
                types.TryGetValue(s.Name, out type);
                if (IsDecorativePlatform(s, type)) result.PlatformIds.Add(s.Id);
                if (railIds.Contains(s.Id) && (HasPrefix(s.Name, BmStationTracks) || Contains(s.Name, "Station Track")
                    || Contains(s.Name, "Station Tracks") || Contains(s.Name, "Cargo Track"))) result.StationTrackIds.Add(s.Id);
            }
            result.StationTrackIds.UnionWith(result.StopTrackIds);
            // Only recognised physical station/stop networks receive the deeper
            // platform tint. Ordinary metro tunnels retain their light rail style.
            foreach (int id in result.StationTrackIds.OrderBy(id => id))
                if (manager.GetWayType(segments[id]).HasFlag(WayType.Metro | WayType.Tunnel))
                    result.UndergroundMetroStationIds.Add(id);
            result.UndergroundMetroStationsWithoutStopTracks.Dispose();
            result.UndergroundMetroStationsWithoutStopTracks = NativeGeometry.Union(result.UndergroundMetroStationIds
                .Except(result.StopTrackIds).OrderBy(id => id).Select(id => NativeGeometry.Network(segments[id], manager.GetWidth(segments[id]))));
            result.UndergroundMetroStations.Dispose();
            result.UndergroundMetroStations = NativeGeometry.Union(result.UndergroundMetroStationIds
                .OrderBy(id => id).Select(id => NativeGeometry.Network(segments[id], manager.GetWidth(segments[id]))));
            var stationTracks = result.StationTrackIds.Select(id => segments[id]).ToList();
            // Stop tracks remain separate even when their prefab is also a station track.
            // Turning their fill off must not undo their use as passage-association anchors.
            foreach (int id in result.StationTrackIds.Except(result.StopTrackIds).OrderBy(id => id))
                figures.Add(NativeGeometry.Network(segments[id], manager.GetWidth(segments[id])));
            result.Platforms = NativeGeometry.Union(result.PlatformIds.OrderBy(id => id).Select(id => NativeGeometry.Network(segments[id], manager.GetWidth(segments[id]))));

            figures.Add(new SKPath(result.Platforms));

            var passages = physical.Where(s => IsPassage(s, types)).ToList();
            var byNode = new Dictionary<int, List<XMLSegment>>();
            foreach (var s in passages)
            foreach (int id in new[] { s.StartNodeId, s.EndNodeId })
            {
                if (!byNode.ContainsKey(id)) byNode[id] = new List<XMLSegment>();
                byNode[id].Add(s);
            }
            var seen = new HashSet<int>();
            foreach (var first in passages.OrderBy(s => s.Id))
            {
                if (seen.Contains(first.Id)) continue;
                var component = new List<XMLSegment>();
                var queue = new Queue<XMLSegment>(); queue.Enqueue(first);
                while (queue.Count != 0)
                {
                    var s = queue.Dequeue();
                    if (!seen.Add(s.Id)) continue;
                    component.Add(s);
                    foreach (int id in new[] { s.StartNodeId, s.EndNodeId })
                        foreach (var next in byNode[id]) if (!seen.Contains(next.Id)) queue.Enqueue(next);
                }
                // A component needs a station/entrance anchor. Once anchored, retain its branches to exits.
                if (!component.Any(s => stationBuildings.Any(b => NearBuilding(s, b, 18, 36))
                    || stationTracks.Any(t => NearNetwork(s, t, (manager.GetWidth(s) + manager.GetWidth(t)) / 2 + 10, 12)))) continue;
                foreach (var s in component)
                {
                    result.PassageIds.Add(s.Id);
                    figures.Add(NativeGeometry.Network(s, manager.GetWidth(s)));
                }
            }
            result.WithoutStopTracks = NativeGeometry.Union(figures);

            result.Geometry = NativeGeometry.Union(new[] { new SKPath(result.WithoutStopTracks) }.Concat(result.StopTrackIds.OrderBy(id => id)
                .Select(id => NativeGeometry.Network(segments[id], manager.GetWidth(segments[id])))));

            return result;
        }

        private static bool IsPassage(XMLSegment s, Dictionary<string, XMLSegmentType> types)
        {
            if (HasPrefix(s.Name, BmPassages)) return true; // Pathway #05 intentionally exports no lanes.
            if (s.StartNode?.UnderGround != true && s.EndNode?.UnderGround != true) return false;
            if (s.ItemClass == "Pedestrian Tunnel") return true;
            XMLSegmentType type;
            return types.TryGetValue(s.Name, out type) && type.Lanes.Any(l => Flag(l.Type, "Pedestrian"))
                && !type.Lanes.Any(l => Flag(l.Type, "Vehicle") || Flag(l.Type, "TransportVehicle"));
        }

        private static bool NearBuilding(XMLSegment s, XMLBuilding b, double gap, double height)
        {
            if (b.Points.Count < 3) return false;
            using var polygon = NativeGeometry.Polygon(b.Points);
            var bounds = polygon.Bounds; bounds.Inflate((float)gap, (float)gap);
            double y = b.Points.Average(p => (double)p.Y);
            return s.Points.Any(p => Math.Abs(p.Y - y) <= height && bounds.Contains(p.X, -p.Z)
                && (polygon.Contains(p.X, -p.Z) || b.Points.Select((a, i) => Distance(p, a, b.Points[(i + 1) % b.Points.Count], height)).Min() <= gap));
        }
        private static bool NearNetwork(XMLSegment a, XMLSegment b, double gap, double height)
        {
            for (int i = 0; i + 1 < b.Points.Count; i++)
                if (a.Points.Any(p => Distance(p, b.Points[i], b.Points[i + 1], height) <= gap)) return true;
            for (int i = 0; i + 1 < a.Points.Count; i++)
                if (b.Points.Any(p => Distance(p, a.Points[i], a.Points[i + 1], height) <= gap)) return true;
            return false;
        }
        private static double Distance(XMLVector3 p, XMLVector3 a, XMLVector3 b, double height)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z, length = dx * dx + dz * dz;
            double t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / length));
            if (Math.Abs(p.Y - (a.Y + t * (b.Y - a.Y))) > height) return double.PositiveInfinity;
            dx = p.X - (a.X + t * dx); dz = p.Z - (a.Z + t * dz);
            return Math.Sqrt(dx * dx + dz * dz);
        }

}
