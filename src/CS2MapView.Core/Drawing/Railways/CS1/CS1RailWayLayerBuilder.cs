using CS2MapView.Beta;
using CS2MapView.CS1.Config;
using CS2MapView.Data;
using CS2MapView.Drawing.Layer;
using CS2MapView.Theme;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Drawing.Railways.CS1;

/// <summary>
/// Upstream native CS1 railway layer lifecycle extended with project-authored
/// physical lane offsets, constrained reconnection and Apple/Google styling.
/// Geometry remains in game metres until the upstream view transform.
/// </summary>
public class CS1RailWayLayerBuilder : IRebuildOnResizeLayerBuilder
{
    private readonly ICS2MapViewRoot root;
    private readonly CSLExportXML raw;
    private readonly RebuildableLayer layer;
    public RailJunctionReport LastJoinReport { get; private set; } = new();
    public LoadProgressInfo.Process ProcessType => LoadProgressInfo.Process.BuildRailways;
    public CS1RailWayLayerBuilder(ICS2MapViewRoot root, CSLExportXML raw)
    {
        this.root = root; this.raw = raw;
        layer = new RebuildableLayer(root, ILayer.LayerNameRailways, CS1MapType.CS1WorldRect, this);
    }
    public List<RailTrackPath> BuildTrackPaths(CSLExportXML? source = null)
    {
        var geometry = new CS1RailTrackGeometry(source ?? raw, BetaMapSession.Get(root, raw).Config);
        var tracks = geometry.BuildTrackPaths(source ?? raw); LastJoinReport = geometry.LastJoinReport; return tracks;
    }
    public Task<ILayer> BuildAsync(LoadProgressInfo? progress, ViewContext vc) => Task.Run<ILayer>(() =>
    {
        lock (layer.DrawObjectsLock) { Build(); progress?.Progress(this, ProcessType, 1f, null); return layer; }
    });
    public Task ResizeAsync(LoadProgressInfo? progress, ViewContext vc) => Task.Run(() =>
    {
        lock (layer.DrawObjectsLock)
        {
            layer.DrawCommands.ClearContent(true); Build(); progress?.Progress(this, ProcessType, 1f, null);
        }
    });
    private void Build()
    {
        var cfg = BetaMapSession.Get(root, raw).Config;
        if (!cfg.ShowRailInfrastructure) return;
        bool google = cfg.Railway.Appearance == RailwayAppearance.Google;
        double renderScale = cfg.CanvasSize / 4096d, toWorld = 17280d / cfg.CanvasSize;
        foreach (var track in BuildTrackPaths())
        {
            var path = new SKPath();
            path.MoveTo((float)track.Points[0].X, (float)track.Points[0].Y);
            foreach (var p in track.Points.Skip(1)) path.LineTo((float)p.X, (float)p.Y);
            var color = TrackColor(track.Mode, track.Tunnel, cfg.Railway.ColorByType, google);
            float width = (float)(Math.Max(0.35, (track.Tunnel || google ? 0.42 : 0.50) * renderScale) * toWorld);
            float[]? dash = track.Tunnel ? [3, 3] : null;
            layer.DrawCommands.Add(new PathDrawCommand { Path = path,
                StrokePaintFunc = (_, _) => new SKPaintCache.StrokeKey(width, color, StrokeType.Round, dash) });
            if (google && !track.Tunnel && track.Mode is WayType.Rail or WayType.Metro or WayType.Tram)
            {
                var ties = BuildTies(track, Math.Max(1.0, 3.2 * renderScale) * toWorld);
                float tieWidth = (float)(Math.Max(0.3, 0.28 * renderScale) * toWorld);
                layer.DrawCommands.Add(new PathDrawCommand { Path = ties,
                    StrokePaintFunc = (_, _) => new SKPaintCache.StrokeKey(tieWidth, color, StrokeType.Butt) });
            }
        }
    }
    private static SKPath BuildTies(RailTrackPath track, double span)
    {
        var result = new SKPath(); double next = span / 2;
        var p = track.Points;
        for (int i = 1; i < p.Count; i++)
        {
            var direction = p[i] - p[i - 1]; double distance = direction.Length;
            if (distance <= 0) continue;
            direction.Normalize();
            var normal = new CS2MapView.CS1.Geometry.MapVector(-direction.Y, direction.X) * (track.TieLength / 2);
            while (next <= distance)
            {
                var center = p[i - 1] + direction * next;
                var a = center - normal; var b = center + normal;
                result.MoveTo((float)a.X, (float)a.Y); result.LineTo((float)b.X, (float)b.Y); next += span;
            }
            next -= distance;
        }
        return result;
    }
    public static SKColor TrackColor(WayType mode, bool tunnel, bool colorByType, bool google)
    {
        if (!colorByType || mode == WayType.Rail) return tunnel ? new SKColor(184, 190, 196)
            : google ? new SKColor(139, 149, 156) : new SKColor(161, 166, 171);
        if (mode == WayType.Metro) return tunnel ? new SKColor(185, 186, 218)
            : google ? new SKColor(141, 142, 189) : new SKColor(159, 160, 209);
        if (mode == WayType.Tram) return tunnel ? new SKColor(173, 197, 171)
            : google ? new SKColor(122, 159, 120) : new SKColor(140, 177, 137);
        var color = mode switch
        {
            WayType.Monorail => new SKColor(203, 144, 84),
            WayType.CableCar => new SKColor(215, 131, 137),
            _ => new SKColor(161, 166, 171)
        };
        return color.WithAlpha(tunnel ? (byte)160 : (byte)255);
    }
}
