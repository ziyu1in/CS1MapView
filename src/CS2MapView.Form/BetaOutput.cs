using CS2MapView.Beta;
using CS2MapView.Config;
using CS2MapView.Data;
using CS2MapView.Drawing.Layer;
using CS2MapView.Import;
using CS2MapView.Util;
using CS2MapView.CS1.Config;
using CSLMapView.XML;
using SkiaSharp;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Serialization;

namespace CS2MapView.Form;

/// <summary>Each preview/export owns its layers and view; no screen layer is resized for export.</summary>
internal sealed class BetaOutputHost : ICS2MapViewRoot, IDisposable
{
    public RenderContext Context { get; }
    public MapData? MapData { get; set; }
    public BetaOutputHost(UserSettings settings, ApplicationConfig features)
    {
        Context = new RenderContext(settings);
        BetaMapSession.SetConfigForHost(this, features);
    }
    public void ReceiveProgressChanged(object sender, LoadProgressEventArgs args)
    {
        if (args.Message != null) Console.WriteLine(args.Message);
    }
    public void Dispose() => MapData?.Dispose();
}

internal static class BetaOutput
{
    public static T Clone<T>(T value)
    {
        using var stream = new MemoryStream();
        var serializer = new XmlSerializer(typeof(T));
        serializer.Serialize(stream, value);
        stream.Position = 0;
        return (T)serializer.Deserialize(stream)!;
    }

    public static void ApplyFeatureSettings(UserSettings us, ApplicationConfig c)
    {
        void Layer(string name, bool visible)
        {
            var layer = us.LayerDrawingOrder?.FirstOrDefault(l => l.Name == name);
            if (layer != null) layer.Visible = visible;
        }
        Layer(ILayer.LayerNameTerrain, c.RenderTerrain);
        Layer(ILayer.LayerNameRoads, c.RenderRoad);
        Layer(ILayer.LayerNameBuildings, c.RenderBuilding);
        Layer("forest", c.RenderForest);
        Layer("stationPremises", c.ShowStationPremises);
        Layer(ILayer.LayerNameGrid, c.RenderGrid);
        Layer(ILayer.LayerNameRailways, c.RenderRail || c.Railway.InfrastructureOnly);
        Layer(ILayer.LayerNameTransportLines, (c.RenderTransport || c.RenderTransportRoute) && !c.Railway.InfrastructureOnly);
        us.RenderRailTrain = c.RenderRailTrain;
        us.RenderRailMetro = c.RenderRailMetro;
        us.RenderRailTram = c.RenderRailTram;
        us.RenderRailMonorail = c.RenderRailMonorail;
        us.RenderRailCableCar = c.RenderRailCableCar;
        us.RenderBuildingNameLabel = c.RenderBuildingName;
        us.RenderParkNameLabels = c.RenderParkName;
        us.RenderDistrictNameLabel = c.RenderDistrictName;
        us.RenderStreetNames = c.RenderRoadName;
        us.RenderMapSymbol = c.RenderMapSymbol;
        us.DisableBuildingBorder = c.DisableBuildingBorder;
        us.HideRICOBuildingsShape = c.HideRICO;
        us.CustomBuildingNameFont = c.BuildingFont;
        us.CustomDistrictNameFont = c.DistrictFont;
        us.CustomStreetNameFont = c.RoadNameFont;
        us.DefaultMapSymbolType = c.MapSymbolStyle;
        if (us.VectorizeTerrainLand != c.UseVectorTerrain)
            us.VectorizeTerrainLand = us.VectorizeTerrainWater = c.UseVectorTerrain;
        us.CS1BuildingPreferences = Clone(c.BuildingPreferences);
        var t = us.TransportRouteMapConfig;
        var r = c.RouteMapConfig;
        t.RenderBusLine = r.RenderBusLine; t.RenderBusStop = r.RenderBusStop;
        t.RenderTrainLine = r.RenderTrainLine; t.RenderTrainStop = r.RenderTrainStation;
        t.RenderMetroLine = r.RenderMetroLine; t.RenderMetroStop = r.RenderMetroStation;
        t.RenderTramLine = r.RenderTramLine; t.RenderTramStop = r.RenderTramStop;
        t.RenderMonorailLine = r.RenderMonorailLine; t.RenderMonorailStop = r.RenderMonorailStation;
        t.RenderAirplaneLine = r.RenderBlimpLine; t.RenderAirplaneStop = r.RenderBlimpStop;
        t.RenderShipLine = r.RenderFerryLine; t.RenderShipStop = r.RenderFerryHarbor;
    }

