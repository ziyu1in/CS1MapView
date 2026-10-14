using CS2MapView.Beta;
using CS2MapView.Config;
using CS2MapView.CS1.Config;
using CS2MapView.Drawing;
using CS2MapView.Drawing.Layer;
using CS2MapView.Drawing.Railways.CS1;
using CS2MapView.Drawing.StationPremises.CS1;
using CS2MapView.Form;
using CS2MapView.Util;
using CSLMapView.XML;
using SkiaSharp;
using Point = CS2MapView.CS1.Geometry.MapPoint;

// Preserves the established road, physical railway and station-area contracts
// while exercising the actual native Skia geometry and layer commands.
internal static class RoadRailPortChecks
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    internal static void RunPolicies()
    {
        RoadWidthChecks.Run();
        RailMetadataPortChecks.RunPolicies();
        StationPremisesPortChecks.RunPolicies();
    }
    internal static void RunSpringdale(CSLExportXML raw, string outputDirectory)
    {
        var features = BetaSettings.Clone(BetaSettings.Config);
        features.CanvasSize = 4096; features.SelectedStyle = "apple-light"; features.RenderRail = true;
        features.Railway = new RailwayConfig();
        features.RenderRailTrain = features.RenderRailMetro = features.RenderRailTram
            = features.RenderRailMonorail = features.RenderRailCableCar = true;
        using var host = new BetaOutputHost(UserSettings.Default, features);
        var session = BetaMapSession.Get(host, raw);
        var config = session.Config;
        var manager = new CS1SegmentManager(raw);
        int widths = 0;
        foreach (var segment in raw.SegmentList.Where(s => float.IsFinite(s.Width) && s.Width > 0))
        {
            Check(Math.Abs(manager.GetWidth(segment) - segment.Width) < .00001,
                "Native CS1 width lost exported precision on segment " + segment.Id); widths++;
        }
        Check(widths == 12753, "Reference exported width coverage changed");
        var rails = raw.SegmentList.Where(s => (manager.GetWayType(s) & RailTrackMetadata.RailModes) != 0
            && !manager.GetWayType(s).HasFlag(WayType.Transport)).ToList();
        var original = rails.ToDictionary(s => s.Id, s => (s.Name, First: s.Points.First(), Last: s.Points.Last()));
        var builder = new CS1RailTrackGeometry(raw, config);
        var paths = builder.BuildTrackPaths(raw); var report = builder.LastJoinReport;
        Check(rails.Count == 5790 && paths.Count == 8142, "Reference physical track coverage changed");
        config.Railway.ReconnectTracks = false;
        var unjoined = builder.BuildTrackPaths(raw);
        Check(builder.LastJoinReport.CandidateNodes == 0, "Reconnection ran with its toggle disabled");
        config.Railway.ReconnectTracks = true;
        var sameAgain = builder.BuildTrackPaths(raw);
        Check(paths.Count == sameAgain.Count && paths.Zip(sameAgain).All(p => p.First.Points.SequenceEqual(p.Second.Points)),
            "Reconnection toggle/rebuild changed the solver result");
        foreach (var network in paths.GroupBy(p => p.SegmentId))
        {
            var before = unjoined.Where(p => p.SegmentId == network.Key).SelectMany(p => p.Points).ToList();
            var after = network.SelectMany(p => p.Points).ToList();
            Check(before.Count == after.Count, "A reconnected track lost geometry");
            for (int i = 0; i < before.Count; i++)
            for (int j = i + 1; j < before.Count; j++)
                Check(Math.Abs((before[i] - before[j]).Length - (after[i] - after[j]).Length) < 1e-7,
                    "Reconnection changed a network's shape or track spacing");
        }
        var counts = paths.GroupBy(p => p.SegmentId).ToDictionary(g => g.Key, g => g.Count());
        var types = raw.SegmentTypeList.ToDictionary(t => t.Name);
        foreach (var rail in rails)
        {
            var lanes = RailTrackMetadata.Read(types.GetValueOrDefault(rail.Name));
            int expected = lanes.All(t => t.Mode is WayType.Monorail or WayType.CableCar) ? 1
                : lanes.Select(t => Math.Round(t.Position, 2)).Distinct().Count();
            Check(counts[rail.Id] == Math.Max(1, expected), "Lane-derived track count changed for " + rail.Name);
        }
        foreach (var mode in new[] { WayType.Monorail, WayType.CableCar })
        {
            var simple = paths.Where(p => p.Mode == mode).ToList();
            Check(simple.Count > 0 && simple.All(p => p.Offset == 0 && counts[p.SegmentId] == 1),
                "A simple network did not retain one centered line: " + mode);
            Check(simple.All(p => p.Points.SequenceEqual(CS1RailTrackGeometry.CleanPoints(
                rails.Single(s => s.Id == p.SegmentId).Points.Select(v => new Point(v.X, -v.Z))))),
                "A simple network left its original centerline: " + mode);
        }
        var quad = rails.First(s => s.Name == "2619651082.R2USA GrWo 4x2_Data");
        Check(counts[quad.Id] == 4, "Springdale quad asset lost its four track paths");
        Check(paths.Any(p => p.Tunnel), "Underground infrastructure disappeared");
        config.RenderRailTrain = false;
        var hiddenTrain = builder.BuildTrackPaths(raw);
        Check(hiddenTrain.All(p => p.Mode != WayType.Rail), "Train visibility was ignored");
        Check(hiddenTrain.All(p => paths.Single(q => q.SegmentId == p.SegmentId && q.Offset == p.Offset).Points.SequenceEqual(p.Points)),
            "Hiding train infrastructure moved another track");
        config.RenderRailTrain = true; config.CanvasSize = 2048;
        var resized = builder.BuildTrackPaths(raw);
        Check(resized.Count == paths.Count && resized.Zip(paths).All(pair => pair.First.Points.SequenceEqual(pair.Second.Points)),
            "Export size changed metre-based reconnection or physical spacing");
        config.CanvasSize = 4096;
        config.Railway.Appearance = RailwayAppearance.Apple;
        using var apple = (BasicLayer)new CS1RailWayLayerBuilder(host, raw).BuildAsync(null, host.Context.ViewContext).GetAwaiter().GetResult();
        config.Railway.Appearance = RailwayAppearance.Google;
        using var google = (BasicLayer)new CS1RailWayLayerBuilder(host, raw).BuildAsync(null, host.Context.ViewContext).GetAwaiter().GetResult();
        var appleCommands = apple.DrawCommands.GetAllOrderedObjects().OfType<PathDrawCommand>().ToList();
        var googleCommands = google.DrawCommands.GetAllOrderedObjects().OfType<PathDrawCommand>().ToList();
        Check(googleCommands.Count > appleCommands.Count, "Google cross-tie appearance is missing");
        foreach (var mode in paths.Select(p => p.Mode).Distinct())
        {
            var expected = CS1RailWayLayerBuilder.TrackColor(mode, false, true, true);
            Check(googleCommands.Any(c => c.StrokePaintFunc?.Invoke(1, 1)?.Color == expected), "Rendered rail palette lost " + mode);
        }
        config.Railway.ShowTunnels = false;
        Check(builder.BuildTrackPaths(raw).All(p => !p.Tunnel), "Tunnel toggle ignored");
        config.Railway.ShowTunnels = true; config.Railway.SeparateTracks = false;
        Check(builder.BuildTrackPaths(raw).Count == rails.Count, "Single-centerline infrastructure mode failed");
        Check(rails.All(s => s.Name == original[s.Id].Name && s.Points.First().Equals(original[s.Id].First)
            && s.Points.Last().Equals(original[s.Id].Last)), "Railway checks changed source assets");
        using var areas = CS1StationPremisesCollection.Collect(raw);
        BmStationPlatformChecks.RunSpringdale(raw);
        Check(areas.BuildingIds.Count == 162 && areas.StopTrackIds.Count == 92 && areas.PlatformIds.Count == 314 && areas.PassageIds.Count == 309,
            "Native station-area asset coverage changed");
        Check(raw.SegmentList.Where(s => areas.StationTrackIds.Contains(s.Id)).All(s =>
            (manager.GetWayType(s) & (WayType.Rail | WayType.Metro | WayType.Monorail | WayType.CableCar)) != 0),
            "Tram-only track received a station-area frame");
        Check(!areas.BuildingIds.Overlaps(areas.ExcludedBuildingIds), "Depot footprint received a station-area frame");
        Check(raw.BuildingList.Where(b => b.ItemClass == "Train Cargo Facility" && !CS1StationPremisesBuilder.ExcludedName(b))
            .All(b => areas.BuildingIds.Contains(b.Id)), "Cargo station lost its area");
        Check(raw.SegmentList.Where(s => s.Name == "2372095863.Pedestrian Tunnel0")
            .All(s => areas.PassageIds.Contains(s.Id)), "Lane-less passage disappeared");
        var bmUndergroundMetro = raw.SegmentList.Where(s => s.Name == "2908783312.BM - 4 L Udrgrnd Station Track #01_Data").ToList();
        Check(bmUndergroundMetro.Count == 2 && bmUndergroundMetro.All(s => areas.UndergroundMetroStationIds.Contains(s.Id)),
            "Springdale's BM underground metro station assets did not receive platform tint");
        Check(areas.UndergroundMetroStationIds.All(id => areas.StationTrackIds.Contains(id)),
            "Deeper metro tint expanded station-area asset membership");
        config.RenderTransportRoute = config.RenderStationPremises = true;
        foreach (var tint in new[] { StationAreaColor.Red, StationAreaColor.Blue })
        foreach (bool fullTracks in new[] { false, true })
        {
            config.StationPremisesColor = tint; config.FrameStopTracks = fullTracks;
            using var layer = (BasicLayer)new CS1StationPremisesLayerBuilder(host, raw).BuildAsync(null).GetAwaiter().GetResult();
            var commands = layer.DrawCommands.GetAllOrderedObjects().OfType<PathDrawCommand>().ToList();
            Check(commands.Count == 3, "Station-area native drawing pass count changed");
            var expected = tint == StationAreaColor.Red ? new SKColor(218, 66, 74, 85) : new SKColor(79, 125, 235, 85);
            Check(commands[0].FillColor?.Alpha == 32 && commands[1].FillColor == expected, "Station tint or opacity changed");
            var metroTint = fullTracks ? areas.UndergroundMetroStations : areas.UndergroundMetroStationsWithoutStopTracks;
            using var missingMetro = metroTint.Op(commands[1].Path!, SKPathOp.Difference);
            Check(missingMetro != null && (missingMetro.IsEmpty || StationPremisesPortChecks.Area(missingMetro) < .1),
                "Visible underground metro station area lost its deeper platform tint");
            foreach (var segment in bmUndergroundMetro)
            {
                var x = (segment.Points[0].X + segment.Points[1].X) / 2;
                var y = -(segment.Points[0].Z + segment.Points[1].Z) / 2;
                using var pixel = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(pixel);
                canvas.Clear(SKColors.Transparent); canvas.Translate(.5f - x, .5f - y);
                commands[0].Draw(canvas, 1, 1); commands[1].Draw(canvas, 1, 1);
                var actual = pixel.GetPixel(0, 0);
                bool marked = fullTracks || !areas.StopTrackIds.Contains(segment.Id) || areas.Platforms.Contains(x, y);
                Check(marked ? actual.Alpha == 85 : actual.Alpha <= 32,
                    "Metro station platform fill opacity or frame-stop toggle changed in actual native drawing");
            }
            using var intersection = commands[0].Path!.Op(commands[1].Path!, SKPathOp.Intersect);
            Check(intersection != null && (intersection.IsEmpty || StationPremisesPortChecks.Area(intersection) < .1),
                "Station fills accumulate opacity at platforms");
            Check(commands.Count(c => c.StrokePaintFunc != null) == 1, "Station parts acquired interior outlines");
            var source = fullTracks ? session.StationPremises.Geometry : session.StationPremises.WithoutStopTracks;
            Check(commands[2].Path!.PointCount == source.PointCount && commands[2].Path!.Bounds == source.Bounds,
                "Station stop-track toggle used stale outline");
        }
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllLines(Path.Combine(outputDirectory, "beta-road-rail-station-acceptance.txt"), new[]
        {
            "Exported fractional widths: " + widths,
            "Infrastructure segments: " + rails.Count + "; physical track paths: " + paths.Count,
            report.ToString(),
            "Station buildings: " + areas.BuildingIds.Count + "; stop tracks: " + areas.StopTrackIds.Count,
            "Passages: " + areas.PassageIds.Count + "; decorative platforms: " + areas.PlatformIds.Count,
            "Underground metro station networks with platform tint: " + areas.UndergroundMetroStationIds.Count
        }.Concat(report.Details));
        Console.WriteLine("PASS native Springdale roads/rail/premises: " + widths + " widths; " + rails.Count
            + " rail assets / " + paths.Count + " physical paths; rigid spacing, filtering, both appearances and native disjoint tints");
    }
}
