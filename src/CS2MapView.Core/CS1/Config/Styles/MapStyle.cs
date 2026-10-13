using CS2MapView.Theme;
using SkiaSharp;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;

namespace CS2MapView.CS1.Config.Styles;

/// <summary>Read-only migration of CS1 style data into native Skia values.</summary>
public sealed class MapStyle
{
    public string FileName { get; set; } = "default";
    public string FileVersion { get; set; } = "5.0";
    public string ContentVersion { get; set; } = "5.0";
    public BorderConfig BuildingBorder { get; set; } = new(.2, .1);
    public BorderConfig StationBorder { get; set; } = new(.4, .2);
    public BorderConfig GridLine { get; set; } = new(1, 0);
    public SizeEx MapSymbolSize { get; set; } = new(2, 4);
    public BorderConfig ContourWide { get; set; } = new(.5, 0);
    public BorderConfig ContourMiddle { get; set; } = new(.2, 0);
    public BorderConfig ContourNarrow { get; set; } = new(.1, 0);
    public BorderConfig CoastLine { get; set; } = new(.3, 0);
    public ColorRules Colors { get; set; } = new();
    public TextColorRules TextColors { get; set; } = new();
    public WayWidth WayWidth { get; set; } = new();
    public WayBorder WayBorder { get; set; } = new();
    public FontSize FontSize { get; set; } = new();
    public static MapStyle Load(string path)
    {
        var xml = XDocument.Load(path).Root ?? throw new InvalidDataException("Style has no root element.");
        var result = new MapStyle { FileName = Path.GetFileNameWithoutExtension(path) };
        ReadObject(result, xml);
        result.Colors.LandColors = ReadPalette(xml.Element("Colors")?.Element("LandColors"));
        result.Colors.WaterColors = ReadPalette(xml.Element("Colors")?.Element("WaterColors"));
        result.Colors.RouteMapColors = xml.Element("Colors")?.Element("RouteMapColors")?.Elements().Select(ReadColor).ToList() ?? [];
        return result;
    }
    private static List<ColorThresholdRule> ReadPalette(XElement? element) => element?.Elements().Select(x => new ColorThresholdRule {
        Threshold = (float)Number(x.Attribute("threshold")?.Value, float.MinValue), Color = ReadColor(x)
    }).ToList() ?? [];
    private static SKColor ReadColor(XElement x) => new((byte)Number(x.Attribute("r")?.Value), (byte)Number(x.Attribute("g")?.Value),
        (byte)Number(x.Attribute("b")?.Value), (byte)Number(x.Attribute("a")?.Value, 255));
    private static double Number(string? text, double fallback = 0) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static void ReadObject(object value, XElement xml)
    {
        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var element = xml.Element(property.Name);
            if (element == null) continue;
            if (property.PropertyType == typeof(SKColor)) property.SetValue(value, ReadColor(element));
            else if (property.PropertyType == typeof(SizeEx)) property.SetValue(value, new SizeEx(Number(element.Attribute("fixed")?.Value), Number(element.Attribute("scaled")?.Value)));
            else if (property.PropertyType == typeof(BorderConfig))
            {
                var size = element.Element("Size");
                var dashes = element.Element("DashStyle");
                property.SetValue(value, new BorderConfig(Number(size?.Attribute("fixed")?.Value), Number(size?.Attribute("scaled")?.Value)) {
                    DashStyle = dashes == null ? null : new DashStyleEx {
                        Dashes = dashes.Element("Dashes")?.Elements().Select(x => Number(x.Value)).ToArray() ?? [],
                        Offset = Number(dashes.Element("Offset")?.Value)
                    }
                });
            }
            else if (property.PropertyType.IsClass && property.PropertyType != typeof(string) && !property.PropertyType.IsGenericType)
                ReadObject(property.GetValue(value)!, element);
        }
    }
}

