using CS2MapView.CS1.Geometry;
using CS2MapView.Drawing.StationPremises.CS1;
using CSLMapView.XML;
using SkiaSharp;

internal static class BmStationPlatformChecks
{
    private sealed record Layout(string Name, float Width, (float Position, float Width)[] Paths,
        float[] Tracks, (double Min, double Max)[] Platforms);

    // Actual exported BM path metadata, checked against the four physical platform layouts.
    private static readonly Layout[] Layouts =
    [
        new("2463160214.Metro Track1", 17, [(6, 3.6f), (-6, 3.6f)], [-2, 2], [(-7.8, -4.2), (4.2, 7.8)]),
        new("2469645459.Metro Track0", 19, [(2.4f, 2.9f), (-2.4f, 2.9f)], [-6, 6], [(-3.85, 3.85)]),
        new("2621140416.Metro Track0", 25, [(10, 3.5f), (-10, 3.5f)], [-6, -2, 2, 6], [(-11.75, -8.25), (8.25, 11.75)]),
        new("2908783312.BM - 4 L Udrgrnd Station Track #01_Data", 36,
            [(14, 3.25f), (-14, 3.25f), (2.27f, 3), (-2.27f, 3)], [-10, -6, 6, 10],
            [(-15.625, -12.375), (-3.77, 3.77), (12.375, 15.625)])
    ];

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static (CSLExportXML Xml, XMLSegment Segment, XMLSegmentType Type) Fixture(Layout layout, bool curved = false)
    {
        var type = new XMLSegmentType { Name = layout.Name, Lanes = layout.Tracks.Select(p =>
            new XMLLaneType { Type = "Vehicle", VehicleType = "Metro", Position = p, Width = 4 }).ToList() };
        type.Lanes.AddRange(layout.Paths.Select(p => new XMLLaneType
            { Type = "Pedestrian", VehicleType = "None", Position = p.Position, Width = p.Width }));
        // Decoration helpers and invalid path records must not widen platform projections.
        type.Lanes.AddRange([
            new XMLLaneType { Type = "None", Position = 0, Width = 30 },
            new XMLLaneType { Type = "Pedestrian", Position = float.NaN, Width = 3 },
            new XMLLaneType { Type = "Pedestrian", Position = 0, Width = float.PositiveInfinity },
            new XMLLaneType { Type = "Pedestrian", Position = 0, Width = -3 }
        ]);
        var segment = new XMLSegment { Id = 100, Name = layout.Name, ItemClass = "Metro Track Tunnel", Width = layout.Width,
            StartNodeId = 1, EndNodeId = 2, Points = curved
                ? [new(0, -12, 0), new(40, -12, 0), new(40, -12, 0), new(80, -12, -20), new(120, -12, -60)]
                : [new(0, -12, 0), new(100, -12, 0)] };
        segment.StartNode = new XMLNode { Id = 1, Position = segment.Points[0], UnderGround = true, SubService = "PublicTransportMetro" };
        segment.EndNode = new XMLNode { Id = 2, Position = segment.Points.Last(), UnderGround = true, SubService = "PublicTransportMetro" };
        var xml = new CSLExportXML { FileVersion = "4.1", SegmentTypeList = [type], SegmentList = [segment],
            NodeList = [segment.StartNode, segment.EndNode] };
        return (xml, segment, type);
    }

