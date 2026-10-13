using CS2MapView.Beta;
using CS2MapView.Data;
using CS2MapView.Drawing.Layer;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Drawing.Forest.CS1;

/// <summary>Native forest overlay from the exporter's 512-square byte density field.</summary>
public sealed class CS1ForestLayerBuilder(ICS2MapViewRoot root, CSLExportXML raw)
{
    public const int Resolution = 512;
    public Task<ILayer> BuildAsync(LoadProgressInfo? progress) => Task.Run<ILayer>(() =>
    {
        var layer = new BasicLayer(root, ILayer.LayerNameForest, CS1MapType.CS1WorldRect);
        var session = BetaMapSession.Get(root, raw);
        if (!session.Config.RenderForest || raw.ForestByteData is not { Length: > 0 } density) return layer;
        if (density.Length != Resolution * Resolution) throw new InvalidDataException("Invalid CS1 forest density resolution.");
        var color = session.CurrentMapStyle.Colors.ForestBase;
        // Filtered images require premultiplied colour channels. SetPixel performs
        // the conversion so transparent samples cannot introduce dark fringes.
        using var bitmap = new SKBitmap(new SKImageInfo(Resolution, Resolution, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (int i = 0; i < density.Length; i++)
            bitmap.SetPixel(i % Resolution, Resolution - 1 - i / Resolution, color.WithAlpha(density[i]));
        var rect = new SKRect(-8640, -8640, 8640, 8640);
        using var recorder = new SKPictureRecorder();
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium };
        var canvas = recorder.BeginRecording(rect);
        canvas.DrawBitmap(bitmap, rect, paint);
        layer.DrawCommands.Add(new NativePictureDrawCommand(recorder.EndRecording(), rect));
        return layer;
    });
}
