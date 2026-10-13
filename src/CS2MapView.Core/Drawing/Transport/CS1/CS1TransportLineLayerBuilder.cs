// Adapted from Gansaku's active Drawing/Transport/CS2/CS2TransportLineLayerBuilder.cs
// at ecba18732777acb3de18a8378f06ab5047bc0e23 (MIT; see LICENSE.upstream).
// This replaces the inactive experimental CS1 transport builder. The inherited
// rotated-layer lifecycle, asynchronous rebuilds, path commands and paint cache
// remain active; its game input is now the preserved CS1 transit model.
using CS2MapView.Beta;
using CS2MapView.Data;
using CS2MapView.Drawing.Layer;
using CS2MapView.CS1.Config;
using CS2MapView.CS1.Transit;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Drawing.Transport.CS1;

public sealed class CS1TransportLineLayerBuilder : IRebuildOnResizeLayerBuilder, IRebuildOnRotateLayerBuilder
{
    public LoadProgressInfo.Process ProcessType => LoadProgressInfo.Process.BuildTransportLines;

    private ICS2MapViewRoot AppRoot { get; }
    private CSLExportXML ImportData { get; }
    private CS1TransportRotatedLayer ResultLayer { get; }
    private int RequestedVersion;

    public CS1TransportLineLayerBuilder(ICS2MapViewRoot appRoot, CSLExportXML importData)
    {
        AppRoot = appRoot;
        ImportData = importData;
        ResultLayer = new CS1TransportRotatedLayer(appRoot,
            appRoot.Context.ViewContext.TextWorldRect, this);
    }

    public Task<ILayer> BuildAsync(LoadProgressInfo? loadProgressInfo)
        => RebuildAsync(loadProgressInfo, AppRoot.Context.ViewContext.Clone());

    public Task<ILayer> BuildAsync(LoadProgressInfo? loadProgressInfo, ViewContext vc)
        => RebuildAsync(loadProgressInfo, vc.Clone());

    public Task ResizeAsync(LoadProgressInfo? loadProgressInfo, ViewContext vc)
        => RebuildAsync(loadProgressInfo, vc.Clone());

    public Task RotateAsync(LoadProgressInfo? loadProgressInfo, ViewContext vc)
        => ResizeAsync(loadProgressInfo, vc);

    private Task<ILayer> RebuildAsync(LoadProgressInfo? loadProgressInfo, ViewContext vc)
    {
        int version = Interlocked.Increment(ref RequestedVersion);
        return Task.Run<ILayer>(() =>
        {
            lock (ResultLayer.DrawObjectsLock)
            {
                // A queued obsolete resize must not overwrite the newest view.
                if (version != Volatile.Read(ref RequestedVersion)) return ResultLayer;
                ResultLayer.ClearAndResizeCommandsList(vc.TextWorldRect, true);
                Build(vc);
                ResultLayer.SetBuiltGeometryTransform(vc.TextWorldTransform);
                loadProgressInfo?.Progress(this, ProcessType, 1f, null);
                return ResultLayer;
            }
        });
    }

