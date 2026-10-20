using CS2MapView.Beta;
using CS2MapView.Config;
using CS2MapView.CS1.Config;
using CS2MapView.Drawing;
using CS2MapView.Drawing.Layer;
using CS2MapView.Drawing.Roads.CS1;
using CS2MapView.Form;
using CS2MapView.Import.CS1;
using CS2MapView.Util;
using CS2MapView.Util.CS1;
using CSLMapView.XML;
using SkiaSharp;

internal static class RoadHierarchyChecks
{
    private const int RoadsPerMode = 6;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static XMLLaneType Lane(string direction, float position, string vehicles = "Car", string type = "Vehicle")
        => new() { Direction = direction, Position = position, Width = 3, Type = type, VehicleType = vehicles };

    private static XMLSegmentType Asset(string name, int forward, int backward, bool highway = false)
    {
        var type = new XMLSegmentType { Name = name, Highway = highway };
        for (int i = 0; i < forward; i++) type.Lanes.Add(Lane("Forward", i * 3 + 1.5f));
        for (int i = 0; i < backward; i++) type.Lanes.Add(Lane("Backward", -i * 3 - 1.5f));
        return type;
    }

    internal static void RunPolicies()
    {
        foreach (var (forward, backward, expected) in new[] {
            (1, 0, RoadImportance.Minor), (1, 1, RoadImportance.Minor), (2, 0, RoadImportance.Minor),
            (2, 1, RoadImportance.Minor), (3, 0, RoadImportance.Minor),
            (2, 2, RoadImportance.Intermediate), (3, 1, RoadImportance.Intermediate), (4, 0, RoadImportance.Intermediate),
            (3, 2, RoadImportance.Intermediate), (5, 0, RoadImportance.Intermediate),
            (3, 3, RoadImportance.Major), (4, 2, RoadImportance.Major), (6, 0, RoadImportance.Major),
            (4, 4, RoadImportance.Major) })
        {
            var lanes = RoadLaneMetadata.Read(Asset("fixture", forward, backward));
            Check(lanes.Forward == forward && lanes.Backward == backward && lanes.Total == forward + backward
                && lanes.Importance == expected, "Total motor-traffic lane classification failed");
        }
        var mixed = Asset("mixed", 1, 1);
        mixed.Lanes[0].VehicleType = "Car, Tram";
        mixed.Lanes.Add(Lane("Forward", 1.5f, "Bus", "TransportVehicle")); // Shared physical position.
        mixed.Lanes.Add(Lane("Forward", 6, "Car", "Parking"));
        mixed.Lanes.Add(Lane("Forward", 7, "Car", "Vehicle, Parking"));
        mixed.Lanes.Add(Lane("Both", 9, "None", "Pedestrian"));
        foreach (var vehicle in new[] { "None", "Bicycle", "Tram", "Train", "Metro", "Monorail", "Plane" })
            mixed.Lanes.Add(Lane("Forward", 12 + mixed.Lanes.Count, vehicle));
        mixed.Lanes.Add(Lane("None", 30));
        mixed.Lanes.Add(Lane("Forward", float.NaN));
        var zeroWidth = Lane("Forward", 33); zeroWidth.Width = 0; mixed.Lanes.Add(zeroWidth);
        Check(RoadLaneMetadata.Read(mixed) == new RoadLaneCounts(1, 1), "Parking, helpers, other modes or duplicate lane entries increased importance");
        mixed.Lanes.Add(Lane("AvoidForward", 4.5f, "Bus", "Vehicle, TransportVehicle"));
        Check(RoadLaneMetadata.Read(mixed) == new RoadLaneCounts(2, 1), "Dedicated bus lane or combined flags were lost");
        mixed.Lanes.Add(Lane("Both", 36));
        Check(RoadLaneMetadata.Read(mixed) == new RoadLaneCounts(3, 2, 1)
            && RoadLaneMetadata.Read(mixed).Total == 4, "Bidirectional lane increased the total twice");
        var shared = new XMLSegmentType { Name = "bidirectional" };
        shared.Lanes.Add(Lane("Both", -1.5f)); shared.Lanes.Add(Lane("Both", 1.5f));
        Check(RoadLaneMetadata.Read(shared).Total == 2 && RoadLaneMetadata.Read(shared).Importance == RoadImportance.Minor,
            "Two bidirectional lanes must not reach the four-lane emphasis threshold");
        var unknown = new XMLSegmentType { Name = "unknown" };
        Check(RoadLaneMetadata.Read(unknown).Importance == RoadImportance.Minor, "Missing metadata must use the conservative minor class");

        foreach (var name in new[] { "carto-voyager", "carto-positron", "apple-light" }) CheckNativeStyles(name);
        CheckNarrowWidths();
        CheckParkingNetworks();
        CheckJoining();
        Console.WriteLine("PASS road hierarchy: four-total-lane minimum, one-way streets, bidirectional/shared/bus lanes, filtering, legacy styles, native ground/tunnel/elevated colors, highway precedence, width independence and joins");
    }

