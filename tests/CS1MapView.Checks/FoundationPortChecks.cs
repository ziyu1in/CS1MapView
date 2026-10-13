using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Serialization;
using CS2MapView;
using CS2MapView.Config;
using CS2MapView.Data;
using CS2MapView.Drawing;
using CS2MapView.Drawing.Layer;
using CS2MapView.Import.CS1;
using CSLMapView.XML;
using SkiaSharp;

internal static class FoundationPortChecks
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Core = typeof(CS1MapType).Assembly;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static byte[] SourceHash(CSLExportXML xml)
    {
        using var stream = new MemoryStream();
        new XmlSerializer(typeof(CSLExportXML)).Serialize(stream, xml);
        return SHA256.HashData(stream.ToArray());
    }
    internal static void Run(ICS2MapViewRoot host, CSLExportXML raw)
    {
        var before = SourceHash(raw);
        var labels = CS1XMLImporter.CreateLabelView(raw);
        Require(before.SequenceEqual(SourceHash(raw)), "Native label simplification changed source data");
        Require(labels.SegmentList.Count > 0 && labels.SegmentList.Count <= raw.SegmentList.Count, "Native label network copy is invalid");
        Require(!ReferenceEquals(labels.SegmentList, raw.SegmentList) && !ReferenceEquals(labels.NodeList, raw.NodeList), "Native labels share mutable source networks");
        Require(Enum.GetNames<MapType>().SequenceEqual(new[] { "CitiesSkylines" }), "A CS2 game map type survived pruning");
        Require(!Core.GetReferencedAssemblies().Any(a => a.Name == "CS2MapView.Serialization"), "Core still references CS2 serialization");
        CheckNativeWidths();
        CheckNativeLabels(host);
        CheckDrawingArea(host);
        CheckNativeForest(host);
        CheckThemeReaders();
        Console.WriteLine($"PASS CS1 foundation: raw preservation, copied curved labels, native exported/fractional widths, park names and grouped transport names; {raw.SegmentList.Count} source / {labels.SegmentList.Count} label segments");
    }

    private static void CheckThemeReaders()
    {
        var directory = CS2MapView.Data.RenderContext.ThemeDirectory;
        var drawing = Path.Combine(directory,"default.c2theme");
        var text = Path.Combine(directory,"default.c2stringtheme");
        using var drawingReader = File.OpenRead(drawing);
        using var textReader = File.OpenRead(text);
        Require(CS2MapView.Theme.DrawingTheme.LoadAsync(drawing).GetAwaiter().GetResult()!=null,
            "Drawing theme cannot load while another viewer reads it");
        Require(CS2MapView.Theme.StringTheme.LoadAsync(text).GetAwaiter().GetResult()!=null,
            "Text theme cannot load while another viewer reads it");
        Console.WriteLine("PASS themes coexist with other read-only viewers/renderers");
    }
    private static void CheckNativeForest(ICS2MapViewRoot host)
    {
        var config = CS2MapView.Beta.BetaSettings.Clone(CS2MapView.Beta.BetaSettings.Config);
        config.RenderForest = true;
        using var forestHost = new CS2MapView.Form.BetaOutputHost(CS2MapView.Form.BetaOutput.Clone(host.Context.UserSettings), config);
        var field = new byte[512 * 512];
        for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
            field[y * 512 + x] = (byte)(x < 256 ? (y < 256 ? 64 : 128) : (y < 256 ? 0 : 255));
        var raw = new CSLExportXML { ForestByteData = field };
        var session = CS2MapView.Beta.BetaMapSession.Get(forestHost, raw);
        var color = session.CurrentMapStyle.Colors.ForestBase;
        using var layer = new CS2MapView.Drawing.Forest.CS1.CS1ForestLayerBuilder(forestHost, raw).BuildAsync(null).GetAwaiter().GetResult();
        var view = new ViewContext { WorldRect = CS1MapType.CS1WorldRect, WorldScaleFactor = 256f / 17280, ViewLeftTop = (-8640,-8640) };
        using var bitmap = new SKBitmap(256,256); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.White);
        layer.DrawLayer(canvas,view,new SKRect(-8640,-8640,8640,8640),new SKRect(0,0,256,256));
        foreach (var sample in new[] { (X:64,Y:192,Alpha:64), (X:64,Y:64,Alpha:128), (X:192,Y:192,Alpha:0), (X:192,Y:64,Alpha:255) })
        {
            var actual = bitmap.GetPixel(sample.X,sample.Y);
            int Blend(byte channel) => (int)Math.Round(channel * sample.Alpha / 255d + 255 - sample.Alpha);
            Require(Math.Abs(actual.Red-Blend(color.Red))<=2 && Math.Abs(actual.Green-Blend(color.Green))<=2 && Math.Abs(actual.Blue-Blend(color.Blue))<=2,
                "Filtered forest density changed the palette or introduced alpha fringes");
        }
        Console.WriteLine("PASS native forest orientation and premultiplied palette at 0/64/128/255 density");
    }
    private sealed class FillLayer : ILayer
    {
        public string LayerName => ILayer.LayerNameTerrain;
        public void DrawLayer(SKCanvas canvas, ViewContext vc, SKRect visible, SKRect canvasRect)
        {
            canvas.SetMatrix(vc.ViewTransform);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(new SKRect(-8640, -8640, 8640, 8640), paint);
            canvas.ResetMatrix();
        }
        public void Dispose() { }
    }
    private static void CheckDrawingArea(ICS2MapViewRoot host)
    {
        var previous = host.Context.DrawingArea;
        host.Context.DrawingArea = new SKRect(-50, -50, 50, 50);
        try
        {
            using var map = new MapData { WorldRect = CS1MapType.CS1WorldRect };
            map.Layers.Add(new FillLayer());
            using var bitmap = new SKBitmap(300, 300);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            var vc = new ViewContext { WorldRect = CS1MapType.CS1WorldRect, WorldScaleFactor = 1, ScaleFactor = 1,
                Angle = (float)(Math.PI / 4), ViewLeftTop = (-150, -150) };
            var savedCount = canvas.SaveCount;
            host.Context.DrawLayers(map, vc, canvas, new SKRect(0, 0, 300, 300));
            Require(bitmap.GetPixel(150, 150) == SKColors.Red && bitmap.GetPixel(215, 150) == SKColors.Red, "Rotated drawing area clipped its interior");
            Require(bitmap.GetPixel(210, 210) == SKColors.White, "Native drawing-area clip uses rotated bounds instead of the actual polygon");
            Require(canvas.SaveCount == savedCount, "Layer output leaks canvas clip state");
            var palette = new CS2MapView.Theme.ColorRules { UseDiscreteContourColors = true,
                LandContourColors = [new(100, default, CS2MapView.Theme.SerializableColor.FromARGB(255, 255, 0, 0)),
                    new(float.MinValue, default, CS2MapView.Theme.SerializableColor.FromARGB(255, 0, 0, 255))] };
            Require(palette.LerpLandContourColors(50).FillColor.ToSKColor() == SKColors.Blue && palette.LerpLandContourColors(100).FillColor.ToSKColor() == SKColors.Red,
                "Native terrain interpolates the CS1 discrete palette");
        }
        finally { host.Context.DrawingArea = previous; }
    }
    private static void CheckNativeWidths()
    {
        var type = new XMLSegmentType { Name = "Foundation Fractional Road" };
        type.Lanes.Add(new XMLLaneType { Type = "Vehicle", VehicleType = "Car", Position = 0, Width = 4.25f });
        type.Lanes.Add(new XMLLaneType { Type = "None", VehicleType = "None", Position = 0, Width = 100 });
        type.Lanes.Add(new XMLLaneType { Type = "Vehicle", VehicleType = "None", Position = 0, Width = 80 });
        type.Lanes.Add(new XMLLaneType { Type = "Vehicle", VehicleType = "Car", Position = float.NaN, Width = 100 });
        var xml = new CSLExportXML { FileVersion = "4.1", SegmentTypeList = [type] };
        var managerType = Core.GetType("CS2MapView.Util.CS1SegmentManager", true)!;
        var manager = Activator.CreateInstance(managerType, xml)!;
        var getWidth = managerType.GetMethod("GetWidth", Members)!;
        float Width(XMLSegment segment) => (float)getWidth.Invoke(manager, [segment])!;
        var segment = new XMLSegment { Name = type.Name, Width = 9.66f };
        Require(Math.Abs(Width(segment) - 9.66) < 0.00001, "Native exported road width lost precedence");
        segment.Width = 17.8f;
        Require(Math.Abs(Width(segment) - 17.8) < 0.00001, "Native roads collapse per-segment widths");
        foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            segment.Width = invalid;
            Require(Width(segment) == 4.25f, "Native usable-lane fallback rounded or included helper lanes");
        }
        xml.FileVersion = "3.60";
        manager = Activator.CreateInstance(managerType, xml)!;
        segment.Width = 1.5f;
        Require(Width(segment) == 1.5f, "Native old-file width precedence failed");
    }
    private static object New(Type type, params object[] arguments) => Activator.CreateInstance(type, Members, null, arguments, null)!;
    private static IEnumerable<object> Ordered(object lookup) => ((IEnumerable)lookup.GetType().GetMethod("GetAllOrderedObjects", Members)!.Invoke(lookup, null)!).Cast<object>();
    private static void CheckNativeLabels(ICS2MapViewRoot host)
    {
        var settings = host.Context.UserSettings;
        var old = (settings.RenderBuildingNameLabel, settings.RenderParkNameLabels, settings.RenderStreetNames, settings.RenderMapSymbol);
        settings.RenderBuildingNameLabel = settings.RenderParkNameLabels = settings.RenderStreetNames = true;
        settings.RenderMapSymbol = false;
        try
        {
            XMLBuilding Building(int id, float x, string name) => new()
            {
                Id = id, Name = "Foundation Station", MyName = name, Service = "PublicTransport", ItemClass = "Train Station",
                Points = [new(x - 10, 0, -10), new(x + 10, 0, -10), new(x + 10, 0, 10), new(x - 10, 0, 10)]
            };
            var synthetic = new CSLExportXML
            {
                BuildingList = [Building(1, -2000, "Terminal (Train)"), Building(2, 2000, "Terminal (Metro)")],
                DistrictParkList = [new XMLDistrictPark { Id = 1, Name = "Foundation Park", Position = new(3000, 0, 3000) }],
                SegmentList = [new XMLSegment { Id = 1, CustomName = "Foundation Street", Points = [new(-5000, 0, -5000), new(-4000, 0, -4500), new(-3000, 0, -5000)] }]
            };
            var vc = host.Context.ViewContext;
            var managerType = Core.GetType("CS2MapView.Drawing.Labels.LabelContentsManager", true)!;
            var manager = New(managerType, (CS2MapView.ReadonlyRect)vc.TextWorldRect);
            var builderType = Core.GetType("CS2MapView.Drawing.Labels.CS1.CS1LabelContentBuilder", true)!;
            var builder = New(builderType, host, synthetic, manager, CS2MapView.Beta.BetaSettings.Config);
            ((Task)builderType.GetMethod("BuildAsync", Members)!.Invoke(builder, [vc])!).GetAwaiter().GetResult();
            var contents = managerType.GetProperty("Contents", Members)!.GetValue(manager)!;
            var entries = Ordered(contents).ToArray();
            Require(entries.Count(e => e.GetType().Name == "ParkNameLabel") == 1, "Native park labels are absent");
            var buildings = entries.Where(e => e.GetType().Name == "BuildingNameLabel").ToArray();
            Require(buildings.Length == 1, "Same cleaned transport-building names were not visually grouped");
            Require((string)buildings[0].GetType().GetField("Text", Members)!.GetValue(buildings[0])! == "Terminal", "Native transport name cleanup failed");
            var center = (SKPoint)buildings[0].GetType().GetField("OriginalPosition", Members)!.GetValue(buildings[0])!;
            Require(center == SKPoint.Empty, "Grouped transport name is not at its mean anchor");
            var generalType = Core.GetType("CS2MapView.Drawing.Labels.GeneralLabelLayerBuilder", true)!;
            var general = New(generalType, host, vc, manager);
            using var layer = ((Task<ILayer>)generalType.GetMethod("BuildAsync", Members)!.Invoke(general, [null])!).GetAwaiter().GetResult();
            var commands = typeof(BasicLayer).GetProperty("DrawCommands", Members)!.GetValue(layer)!;
            Require(Ordered(commands).Any(c => c is TextOnPathDrawCommand), "Street labels do not use native path-following commands");
        }
        finally
        {
            (settings.RenderBuildingNameLabel, settings.RenderParkNameLabels, settings.RenderStreetNames, settings.RenderMapSymbol) = old;
        }
    }
}