    private void Build(ViewContext vc)
    {
        var session = BetaMapSession.Get(AppRoot, ImportData);
        session.RefreshTransport();
        var config = session.Config;
        var manager = session.TransportManager;
        if ((!config.ShowTransitRoutes && !config.ShowTransitLabels) || manager is null)
        {
            // Clear targets together with the hidden drawing, including a plain
            // route-layer toggle as well as infrastructure-only mode.
            using var hiddenStations = session.BuildStationCommand(vc);
            return;
        }

        var ts = config.RouteMapConfig;
        // Explicit CS1 mode dispatch: Airplane/Ship are the exporter names for
        // the supported blimp/ferry services. Trolleybus shares the bus switches.
        foreach (var route in manager.TransportList)
            route.IsLineRenderingTarget = config.ShowTransitRoutes && IsDrawingTargetLine(route.TransportType, ts) && route.ViewerVisible;

        double canvasSize = config.CanvasSize;
        double renderScale = canvasSize / 4096d;
        float canvasToRotatedWorld = (float)(17280d / canvasSize * vc.ViewScaleFromWorld);
        foreach (var route in manager.TransportList.Where(t => t.IsLineRenderingTarget && t.EffectiveVisible).Reverse())
        {
            var color = (ts.AutoColoring ? route.AutoColor : route.UserDefinedColor)
                ?? SKColors.Black;
            var skColor = color;
            // Ordered path-unit reconstruction happens in the CS1 TransportManager,
            // before base-map simplification. Do not smooth or merge these points.
            var parts = route.Stops.Where(s => s.Node is not null)
                .SelectMany(s => s.PointsToNextStop).ToList();
            foreach (var part in parts)
            {
                if (part.Points.Count < 2 || part.RefSegment is null) continue;
                part.GetLaneAndPosition(manager.TransportList, route.Id, out int lanes, out int position);
                var path = new SKPath();
                path.MoveTo(ToRotatedWorld(part.Points[0], vc));
                foreach (var point in part.Points.Skip(1)) path.LineTo(ToRotatedWorld(point, vc));
                float width = (float)TransportMapBuilder.GetLinePenWidth(lanes, position,
                    ts.WidenOnSharedLines, renderScale) * canvasToRotatedWorld;
                ResultLayer.DrawCommands.Add(new PathDrawCommand
                {
                    Path = path,
                    StrokePaintFunc = (_, _) => new SKPaintCache.StrokeKey(width, skColor, Theme.StrokeType.Round)
                });
            }
            if (!ts.MarkOneWayRoutes) continue;
            foreach (var arrow in TransportMapBuilder.RouteArrows(parts, route.Id))
            {
                var path = ArrowPath(arrow.Position, arrow.Angle, renderScale, canvasSize, vc);
                ResultLayer.DrawCommands.Add(new PathDrawCommand { Path = path, FillColor = skColor });
            }
        }

        // These custom markers retain their own layout and local transfer-group
        // ownership. The upstream general label arranger never moves them.
        var stations = session.BuildStationCommand(vc);
        if (config.ShowTransitLabels) ResultLayer.DrawCommands.Add(stations);
        else stations.Dispose();
    }

    internal static bool IsDrawingTargetLine(string mode, TransportRouteMapConfig settings) => mode switch
    {
        "Bus" or "Trolleybus" => settings.RenderBusLine,
        "Tram" => settings.RenderTramLine,
        "Metro" => settings.RenderMetroLine,
        "Train" => settings.RenderTrainLine,
        "Monorail" => settings.RenderMonorailLine,
        "Airplane" => settings.RenderBlimpLine,
        "Ship" => settings.RenderFerryLine,
        _ => false
    };

    private static SKPoint ToRotatedWorld(XMLVector3 point, ViewContext vc)
        => vc.TextWorldTransform.MapPoint(new SKPoint(point.X, -point.Z));

    private static SKPath ArrowPath(XMLVector3 position, double angle, double renderScale,
        double canvasSize, ViewContext vc)
    {
        // Same triangle and fixed-plus-scaled sizing as the current CS1 renderer,
        // expressed in game metres before the inherited view rotation and scale.
        double size = TransportMapBuilder.GetLinePenWidth(1, 0, false, 1) * 2.3
            * renderScale * 17280d / canvasSize;
        double c = Math.Cos(angle), s = Math.Sin(angle);
        SKPoint Vertex(double x, double y) => vc.TextWorldTransform.MapPoint(new SKPoint(
            (float)(position.X + x * c - y * s), (float)(-position.Z + x * s + y * c)));
        var path = new SKPath();
        path.MoveTo(Vertex(-size / 2, -size * .4));
        path.LineTo(Vertex(-size / 2, size * .4));
        path.LineTo(Vertex(size / 2, 0));
        path.Close();
        return path;
    }
}