    private static CSLExportXML Network()
    {
        var xml = new CSLExportXML { FileVersion = "4.1", City = "Road hierarchy checks" };
        var assets = new[] { Asset("minor", 1, 1), Asset("intermediate", 2, 2), Asset("major", 3, 3),
            Asset("highway", 1, 0, true), Asset("minor-one-way-two", 2, 0), Asset("minor-one-way-three", 3, 0) };
        xml.SegmentTypeList.AddRange(assets);
        for (int mode = 0; mode < 3; mode++)
        for (int importance = 0; importance < assets.Length; importance++)
        {
            int index = mode * assets.Length + importance;
            float z = -index * 200;
            var start = new XMLNode { Id = index * 2 + 1, Position = new(0, mode == 1 ? -10 : mode == 2 ? 10 : 0, z), Service = "Road", OnGround = mode == 0, UnderGround = mode == 1 };
            var end = new XMLNode { Id = index * 2 + 2, Position = new(1000, start.Position.Y, z), Service = "Road", OnGround = mode == 0, UnderGround = mode == 1 };
            xml.NodeList.AddRange([start, end]);
            xml.SegmentList.Add(new XMLSegment { Id = index + 1, Name = assets[importance].Name, ItemClass = "Road", Width = 24,
                StartNodeId = start.Id, EndNodeId = end.Id, StartNode = start, EndNode = end,
                Points = [start.Position, end.Position] });
        }
        return xml;
    }