    public static void ReadFeatureSettings(UserSettings us, ApplicationConfig c)
    {
        c.RenderTerrain = us.IsLayerVisible(ILayer.LayerNameTerrain);
        c.RenderRoad = us.IsLayerVisible(ILayer.LayerNameRoads);
        c.RenderBuilding = us.IsLayerVisible(ILayer.LayerNameBuildings);
        c.RenderGrid = us.IsLayerVisible(ILayer.LayerNameGrid);
        if (!c.Railway.InfrastructureOnly) c.RenderRail = us.IsLayerVisible(ILayer.LayerNameRailways);
        if (!c.Railway.InfrastructureOnly && !us.IsLayerVisible(ILayer.LayerNameTransportLines)) c.RenderTransport = c.RenderTransportRoute = false;
        else if (!c.Railway.InfrastructureOnly && !(c.RenderTransport || c.RenderTransportRoute)) c.RenderTransport = c.RenderTransportRoute = true;
        c.RenderRailTrain = us.RenderRailTrain; c.RenderRailMetro = us.RenderRailMetro;
        c.RenderRailTram = us.RenderRailTram; c.RenderRailMonorail = us.RenderRailMonorail; c.RenderRailCableCar = us.RenderRailCableCar;
        c.RenderBuildingName = us.RenderBuildingNameLabel; c.RenderDistrictName = us.RenderDistrictNameLabel;
        c.RenderParkName = us.RenderParkNameLabels;
        c.RenderForest = us.IsLayerVisible("forest");
        c.RenderStationPremises = us.IsLayerVisible("stationPremises");
        c.RenderRoadName = us.RenderStreetNames; c.RenderMapSymbol = us.RenderMapSymbol;
        c.DisableBuildingBorder = us.DisableBuildingBorder; c.HideRICO = us.HideRICOBuildingsShape;
        var defaultFonts = new ApplicationConfig();
        c.BuildingFont = us.CustomBuildingNameFont ?? defaultFonts.BuildingFont;
        c.DistrictFont = us.CustomDistrictNameFont ?? defaultFonts.DistrictFont;
        c.RoadNameFont = us.CustomStreetNameFont ?? defaultFonts.RoadNameFont;
        c.MapSymbolStyle = us.DefaultMapSymbolType ?? c.MapSymbolStyle;
        c.UseVectorTerrain = us.VectorizeTerrainLand;
        var t = us.TransportRouteMapConfig;
        var r = c.RouteMapConfig;
        r.RenderBusLine = t.RenderBusLine; r.RenderBusStop = t.RenderBusStop;
        r.RenderTrainLine = t.RenderTrainLine; r.RenderTrainStation = t.RenderTrainStop;
        r.RenderMetroLine = t.RenderMetroLine; r.RenderMetroStation = t.RenderMetroStop;
        r.RenderTramLine = t.RenderTramLine; r.RenderTramStop = t.RenderTramStop;
        r.RenderMonorailLine = t.RenderMonorailLine; r.RenderMonorailStation = t.RenderMonorailStop;
        r.RenderBlimpLine = t.RenderAirplaneLine; r.RenderBlimpStop = t.RenderAirplaneStop;
        r.RenderFerryLine = t.RenderShipLine; r.RenderFerryHarbor = t.RenderShipStop;
    }

    public static void SetView(ViewContext vc, SKRect crop, float radians, int width, int height)
    {
        if (crop.Width <= 0 || crop.Height <= 0) throw new ArgumentException("Crop width and height must be positive.");
        var rotated = SKPathEx.GetTransformedRectBound(crop, SKMatrix.CreateRotation(radians));
        vc.Angle = radians;
        vc.ScaleFactor = Math.Min(width / rotated.Width, height / rotated.Height) / vc.WorldScaleFactor;
        var scale = vc.ViewScaleFromWorld;
        vc.ViewLeftTop = (rotated.Left - (width / scale - rotated.Width) / 2, rotated.Top - (height / scale - rotated.Height) / 2);
    }

