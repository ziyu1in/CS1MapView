using CS2MapView.Config;
using System.Xml.Serialization;
using System.ComponentModel;

namespace CS2MapView.CS1.Config;

/// <summary>CS1-specific options extending the published Core settings model.</summary>
public sealed class ApplicationConfig
{
    [XmlAttribute("version")] public string Version { get; set; } = "5.0";
    public string SelectedStyle { get; set; } = "apple-light";
    public double CanvasSize { get; set; } = 4096;
    public double RenderAreaSize { get; set; } = 9;
    [Browsable(false)] public double Angle { get; set; }
    public bool RenderTerrain { get; set; } = true;
    public bool RenderForest { get; set; } = true;
    public bool RenderBuilding { get; set; } = true;
    public bool RenderRoad { get; set; } = true;
    public bool RenderGrid { get; set; } = true;
    public bool RenderRail { get; set; } = true;
    public bool RenderRailTrain { get; set; } = true;
    public bool RenderRailTram { get; set; } = true;
    public bool RenderRailMetro { get; set; } = true;
    public bool RenderRailCableCar { get; set; } = true;
    public bool RenderRailMonorail { get; set; } = true;
    public bool RenderDistrictName { get; set; } = true;
    public bool RenderBuildingName { get; set; } = true;
    public bool RenderParkName { get; set; } = true;
    public bool RenderMapSymbol { get; set; } = true;
    public bool RenderRoadName { get; set; } = true;
    public bool RenderTransport { get; set; }
    public bool RenderTransportRoute { get; set; }
    public bool RenderStationPremises { get; set; } = true;
    public bool FrameStopTracks { get; set; } = true;
    public StationAreaColor StationPremisesColor { get; set; } = StationAreaColor.Red;
    public RailwayConfig Railway { get; set; } = new();
    public TransportRouteMapConfig RouteMapConfig { get; set; } = new();
    [Browsable(false)] public bool DisableBitmapCache { get; set; }
    [Browsable(false)] public bool ReduceContrast { get; set; }
    public bool DisableBuildingBorder { get; set; }
    public bool HideRICO { get; set; }
    public string BuildingFont { get; set; } = "Meiryo";
    public string DistrictFont { get; set; } = "Meiryo";
    public string DistrictParkFont { get; set; } = "Meiryo";
    public string RoadNameFont { get; set; } = "Meiryo";
    [Browsable(false)] public string TransportFont { get; set; } = "Meiryo";
    public string MapSymbolStyle { get; set; } = "ja+";
    public bool UseVectorTerrain { get; set; } = true;
    public bool DisplayContourLines { get; set; }
    public string ContourIntervals { get; set; } = "Default";
    [Browsable(false)] public int SVGImageLength { get; set; } = 1024;
    [Browsable(false)] public int SVGTerrainDivisions { get; set; } = 5;
    [Browsable(false)] public bool SVGTextAsGeometry { get; set; }
    [Browsable(false)] public int MaxParallelism { get; set; }
    public CS1HiddenObjectInfo IgnoreBuildingShape { get; set; } = CS1HiddenObjectInfo.DefaultBuildings();
    public CS1HiddenObjectInfo IgnoreMapSymbols { get; set; } = CS1HiddenObjectInfo.DefaultMapSymbols();
    public CS1HiddenObjectInfo IgnoreSegments { get; set; } = CS1HiddenObjectInfo.DefaultSegments();
    [XmlArrayItem("Regex")] public List<string> SampleRouteNumbering { get; set; } = [@"^\[([^\]]+)\].*$", @"^([^ ]*\d+[^ ]*).*$"];
    [XmlArrayItem("Asset")] public List<BuildingPreference> BuildingPreferences { get; set; } = [];
    [XmlIgnore] public bool ShowRailInfrastructure => Railway.InfrastructureOnly || RenderRail;
    [XmlIgnore] public bool ShowTransitRoutes => !Railway.InfrastructureOnly && RenderTransportRoute;
    [XmlIgnore] public bool ShowTransitLabels => !Railway.InfrastructureOnly && RenderTransport;
    [XmlIgnore] public bool ShowStationPremises => RenderStationPremises;
}