    private static void CheckNativeStyles(string styleName)
    {
        var xml = Network();
        var config = new ApplicationConfig { SelectedStyle = styleName, CanvasSize = 4096 };
        using var host = new BetaOutputHost(UserSettings.Default, config);
        var session = BetaMapSession.Get(host, xml);
        var style = session.CurrentMapStyle;
        Check(style.FileName == styleName && style.RoadHierarchy.Enabled == styleName.StartsWith("carto-"), "Style was not discovered or hierarchy opt-in was lost");
        var builder = new CS1RoadLayerBuilder(host, xml);
        using var layer = (BasicLayer)builder.BuildAsync(null).GetAwaiter().GetResult();
        var commands = layer.DrawCommands.GetAllOrderedObjects().OfType<PathDrawCommand>().ToArray();
        var widths = xml.SegmentList.Select(builder.GetDrawingWidth).Distinct().ToArray();
        Check(widths.Length == 1, "Lane count or highway class changed an equal exported width");
        foreach (var segment in xml.SegmentList)
        {
            int index = segment.Id - 1;
            int importance = index % RoadsPerMode;
            if (importance >= 4) importance = 0; // Two- and three-lane one-way streets must use minor colors.
            int mode = index / RoadsPerMode;
            var c = style.Colors;
            var palette = importance == 2 ? style.RoadHierarchy.Major : style.RoadHierarchy.Intermediate;
            SKColor expected = importance == 3 ? mode == 1 ? c.HighwayTunnel : mode == 2 ? c.HighwayEleavted : c.Highway
                : style.RoadHierarchy.Enabled && importance > 0 ? mode == 1 ? palette.TunnelFill : mode == 2 ? palette.ElevatedFill : palette.Fill
                : mode == 1 ? c.RoadTunnel : mode == 2 ? c.RoadEleavted : c.Road;
            SKColor border = importance == 3 ? mode == 1 ? c.HighwayTunnelBorder : mode == 2 ? c.HighwayElevatedBorder : c.HighwayBorder
                : style.RoadHierarchy.Enabled && importance > 0 ? mode == 1 ? palette.TunnelBorder : mode == 2 ? palette.ElevatedBorder : palette.Border
                : mode == 1 ? c.RoadTunnelBorder : mode == 2 ? c.RoadElevatedBorder : c.RoadBorder;
            var segmentCommands = commands.Where(command => Math.Abs(command.Path!.Bounds.MidY - index * 200) < .01f).ToArray();
            Check(segmentCommands.Any(command => command.StrokePaintFunc?.Invoke(1, 1)?.Color == expected), "Native fill lost road class on " + styleName);
            Check(segmentCommands.Any(command => command.StrokePaintFunc?.Invoke(1, 1)?.Color == border), "Native border lost road class on " + styleName);
            using var pixel = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(pixel);
            canvas.Clear(SKColors.Transparent); canvas.Translate(-499.5f, .5f - index * 200);
            foreach (var command in commands) command.Draw(canvas, 1, 1);
            Check(pixel.GetPixel(0, 0) == expected, "Actual rendered road center has the wrong color on " + styleName);
        }
        var manager = new CS1SegmentManager(xml);
        var local = xml.SegmentList[0];
        local.Width = 48;
        Check(manager.GetRoadImportance(local) == RoadImportance.Minor && builder.GetDrawingWidth(local) > widths[0], "Wider sidewalks incorrectly promoted a road");
        var ramp = xml.SegmentList[3]; ramp.Width = 9.66f;
        Check(manager.GetRoadImportance(ramp) == RoadImportance.Highway && builder.GetDrawingWidth(ramp) < widths[0], "Narrow ramp lost highway styling or its own width");
        Check(xml.SegmentList.Count == 3 * RoadsPerMode && xml.SegmentList[0].Points[0].X == 0, "Road rendering mutated the raw source network");
        if (styleName == "carto-voyager")
        {
            var intermediate = style.RoadHierarchy.Intermediate;
            var major = style.RoadHierarchy.Major;
            Check(YellowContrast(intermediate.Fill) > YellowContrast(style.Colors.HighwayTunnel)
                && YellowContrast(major.Fill) > YellowContrast(intermediate.Fill)
                && YellowContrast(style.Colors.Highway) > YellowContrast(major.Fill),
                "Voyager yellow must increase from highway tunnels to avenues, boulevards and surface highways");
            Check(intermediate.ElevatedFill == intermediate.Fill && major.ElevatedFill == major.Fill
                && YellowContrast(intermediate.TunnelFill) < YellowContrast(intermediate.Fill)
                && YellowContrast(major.TunnelFill) < YellowContrast(major.Fill),
                "Voyager elevated streets lost their yellow or street tunnels lost their muted fills");
        }
        if (styleName == "carto-positron")
        {
            var narrow = style.RoadHierarchy.Narrow;
            Check(style.RoadHierarchy.NarrowRoadMaxWidth == 12 && narrow.Fill.Red < 235 && narrow.Fill.Green < 235 && narrow.Fill.Blue < 235
                && narrow.Border == narrow.Fill && narrow.ElevatedFill == narrow.Fill,
                "Positron physically narrow roads must use the gray palette");
            Check(style.Colors.Road == SKColors.White && style.Colors.RoadEleavted == SKColors.White
                && style.RoadHierarchy.Intermediate.Fill == SKColors.White
                && style.RoadHierarchy.Major.Fill == SKColors.White && style.Colors.Highway == SKColors.White,
                "Positron normal-width roads must stay white independently of lane count");
        }
        static int YellowContrast(SKColor color) => color.Red - color.Blue;
    }

