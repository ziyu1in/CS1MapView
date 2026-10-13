using CS2MapView.CS1.Config;
using CS2MapView.CS1.Config.Styles;
using CS2MapView.CS1.Transit;
using CS2MapView.Config;
using CS2MapView.Drawing;
using SkiaSharp;
using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.Serialization;
using ApplicationConfig = CS2MapView.CS1.Config.ApplicationConfig;

namespace CS2MapView.Beta;

/// <summary>Native beta settings with read-only import of stable CS1 settings.</summary>
public static class BetaSettings
{
    public static ApplicationConfig Config { get; private set; } = new();
    public static string ConfigPath { get; private set; } = Path.Combine(AppContext.BaseDirectory, ProjectInfo.Name + ".config.xml");
    public static List<BetaRouteOverride> RouteOverrides { get; private set; } = [];
    public static MapStyleCollection StyleCollection => new(Path.Combine(AppContext.BaseDirectory, "Styles"));
    public static MapStyle LoadStyle(string name) => StyleCollection.Find(name, true)!;
    public static void Load(string? migrationPath = null)
    {
        var source = migrationPath;
        var storedConfig = ProjectInfo.SettingsPathForRead(AppContext.BaseDirectory, "config.xml");
        if (source == null && File.Exists(storedConfig)) source = storedConfig;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); source == null && dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "CSLMapViewConfig.xml");
            if (File.Exists(candidate)) source = candidate;
        }
        source ??= Path.Combine(AppContext.BaseDirectory, "Migration", "CSLMapViewConfig.xml");
        Config = File.Exists(source) ? ImportConfig(source) : new ApplicationConfig();
        if (!double.IsFinite(Config.CanvasSize) || Config.CanvasSize <= 0) Config.CanvasSize = 4096;
        if (!double.IsFinite(Config.RenderAreaSize) || Config.RenderAreaSize <= 0) Config.RenderAreaSize = 9;
        var overrides = ProjectInfo.SettingsPathForRead(AppContext.BaseDirectory, "routes.xml");
        RouteOverrides = [];
        if (File.Exists(overrides))
        {
            using var stream = File.OpenRead(overrides);
            RouteOverrides = (List<BetaRouteOverride>)new XmlSerializer(typeof(List<BetaRouteOverride>)).Deserialize(stream)!;
        }
    }
    /// <summary>Maps known stable XML fields into published Core models without loading recovered code.</summary>
    public static ApplicationConfig ImportConfig(string path)
    {
        var xml = XDocument.Load(path).Root ?? throw new InvalidDataException("Settings have no root element.");
        var result = new ApplicationConfig();
        ReadScalars(result, xml);
        ReadScalars(result.Railway, xml.Element("Railway"));
        ReadScalars(result.RouteMapConfig, xml.Element("RouteMapConfig"));
        ReadIgnore(result.IgnoreBuildingShape, xml.Element("IgnoreBuildingShape"));
        ReadIgnore(result.IgnoreMapSymbols, xml.Element("IgnoreMapSymbols"));
        ReadIgnore(result.IgnoreSegments, xml.Element("IgnoreSegments"));
        if (xml.Element("SampleRouteNumbering") is XElement numbering)
            result.SampleRouteNumbering = numbering.Elements().Select(x => x.Value).ToList();
        result.BuildingPreferences = xml.Element("BuildingPreferences")?.Elements().Select(x =>
        {
            bool? hidden = bool.TryParse(x.Attribute("hide")?.Value ?? x.Attribute("hideShape")?.Value, out var v) ? v : null;
            string? visibility = x.Attribute("nameVisibility")?.Value;
            Enum.TryParse<NameVisibility>(visibility, true, out var name);
            if (string.Equals(visibility, "AllVisible", StringComparison.OrdinalIgnoreCase)) name = NameVisibility.FullVisible;
            return new BuildingPreference {
                SteamId = x.Attribute("steamId")?.Value, Name = x.Attribute("name")?.Value,
                HideShape = hidden, NameVisibility = name, MapSymbol = LegacyMapSymbolKeys.Normalize(x.Attribute("symbol")?.Value)
            };
        }).ToList() ?? [];
        result.Version = "5.0";
        return result;
    }
    private static void ReadScalars(object target, XElement? xml)
    {
        if (xml == null) return;
        foreach (var element in xml.Elements())
        {
            var property = target.GetType().GetProperty(element.Name.LocalName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite) continue;
            var type = property.PropertyType;
            if (!(type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))) continue;
            try { property.SetValue(target, type.IsEnum ? Enum.Parse(type, element.Value, true) : Convert.ChangeType(element.Value, type, CultureInfo.InvariantCulture)); }
            catch (FormatException) { }
            catch (ArgumentException) { }
            catch (OverflowException) { }
        }
    }
    private static void ReadIgnore(CS1HiddenObjectInfo target, XElement? xml)
    {
        if (xml == null) return;
        target.IgnoreByName = xml.Element("ByName")?.Elements().Select(x => x.Value).ToList() ?? [];
        target.IgnoreByItemClass = xml.Element("ByItemClass")?.Elements().Select(x => x.Value).ToList() ?? [];
        target.Refresh();
    }
    public static ApplicationConfig Clone(ApplicationConfig config)
    {
        using var stream = new MemoryStream();
        var serializer = new XmlSerializer(typeof(ApplicationConfig));
        serializer.Serialize(stream, config); stream.Position = 0;
        return (ApplicationConfig)serializer.Deserialize(stream)!;
    }
    public static void Save(ApplicationConfig? config = null)
    {
        if (config != null) Config = Clone(config);
        SaveXml(ConfigPath, Config);
        SaveXml(Path.Combine(AppContext.BaseDirectory, ProjectInfo.Name + ".routes.xml"), RouteOverrides);
    }
    private static void SaveXml<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp)) new XmlSerializer(typeof(T)).Serialize(stream, value);
        File.Move(tmp, path, true);
    }
    internal static void ApplyRoutes(string city, TransportManager manager)
    {
        foreach (var route in manager.TransportList)
        {
            var saved = RouteOverrides.LastOrDefault(o => o.City == city && o.Id == route.Id && o.Name == route.Name);
            if (saved == null) continue;
            route.ViewerVisible = saved.Visible;
            route.ViewerDefinedLineNo = saved.Shorthand;
            if (SKColor.TryParse(saved.Color, out var color)) route.UserDefinedColor = color;
        }
    }
    internal static void StoreRoutes(string city, TransportManager manager)
    {
        foreach (var route in manager.TransportList)
        {
            RouteOverrides.RemoveAll(o => o.City == city && o.Id == route.Id);
            var color = route.UserDefinedColor;
            RouteOverrides.Add(new BetaRouteOverride { City = city, Id = route.Id, Name = route.Name,
                Visible = route.ViewerVisible, Shorthand = route.ViewerDefinedLineNo,
                Color = color.HasValue ? $"#{color.Value.Alpha:X2}{color.Value.Red:X2}{color.Value.Green:X2}{color.Value.Blue:X2}" : null });
        }
    }
}

public class BetaRouteOverride
{
    [XmlAttribute] public string City { get; set; } = "";
    [XmlAttribute] public int Id { get; set; }
    [XmlAttribute] public string? Name { get; set; }
    [XmlAttribute] public bool Visible { get; set; } = true;
    [XmlAttribute] public string? Shorthand { get; set; }
    [XmlAttribute] public string? Color { get; set; }
}