    public static async Task<BetaOutputHost> FromScreenAsync(MainForm source, SKRect rotatedCrop, int width, int height)
    {
        if (source.Context.SourceMapType is not CS1MapType map) throw new InvalidOperationException("Load a CS1 map first.");
        var sourceSession = source.Session!;
        var features = Clone(sourceSession.Config);
        var host = new BetaOutputHost(Clone(source.Context.UserSettings), features);
        try
        {
            host.Context.ViewContext.WorldRect = map.WorldRect;
            var vc = host.Context.ViewContext;
            vc.Angle = source.Context.ViewContext.Angle;
            vc.ScaleFactor = Math.Min(width / rotatedCrop.Width, height / rotatedCrop.Height) / vc.WorldScaleFactor;
            var scale = vc.ViewScaleFromWorld;
            vc.ViewLeftTop = (rotatedCrop.Left - (width / scale - rotatedCrop.Width) / 2, rotatedCrop.Top - (height / scale - rotatedCrop.Height) / 2);
            features.CanvasSize = 17280d * scale;
            BetaMapSession.SetConfigForHost(host, features);
            var independentMap = new CS1MapType(map.GetImportData());
            host.Context.SourceMapType = independentMap;
            var session = BetaMapSession.Get(host, independentMap.GetImportData());
            foreach (var route in session.TransportManager.TransportList)
            {
                var choice = sourceSession.TransportManager.TransportList.FirstOrDefault(r => r.Id == route.Id);
                if (choice == null) continue;
                route.ViewerVisible = choice.ViewerVisible;
                route.UserDefinedColor = choice.UserDefinedColor;
                route.ViewerDefinedLineNo = choice.ViewerDefinedLineNo;
            }
            session.TransportManager.RebuildStationLayout(features.RouteMapConfig);
            if (sourceSession.SelectedGroupId.HasValue) session.HighlightGroup(sourceSession.SelectedGroupId.Value);
            host.MapData = await independentMap.BuildAll(host, null);
            host.MapData.FileName = source.MapData?.FileName;
            return host;
        }
        catch { host.Dispose(); throw; }
    }

    public static SKBitmap DrawBitmap(ICS2MapViewRoot host, int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        host.Context.DrawLayers(host.MapData!, canvas, new SKRect(0, 0, width, height));
        return bitmap;
    }

    public static void Save(ICS2MapViewRoot host, string path, int width, int height)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (extension is ".svg" or ".svgz")
        {
            using var stream = new SKDynamicMemoryWStream();
            using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, width, height), stream))
                host.Context.DrawLayers(host.MapData!, canvas, new SKRect(0, 0, width, height));
            using var svg = stream.DetachAsData();
            using var file = File.Create(path);
            if (extension == ".svgz") { using var gzip = new GZipStream(file, CompressionLevel.Optimal); svg.SaveTo(gzip); }
            else svg.SaveTo(file);
        }
        else
        {
            using var bitmap = DrawBitmap(host, width, height);
            using var file = File.Create(path);
            if (extension == ".bmp")
            {
                using var image = ToWinBitmap(bitmap);
                image.Save(file, System.Drawing.Imaging.ImageFormat.Bmp);
            }
            else
            {
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(extension switch
                {
                    ".png" => SKEncodedImageFormat.Png,
                    ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                    _ => throw new ArgumentException("Output must be PNG, JPEG, BMP, SVG or SVGZ.")
                }, extension == ".png" ? 100 : 95);
                encoded.SaveTo(file);
            }
        }
    }

    public static unsafe Bitmap ToWinBitmap(SKBitmap bitmap)
    {
        var result = new Bitmap(bitmap.Width, bitmap.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var pixels = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, result.PixelFormat);
        try
        {
            for (var y = 0; y < bitmap.Height; y++)
                new ReadOnlySpan<byte>((byte*)bitmap.GetPixels().ToPointer() + y * bitmap.RowBytes, bitmap.Width * 4)
                    .CopyTo(new Span<byte>((byte*)pixels.Scan0.ToPointer() + y * pixels.Stride, bitmap.Width * 4));
        }
        finally { result.UnlockBits(pixels); }
        return result;
    }

    public static void SaveHeightmap(CSLExportXML raw, string output)
    {
        var n = CSLExportXML.TERRAIN_RESOLUTION;
        if (!output.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Heightmaps require .png output.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        using var pixels = new MemoryStream();
        using (var compressed = new ZLibStream(pixels, CompressionLevel.Optimal, true))
        {
        for (var y = 0; y < n; y++)
        {
            compressed.WriteByte(0); // PNG filter: none; preserve the original CS1 1/64 m samples.
            for (var x = 0; x < n; x++)
            {
                var gray = (ushort)Math.Clamp(raw.LandArray[(n - y - 1) * n + x] * 64f, 0, 65535);
                compressed.WriteByte((byte)(gray >> 8)); compressed.WriteByte((byte)gray);
            }
        }
        }
        using var file = File.Create(output);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), n);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), n);
        header[8] = 16; // 16-bit grayscale, matching the current viewer's heightmap contract.
        WritePngChunk(file, "IHDR", header);
        WritePngChunk(file, "IDAT", pixels.ToArray());
        WritePngChunk(file, "IEND", []);
    }

    private static void WritePngChunk(Stream output, string name, byte[] bytes)
    {
        Span<byte> integer = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(integer, bytes.Length); output.Write(integer);
        var type = System.Text.Encoding.ASCII.GetBytes(name); output.Write(type); output.Write(bytes);
        uint crc = uint.MaxValue;
        foreach (var value in type.Concat(bytes))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(integer, ~crc); output.Write(integer);
    }
}

