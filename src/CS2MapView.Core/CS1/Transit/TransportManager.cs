using CS2MapView.CS1.Config;
using CS2MapView.CS1.Config.Styles;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.CS1.Transit;

/// <summary>
/// Newly authored decoder of the exporter's ordered physical segment references.
/// Raw XML nodes and paths remain untouched; repeated logical stops remain separate.
/// </summary>
public sealed class TransportManager
{
    public List<TransportInfo> TransportList { get; set; } = [];
    public Dictionary<int, RouteSegment> RouteSegmentMap { get; } = [];
    public StationDisplayLayout? StationLayout { get; set; }
    public List<MergedStation> MergedStations { get; set; } = [];
    public List<MergedStation> SelectableStations => StationLayout?.Stations ?? MergedStations;
    public MergedStation? HighlightedStation { get; private set; }
    public List<string> Diagnostics { get; } = [];

    public void LoadInitialData(CSLExportXML raw, ApplicationConfig config, MapStyle style)
    {
        TransportList.Clear(); RouteSegmentMap.Clear(); Diagnostics.Clear(); HighlightedStation = null;
        var nodes = raw.NodeList.ToDictionary(n => n.Id);
        var segments = raw.SegmentList.ToDictionary(s => s.Id);
        var legs = raw.SegmentList.Where(s => s.PathUnit != null || s.ItemClass is "Ship Line" or "Ferry Line" or "Airplane Line" or "Blimp Line").GroupBy(s => (s.StartNodeId, s.EndNodeId))
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).ToList());
        foreach (var source in raw.TransportList)
        {
            var route = new TransportInfo { Id = source.Id, Name = source.Name ?? "", TransportType = source.TransportType,
                ViewerDefinedLineNo = source.ViewerDefinedLineNo, ViewerVisible = source.ViewerVisible,
                UserDefinedColor = source.UserColor == null ? null : new SKColor(source.UserColor.R, source.UserColor.G, source.UserColor.B, source.UserColor.A) };
            TransportList.Add(route);
            foreach (var sourceStop in source.Stops)
            {
                nodes.TryGetValue(sourceStop.Node, out var node);
                route.Stops.Add(new TransportStop { NodeId = sourceStop.Node, Node = node,
                    TransportId = route.Id, TransportType = route.TransportType,
                    PositionOnSegment = Copy(node?.Position ?? new XMLVector3()) });
            }
            var routeLegs = new List<XMLSegment?>();
            for (int i = 0; i < route.Stops.Count; i++)
            {
                var stop = route.Stops[i]; var next = route.Stops[(i + 1) % route.Stops.Count];
                legs.TryGetValue((stop.NodeId, next.NodeId), out var choices);
                var leg = choices?.FirstOrDefault(s => (s.ItemClass ?? "").StartsWith(route.TransportType, StringComparison.OrdinalIgnoreCase))
                    ?? choices?.FirstOrDefault();
                routeLegs.Add(leg);
                var firstId = leg?.PathUnit?.SegmentIdList.FirstOrDefault() ?? 0;
                if (segments.TryGetValue(firstId, out var first) && first.Points.Count > 0)
                {
                    // Exported station platform geometry is the authoritative physical stop position.
                    stop.PositionOnSegment = first.Points.Count == 5 ? Copy(first.Points[2]) : new XMLVector3(
                        (float)first.Points.Average(p => p.X), (float)first.Points.Average(p => p.Y), (float)first.Points.Average(p => p.Z));
                }
            }
            for (int i = 0; i < route.Stops.Count; i++)
            {
                var stop = route.Stops[i]; var next = route.Stops[(i + 1) % route.Stops.Count];
                var leg = routeLegs[i];
                if (stop.Node == null || leg == null)
                {
                    route.HadLoadError = true; Diagnostics.Add($"Service {route.Id}: missing exported path from {stop.NodeId} to {next.NodeId}."); continue;
                }
                var exportedIds = leg.PathUnit?.SegmentIdList ?? [];
                var ids = exportedIds.Where((id, index) => index == 0 || exportedIds[index - 1] != id).ToList();
                stop.OriginalSegmentIds = ids.ToList();
                var physical = new List<XMLSegment>();
                foreach (int id in ids)
                {
                    if (!segments.TryGetValue(id, out var segment) || segment.Points.Count < 2 || segment.Points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Z)))
                    { route.HadLoadError = true; Diagnostics.Add($"Service {route.Id}: physical segment {id} is absent or empty."); continue; }
                    physical.Add(segment);
                }
                if (physical.Count == 0)
                {
                    // Ferry and air services can legitimately contain no physical road/track references.
                    if (route.TransportType is "Ship" or "Airplane" && leg.Points.Count >= 2 && leg.Points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Z)))
                    { physical.Add(leg); stop.OriginalSegmentIds = [leg.Id]; }
                    else { route.HadLoadError = true; continue; }
                }
                var orientation = Orient(physical, stop.PositionOnSegment, next.PositionOnSegment);
                for (int j = 0; j < physical.Count; j++)
                {
                    var segment = physical[j];
                    if (!RouteSegmentMap.TryGetValue(segment.Id, out var shared))
                    {
                        nodes.TryGetValue(segment.StartNodeId, out var a); nodes.TryGetValue(segment.EndNodeId, out var b);
                        shared = new RouteSegment { SegmentId = segment.Id, Points = segment.Points.Select(Copy).ToList(),
                            NetworkLevel = a?.UnderGround == true || b?.UnderGround == true ? -1 : a?.OnGround == false || b?.OnGround == false ? 1 : 0 };
                        RouteSegmentMap.Add(segment.Id, shared);
                    }
                    stop.DrawRoute.Add(shared);
                    bool closedSingle = physical.Count == 1 && Distance(stop.PositionOnSegment, next.PositionOnSegment) < .001;
                    var points = (orientation[j] ? segment.Points : segment.Points.AsEnumerable().Reverse()).Select(Copy).ToList();
                    if (j == 0) points = ClipFrom(points, stop.PositionOnSegment);
                    if (j == physical.Count - 1 && !closedSingle) points = ClipTo(points, next.PositionOnSegment);
                    if (closedSingle) points.AddRange(points.Take(points.Count - 1).Reverse().Select(Copy).ToArray());
                    // The middle segment sequence is retained in full, including long monorail terminal journeys.
                    if (j == 0 && Distance(points[0], stop.PositionOnSegment) > .001) points.Insert(0, Copy(stop.PositionOnSegment));
                    if (j == physical.Count - 1 && Distance(points[^1], next.PositionOnSegment) > .001) points.Add(Copy(next.PositionOnSegment));
                    if (stop.PointsToNextStop.Count > 0)
                    {
                        var end = stop.PointsToNextStop[^1].Points[^1];
                        if (Distance(end, points[0]) > .001) points.Insert(0, Copy(end));
                    }
                    var users = orientation[j] ? shared.ForwardTransports : shared.BackwardTransports;
                    if (!users.Contains(route.Id)) users.Add(route.Id);
                    if (closedSingle) { var reverseUsers = orientation[j] ? shared.BackwardTransports : shared.ForwardTransports; if (!reverseUsers.Contains(route.Id)) reverseUsers.Add(route.Id); }
                    stop.PointsToNextStop.Add(new PointsWithWidth { RefSegment = shared, Points = points });
                }
                // A terminal loop is a tiny closed journey, not a pair of nearby display platforms.
                // Keep its complete source path and shared-segment directions for inspection.
                double terminalRadius = float.IsFinite(config.RouteMapConfig.StopsMergingRange) ? Math.Max(0, config.RouteMapConfig.StopsMergingRange) : 0;
                if (config.RouteMapConfig.DetectEndLoop && Distance(stop.PositionOnSegment, next.PositionOnSegment) < 1
                    && stop.PointsToNextStop.Count > 0 && stop.PointsToNextStop.SelectMany(part => part.Points).All(p => Distance(p, stop.PositionOnSegment) <= terminalRadius))
                {
                    stop.IsTerminalLoopHidden = true;
                    stop.PointsToNextStop = [new PointsWithWidth { RefSegment = stop.DrawRoute[0], Points = [Copy(stop.PositionOnSegment), Copy(stop.PositionOnSegment)] }];
                }
            }
        }
        AssignPalette(style);
        StationLayout = StationDisplayLayout.Build(TransportList, config.RouteMapConfig);
        MergedStations = StationGrouping.MergeStations(TransportList, config.RouteMapConfig);
    }

    public void RebuildStationLayout(TransportRouteMapConfig config)
    { StationLayout = StationDisplayLayout.Build(TransportList, config); MergedStations = StationGrouping.MergeStations(TransportList, config); }

    public void HighlightStation(MergedStation station)
    {
        if (!SelectableStations.Contains(station)) { ClearHighlight(); return; }
        HighlightedStation = station;
        var nodes = station.Stops.Select(s => s.NodeId).ToHashSet();
        foreach (var route in TransportList) route.HighlightVisible = route.Stops.Any(s => nodes.Contains(s.NodeId));
    }
    public void ClearHighlight()
    { HighlightedStation = null; foreach (var route in TransportList) route.HighlightVisible = true; }

    private void AssignPalette(MapStyle style)
    {
        SKColor[] colors = style.Colors.RouteMapColors.Select(c => (SKColor)c).ToArray();
        if (colors.Length == 0) colors = [SKColors.RoyalBlue, SKColors.Crimson, SKColors.DarkGreen, SKColors.DarkOrange];
        var uses = new int[colors.Length];
        var routes = TransportList.Where(r => !r.HadLoadError).ToDictionary(r => r.Id);
        foreach (var category in routes.Values.GroupBy(r => r.TransportType is "Bus" or "Tram" or "Trolleybus" ? 0 : r.TransportType == "Metro" ? 1 : 2))
        {
            var memberships = category.ToDictionary(r => r.Id, r => r.Stops.SelectMany(s => s.DrawRoute).Select(s => s.SegmentId).ToHashSet());
            var conflicts = category.ToDictionary(r => r.Id, r => category.Where(other => memberships[r.Id].Overlaps(memberships[other.Id])).Select(other => other.Id).ToHashSet());
            foreach (var route in category.OrderByDescending(r => conflicts[r.Id].Count).ThenBy(r => r.Id))
            {
                int index = Enumerable.Range(0, colors.Length).OrderBy(c => conflicts[route.Id].Count(id => routes[id].AutoColor == colors[c]))
                    .ThenBy(c => uses[c]).ThenBy(c => c).First();
                route.AutoColor = colors[index]; uses[index]++;
            }
        }
    }

    // Two-state dynamic programme chooses endpoint directions using the complete ordered journey.
    // It does not infer directions from a station display group or rewrite the exported path.
    private static bool[] Orient(List<XMLSegment> segments, XMLVector3 origin, XMLVector3 destination)
    {
        int count = segments.Count; var cost = new double[count, 2]; var previous = new int[count, 2];
        XMLVector3 Start(int i, int state) => state == 0 ? segments[i].Points[0] : segments[i].Points[^1];
        XMLVector3 End(int i, int state) => state == 0 ? segments[i].Points[^1] : segments[i].Points[0];
        for (int state = 0; state < 2; state++) cost[0, state] = Distance(origin, Start(0, state));
        for (int i = 1; i < count; i++) for (int state = 0; state < 2; state++)
        {
            double a = cost[i - 1, 0] + Distance(End(i - 1, 0), Start(i, state));
            double b = cost[i - 1, 1] + Distance(End(i - 1, 1), Start(i, state));
            previous[i, state] = a <= b ? 0 : 1; cost[i, state] = Math.Min(a, b);
        }
        int last = cost[count - 1, 0] + Distance(End(count - 1, 0), destination)
            <= cost[count - 1, 1] + Distance(End(count - 1, 1), destination) ? 0 : 1;
        var forward = new bool[count];
        for (int i = count - 1; i >= 0; i--) { forward[i] = last == 0; last = previous[i, last]; }
        return forward;
    }

    private static List<XMLVector3> ClipFrom(List<XMLVector3> points, XMLVector3 point)
    {
        var (index, projection) = Project(points, point);
        return new[] { projection }.Concat(points.Skip(index + 1)).ToList();
    }
    private static List<XMLVector3> ClipTo(List<XMLVector3> points, XMLVector3 point)
    {
        var (index, projection) = Project(points, point);
        return points.Take(index + 1).Append(projection).ToList();
    }
    private static (int, XMLVector3) Project(List<XMLVector3> points, XMLVector3 point)
    {
        int bestIndex = 0; double bestDistance = double.MaxValue; XMLVector3 best = Copy(points[0]);
        for (int i = 1; i < points.Count; i++)
        {
            var a = points[i - 1]; var b = points[i]; double dx = b.X - a.X, dz = b.Z - a.Z;
            double length = dx * dx + dz * dz; double t = length < 1e-12 ? 0 : Math.Clamp(((point.X - a.X) * dx + (point.Z - a.Z) * dz) / length, 0, 1);
            var candidate = new XMLVector3((float)(a.X + dx * t), (float)(a.Y + (b.Y - a.Y) * t), (float)(a.Z + dz * t));
            double distance = Distance(candidate, point);
            if (distance < bestDistance) { bestDistance = distance; bestIndex = i - 1; best = candidate; }
        }
        return (bestIndex, best);
    }
    private static XMLVector3 Copy(XMLVector3 p) => new(p.X, p.Y, p.Z);
    private static double Distance(XMLVector3 a, XMLVector3 b) => Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Z - b.Z) * (a.Z - b.Z));
}
