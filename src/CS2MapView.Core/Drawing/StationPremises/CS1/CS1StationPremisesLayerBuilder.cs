using CS2MapView.Beta;
using CS2MapView.CS1.Config;
using CS2MapView.CS1.Geometry;
using CS2MapView.Data;
using CS2MapView.Drawing.Layer;
using CS2MapView.Theme;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Drawing.StationPremises.CS1;

public sealed class CS1StationPremisesLayerBuilder(ICS2MapViewRoot root, CSLExportXML raw)
{
    public Task<ILayer> BuildAsync(LoadProgressInfo? progress) => Task.Run<ILayer>(() =>
    {
        var layer = new BasicLayer(root, "stationPremises", CS1MapType.CS1WorldRect);
        var session = BetaMapSession.Get(root, raw);
        if (!session.Config.ShowStationPremises) return layer;
        var areas = session.StationPremises;
        var outline = session.Config.FrameStopTracks ? areas.Geometry : areas.WithoutStopTracks;
        using var platforms = NativeGeometry.Union(new[] { new SKPath(areas.Platforms),
            new SKPath(session.Config.FrameStopTracks ? areas.UndergroundMetroStations : areas.UndergroundMetroStationsWithoutStopTracks) });
        var light = outline.Op(platforms, SKPathOp.Difference) ?? throw new InvalidOperationException("Unable to subtract station platforms.");
        bool blue = session.Config.StationPremisesColor == StationAreaColor.Blue;
        var color = blue ? new SKColor(79, 125, 235) : new SKColor(218, 66, 74);
        var edge = blue ? new SKColor(66, 111, 215) : new SKColor(205, 58, 66);
        layer.DrawCommands.Add(new PathDrawCommand { Path = light, FillColor = color.WithAlpha(32) });
        layer.DrawCommands.Add(new PathDrawCommand { Path = new SKPath(platforms), FillColor = color.WithAlpha(85) });
        double canvas = session.Config.CanvasSize;
        float width = (float)(Math.Max(0.65, 1.1 * canvas / 17280d) * 17280d / canvas);
        layer.DrawCommands.Add(new PathDrawCommand { Path = new SKPath(outline),
            StrokePaintFunc = (_, _) => new SKPaintCache.StrokeKey(width, edge.WithAlpha(160), StrokeType.Round) });
        return layer;
    });
}