internal static class BetaRenderCommand
{
    public const string Usage = ProjectInfo.Name + " --render map.cslmap.gz --output map.png [--size 8192] [--style apple-light] [--config config.xml] [--transit] [--crop x,y,width,height] [--rotate degrees] [--highlight stop-node-id]\n" + ProjectInfo.Name + " --heightmap map.cslmap.gz --output heights.png\nCrop coordinates are game metres: X east, Y=-Z south. Highlight accepts an original physical stop node ID.";
    public static async Task<int> RunAsync(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            if (name == "--transit") { options[name] = "true"; continue; }
            if (!new[] { "--render", "--heightmap", "--output", "--size", "--style", "--config", "--crop", "--rotate", "--highlight" }.Contains(name))
                throw new ArgumentException($"Unknown option: {name}");
            if (++i >= args.Length) throw new ArgumentException($"Missing value for {name}");
            options[name] = args[i];
        }
        if (!options.TryGetValue("--output", out var output)) throw new ArgumentException("--output is required.");
        var input = options.GetValueOrDefault("--render") ?? options.GetValueOrDefault("--heightmap") ?? throw new ArgumentException("--render is required.");
        BetaSettings.Load(options.GetValueOrDefault("--config"));
        var features = BetaOutput.Clone(BetaSettings.Config);
        features.SelectedStyle = options.GetValueOrDefault("--style", "apple-light");
        var size = int.Parse(options.GetValueOrDefault("--size", "8192"), CultureInfo.InvariantCulture);
        if (size < 32 || size > 32768) throw new ArgumentOutOfRangeException("--size", "Image size must be 32..32768.");
        features.CanvasSize = size;
        if (options.ContainsKey("--transit"))
        {
            features.RenderTransport = features.RenderTransportRoute = true;
            foreach (var p in features.RouteMapConfig.GetType().GetProperties().Where(p => p.Name.StartsWith("Render") && p.PropertyType == typeof(bool))) p.SetValue(features.RouteMapConfig, true);
        }
        var us = UserSettings.LoadOrDefault(false);
        BetaOutput.ApplyFeatureSettings(us, features);
        using var host = new BetaOutputHost(us, features);
        var vc = host.Context.ViewContext;
        vc.WorldRect = CS1MapType.CS1WorldRect;
        var crop = new SKRect(vc.WorldRect.Left, vc.WorldRect.Top, vc.WorldRect.Right, vc.WorldRect.Bottom);
        if (options.TryGetValue("--crop", out var cropText))
        {
            var values = cropText.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (values.Length != 4 || values.Any(v => !float.IsFinite(v))) throw new ArgumentException("Crop must be x,y,width,height in game metres.");
            crop = SKRect.Create(values[0], values[1], values[2], values[3]);
        }
        var angle = float.Parse(options.GetValueOrDefault("--rotate", "0"), CultureInfo.InvariantCulture) * (float)Math.PI / 180f;
        if (!float.IsFinite(angle)) throw new ArgumentException("Rotation must be finite.");
        var bound = SKPathEx.GetTransformedRectBound(crop, SKMatrix.CreateRotation(angle));
        var width = Math.Max(1, (int)Math.Round(size * Math.Min(1, bound.Width / bound.Height)));
        var height = Math.Max(1, (int)Math.Round(size * Math.Min(1, bound.Height / bound.Width)));
        BetaOutput.SetView(vc, crop, angle, width, height);
        features.CanvasSize = 17280d * vc.ViewScaleFromWorld;
        BetaMapSession.SetConfigForHost(host, features);
        var error = await new ExportFileImporter(host).ImportAsync(input);
        if (error != null) throw new InvalidOperationException(error);
        if (host.Context.SourceMapType is not CS1MapType source) throw new InvalidOperationException("CS1 input is required.");
        var session = BetaMapSession.Get(host, source.GetImportData());
        if (options.TryGetValue("--highlight", out var node))
        {
            if (!session.HighlightStopNode(int.Parse(node, CultureInfo.InvariantCulture))) throw new ArgumentException($"No selectable station contains stop node {node}.");
            await host.MapData!.RebuildLayersOnResizeOrRotate(vc, null);
        }
        if (options.ContainsKey("--heightmap")) BetaOutput.SaveHeightmap(source.GetImportData(), output);
        else BetaOutput.Save(host, output, width, height);
        Console.WriteLine($"Saved {Path.GetFullPath(output)} ({width} x {height}).");
        return 0;
    }
}
