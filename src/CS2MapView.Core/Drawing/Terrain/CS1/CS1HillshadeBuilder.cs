using System.Runtime.CompilerServices;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Drawing.Terrain.CS1;

/// <summary>Soft relief from the saved terrain; independent of map styling and view scale.</summary>
internal static class CS1HillshadeBuilder
{
    // Cache numerical illumination, not a disposable drawing shared across hosts.
    // A map's cached field is released with that map; panning and zooming reuse it.
    private static readonly ConditionalWeakTable<CSLExportXML, Lazy<float[]>> Fields = new();
    private const float FlatLight = 0.70710678f;

    internal static IDrawCommand? Create(CSLExportXML raw, int strengthPercent)
    {
        var strength = Math.Clamp(strengthPercent, 0, 100) / 100f;
        if (strength == 0) return null;
        var field = Fields.GetValue(raw, key => new Lazy<float[]>(() => Calculate(key))).Value;
        const int n = CSLExportXML.TERRAIN_RESOLUTION;
        using var bitmap = new SKBitmap(n, n, SKColorType.Bgra8888, SKAlphaType.Premul);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            var delta = field[y * n + x];
            var alpha = (byte)Math.Clamp((int)MathF.Round(strength * (delta < 0
                ? 130 * -delta / FlatLight : 115 * delta / (1 - FlatLight))), 0, 180);
            bitmap.SetPixel(x, n - 1 - y, delta < 0
                ? new SKColor(59, 68, 74, alpha) : new SKColor(255, 255, 255, alpha));
        }
        var rect = new SKRect(-8640, -8640, 8640, 8640);
        using var recorder = new SKPictureRecorder();
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
        recorder.BeginRecording(rect).DrawBitmap(bitmap, rect, paint);
        return new NativePictureDrawCommand(recorder.EndRecording(), rect);
    }

    private static float[] Calculate(CSLExportXML raw)
    {
        const int n = CSLExportXML.TERRAIN_RESOLUTION;
        var fine = Smooth(raw.LandArray, n, 2);
        var broad = Smooth(raw.LandArray, n, 6);
        var result = new float[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            // Suppress seabed relief and filtered shoreline fringes. Native water
            // fills are also drawn above this overlay, using their exact outlines.
            bool wet = false;
            for (int yy = Math.Max(0, y - 1); yy <= Math.Min(n - 1, y + 1); yy++)
            for (int xx = Math.Max(0, x - 1); xx <= Math.Min(n - 1, x + 1); xx++)
                wet |= raw.WaterArray[yy * n + xx] >= 0.2f;
            if (!wet)
                result[y * n + x] = 0.7f * Illuminate(fine, n, x, y)
                    + 0.3f * Illuminate(broad, n, x, y) - FlatLight;
        }
        return result;
    }

    private static float Illuminate(float[] heights, int n, int x, int y)
    {
        int xm = Math.Max(0, x - 1), xp = Math.Min(n - 1, x + 1);
        int ym = Math.Max(0, y - 1), yp = Math.Min(n - 1, y + 1);
        // Horn gradients: eight times the 16-metre sample spacing.
        float dx = (heights[ym * n + xp] + 2 * heights[y * n + xp] + heights[yp * n + xp]
            - heights[ym * n + xm] - 2 * heights[y * n + xm] - heights[yp * n + xm]) / 128f;
        float dy = (heights[yp * n + xm] + 2 * heights[yp * n + x] + heights[yp * n + xp]
            - heights[ym * n + xm] - 2 * heights[ym * n + x] - heights[ym * n + xp]) / 128f;
        float normal = MathF.Sqrt(1 + dx * dx + dy * dy);
        float Light(float lx, float ly) => MathF.Max(0, (-dx * lx - dy * ly + FlatLight) / normal);
        // Saved z increases northward; bitmap y is flipped on output. Northwest
        // dominates, with weaker west/north light to soften directional shadows.
        return 0.7f * Light(-0.5f, 0.5f) + 0.15f * Light(-FlatLight, 0) + 0.15f * Light(0, FlatLight);
    }

    private static float[] Smooth(float[] source, int n, int passes)
    {
        var a = (float[])source.Clone();
        var b = new float[a.Length];
        var c = new float[a.Length];
        int[] weights = [1, 4, 6, 4, 1];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float sum = 0;
                for (int k = -2; k <= 2; k++) sum += weights[k + 2] * a[y * n + Math.Clamp(x + k, 0, n - 1)];
                b[y * n + x] = sum / 16;
            }
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float sum = 0;
                for (int k = -2; k <= 2; k++) sum += weights[k + 2] * b[Math.Clamp(y + k, 0, n - 1) * n + x];
                c[y * n + x] = sum / 16;
            }
            (a, c) = (c, a);
        }
        return a;
    }
}
