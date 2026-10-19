using CS2MapView.CS1.Config;
using CS2MapView.CS1.Config.Styles;
using MapStyle = CS2MapView.CS1.Config.Styles.MapStyle;
using SkiaSharp;

namespace CS2MapView.Theme;

/// <summary>
/// Translates the imported CS1 style into upstream theme and text infrastructure.
/// CS1 fixed-plus-scaled measurements are converted to game metres at the render
/// canvas size; upstream view, crop, rotation and export matrices then apply once.
/// This profile does not alter source nodes, route membership or cluster distances.
/// </summary>
public static class CS1AppearanceProfile
{
    /// <summary>Apply a CS1 style to native terrain, grid, labels and SVG symbols.</summary>
    public static void Apply(ICS2MapViewRoot appRoot, MapStyle style, double canvasSize, ApplicationConfig? config = null)
    {
        if (!double.IsFinite(canvasSize) || canvasSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(canvasSize));
        var theme = DrawingTheme.Default;
        theme.Name = "cs1-current-appearance";
        var colors = theme.Colors!;
        colors.UseDiscreteContourColors = true;
        var c = style.Colors;
        static SerializableColor Color(SKColor value) => SerializableColor.FromSKColor(value);
        Width Size(SizeEx? value) => Width.FromMetres(value is null ? 0f : (float)(value.CalcSize(canvasSize / 4096d) * 17280d / canvasSize));
        StrokeStyle? Border(BorderConfig? value) => value is null ? null : new StrokeStyle(Size(value.Size),
            value.DashStyle?.Dashes?.Select(d => (float)d).ToArray(), StrokeType.Round);
        StringStyle Text(string? family, SizeEx? size, SizeEx? halo) => new(family ?? "Meiryo", Size(size), new StrokeStyle(Size(halo)));
        // Smooth preset contours are separate strokes; these paths only fill land.
        var contourColor = default(SerializableColor);
        colors.LandContourColors = c.LandColors.Select(s => new ContourColor(s.Threshold, contourColor,
            SerializableColor.FromSKColor(s.Color))).ToList();
        colors.WaterContourColors = c.WaterColors.Select(s => new ContourColor(s.Threshold, Color(c.Coastline),
            SerializableColor.FromSKColor(s.Color))).ToList();
        colors.GridLine = Color(c.GridLine);
        colors.BuildingBorder = Color(c.BuildingBorder);
        colors.ResidentialBuilding = Color(c.ResidentialBuilding);
        colors.CommercialBuilding = Color(c.CommercialBuilding);
        colors.IndustrialBuilding = Color(c.IndustrialBuilding);
        colors.OfficeBuilding = Color(c.OfficeBuilding);
        colors.PublicTransportBuilding = Color(c.PublicTransportBuilding);
        colors.PublicBuilding = Color(c.PublicBuilding);
        colors.BeautificationBuilding = Color(c.BeautificationBuilding);
        colors.Train = Color(c.Railway);
        colors.TrainDash = Color(c.RailwayDash);
        colors.TrainTunnel = Color(c.RailwayTunnel);
        colors.TrainTunnelDash = Color(c.RailwayTunnel);
        colors.Metroway = Color(c.Metroway);
        colors.MetrowayDash = Color(c.MetrowayDash);
        colors.Tramway = Color(c.Tramway);
        colors.TramwayDash = Color(c.TramwayDash);
        colors.Road = Color(c.Road);
        colors.RoadBorder = Color(c.RoadBorder);
        colors.RoadTunnel = Color(c.RoadTunnel);
        colors.RoadTunnelBorder = Color(c.RoadTunnelBorder);
        colors.RoadElevated = Color(c.RoadEleavted);
        colors.RoadElevatedBorder = Color(c.RoadElevatedBorder);
        colors.Highway = Color(c.Highway);
        colors.HighwayBorder = Color(c.HighwayBorder);
        colors.HighwayTunnel = Color(c.HighwayTunnel);
        colors.HighwayTunnelBorder = Color(c.HighwayTunnelBorder);
        colors.HighwayElevated = Color(c.HighwayEleavted);
        colors.HighwayElevatedBorder = Color(c.HighwayElevatedBorder);
        colors.Beautification = Color(c.Beautification);
        colors.BeautificationBorder = Color(c.BeautificationBorder);
        colors.BeautificationTunnel = Color(c.BeautificationTunnel);
        colors.BeautificationTunnelBorder = Color(c.BeautificationTunnelBorder);
        colors.BeautificationElevated = Color(c.BeautificationElevated);
        colors.BeautificationElevatedBorder = Color(c.BeautificationElevatedBorder);
        colors.ForestBase = Color(c.ForestBase);
        colors.CableCarWay = Color(c.CableCarWay);
        colors.MonorailWay = Color(c.MonorailWay);
        colors.TransportStopStroke = SerializableColor.FromARGB(255, 0, 0, 0);
        colors.TransportStopFill = SerializableColor.FromARGB(255, 255, 255, 255);
        colors.StringColors = new StringColors
        {
            BuildingNameFill = Color(style.TextColors.BuildingName),
            BuildingNameStroke = Color(style.TextColors.BuildingNameBorder),
            DistrictNameFill = Color(style.TextColors.DistrictName),
            DistrictNameStroke = Color(style.TextColors.DistrictNameBorder),
            StreetNameFill = Color(style.TextColors.RoadName),
            StreetNameStroke = Color(style.TextColors.RoadNameBorder),
            ParkNameFill = Color(style.TextColors.DistrictParkName),
            ParkNameStroke = Color(style.TextColors.DistrictParkNameBorder)
        };
        theme.Strokes!.Building = Border(style.BuildingBorder);
        theme.Strokes.Station = Border(style.StationBorder);
        theme.Strokes.Grid = Border(style.GridLine);
        theme.MapSymbolSize = Size(style.MapSymbolSize);
        appRoot.Context.Theme = theme;
        var areaSize = config?.RenderAreaSize ?? 9;
        if (!double.IsFinite(areaSize) || areaSize <= 0)
            areaSize = 9;
        var halfArea = (float)(8640d * areaSize / 9d);
        appRoot.Context.DrawingArea = new SkiaSharp.SKRect(-halfArea, -halfArea, halfArea, halfArea);
        var us = appRoot.Context.UserSettings;
        appRoot.Context.StringTheme = new StringTheme
        {
            Name = "cs1-current-appearance",
            BuildingName = Text(us.CustomBuildingNameFont ?? config?.BuildingFont, style.FontSize.BuildingFontSize, style.FontSize.BuildingFontBorderSize),
            DistrictName = Text(us.CustomDistrictNameFont ?? config?.DistrictFont, style.FontSize.DistrictFontSize, style.FontSize.DistrictFontBorderSize),
            StreetName = Text(us.CustomStreetNameFont ?? config?.RoadNameFont, style.FontSize.RoadNameFontSize, style.FontSize.RoadNameFontBorderSize),
            ParkName = Text(config?.DistrictParkFont, style.FontSize.DistrictParkFontSize, style.FontSize.DistrictParkFontBorderSize)
        };
        var selectedHeights = c.LandColors.Where(x => x.Threshold != float.MinValue)
            .Select(x => new ContourStrokeStyle(x.Threshold, Size(style.ContourMiddle?.Size))).ToList();
        foreach (var height in selectedHeights)
        {
            var border = height.Height % 100 == 0 ? style.ContourWide : height.Height % 50 == 0 ? style.ContourMiddle : style.ContourNarrow;
            height.Width = Size(border?.Size);
            height.DashEffect = border?.DashStyle?.Dashes?.Select(d => (float)d).ToArray();
        }
        appRoot.Context.ContourHeights = new ContourHeights
        {
            Name = "cs1-current-appearance",
            LandHeights = selectedHeights,
            WaterHeights = c.WaterColors.Select(x => x.Threshold == float.MinValue ? 0 : x.Threshold)
                .Append(0).Distinct().Order().Select(height => new ContourStrokeStyle(height,
                    height == 0 ? Size(style.CoastLine?.Size) : Width.FromMetres(0))).ToList()
        };
    }
}