/// <summary>CS1 two-part measurements converted to upstream native Width/StrokeStyle.</summary>
public sealed class SizeEx(double fixedSize = 0, double scaledSize = 0)
{
    public double FixedSize { get; set; } = fixedSize;
    public double ScaledSize { get; set; } = scaledSize;
    public double CalcSize(double renderScale) => FixedSize + ScaledSize * renderScale;
    public Width ToWidth(double canvasSize) => Width.FromMetres((float)(CalcSize(canvasSize / 4096) * 17280 / canvasSize));
}
public sealed class BorderConfig(double fixedSize = 0, double scaledSize = 0)
{
    public SizeEx Size { get; set; } = new(fixedSize, scaledSize);
    public DashStyleEx? DashStyle { get; set; }
    public StrokeStyle ToStroke(double canvasSize) => new(Size.ToWidth(canvasSize), DashStyle?.Dashes.Select(x => (float)x).ToArray(), StrokeType.Round);
}
public sealed class DashStyleEx
{
    public double[] Dashes { get; set; } = [];
    public double Offset { get; set; }
}
public sealed class ColorThresholdRule
{
    public float Threshold { get; set; }
    public SKColor Color { get; set; }
}
public sealed class ColorRules
{
    public List<ColorThresholdRule> LandColors { get; set; } = [new() { Threshold = float.MinValue, Color = new SKColor(240, 240, 235) }];
    public List<ColorThresholdRule> WaterColors { get; set; } = [new() { Threshold = float.MinValue, Color = new SKColor(170, 210, 230) }];
    public List<SKColor> RouteMapColors { get; set; } = [SKColors.Red, SKColors.Green, SKColors.Blue];
    public SKColor GridLine { get; set; }
    public SKColor BuildingBorder { get; set; }
    public SKColor ResidentialBuilding { get; set; }
    public SKColor CommercialBuilding { get; set; }
    public SKColor IndustrialBuilding { get; set; }
    public SKColor OfficeBuilding { get; set; }
    public SKColor PublicTransportBuilding { get; set; }
    public SKColor PublicBuilding { get; set; }
    public SKColor BeautificationBuilding { get; set; }
    public SKColor Railway { get; set; }
    public SKColor RailwayDash { get; set; }
    public SKColor RailwayTunnel { get; set; }
    public SKColor Metroway { get; set; }
    public SKColor MetrowayDash { get; set; }
    public SKColor Tramway { get; set; }
    public SKColor TramwayDash { get; set; }
    public SKColor Road { get; set; }
    public SKColor RoadBorder { get; set; }
    public SKColor RoadTunnel { get; set; }
    public SKColor RoadTunnelBorder { get; set; }
    public SKColor RoadEleavted { get; set; }
    public SKColor RoadElevatedBorder { get; set; }
    public SKColor Highway { get; set; }
    public SKColor HighwayBorder { get; set; }
    public SKColor HighwayTunnel { get; set; }
    public SKColor HighwayTunnelBorder { get; set; }
    public SKColor HighwayEleavted { get; set; }
    public SKColor HighwayElevatedBorder { get; set; }
    public SKColor Beautification { get; set; }
    public SKColor BeautificationBorder { get; set; }
    public SKColor BeautificationTunnel { get; set; }
    public SKColor BeautificationTunnelBorder { get; set; }
    public SKColor BeautificationElevated { get; set; }
    public SKColor BeautificationElevatedBorder { get; set; }
    public SKColor ForestBase { get; set; }
    public SKColor CableCarWay { get; set; }
    public SKColor MonorailWay { get; set; }
    public SKColor ContourLine { get; set; }
    public SKColor Coastline { get; set; }
}
public sealed class TextColorRules
{
    public SKColor DistrictName { get; set; } = new(50, 50, 50);
    public SKColor DistrictNameBorder { get; set; } = new(255, 255, 255, 180);
    public SKColor BuildingName { get; set; } = new(60, 60, 60);
    public SKColor BuildingNameBorder { get; set; } = new(255, 255, 255, 220);
    public SKColor DistrictParkName { get; set; } = new(50, 50, 50);
    public SKColor DistrictParkNameBorder { get; set; } = new(255, 255, 255, 180);
    public SKColor RoadName { get; set; } = new(60, 60, 60, 180);
    public SKColor RoadNameBorder { get; set; } = new(255, 255, 255, 180);
}
public sealed class WayWidth
{
    public SizeEx TrainRail { get; set; } = new(2, 0);
    public SizeEx TrainRailDash { get; set; } = new(1, 0);
    public SizeEx TrainRailTunnel { get; set; } = new(2, 0);
    public SizeEx TramRail { get; set; } = new(2, 0);
    public SizeEx TramRailDash { get; set; } = new(1, 0);
    public SizeEx MetroRail { get; set; } = new(2, 0);
    public SizeEx MetroRailDash { get; set; } = new(1, 0);
    public SizeEx Beautification { get; set; } = new(1, 0);
    public SizeEx CableCarRail { get; set; } = new(1, 0);
    public SizeEx CableCarDecorationSpan { get; set; } = new(8, 0);
    public SizeEx CableCarDecorationLength { get; set; } = new(2, 0);
    public SizeEx CableCarDecorationWidth { get; set; } = new(1, 0);
    public SizeEx CableCarStationWidth { get; set; } = new(4, 0);
    public SizeEx MonorailRail { get; set; } = new(2, 0);
    public SizeEx MonorailDecorationSpan { get; set; } = new(8, 0);
    public SizeEx MonorailDecorationLength { get; set; } = new(2, 0);
    public SizeEx MonorailDecorationWidth { get; set; } = new(1, 0);
    public SizeEx MonorailStationWidth { get; set; } = new(4, 0);
    public SizeEx RoadNarrow { get; set; } = new(.5, 1);
    public SizeEx Road { get; set; } = new(.5, 2);
    public SizeEx RoadWide { get; set; } = new(.5, 3);
    public SizeEx RoadVeryWide { get; set; } = new(.5, 4);
    public SizeEx HighwayRamp { get; set; } = new(.5, 2);
    public SizeEx Highway { get; set; } = new(.5, 3);
    public SizeEx HighwayWide { get; set; } = new(.5, 4);
}
public sealed class WayBorder
{
    public BorderConfig BeautificationBorder { get; set; } = new();
    public BorderConfig BeautificationTunnelBorder { get; set; } = new();
    public BorderConfig BeautificationElevatedBorder { get; set; } = new();
    public BorderConfig RoadBorder { get; set; } = new();
    public BorderConfig RoadTunnelBorder { get; set; } = new();
    public BorderConfig RoadElevatedBorder { get; set; } = new();
    public BorderConfig HighwayBorder { get; set; } = new();
    public BorderConfig HighwayTunnelBorder { get; set; } = new();
    public BorderConfig HighwayElevatedBorder { get; set; } = new();
}
public sealed class FontSize
{
    public SizeEx TransportStopFontSize { get; set; } = new(3, 0);
    public SizeEx TransportStopFontBorderSize { get; set; } = new(.5, 0);
    public SizeEx DistrictFontSize { get; set; } = new(10, 0);
    public SizeEx DistrictFontBorderSize { get; set; } = new(1, 0);
    public SizeEx DistrictParkFontSize { get; set; } = new(5, 0);
    public SizeEx DistrictParkFontBorderSize { get; set; } = new(.5, 0);
    public SizeEx BuildingFontSize { get; set; } = new(5, 0);
    public SizeEx BuildingFontBorderSize { get; set; } = new(.5, 0);
    public SizeEx RoadNameFontSize { get; set; } = new(5, 0);
    public SizeEx RoadNameFontBorderSize { get; set; } = new(.5, 0);
}
public sealed class MapStyleCollection
{
    public List<MapStyle> Children { get; } = [];
    public MapStyleCollection(string? directory = null)
    {
        directory ??= Path.Combine(AppContext.BaseDirectory, "Styles");
        if (Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFiles(directory, "*.cmvstyle").Order(StringComparer.OrdinalIgnoreCase)) Children.Add(MapStyle.Load(path));
        if (Children.Count == 0) Children.Add(new MapStyle());
    }
    public MapStyle? Find(string? name, bool useDefault) => Children.FirstOrDefault(x => x.FileName == name)
        ?? (useDefault ? Children.FirstOrDefault(x => x.FileName == "default") ?? Children.First() : null);
}