    private static void CheckNarrowWidths()
    {
        var xml = Network();
        foreach (var type in xml.SegmentTypeList.Where(type => !type.Highway)) type.Lanes = Asset(type.Name, 2, 0).Lanes;
        float[] widths = [8, 12, 12.01f, 9.66f, 16, 32];
        foreach (var segment in xml.SegmentList) segment.Width = widths[(segment.Id - 1) % RoadsPerMode];
        using var host = new BetaOutputHost(UserSettings.Default, new ApplicationConfig { SelectedStyle = "carto-positron", CanvasSize = 4096 });
        var style = BetaMapSession.Get(host, xml).CurrentMapStyle;
        var builder = new CS1RoadLayerBuilder(host, xml);
        using var layer = (BasicLayer)builder.BuildAsync(null).GetAwaiter().GetResult();
        var commands = layer.DrawCommands.GetAllOrderedObjects().OfType<PathDrawCommand>().ToArray();
        foreach (var segment in xml.SegmentList)
        {
            int index = segment.Id - 1, role = index % RoadsPerMode, mode = index / RoadsPerMode;
            var c = style.Colors;
            var palette = style.RoadHierarchy.Narrow;
            var expected = role < 2 ? mode == 1 ? palette.TunnelFill : mode == 2 ? palette.ElevatedFill : palette.Fill
                : role == 3 ? mode == 1 ? c.HighwayTunnel : mode == 2 ? c.HighwayEleavted : c.Highway
                : mode == 1 ? c.RoadTunnel : mode == 2 ? c.RoadEleavted : c.Road;
            using var pixel = new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(pixel);
            canvas.Clear(SKColors.Transparent); canvas.Translate(-499.5f, .5f - index * 200);
            foreach (var command in commands) command.Draw(canvas, 1, 1);
            Check(pixel.GetPixel(0, 0) == expected, "Physical width did not select the correct Positron palette at " + segment.Width + " m");
            Check(commands.Where(command => Math.Abs(command.Path!.Bounds.MidY - index * 200) < .01f)
                .Any(command => command.StrokePaintFunc?.Invoke(1, 1) is { } key && key.Color == expected
                    && Math.Abs(key.Width - builder.GetDrawingWidth(segment)) < .0001f),
                "The gray/white distinction changed the exported drawing width");
        }
        Console.WriteLine("PASS Positron width-based gray: 8/12 m gray, 12.01/16/32 m white, highway exception and independent lane count in all three elevation modes");
    }

    private static void CheckJoining()
    {
        var xml = Network();
        var first = xml.SegmentList[0]; var second = xml.SegmentList[1];
        var finish = xml.NodeList[3]; finish.Position = new(2000, 0, 0);
        second.StartNodeId = first.EndNodeId; second.StartNode = first.EndNode;
        second.Points = [first.EndNode.Position, finish.Position]; second.EndNode = finish;
        xml.NodeList = [xml.NodeList[0], xml.NodeList[1], finish];
        xml.SegmentList = [first, second];
        var oldView = CS1XMLImporter.CreateLabelView(xml);
        var hierarchyView = CS1XMLImporter.CreateLabelView(xml, preserveRoadHierarchy: true);
        Check(oldView.SegmentList.Count == 1, "Legacy equal-width joining changed");
        Check(hierarchyView.SegmentList.Count == 2, "Joining erased the lane-class boundary between equal-width roads");
        Check(xml.SegmentList.Count == 2 && first.Points.Count == 2, "Display joining mutated source geometry");
    }

