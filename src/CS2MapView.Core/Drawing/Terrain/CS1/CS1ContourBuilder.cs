using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CS2MapView.CS1.Config;
using CS2MapView.Theme;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Drawing.Terrain.CS1;

/// <summary>Interpolated, smoothly joined elevation contours with three display presets.</summary>
internal static class CS1ContourBuilder
{
    internal sealed record Preset(int Passes, int Interval, float MinimumLength,
        SKColor MinorColor, SKColor MajorColor, float MinorWidth, float MajorWidth);
    internal sealed record Contour(float Height, bool Closed, SKPoint[] Points);
    private static readonly ConditionalWeakTable<CSLExportXML, ConcurrentDictionary<ContourDetail, Lazy<Contour[]>>> Cache = new();

    internal static Preset GetPreset(ContourDetail detail) => detail switch {
        ContourDetail.Fine => new(1, 25, 64, SKColor.Parse("#A2A18D"), SKColor.Parse("#858572"), .65f, 1.15f),
        ContourDetail.Sparse => new(6, 50, 160, SKColor.Parse("#9B9E94"), SKColor.Parse("#70756D"), .90f, 1.55f),
        _ => new(3, 25, 96, SKColor.Parse("#9B9282"), SKColor.Parse("#776D5E"), .80f, 1.45f)
    };

    internal static Contour[] GetContours(CSLExportXML raw, ContourDetail detail) => Cache.GetOrCreateValue(raw)
        .GetOrAdd(detail, key => new Lazy<Contour[]>(() => Calculate(raw, GetPreset(key)))).Value;

    internal static IEnumerable<IDrawCommand> Create(CSLExportXML raw, ContourDetail detail)
    {
        var preset = GetPreset(detail);
        StrokeStyleWithColor Stroke(SKColor color, float pixels) => new(SerializableColor.FromSKColor(color),
            new Width(pixels, 0, pixels, pixels, 0)) { StrokeType = StrokeType.Round };
        var minor = Stroke(preset.MinorColor, preset.MinorWidth);
        var major = Stroke(preset.MajorColor, preset.MajorWidth);
        // Native commands own their paths; only immutable numerical geometry is cached.
        foreach (var contour in GetContours(raw, detail).OrderBy(c => c.Height % 100 == 0))
        {
            var stroke = contour.Height % 100 == 0 ? major : minor;
            yield return new PathDrawCommand { Path = CreatePath(contour), StrokePaintFunc = stroke.ToCacheKey };
        }
    }

