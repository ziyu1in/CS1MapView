using System.Security.Cryptography;
using System.IO.Compression;
using CS2MapView.Beta;
using CS2MapView.Config;
using CS2MapView.Data;
using CS2MapView.Form;
using CS2MapView.Import;
using SkiaSharp;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args[0]);
            var map = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "samples", "Springdale-20260927-124147.cslmap.gz");
            var reports = Path.Combine(root, "artifacts", "checks"); Directory.CreateDirectory(reports);
            string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
            var before = Hash(map);
            Require(before == "C9EBCB5BA1371BAE962FB536649AFD482F91BCC523B4B7471B91E01F3921F718", "Reference save changed before CS1MapView checks");
            ProjectIdentityChecks.Run();
            BetaSettings.Load(Path.Combine(root, "CSLMapViewConfig.xml"));
            BuildingPreferenceMigrationChecks.Run();
            var config = BetaSettings.Config;
            config.RenderTransportRoute = config.RenderTransport = true;
            config.RouteMapConfig.RenderTrainLine = config.RouteMapConfig.RenderMetroLine = config.RouteMapConfig.RenderMonorailLine = true;
            config.RouteMapConfig.RenderBusLine = config.RouteMapConfig.RenderTramLine = config.RouteMapConfig.RenderBlimpLine = config.RouteMapConfig.RenderFerryLine = true;
            config.RouteMapConfig.RenderTrainStation = config.RouteMapConfig.RenderMetroStation = config.RouteMapConfig.RenderMonorailStation = true;
            config.RouteMapConfig.RenderBusStop = config.RouteMapConfig.RenderTramStop = config.RouteMapConfig.RenderBlimpStop = config.RouteMapConfig.RenderFerryHarbor = true;
            config.RouteMapConfig.UseCustomTransferModes = true;
            config.RenderForest = true; config.SelectedStyle = "apple-light"; config.CanvasSize = 4096;
            if (args.Contains("--ui-only"))
            {
                using var uiHost = new BetaOutputHost(UserSettings.Default, config);
                BetaOutput.ApplyFeatureSettings(uiHost.Context.UserSettings, config);
                Require(new ExportFileImporter(uiHost).ImportAsync(map).GetAwaiter().GetResult() == null, "UI source import failed");
                FormsUiPortChecks.Run(uiHost, ((CS1MapType)uiHost.Context.SourceMapType!).GetImportData(), reports);
                return 0;
            }
            TransitPortChecks.RunPolicies();
            RoadRailPortChecks.RunPolicies();
            var settings = UserSettings.Default;
            BetaOutput.ApplyFeatureSettings(settings, config);
            using var host = new BetaOutputHost(settings, config);
            Require(new ExportFileImporter(host).ImportAsync(map).GetAwaiter().GetResult() == null, "GZip import failed");
            var source = (CS1MapType)host.Context.SourceMapType!;
            var raw = source.GetImportData();
            var session = BetaMapSession.Get(host, raw);
            FoundationPortChecks.Run(host, raw);
            var ids = raw.SegmentList.Select(s => s.Id).ToArray();
            using (session.BuildStationCommand(host.Context.ViewContext)) { }
            TransitPortChecks.RunSpringdale(session, raw, reports);
            RoadRailPortChecks.RunSpringdale(raw, reports);
            TransitPortChecks.CheckStaleLayerAnchor(host);
            FormsUiPortChecks.Run(host, raw, reports);
            Require(ids.SequenceEqual(raw.SegmentList.Select(s => s.Id)), "Base-map build mutated raw segment IDs");
            Require(!typeof(CS1MapType).Assembly.GetTypes().Any(t => t.FullName!.Contains("CS2MapDataDeserializer") || t.FullName.Contains("CS2MapDataSet")), "CS2 import dependency survived pruning");
            Require(!typeof(CS1MapType).Assembly.GetReferencedAssemblies().Any(a => a.Name is "CSLMapView" or "PresentationCore" or "PresentationFramework" or "WindowsBase"), "Legacy/WPF dependency survived native port");
            Require(Hash(typeof(CSLMapView.XML.CSLExportXML).Assembly.Location) == "5870FEBB658D13122DB380B61D765F5A5F99004D16532C7D0E898E32EEA19B62", "Published XML dependency was changed");
            var plain = Path.Combine(reports, "Springdale.cslmap");
            using (var input = new GZipStream(File.OpenRead(map), CompressionMode.Decompress))
            using (var output = File.Create(plain)) input.CopyTo(output);
            using var plainHost = new BetaOutputHost(UserSettings.Default, config);
            Require(new ExportFileImporter(plainHost).ImportAsync(plain).GetAwaiter().GetResult() == null, "Plain map import failed");
            var plainRaw = ((CS1MapType)plainHost.Context.SourceMapType!).GetImportData();
            Require(plainRaw.TransportList.Count == raw.TransportList.Count && plainRaw.SegmentList.Count == raw.SegmentList.Count, "Plain/GZip source data differs");
            Require(Hash(map) == before, "Reference save changed during beta checks");
            File.WriteAllText(Path.Combine(reports, "acceptance.txt"), $"PASS raw source, plain/GZip, {raw.TransportList.Count} complete services, station clusters/badges/visibility, deferred merger, transformed anchors. Save SHA256={before}");
            Console.WriteLine("PASS raw source, plain/GZip and CS1MapView rendering foundation");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