    private static void CheckParkingNetworks()
    {
        foreach (var name in new[] { "Parking Lot 01", "1285201733.58m Parking Lot_Data", "ParkingLotRoad", "asset.parking-lot-road_Data",
            "asset.PARKING_LOT_Service_Data", "Parking Lots", "asset.Car Park_Data", "CarPark Service Road" })
            Check(RoadLaneMetadata.IsParkingLotNetwork(name), "Parking-lot network identity was missed: " + name);
        foreach (var name in new[] { "Four Lane Road with Parking", "Parking Lottery Avenue", "Park Avenue", "Car Parkway", "Road without parking" })
            Check(!RoadLaneMetadata.IsParkingLotNetwork(name), "Ordinary road was treated as a parking-lot network: " + name);
        Check(!RoadLaneMetadata.IsParkingLotNetwork(null), "Missing network identity was treated as a parking lot");

        var xml = Network();
        string[] names = ["1285201733.58m Parking Lot_Data", "1285201733.40m Parking Lot_Data", "Parking Lot 01",
            "highway", "Four Lane Road with Parking", "Parking Lottery Avenue"];
        int[] lanes = [6,4,6,1,4,6];
        float[] widths = [58,40,24,9.66f,24,24];
        RoadImportance[] classes = [RoadImportance.Minor,RoadImportance.Minor,RoadImportance.Minor,
            RoadImportance.Highway,RoadImportance.Intermediate,RoadImportance.Major];
        for (int i = 0; i < names.Length; i++)
        {
            var type = xml.SegmentTypeList[i];
            type.Name = names[i]; type.Lanes = Asset(names[i],lanes[i],0).Lanes;
        }
        xml.SegmentTypeList[4].Lanes.Add(Lane("Forward",-6,"Car","Parking"));
        xml.SegmentTypeList[4].Lanes.Add(Lane("Backward",-9,"Car","Parking"));
        foreach (var segment in xml.SegmentList)
        {
            int role = (segment.Id - 1) % RoadsPerMode;
            segment.Name = names[role]; segment.Width = widths[role];
            if (role == 4) segment.CustomName = "Parking Lot Avenue";
        }
        var manager = new CS1SegmentManager(xml);
        foreach (var segment in xml.SegmentList)
        {
            int role = (segment.Id - 1) % RoadsPerMode;
            Check(manager.GetRoadLanes(segment).Total == lanes[role], "Parking exclusion changed physical traffic-lane metadata");
            Check(manager.GetRoadImportance(segment) == classes[role] && manager.GetWidth(segment) == widths[role],
                "Parking exclusion changed widths or ordinary-road/highway classification");
        }
        foreach (var styleName in new[] { "carto-voyager", "carto-positron", "apple-light" })
        {
            using var host = new BetaOutputHost(UserSettings.Default,new ApplicationConfig { SelectedStyle = styleName, CanvasSize = 4096 });
            var style = BetaMapSession.Get(host,xml).CurrentMapStyle;
            var builder = new CS1RoadLayerBuilder(host,xml);
            using var layer = (BasicLayer)builder.BuildAsync(null).GetAwaiter().GetResult();
            var commands = layer.DrawCommands.GetAllOrderedObjects().OfType<PathDrawCommand>().ToArray();
            foreach (var segment in xml.SegmentList)
            {
                int index = segment.Id - 1, role = index % RoadsPerMode, mode = index / RoadsPerMode;
                var colors = style.Colors;
                var palette = role == 4 ? style.RoadHierarchy.Intermediate : style.RoadHierarchy.Major;
                SKColor expected = role == 3 ? mode == 1 ? colors.HighwayTunnel : mode == 2 ? colors.HighwayEleavted : colors.Highway
                    : style.RoadHierarchy.Enabled && role >= 4 ? mode == 1 ? palette.TunnelFill : mode == 2 ? palette.ElevatedFill : palette.Fill
                    : mode == 1 ? colors.RoadTunnel : mode == 2 ? colors.RoadEleavted : colors.Road;
                SKColor border = role == 3 ? mode == 1 ? colors.HighwayTunnelBorder : mode == 2 ? colors.HighwayElevatedBorder : colors.HighwayBorder
                    : style.RoadHierarchy.Enabled && role >= 4 ? mode == 1 ? palette.TunnelBorder : mode == 2 ? palette.ElevatedBorder : palette.Border
                    : mode == 1 ? colors.RoadTunnelBorder : mode == 2 ? colors.RoadElevatedBorder : colors.RoadBorder;
                var selected = commands.Where(command => Math.Abs(command.Path!.Bounds.MidY - index * 200) < .01f).ToArray();
                Check(selected.Any(command => command.StrokePaintFunc?.Invoke(1,1)?.Color == border), "Parking-lot outline retained importance emphasis");
                Check(selected.Any(command => command.StrokePaintFunc?.Invoke(1,1) is { } key && key.Color == expected
                    && Math.Abs(key.Width-builder.GetDrawingWidth(segment)) < .0001f), "Parking-lot exclusion changed native fill or drawing width");
                using var pixel = new SKBitmap(1,1,SKColorType.Bgra8888,SKAlphaType.Premul);
                using var canvas = new SKCanvas(pixel);
                canvas.Clear(SKColors.Transparent); canvas.Translate(-499.5f,.5f-index*200);
                foreach (var command in commands) command.Draw(canvas,1,1);
                Check(pixel.GetPixel(0,0) == expected, "Native pixels retained parking-lot emphasis or removed ordinary-road emphasis");
            }
        }
        var first = xml.SegmentList[0]; var second = xml.SegmentList[4];
        var finish = second.EndNode; finish.Position = new(2000,0,0);
        second.StartNodeId = first.EndNodeId; second.StartNode = first.EndNode;
        second.Points = [first.EndNode.Position,finish.Position]; second.Width = first.Width;
        second.CustomName = first.CustomName;
        xml.NodeList = [first.StartNode,first.EndNode,finish]; xml.SegmentList = [first,second];
        Check(CS1XMLImporter.CreateLabelView(xml).SegmentList.Count == 1, "Parking fixture is not an otherwise joinable road pair");
        Check(CS1XMLImporter.CreateLabelView(xml,preserveRoadHierarchy:true).SegmentList.Count == 2,
            "Display joining erased the parking-lot/ordinary-road emphasis boundary");
        Console.WriteLine("PASS parking-lot networks: excluded from lane-based emphasis in all elevation modes; physical widths/lanes, curbside-parking streets, custom names, highways and joining preserved");
    }

