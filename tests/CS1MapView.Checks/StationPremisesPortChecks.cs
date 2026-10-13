using System.Xml.Serialization;
using CS2MapView.CS1.Config;
using CS2MapView.CS1.Geometry;
using CS2MapView.Drawing.StationPremises.CS1;
using CSLMapView.XML;
using SkiaSharp;
using Point = CS2MapView.CS1.Geometry.MapPoint;

internal static class StationPremisesPortChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static bool Contains(SKPath path, Point point) => path.Contains((float)point.X, (float)-point.Y);
    internal static double Area(SKPath path)
    {
        // Native contour integration also verifies that boolean output remains usable.
        using var measure = new SKPathMeasure(path, true);
        double signedArea = 0;
        do
        {
            var length = measure.Length;
            if (length <= 0 || !measure.GetPosition(0, out var first)) continue;
            var previous = first;
            int steps = Math.Max(4, (int)Math.Ceiling(length / .05));
            for (int i = 1; i <= steps; i++)
            {
                if (!measure.GetPosition(length * i / steps, out var current)) continue;
                signedArea += (double)previous.X * current.Y - (double)current.X * previous.Y;
                previous = current;
            }
            signedArea += (double)previous.X * first.Y - (double)first.X * previous.Y;
        } while (measure.NextContour());
        return Math.Abs(signedArea) / 2;
    }
    private static SKPath Rect(float x, float y, float width, float height)
    {
        var path = new SKPath(); path.AddRect(new SKRect(x,y,x+width,y+height)); return path;
    }
    private static XMLBuilding Building(int id, string name, double x, string custom = null, string parent = null, string itemClass = "Train Facility", string subService = "PublicTransportTrain") => new XMLBuilding {
        Id = id, Name = name, MyName = custom, ParentBuildingId = parent, Service = "PublicTransport", SubService = subService, ItemClass = itemClass,
        Points = new List<XMLVector3> { new XMLVector3((float)x, 0, 0), new XMLVector3((float)x + 20, 0, 0), new XMLVector3((float)x + 20, 0, 20), new XMLVector3((float)x, 0, 20) }
    };

    private static XMLSegment Segment(CSLExportXML xml, int id, string name, string itemClass, double x1, double z1, double x2, double z2, double y = 0, int? start = null, int? end = null, float width = 8)
    {
        var s = new XMLSegment { Id = id, Name = name, ItemClass = itemClass, Width = width, StartNodeId = start ?? id * 2, EndNodeId = end ?? id * 2 + 1,
            Points = new List<XMLVector3> { new XMLVector3((float)x1, (float)y, (float)z1), new XMLVector3((float)x2, (float)y, (float)z2) } };
        s.StartNode = new XMLNode { Id = s.StartNodeId, Position = s.Points[0], OnGround = y == 0, UnderGround = y < 0, SubService = "PublicTransportTrain" };
        s.EndNode = new XMLNode { Id = s.EndNodeId, Position = s.Points[1], OnGround = y == 0, UnderGround = y < 0, SubService = "PublicTransportTrain" };
        xml.SegmentList.Add(s);
        foreach (var n in new[] { s.StartNode, s.EndNode }) if (!xml.NodeList.Any(p => p.Id == n.Id)) xml.NodeList.Add(n);
        return s;
    }

    internal static void RunPolicies()
    {
        var xml = new CSLExportXML { FileVersion = "4.1" };
        xml.BuildingList.AddRange(new[] { Building(1, "Passenger station", 0), Building(2, "Freight terminal", 400, itemClass: "Train Cargo Facility"),
            Building(3, "Passenger station", 700, "WEST DEPOT"), Building(4, "Unnamed annex", 725, parent: "3"), Building(5, "Subway Yard", 800) });
        xml.BuildingList.AddRange(new[] {
            Building(6, "Airport terminal", 3000, subService: "PublicTransportPlane"), Building(7, "Airport annex", 3025, parent: "6"),
            Building(8, "Tram station", 4000, subService: "PublicTransportTram"), Building(9, "Tram annex", 4025, parent: "8"),
            Building(14, "Station roof", 20, parent: "1", subService: "PublicTransportPlane") });
        xml.SegmentTypeList.Add(new XMLSegmentType { Name = "Rail", Lanes = new List<XMLLaneType> { new XMLLaneType { Type = "Vehicle", VehicleType = "Train", Width = 3 } } });
        xml.SegmentTypeList.Add(new XMLSegmentType { Name = "Station Track", Lanes = new List<XMLLaneType> { new XMLLaneType { Type = "Vehicle", VehicleType = "Train", Width = 3 } } });
        foreach (string name in new[] { "Passage", "2372095863.Pedestrian Tunnel0", "Platform sg concrete", "Decorative Border", "Service" })
            xml.SegmentTypeList.Add(new XMLSegmentType { Name = name });
        Segment(xml, 0, "Rail", "Train Track", 2000, 0, 2200, 0);
        Segment(xml, 10, "Station Track", "Train Track", 0, 0, 200, 0);
        Segment(xml, 50, "Station Track", "Train Track", 1000, 0, 1100, 0);
        Segment(xml, 12, "Rail", "Train Track", 200, 0, 400, 0);
        var route = Segment(xml, 11, "Service", "Train Line", 0, 0, 400, 0, start: 1000, end: 1001);
        route.PathUnit = new XMLPathUnit { SegmentIdList = new List<int> { 10, 12 } };
        var empty = Segment(xml, 13, "Service", "Train Line", 0, 0, 400, 0, start: 1002);
        empty.PathUnit = new XMLPathUnit();
        xml.TransportList.Add(new XMLTransport { TransportType = "Train", Stops = new List<XMLStop> { new XMLStop { Node = 1000 }, new XMLStop { Node = 1002 } } });
        Segment(xml, 30, "Passage", "Pedestrian Tunnel", 10, 20, 10, 80, -12, end: 301, width: 4);
        Segment(xml, 31, "2372095863.Pedestrian Tunnel0", "Pedestrian Tunnel", 10, 80, 10, 250, -12, start: 301, width: 4);
        Segment(xml, 32, "Passage", "Pedestrian Tunnel", 1500, 1500, 1550, 1500, -12, width: 4);
        Segment(xml, 33, "Passage", "Pedestrian Tunnel", 10, 20, 10, 80, -100, width: 4);
        Segment(xml, 40, "Platform sg concrete", "Beautification Item", 500, 500, 600, 500);
        Segment(xml, 42, "Platform sg concrete", "Beautification Item", 550, 500, 650, 500);
        Segment(xml, 41, "Decorative Border", "Beautification Item", 700, 500, 800, 500);
        xml.SegmentTypeList.Add(new XMLSegmentType { Name = "Tram Station Track Platform", Lanes = new List<XMLLaneType> {
            new XMLLaneType { Type = "Vehicle", VehicleType = "Tram", Width = 3 } } });
        var tram = Segment(xml, 60, "Tram Station Track Platform", "Beautification Item", 4000, 0, 4200, 0);
        tram.StartNode.SubService = tram.EndNode.SubService = "PublicTransportTram";
        var tramRoute = Segment(xml, 61, "Service", "Tram Line", 4000, 0, 4200, 0, start: 6000, end: 6001);
        tramRoute.PathUnit = new XMLPathUnit { SegmentIdList = new List<int> { 60 } };
        xml.TransportList.Add(new XMLTransport { TransportType = "Tram", Stops = new List<XMLStop> { new XMLStop { Node = 6000 }, new XMLStop { Node = 6001 } } });
        Segment(xml, 62, "Passage", "Pedestrian Tunnel", 4010, 20, 4010, 80, -12, width: 4);
        Segment(xml, 63, "Passage", "Pedestrian Tunnel", 3010, 20, 3010, 80, -12, width: 4);
        var areas = CS1StationPremisesCollection.Collect(xml);
        Check(areas.BuildingIds.SetEquals(new[] { 1, 2, 14 }), "Facility filtering lost train roofs or retained airport/tram families");
        Check(!areas.StationTrackIds.Contains(60) && !Contains(areas.Geometry, new Point(4100, 0)), "Tram station-track frame leaked");
        Check(areas.ExcludedBuildingIds.SetEquals(new[] { 3, 4, 5 }), "Custom/asset name exclusion did not cover depot children");
        Check(areas.StopTrackIds.SetEquals(new[] { 10 }), "A whole service leg or an empty path became a station track");
        Check(areas.PassageIds.SetEquals(new[] { 30, 31 }), "Connected branches, lane-less passages, isolated tunnels or depth filtering failed");
        Check(areas.PlatformIds.SetEquals(new[] { 40, 42 }), "Decorative platform classification failed");
        Check(!Contains(areas.WithoutStopTracks, new Point(199, 0)), "Recognized station asset bypassed stop-track exclusion");
        Check(Contains(areas.WithoutStopTracks, new Point(10, 10)) && Contains(areas.WithoutStopTracks, new Point(10, 230))
            && Contains(areas.WithoutStopTracks, new Point(550, 500)) && Contains(areas.WithoutStopTracks, new Point(1050, 0)), "Stop-track toggle removed lots, passages, platforms or unused station tracks");
        Check(Math.Abs(Area(areas.Platforms) - 1200) < .1, "Overlapping platforms were not united before deeper tinting");
        Check(Contains(areas.Geometry, new Point(199, 0)) && !Contains(areas.Geometry, new Point(220, 0)), "Full stop track segment was clipped or extended");
        Check(Contains(areas.Geometry, new Point(550, 503.9))
            && !Contains(areas.Geometry, new Point(550, 504.2)), "Decorative platform width was not preserved");
        using var merged = NativeGeometry.Union(new[] { Rect(0, 0, 10, 10), Rect(5, 0, 10, 10) });
        Check(Math.Abs(Area(merged) - 150) < 0.1, "Overlapping frames were not geometrically united");
        Check(!areas.Geometry.IsEmpty && !areas.WithoutStopTracks.IsEmpty && !areas.Platforms.IsEmpty, "Native station boolean geometry disappeared");
        Console.WriteLine("PASS station areas: cargo/lot inclusion, depot families, exact full stop segment, empty paths, connected/lane-less passages, depth checks, decorative widths and overlap union");

        CheckUndergroundMetroStationGeometry();

        var config = new ApplicationConfig { RenderStationPremises = false, RenderTransportRoute = true, RenderRail = false,
            FrameStopTracks = false, StationPremisesColor = StationAreaColor.Blue };
        var serializer = new XmlSerializer(typeof(ApplicationConfig));
        using (var writer = new StringWriter()) {
            serializer.Serialize(writer, config);
            var restored = (ApplicationConfig)serializer.Deserialize(new StringReader(writer.ToString()));
            Check(!restored.RenderStationPremises && !restored.ShowStationPremises, "Station preference did not persist");
            Check(!restored.FrameStopTracks && restored.StationPremisesColor == StationAreaColor.Blue, "Stop-track or frame-color preference did not persist");
            restored.RenderStationPremises = true; Check(restored.ShowStationPremises, "Transit station layer unavailable");
            restored.Railway.InfrastructureOnly = true; restored.RenderTransportRoute = false;
            Check(restored.ShowStationPremises, "Station areas missing from infrastructure-only view");
            restored.RenderStationPremises = false; Check(!restored.ShowStationPremises, "Infrastructure view ignored station preference");
            restored.RenderStationPremises = true; restored.Railway.InfrastructureOnly = false;
            Check(restored.ShowStationPremises, "Station areas depended on routes or tracks being visible");
            restored.RenderRail = true; Check(restored.ShowStationPremises, "Station areas missing with ordinary tracks and no transit routes");
            restored.RenderStationPremises = false;
            Check(!restored.ShowStationPremises, "Ordinary tracks overrode the independent station-area preference");
        }
        var legacy = (ApplicationConfig)serializer.Deserialize(new StringReader("<ApplicationConfig />"));
        Check(legacy.FrameStopTracks && legacy.StationPremisesColor == StationAreaColor.Red && legacy.Railway.ColorByType, "Defaults for older configs failed");

    }

    private static void CheckUndergroundMetroStationGeometry()
    {
        var xml = new CSLExportXML { FileVersion = "4.1" };
        foreach (var name in new[] { "Metro Station Track", "Metro Tunnel" })
            xml.SegmentTypeList.Add(new XMLSegmentType { Name = name, Lanes = [new XMLLaneType { Type = "Vehicle", VehicleType = "Metro", Width = 4 }] });
        xml.SegmentTypeList.Add(new XMLSegmentType { Name = "Train Station Track", Lanes = [new XMLLaneType { Type = "Vehicle", VehicleType = "Train", Width = 4 }] });
        xml.SegmentTypeList.Add(new XMLSegmentType { Name = "Platform sg concrete" });
        xml.SegmentTypeList.Add(new XMLSegmentType { Name = "Service" });
        Segment(xml, 100, "Metro Station Track", "Metro Track", 0, 0, 100, 0, -12, width: 20);
        Segment(xml, 101, "Metro Station Track", "Metro Track", 0, 50, 100, 50, width: 20);
        Segment(xml, 102, "Metro Tunnel", "Metro Track", 0, 100, 100, 100, -12, width: 20);
        Segment(xml, 103, "Metro Station Track", "Metro Track", 0, 150, 100, 150, -12, width: 20);
        var route = Segment(xml, 104, "Service", "Metro Line", 0, 150, 100, 150, -12, start: 2000);
        route.PathUnit = new XMLPathUnit { SegmentIdList = [103] };
        xml.TransportList.Add(new XMLTransport { TransportType = "Metro", Stops = [new XMLStop { Node = 2000 }] });
        Segment(xml, 105, "Platform sg concrete", "Beautification Item", 50, 0, 150, 0, width: 20);
        Segment(xml, 106, "Train Station Track", "Train Track", 0, 200, 100, 200, -12, width: 20);
        var positions = xml.SegmentList.ToDictionary(s => s.Id, s => s.Points.ToArray());
        using var areas = CS1StationPremisesCollection.Collect(xml);
        Check(areas.UndergroundMetroStationIds.SetEquals(new[] { 100, 103 }),
            "Underground metro station tint included a surface station, ordinary tunnel, train station or logical service path");
        Check(Contains(areas.UndergroundMetroStations, new Point(50, 9.9)) && !Contains(areas.UndergroundMetroStations, new Point(50, 10.1)),
            "Underground metro station tint changed its physical network width");
        Check(Contains(areas.UndergroundMetroStations, new Point(50, 150))
            && !Contains(areas.UndergroundMetroStationsWithoutStopTracks, new Point(50, 150))
            && Contains(areas.UndergroundMetroStationsWithoutStopTracks, new Point(50, 0)),
            "Stop-track preference lost its underground metro station tint semantics");
        using var combined = NativeGeometry.Union([new SKPath(areas.Platforms), new SKPath(areas.UndergroundMetroStations)]);
        Check(Math.Abs(Area(combined) - 5000) < .1, "Metro station and decorative platform overlap was not united once");
        Check(xml.SegmentList.All(s => s.Points.SequenceEqual(positions[s.Id])), "Station tint classification changed raw network points");
        Console.WriteLine("PASS underground metro station tint classification: station/stop only, original width, surface/ordinary tunnel/train exclusions, frame-stop switch and platform overlap union");
    }
}