    internal static void RunPolicies()
    {
        foreach (var layout in Layouts)
        {
            var (xml, segment, type) = Fixture(layout);
            using var areas = CS1StationPremisesCollection.Collect(xml);
            var platforms = areas.UndergroundMetroStations;
            Check(areas.UndergroundMetroStationIds.SetEquals([100]), "BM station identity disappeared: " + layout.Name);
            for (double offset = -layout.Width / 2d + .1; offset < layout.Width / 2d; offset += .2)
            {
                if (layout.Platforms.Any(b => Math.Abs(offset - b.Min) < .03 || Math.Abs(offset - b.Max) < .03)) continue;
                bool expected = layout.Platforms.Any(b => offset > b.Min && offset < b.Max);
                Check(platforms.Contains(50, (float)offset) == expected, "BM platform cross-section mismatch: " + layout.Name);
            }
            Check(Math.Abs(StationPremisesPortChecks.Area(platforms) - 100 * layout.Platforms.Sum(b => b.Max - b.Min)) < .1,
                "BM platform area includes tracks or a center gap: " + layout.Name);
            Check(layout.Tracks.All(p => !platforms.Contains(50, p)), "BM tracks received dark platform tint: " + layout.Name);
            Check(areas.Geometry.Contains(50, layout.Width / 2 - .1f) && !platforms.Contains(-.1f, 0) && !platforms.Contains(100.1f, 0),
                "BM station footprint or flat platform ends changed: " + layout.Name);

            // One logical service selects the physical station segment; its framing switch must remove both fills.
            xml.SegmentTypeList.Add(new XMLSegmentType { Name = "Service" });
            xml.SegmentList.Add(new XMLSegment { Id = 101, Name = "Service", ItemClass = "Metro Line", StartNodeId = 9000,
                EndNodeId = 9001, Points = segment.Points.ToList(), PathUnit = new XMLPathUnit { SegmentIdList = [100] } });
            xml.TransportList.Add(new XMLTransport { TransportType = "Metro", Stops = [new XMLStop { Node = 9000 }] });
            using var stopped = CS1StationPremisesCollection.Collect(xml);
            Check(stopped.StopTrackIds.SetEquals([100]) && !stopped.UndergroundMetroStations.IsEmpty
                && stopped.UndergroundMetroStationsWithoutStopTracks.IsEmpty && stopped.WithoutStopTracks.IsEmpty,
                "BM stop-track framing switch left platform tint behind: " + layout.Name);

            var curved = Fixture(layout, true);
            var original = curved.Segment.Points.ToArray();
            using var curveAreas = CS1StationPremisesCollection.Collect(curved.Xml);
            // The middle leg is (40,0)..(80,20) in map coordinates; use its independent unit normal.
            foreach (var band in layout.Platforms)
            {
                double offset = (band.Min + band.Max) / 2;
                Check(curveAreas.UndergroundMetroStations.Contains((float)(60 - offset / Math.Sqrt(5)),
                    (float)(10 + 2 * offset / Math.Sqrt(5))), "BM platform did not follow its curved segment: " + layout.Name);
            }
            foreach (float offset in layout.Tracks)
                Check(!curveAreas.UndergroundMetroStations.Contains((float)(60 - offset / Math.Sqrt(5)),
                    (float)(10 + 2 * offset / Math.Sqrt(5))), "Curved BM track received platform tint: " + layout.Name);
            using var escaped = curveAreas.UndergroundMetroStations.Op(curveAreas.Geometry, SKPathOp.Difference);
            Check(escaped != null && (escaped.IsEmpty || StationPremisesPortChecks.Area(escaped) < .1)
                && curved.Segment.Points.SequenceEqual(original), "BM platforms escaped the station footprint or mutated source geometry");

            // Duplicate paths share one tint footprint; older exports keep their established fallback.
            type.Lanes.Add(type.Lanes.First(l => l.Type == "Pedestrian"));
            using var duplicated = BmStationPlatformGeometry.Build(segment, type, layout.Width);
            Check(Math.Abs(StationPremisesPortChecks.Area(duplicated) - StationPremisesPortChecks.Area(platforms)) < .1,
                "Duplicate BM pedestrian paths changed the platform footprint");
            using var missing = BmStationPlatformGeometry.Build(segment, null, layout.Width);
            Check(Math.Abs(StationPremisesPortChecks.Area(missing) - 100 * layout.Width) < .1,
                "BM exports without pedestrian metadata lost their existing fallback");
        }
        Console.WriteLine("PASS BM platforms: all four layouts, continuous center islands, light tracks, exported widths, curves, clipping, stop-track switch, duplicates and source preservation");
    }

    internal static void RunSpringdale(CSLExportXML xml)
    {
        var types = xml.SegmentTypeList.ToDictionary(t => t.Name);
        int count = 0;
        foreach (var layout in Layouts)
        {
            var segments = xml.SegmentList.Where(s => s.Name == layout.Name && s.PathUnit == null).ToList();
            Check(segments.Count > 0, "Springdale is missing a supported BM platform layout: " + layout.Name);
            foreach (var segment in segments)
            {
                using var platforms = BmStationPlatformGeometry.Build(segment, types[segment.Name], segment.Width);
                using var footprint = NativeGeometry.Network(segment, segment.Width);
                Check(!platforms.IsEmpty && StationPremisesPortChecks.Area(platforms) < StationPremisesPortChecks.Area(footprint) * .6,
                    "Springdale BM platform tint still covers its whole station segment: " + segment.Id);
                var a = segment.Points[0]; var b = segment.Points[1];
                double dx = b.X - a.X, dy = a.Z - b.Z, length = Math.Sqrt(dx * dx + dy * dy);
                double x = (a.X + b.X) / 2d, y = -(a.Z + b.Z) / 2d;
                foreach (var band in layout.Platforms)
                {
                    double offset = (band.Min + band.Max) / 2;
                    Check(platforms.Contains((float)(x - dy * offset / length), (float)(y + dx * offset / length)),
                        "Springdale BM platform projection missed a platform: " + segment.Id);
                }
                Check(layout.Tracks.All(offset => !platforms.Contains((float)(x - dy * offset / length), (float)(y + dx * offset / length))),
                    "Springdale BM platform projection darkened a metro track: " + segment.Id);
                count++;
            }
        }
        Check(count == 33, "Springdale BM station segment coverage changed");
        Console.WriteLine("PASS Springdale BM platform projections: " + count + " station segments across all four layouts; platform-only darker tint");
    }
}