public enum StationAreaColor { Red, Blue }
public enum RailwayAppearance { Apple, Google }
public sealed class RailwayConfig
{
    public bool InfrastructureOnly { get; set; }
    public RailwayAppearance Appearance { get; set; } = RailwayAppearance.Apple;
    public bool SeparateTracks { get; set; } = true;
    public bool ReconnectTracks { get; set; } = true;
    public bool ShowTunnels { get; set; } = true;
    public bool ColorByType { get; set; } = true;
}

/// <summary>Published route settings plus CS1 station-layout and transfer extensions.</summary>
[XmlRoot("TransportRouteMapConfig"), XmlType("CS1TransportRouteMapConfig")]
public sealed class TransportRouteMapConfig : CS2MapView.Config.TransportRouteMapConfig
{
    [DisplayName("Cluster search radius (m)"), Description("Search distance for aligned physical stops. Default 90 m. Independent of symbol thickness and transfer-link distance.")]
    public double StationClusterRadius { get; set; } = 90;
    [DisplayName("Transfer link distance (m)"), Description("Maximum distance between actual stops when linking separate symbols. Default 100 m. Shortest legal connectors preserve connectivity without loops.")]
    public double StationLinkDistance { get; set; } = 100;
    [DisplayName("Show mode letters")] public bool ShowStationModeBadges { get; set; } = true;
    [DisplayName("One transit modal tag per cluster")] public bool SplitStationBadgesByCluster { get; set; } = true;
    public bool RenderMetroStation { get => RenderMetroStop; set => RenderMetroStop = value; }
    public bool RenderTrainStation { get => RenderTrainStop; set => RenderTrainStop = value; }
    public bool RenderMonorailStation { get => RenderMonorailStop; set => RenderMonorailStop = value; }
    public bool RenderBlimpLine { get => RenderAirplaneLine; set => RenderAirplaneLine = value; }
    public bool RenderBlimpStop { get => RenderAirplaneStop; set => RenderAirplaneStop = value; }
    public bool RenderFerryHarbor { get => RenderShipStop; set => RenderShipStop = value; }
    [DisplayName("Connect bus, trolleybus and tram"), Description("Legacy transfer rule, used only when selected transfer modes are off.")]
    public bool MergeBusTramStop { get; set; } = true;
    [DisplayName("Connect train and metro"), Description("Legacy transfer rule, used only when selected transfer modes are off.")]
    public bool MergeTrainMetroStaion { get; set; }
    [DisplayName("Use selected transfer modes"), Description("On: use the individual transfer modes below. Off: use the legacy bus/tram and train/metro connection rules."), RefreshProperties(RefreshProperties.All)]
    public bool UseCustomTransferModes { get; set; }
    public bool TransferBus { get; set; } = true;
    public bool TransferTrolleybus { get; set; } = true;
    public bool TransferTram { get; set; } = true;
    public bool TransferMetro { get; set; } = true;
    public bool TransferTrain { get; set; } = true;
    public bool TransferMonorail { get; set; } = true;
    public bool TransferBlimp { get; set; } = true;
    public bool TransferFerry { get; set; } = true;
    public bool AutoColoring { get; set; }
    public bool WidenOnSharedLines { get; set; } = true;
    [DisplayName("Hide tiny terminal loops")] public bool DetectEndLoop { get; set; }
    public bool MarkOneWayRoutes { get; set; }
    [DisplayName("Combine identical route shorthands")] public bool UseMergedRouteNumberings { get; set; } = true;
    [XmlIgnore] public double SafeStationClusterRadius => Safe(StationClusterRadius, 90);
    [XmlIgnore] public double SafeStationLinkDistance => Safe(StationLinkDistance, 100);
    private static double Safe(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 512) : fallback;
    public bool CanConnectTransfers(string a, string b)
    {
        if (a == b) return true;
        bool selected(string mode) => mode switch {
            "Bus" => TransferBus, "Trolleybus" => TransferTrolleybus, "Tram" => TransferTram,
            "Metro" => TransferMetro, "Train" => TransferTrain, "Monorail" => TransferMonorail,
            "Airplane" => TransferBlimp, "Ship" => TransferFerry, _ => false };
        return UseCustomTransferModes ? selected(a) && selected(b)
            : (MergeBusTramStop && new[] { "Bus", "Tram", "Trolleybus" }.Contains(a) && new[] { "Bus", "Tram", "Trolleybus" }.Contains(b))
              || (MergeTrainMetroStaion && new[] { "Train", "Metro" }.Contains(a) && new[] { "Train", "Metro" }.Contains(b));
    }
}