    internal static SKPath CreatePath(Contour contour)
    {
        var points = contour.Points;
        int count = points.Length;
        if (contour.Closed && count > 1 && points[0] == points[^1]) count--;
        var path = new SKPath();
        if (count < 2) return path;
        static SKPoint Mid(SKPoint a, SKPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        if (contour.Closed)
        {
            path.MoveTo(Mid(points[count - 1], points[0]));
            for (int i = 0; i < count; i++) path.QuadTo(points[i], Mid(points[i], points[(i + 1) % count]));
            path.Close();
        }
        else
        {
            path.MoveTo(points[0]);
            path.LineTo(Mid(points[0], points[1]));
            for (int i = 1; i < count - 1; i++) path.QuadTo(points[i], Mid(points[i], points[i + 1]));
            path.LineTo(points[count - 1]);
        }
        return path;
    }

    private static Contour[] Calculate(CSLExportXML raw, Preset preset)
    {
        const int n = CSLExportXML.TERRAIN_RESOLUTION;
        var heights = Smooth(raw.LandArray, n, preset.Passes);
        for (int i = 0; i < heights.Length; i++) heights[i] -= raw.SeaLevel;
        var wet = raw.WaterArray.Select(depth => depth >= .2f).ToArray();
        // A two-cell buffer keeps open contour ends clear of the native shore.
        for (int pass = 0; pass < 2; pass++)
        {
            var expanded = new bool[wet.Length];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                for (int yy = Math.Max(0, y - 1); yy <= Math.Min(n - 1, y + 1); yy++)
                for (int xx = Math.Max(0, x - 1); xx <= Math.Min(n - 1, x + 1); xx++)
                    expanded[y * n + x] |= wet[yy * n + xx];
            wet = expanded;
        }
        var result = new List<Contour>();
        var minimum = heights.Min();
        var maximum = heights.Max();
        // Retain 25 m detail below 500 m for A/B, then use 50 m; C uses 50 m throughout.
        float level = MathF.Floor(minimum / preset.Interval) * preset.Interval;
        for (; level <= maximum; level += level < 500 ? preset.Interval : 50)
        foreach (var contour in TraceLevel(heights, wet, n, level, 16, new SKPoint(-8640, 8640)))
        {
            float length = 0;
            for (int i = 1; i < contour.Points.Length; i++)
                length += SKPoint.Distance(contour.Points[i - 1], contour.Points[i]);
            if (contour.Points.Length >= 4 && length >= (level % 100 == 0 ? 64 : preset.MinimumLength))
                result.Add(contour);
        }
        return result.ToArray();
    }

    /// <summary>Marching squares with shared edge identities and an asymptotic saddle decision.</summary>
    internal static IEnumerable<Contour> TraceLevel(float[] heights, bool[] wet, int n, float level, float spacing, SKPoint origin)
    {
        var points = new Dictionary<int, SKPoint>();
        var edges = new List<(int A, int B)>();
        var adjacency = new Dictionary<int, List<int>>();
        int horizontalCount = n * (n - 1);
        int Edge(int x, int y, int edge)
        {
            bool horizontal = edge is 0 or 2;
            int xx = x + (edge == 1 ? 1 : 0), yy = y + (edge == 2 ? 1 : 0);
            int key = horizontal ? yy * (n - 1) + xx : horizontalCount + yy * n + xx;
            if (!points.ContainsKey(key))
            {
                float a = heights[yy * n + xx];
                float b = heights[(yy + (horizontal ? 0 : 1)) * n + xx + (horizontal ? 1 : 0)];
                float t = Math.Clamp((level - a) / (b - a), 0, 1);
                points.Add(key, new SKPoint(origin.X + (xx + (horizontal ? t : 0)) * spacing,
                    origin.Y - (yy + (horizontal ? 0 : t)) * spacing));
            }
            return key;
        }
        void Connect(int x, int y, int a, int b)
        {
            int start = Edge(x, y, a), end = Edge(x, y, b), id = edges.Count;
            edges.Add((start, end));
            if (!adjacency.TryGetValue(start, out var from)) adjacency[start] = from = [];
            if (!adjacency.TryGetValue(end, out var to)) adjacency[end] = to = [];
            from.Add(id); to.Add(id);
        }
        for (int y = 0; y < n - 1; y++)
        for (int x = 0; x < n - 1; x++)
        {
            int i = y * n + x;
            if (wet[i] || wet[i + 1] || wet[i + n] || wet[i + n + 1]) continue;
            float a = heights[i], b = heights[i + 1], c = heights[i + n + 1], d = heights[i + n];
            int code = (a >= level ? 1 : 0) | (b >= level ? 2 : 0) | (c >= level ? 4 : 0) | (d >= level ? 8 : 0);
            switch (code)
            {
                case 1: case 14: Connect(x, y, 3, 0); break;
                case 2: case 13: Connect(x, y, 0, 1); break;
                case 3: case 12: Connect(x, y, 3, 1); break;
                case 4: case 11: Connect(x, y, 1, 2); break;
                case 6: case 9: Connect(x, y, 0, 2); break;
                case 7: case 8: Connect(x, y, 3, 2); break;
                case 5: case 10:
                    double determinant = ((double)a - level) * (c - level) - ((double)b - level) * (d - level);
                    if (determinant >= 0) { Connect(x, y, 0, 1); Connect(x, y, 2, 3); }
                    else { Connect(x, y, 3, 0); Connect(x, y, 1, 2); }
                    break;
            }
        }
        var visited = new bool[edges.Count];
        // Open ends first, then stable IDs for the remaining closed loops.
        foreach (var start in adjacency.Keys.OrderBy(k => adjacency[k].Count == 1 ? 0 : 1).ThenBy(k => k))
        {
            if (adjacency[start].All(id => visited[id])) continue;
            int here = start;
            bool closed = false;
            var chain = new List<SKPoint> { points[start] };
            while (true)
            {
                int edge = adjacency[here].FirstOrDefault(id => !visited[id], -1);
                if (edge < 0) break;
                visited[edge] = true;
                here = edges[edge].A == here ? edges[edge].B : edges[edge].A;
                var point = points[here];
                if (SKPoint.Distance(chain[^1], point) > .01f) chain.Add(point);
                if (here == start) { closed = true; break; }
            }
            if (chain.Count >= 2) yield return new Contour(level, closed, chain.ToArray());
        }
    }

    private static float[] Smooth(float[] source, int n, int passes)
    {
        var a = (float[])source.Clone(); var b = new float[a.Length]; var c = new float[a.Length];
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