    internal static void RunSpringdale(CSLExportXML raw, string outputDirectory)
    {
        var manager = new CS1SegmentManager(raw);
        var roads = raw.SegmentList.Where(segment => manager.GetWayType(segment).HasFlag(WayType.Road)
            && !manager.GetWayType(segment).HasFlag(WayType.Transport)
            && manager.GetRoadLanes(segment).Total > 0).ToArray();
        Check(roads.Length > 0, "No physical motor-traffic roads were classified in Springdale");
        var groups = roads.GroupBy(manager.GetRoadImportance).OrderBy(group => group.Key).ToArray();
        Check(groups.Length == 4, "Springdale road hierarchy is missing a class");
        var parking = roads.Where(segment => RoadLaneMetadata.IsParkingLotNetwork(segment.Name)).ToArray();
        Check(parking.Length == 37 && parking.Select(segment => segment.Name).Distinct().Count() == 5
            && parking.All(segment => manager.GetRoadImportance(segment) == RoadImportance.Minor),
            "Springdale parking-lot network assets retained lane-based emphasis");
        Check(parking.Count(segment => manager.GetRoadLanes(segment).Total >= 4) == 7,
            "Springdale multi-aisle parking-lot coverage changed");
        Console.WriteLine("PASS Springdale parking lots: all 37 segments across five assets stay minor; seven four/six-lane lot segments lose emphasis");
        foreach (var road in roads.Where(segment => !manager.GetWayType(segment).HasFlag(WayType.Highway)
            && manager.GetRoadLanes(segment).Total < 4))
            Check(manager.GetRoadImportance(road) == RoadImportance.Minor, "Springdale road with fewer than four total lanes received emphasis");
        foreach (var road in roads.Where(segment => segment.Name.StartsWith("1889646434.")))
            Check(manager.GetRoadLanes(road).BusiestDirection == 1 && manager.GetRoadImportance(road) == RoadImportance.Highway,
                "Springdale 1L highway helper lane increased traffic lane count");
        var positron = BetaSettings.LoadStyle("carto-positron");
        var cityOneWays = roads.Where(segment => segment.Name.StartsWith("2253466511.BIG Urban Roads 2 Lane 1 Wa")).ToArray();
        Check(cityOneWays.Length > 0 && cityOneWays.All(segment => manager.GetRoadLanes(segment).Total == 2
            && manager.GetWidth(segment) == 16 && manager.GetWidth(segment) > positron.RoadHierarchy.NarrowRoadMaxWidth),
            "Springdale's normal-width two-lane one-way streets were treated as physically narrow");
        Console.WriteLine("PASS Springdale two-lane one-way streets: " + cityOneWays.Length + " segments at 16 m, above the gray-road width threshold");
        File.WriteAllLines(Path.Combine(outputDirectory, "road-hierarchy.txt"), groups.Select(group => group.Key + ": " + group.Count() + " segments"));
        Console.WriteLine("PASS Springdale road hierarchy: " + string.Join(", ", groups.Select(group => group.Key + "=" + group.Count())));
    }
}
