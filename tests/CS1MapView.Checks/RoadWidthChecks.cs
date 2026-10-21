using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using CS2MapView.Beta;
using CS2MapView.Config;
using CS2MapView.Form;
using CS2MapView.Util;
using CS2MapView.Drawing.Roads.CS1;
using CSLMapView.XML;

internal static class RoadWidthChecks
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type ManagerType = typeof(CS1RoadLayerBuilder).Assembly.GetType("CS2MapView.Util.CS1SegmentManager", true);
    private static readonly MethodInfo GetWidth = ManagerType.GetMethod("GetWidth", Internal);

    private static object Manager(CSLExportXML xml) => Activator.CreateInstance(ManagerType, new object[] { xml });
    private static double Width(object manager, XMLSegment seg) => Convert.ToDouble(GetWidth.Invoke(manager, new object[] { seg }));
    private static void Equal(double expected, double actual, string label)
    {
        if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(expected - actual) > 0.00001)
            throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
    }

    private static XMLLaneType Lane(string type, string vehicles, float position, float width)
        => new XMLLaneType { Type = type, VehicleType = vehicles, Position = position, Width = width };

    internal static void Run(string? mapFile = null)
    {
        var type = new XMLSegmentType { Name = "Custom Mod Highway", Highway = true };
        type.Lanes.Add(Lane("Vehicle", "Car", 0, 4.25f));
        type.Lanes.Add(Lane("Vehicle", "None", 0, 22)); // Actual Reference map trigger.
        type.Lanes.Add(Lane("None", "None", 0, 32)); // Tunnel/helper metadata.
        type.Lanes.Add(Lane("Vehicle", null, 0, 100));
        var xml = new CSLExportXML { FileVersion = "4.1" };
        xml.SegmentTypeList.Add(type);
        object manager = Manager(xml);
        var segment = new XMLSegment { Name = type.Name, Width = 9.66f };
        Equal(9.66, Width(manager, segment), "Exported width overrides non-traffic lane");
        segment.Width = 17.8f;
        Equal(17.8, Width(manager, segment), "Segments of the same type retain their own widths");
        segment.Width = 1.5f;
        Equal(1.5, Width(manager, segment), "Valid narrow width is preserved");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            segment.Width = invalid;
            Equal(4.25, Width(manager, segment), "Invalid exported width uses fractional traffic span");
        }

        type.Lanes.Add(Lane("Vehicle", "Car", float.NaN, 200));
        type.Lanes.Add(Lane("Vehicle", "Car", float.PositiveInfinity, 200));
        type.Lanes.Add(Lane("Vehicle", "Car", 500, -1));
        type.Lanes.Add(Lane("Vehicle", "Car", 500, 0));
        type.Lanes.Add(Lane("Vehicle", "Car", 500, float.PositiveInfinity));
        type.Lanes.Add(Lane("Vehicle", "Car", 500, float.NaN));
        Equal(4.25, Width(Manager(xml), segment), "Invalid lane dimensions are ignored");
        type.Lanes.Clear();
        Equal(3, Width(Manager(xml), segment), "Empty lane fallback");
        type.Lanes.Add(Lane("Vehicle", "None", 0, 22));
        Equal(3, Width(Manager(xml), segment), "Only helper lanes fallback");
        type.Lanes.Add(Lane("Pedestrian", "None", 0, 3.5f));
        Equal(3.5, Width(Manager(xml), segment), "Pedestrian fallback");
        type.Lanes.Clear();
        type.Lanes.Add(Lane("Vehicle, TransportVehicle", "Car", -2, 3));
        type.Lanes.Add(Lane("TransportVehicle", "Car", 2, 3));
        Equal(7, Width(Manager(xml), segment), "Combined lane flags and transport lanes fallback");

        xml.FileVersion = "3.60";
        manager = Manager(xml);
        segment.Width = 9.66f;
        Equal(9.66, Width(manager, segment), "Legacy export with usable width");
        segment.Width = 0;
        segment.ItemClass = "Road";
        segment.StartNode = new XMLNode { OnGround = true, Service = "Road" };
        segment.EndNode = new XMLNode { OnGround = true, Service = "Road" };
        Equal(32, Width(manager, segment), "Legacy name fallback when width is absent");

        xml.FileVersion = "4.1";
        var config = BetaSettings.Clone(BetaSettings.Config);
        config.CanvasSize = 4096; config.SelectedStyle = "apple-light";
        using var host = new BetaOutputHost(UserSettings.Default, config);
        var builder = new CS1RoadLayerBuilder(host, xml);
        segment.Width = 9.66f;
        double rampStroke = builder.GetDrawingWidth(segment) * config.CanvasSize / 17280d;
        segment.Width = 17.8f;
        double mainStroke = builder.GetDrawingWidth(segment) * config.CanvasSize / 17280d;
        Equal(1.949, rampStroke, "1L connector stroke");
        Equal(3.17, mainStroke, "3L main carriageway stroke");
        if (rampStroke >= mainStroke) throw new InvalidOperationException("Narrow connector must remain narrower than the main carriageway.");
        Console.WriteLine("PASS road widths: exported metadata, per-segment precision, invalid metadata fallbacks, lane filtering, legacy files and stroke ordering");

        if (!string.IsNullOrEmpty(mapFile)) CheckMap(mapFile);
    }

    private static void CheckMap(string mapFile)
    {
        CSLExportXML xml;
        using (var file = File.OpenRead(mapFile))
        using (Stream stream = mapFile.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? (Stream)new GZipStream(file, CompressionMode.Decompress) : file)
            xml = (CSLExportXML)new XmlSerializer(typeof(CSLExportXML)).Deserialize(stream);
        object manager = Manager(xml);
        int checkedSegments = 0;
        foreach (var segment in xml.SegmentList)
        {
            if (segment.Width > 0 && !float.IsNaN(segment.Width) && !float.IsInfinity(segment.Width))
            {
                Equal(segment.Width, Width(manager, segment), "Exported map segment " + segment.Id);
                checkedSegments++;
            }
        }
        if (checkedSegments == 0) throw new InvalidOperationException("Map has no valid exported widths to check.");
        foreach (var group in xml.SegmentList.Where(s => s.Name.StartsWith("1889646434.") || s.Name.StartsWith("1889649058.")).GroupBy(s => s.Name).OrderBy(g => g.Key))
            Console.WriteLine("PASS asset " + group.Key + ": " + group.Count() + " segments, width=" + Width(manager, group.First()).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        Console.WriteLine("PASS real map widths: " + checkedSegments + " segments in " + xml.City);
    }
}
