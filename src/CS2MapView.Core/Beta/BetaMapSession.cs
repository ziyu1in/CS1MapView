using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using CS2MapView.CS1.Config;
using CS2MapView.CS1.Config.Styles;
using CS2MapView.CS1.Geometry;
using CS2MapView.CS1.Transit;
using CS2MapView.Data;
using CS2MapView.Drawing;
using CS2MapView.Drawing.Layer;
using CS2MapView.Drawing.Roads.CS1;
using CS2MapView.Drawing.Buildings.CS1;
using CS2MapView.Drawing.Railways.CS1;
using CS2MapView.Drawing.Forest.CS1;
using CS2MapView.Drawing.StationPremises.CS1;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Beta;

/// <summary>One host's native feature models and selection; source data stays immutable.</summary>
public sealed class BetaMapSession
{
    private sealed class Holder { public ApplicationConfig? Config; public BetaMapSession? Session; }
    private static readonly ConditionalWeakTable<ICS2MapViewRoot, Holder> Sessions = new();
    private readonly ICS2MapViewRoot appRoot;
    private readonly object buildLock = new();
    private string? transportSettings;
    public ApplicationConfig Config { get; }
    public MapStyle CurrentMapStyle { get; private set; }
    public CSLExportXML Raw { get; }
    public CS1StationPremisesCollection StationPremises { get; }
    public TransportManager TransportManager { get; private set; } = new();
    internal List<StationHitTarget> StationHitTargets { get; private set; } = [];
    public int? SelectedGroupId => TransportManager.HighlightedStation?.Id;
    public static void SetConfigForHost(ICS2MapViewRoot root, ApplicationConfig config)
    {
        var holder = Sessions.GetOrCreateValue(root);
        lock (holder) { holder.Config = BetaSettings.Clone(config); holder.Session = null; }
    }
    public static BetaMapSession Get(ICS2MapViewRoot root, CSLExportXML raw)
    {
        var holder = Sessions.GetOrCreateValue(root);
        lock (holder)
        {
            if (holder.Session == null || !ReferenceEquals(holder.Session.Raw, raw))
                holder.Session = new BetaMapSession(root, raw, holder.Config ?? BetaSettings.Config);
            return holder.Session;
        }
    }
    private BetaMapSession(ICS2MapViewRoot root, CSLExportXML raw, ApplicationConfig config)
    {
        appRoot = root; Raw = raw; Config = BetaSettings.Clone(config);
        CurrentMapStyle = BetaSettings.LoadStyle(Config.SelectedStyle);
        StationPremises = CS1StationPremisesCollection.Collect(raw);
        RefreshTransport();
    }
    public void RefreshTransport()
    {
        lock (buildLock)
        {
            if (Config.Railway.InfrastructureOnly) { ClearHighlight(); ClearHitTargets(); }
            CurrentMapStyle = BetaSettings.LoadStyle(Config.SelectedStyle);
            Theme.CS1AppearanceProfile.Apply(appRoot, CurrentMapStyle, Config.CanvasSize, Config);
            using var writer = new StringWriter();
            new XmlSerializer(typeof(TransportRouteMapConfig)).Serialize(writer, Config.RouteMapConfig);
            string settings = Config.SelectedStyle + writer;
            string key = settings + RouteVisibilitySignature(TransportManager);
            if (key == transportSettings) return;
            var selectedNodes = TransportManager.HighlightedStation?.Stops.Select(s => s.NodeId).Distinct().ToArray();
            var choices = TransportManager.TransportList.ToDictionary(t => t.Id, t => (t.ViewerVisible, t.UserDefinedColor, t.ViewerDefinedLineNo));
            var manager = new TransportManager();
            manager.LoadInitialData(Raw, Config, CurrentMapStyle);
            BetaSettings.ApplyRoutes(Raw.City ?? "", manager);
            foreach (var route in manager.TransportList)
                if (choices.TryGetValue(route.Id, out var choice))
                { route.ViewerVisible = choice.ViewerVisible; route.UserDefinedColor = choice.UserDefinedColor; route.ViewerDefinedLineNo = choice.ViewerDefinedLineNo; }
            manager.RebuildStationLayout(Config.RouteMapConfig);
            TransportManager = manager;
            transportSettings = settings + RouteVisibilitySignature(manager);
            if (selectedNodes != null)
                foreach (var node in selectedNodes) if (HighlightStopNode(node)) break;
        }
    }
    // HighlightVisible is transient. Only manual service visibility affects display membership.
    private static string RouteVisibilitySignature(TransportManager manager) => "|visibility:" + string.Join(",",
        manager.TransportList.OrderBy(route => route.Id).Select(route =>
            route.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + (route.ViewerVisible ? "1" : "0")));
    public void SaveRouteOverrides() { lock (buildLock) { BetaSettings.StoreRoutes(Raw.City ?? "", TransportManager); BetaSettings.Save(Config); } }
    public void ClearHighlight() => TransportManager.ClearHighlight();
    public bool HighlightStopNode(int nodeId)
    {
        var station = TransportManager.SelectableStations?.FirstOrDefault(s => s.Stops.Any(t => t.NodeId == nodeId));
        if (station == null) return false;
        TransportManager.HighlightStation(station); return true;
    }
    public bool HighlightGroup(int id)
    {
        var station = TransportManager.SelectableStations?.FirstOrDefault(s => s.Id == id);
        if (station == null) return false;
        TransportManager.HighlightStation(station); return true;
    }
    public bool SelectAtWorld(SKPoint world)
    {
        if (Config.Railway.InfrastructureOnly || !Config.ShowTransitLabels) return false;
        var scale = Config.CanvasSize / 17280d;
        var point = new MapPoint((world.X + 8640d) * scale, (world.Y + 8640d) * scale);
        double pad = 3 / Math.Max(0.001, appRoot.Context.ViewContext.ViewScaleFromWorld) * scale;
        var hit = StationHitTargets.Where(h => h.Contains(point, pad))
            .OrderBy(h => h.Station.Stops.Min(s =>
            {
                var p = s.PositionOnSegment ?? s.Node?.Position;
                if (p == null) return double.MaxValue;
                double dx = (p.X + 8640) * scale - point.X, dy = (-p.Z + 8640) * scale - point.Y;
                return dx * dx + dy * dy;
            })).ThenBy(h => h.Station.Id).FirstOrDefault();
        var before = SelectedGroupId;
        if (hit == null || hit.Station == TransportManager.HighlightedStation) ClearHighlight();
        else TransportManager.HighlightStation(hit.Station);
        return before != SelectedGroupId;
    }
    public IDrawCommand BuildStationCommand(ViewContext vc)
    {
        lock (buildLock)
        {
            var scale = (float)(17280d / Config.CanvasSize);
            var matrix = SKMatrix.CreateScale(scale, scale).PostConcat(SKMatrix.CreateTranslation(-8640, -8640)).PostConcat(vc.TextWorldTransform);
            var result = StationDisplayRenderer.Build(Config, CurrentMapStyle, TransportManager, matrix);
            ClearHitTargets();
            StationHitTargets = result.Hits;
            return result.Command;
        }
    }
    private void ClearHitTargets()
    {
        foreach (var hit in StationHitTargets) hit.Dispose();
        StationHitTargets = [];
    }
    public Task<ILayer> BuildLayerAsync(string layerName, LoadProgressInfo? lpi) => layerName switch
    {
        ILayer.LayerNameRoads when Config.RenderRoad => new CS1RoadLayerBuilder(appRoot, Raw).BuildAsync(lpi),
        ILayer.LayerNameBuildings when Config.RenderBuilding => new CS1BuildingLayerBuilder(appRoot, Raw).BuildAsync(lpi),
        ILayer.LayerNameRailways when Config.ShowRailInfrastructure => new CS1RailWayLayerBuilder(appRoot, Raw).BuildAsync(lpi, appRoot.Context.ViewContext),
        ILayer.LayerNameForest when Config.RenderForest => new CS1ForestLayerBuilder(appRoot, Raw).BuildAsync(lpi),
        ILayer.LayerNameStationPremises when Config.ShowStationPremises => new CS1StationPremisesLayerBuilder(appRoot, Raw).BuildAsync(lpi),
        _ => Task.FromResult<ILayer>(new BasicLayer(appRoot, layerName, CS1MapType.CS1WorldRect))
    };
}
