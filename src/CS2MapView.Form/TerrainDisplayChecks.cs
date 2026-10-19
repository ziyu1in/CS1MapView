using CS2MapView.Beta;
using CS2MapView.Config;
using CS2MapView.Data;
using CS2MapView.Drawing.Layer;
using CS2MapView.Drawing.Terrain.CS1;
using CSLMapView.XML;
using SkiaSharp;
using ApplicationConfig = CS2MapView.CS1.Config.ApplicationConfig;
using CS2MapView.CS1.Config;

namespace CS2MapView.Form;

/// <summary>Exercise independent terrain controls through native screen/export rendering.</summary>
internal static class TerrainDisplayChecks
{
    internal static void Run(CSLExportXML raw, string reports)
    {
        var defaults = new ApplicationConfig();
        Require(!defaults.RenderHillshading && !defaults.DisplayContourLines && defaults.HillshadingStrength == 82,
            "Existing maps must retain flat terrain unless shading is enabled");
        var importedPath = Path.Combine(reports, "terrain-settings.xml");
        File.WriteAllText(importedPath, "<ApplicationConfig><RenderHillshading>true</RenderHillshading><HillshadingStrength>65</HillshadingStrength><DisplayContourLines>false</DisplayContourLines><ContourIntervals>Low</ContourIntervals></ApplicationConfig>");
        var imported = BetaSettings.ImportConfig(importedPath);
        var roundtrip = BetaSettings.Clone(imported);
        Require(roundtrip.RenderHillshading && roundtrip.HillshadingStrength == 65 && !roundtrip.DisplayContourLines && roundtrip.ContourIntervals == "Low",
            "Independent terrain options did not survive XML import/serialization");
        imported.HillshadingStrength = -1; Require(imported.HillshadingStrength == 0, "Negative shading strength was accepted");
        imported.HillshadingStrength = 101; Require(imported.HillshadingStrength == 100, "Excessive shading strength was accepted");
        var originalLand = (float[])raw.LandArray.Clone();
        var originalWater = (float[])raw.WaterArray.Clone();
        foreach (var detail in Enum.GetValues<ContourDetail>())
        {
            imported.ContourDetailLevel = detail;
            Require(BetaSettings.Clone(imported).ContourDetailLevel == detail, "Contour preset was not persisted");
        }
        foreach (bool vectorLand in new[] { true, false })
        foreach (bool vectorWater in new[] { true, false })
        {
            using var plain = Render(false, 82, false, vectorLand, vectorWater);
            using var zero = Render(true, 0, false, vectorLand, vectorWater);
            using var soft = Render(true, 25, false, vectorLand, vectorWater);
            using var shaded = Render(true, 82, false, vectorLand, vectorWater);
            using var contours = Render(false, 82, true, vectorLand, vectorWater);
            using var both = Render(true, 82, true, vectorLand, vectorWater);
            Require(Difference(plain, zero) == 0, "Zero shading strength changed terrain pixels");
            long softDifference = Difference(plain, soft), shadeDifference = Difference(plain, shaded);
            Require(softDifference > 10000 && shadeDifference > softDifference,
                "Shading strength did not change the rendered terrain");
            Require(Difference(plain, contours) > 10000 && Difference(shaded, both) > 10000,
                "Contour lines did not render independently with/without hillshading");
            CheckWater(plain, shaded, raw);
            CheckWater(plain, both, raw);
            if (vectorLand && vectorWater)
            {
                using var repeated = Render(true, 82, false, true, true);
                Require(Difference(shaded, repeated) == 0, "Independent output hosts changed cached relief or retained disposed drawings");
                using var rotated = Render(true, 82, false, true, true, MathF.PI / 2);
                Require(Difference(shaded, rotated) > 10000, "Hillshading ignored the rotated map view");
                Save(shaded, Path.Combine(reports, "terrain-shading.png"));
                Save(both, Path.Combine(reports, "terrain-shading-contours.png"));
                using var fine = Render(true, 82, true, true, true, detail: ContourDetail.Fine);
                using var sparse = Render(true, 82, true, true, true, detail: ContourDetail.Sparse);
                Require(Difference(fine, both) > 10000 && Difference(sparse, both) > 10000,
                    "Contour preset choices produced identical rendered maps");
                CheckWater(plain, fine, raw); CheckWater(plain, sparse, raw);
            }
        }
        Require(originalLand.SequenceEqual(raw.LandArray) && originalWater.SequenceEqual(raw.WaterArray),
            "Shading or contours changed saved terrain/water data");
        Console.WriteLine("PASS optional terrain relief: saved independent controls, strength/zero, all vector/raster combinations, water, repeated hosts, rotation and unchanged source heights");

        SKBitmap Render(bool shade, int strength, bool contours, bool vectorLand, bool vectorWater, float angle = 0, ContourDetail detail = ContourDetail.Balanced)
        {
            var config = new ApplicationConfig {
                CanvasSize = 1024, RenderHillshading = shade, HillshadingStrength = strength,
                DisplayContourLines = contours, ContourDetailLevel = detail
            };
            var settings = UserSettings.Default;
            settings.VectorizeTerrainLand = vectorLand;
            settings.VectorizeTerrainWater = vectorWater;
            using var host = new BetaOutputHost(settings, config);
            BetaOutput.SetView(host.Context.ViewContext, new SKRect(-8640, -8640, 8640, 8640), angle, 1024, 1024);
            host.Context.ViewContext.WorldRect = CS1MapType.CS1WorldRect;
            host.MapData = new MapData { WorldRect = CS1MapType.CS1WorldRect };
            host.MapData.Layers.Add(new CS1TerrainLayerBuilder(host, raw).BuildAsync(null).GetAwaiter().GetResult());
            return BetaOutput.DrawBitmap(host, 1024, 1024);
        }
    }

    private static void CheckWater(SKBitmap plain, SKBitmap shaded, CSLExportXML raw)
    {
        const int n = CSLExportXML.TERRAIN_RESOLUTION;
        int checkedPixels = 0;
        for (int y = 3; y < n - 3; y += 8)
        for (int x = 3; x < n - 3; x += 8)
        {
            bool interior = true;
            for (int yy = y - 3; yy <= y + 3; yy++)
            for (int xx = x - 3; xx <= x + 3; xx++)
                interior &= raw.WaterArray[yy * n + xx] >= 0.2f;
            if (!interior) continue;
            int px = (int)((x + 0.5f) * plain.Width / n);
            int py = (int)((n - y - 0.5f) * plain.Height / n);
            Require(plain.GetPixel(px, py) == shaded.GetPixel(px, py), "Hillshading changed an interior water pixel");
            checkedPixels++;
        }
        Require(checkedPixels > 100, "Insufficient actual water samples checked");
    }

    private static long Difference(SKBitmap a, SKBitmap b)
    {
        var x = a.GetPixelSpan(); var y = b.GetPixelSpan(); long difference = 0;
        for (int i = 0; i < x.Length; i++) difference += Math.Abs(x[i] - y[i]);
        return difference;
    }
    private static void Save(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path); data.SaveTo(stream);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
