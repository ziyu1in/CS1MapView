using System.Xml.Linq;
using System.Xml.Serialization;
using CS2MapView.Beta;
using CS2MapView.Config;
using CS2MapView.Data;
using CS2MapView.Drawing;
using CS2MapView.Drawing.Labels;
using CS2MapView.Drawing.Labels.CS1;
using CS2MapView.Form;
using CSLMapView.XML;

internal static class BuildingPreferenceMigrationChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Run()
    {
        var source = Path.GetTempFileName(); var native = Path.GetTempFileName();
        try
        {
            var legacyKeys = ("ja.castle ja.cemetery ja.powerStation ja.fireStation ja.highSchool ja.highTower ja.historicSite ja.hospital "
                + "ja.meteorologicalAgency ja.policeStation ja.port ja.radioTower ja.school ja.university ja.windTurbine ja.cityHall "
                + "ja.localGovernment ja.shintoShrine ja.lighthouse ja.buddhistTemple ja.onsen ja.ferry ja.postOffice "
                + "maki.castle maki.cemetery maki.college maki.hospital maki.harbor maki.fireStation maki.police maki.prison maki.school "
                + "maki.airport maki.doctor maki.ferry maki.townHall maki.recycling maki.post maki.parking maki.park maki.bus maki.car "
                + "maki.rail maki.railLight maki.railMetro maki.aerialway maki.lighthouse maki.monument maki.heliport maki.attraction maki.danger").Split(' ');
            var preferences = new XElement("BuildingPreferences",
                new XElement("Asset", new XAttribute("steamId", "42"), new XAttribute("name", "Migration clinic"),
                    new XAttribute("hide", "false"), new XAttribute("nameVisibility", "AllVisible"), new XAttribute("symbol", "ja.hospital")),
                new XElement("Asset", new XAttribute("steamId", "43"), new XAttribute("name", "Legacy visible"),
                    new XAttribute("nameVisibility", "Visible"), new XAttribute("symbol", "system.none")),
                new XElement("Asset", new XAttribute("steamId", "44"), new XAttribute("name", "Native override"),
                    new XAttribute("hideShape", "true"), new XAttribute("nameVisibility", "FullVisible"), new XAttribute("symbol", "maki/fire-station")),
                new XElement("Asset", new XAttribute("steamId", "custom"), new XAttribute("name", "Custom override"),
                    new XAttribute("nameVisibility", "Invisible"), new XAttribute("symbol", "custom.symbol.with.dots")));
            foreach (var key in legacyKeys) preferences.Add(new XElement("Asset", new XAttribute("name", key), new XAttribute("symbol", key)));
            new XDocument(new XElement("ApplicationConfig", new XElement("SelectedStyle", "apple-light"), preferences)).Save(source);
            var original = File.ReadAllText(source);
            var config = BetaSettings.ImportConfig(source);
            Check(File.ReadAllText(source) == original, "Building preference migration rewrote the stable settings input");
            var clinic = config.BuildingPreferences.Single(p => p.SteamId == "42");
            Check(clinic.NameVisibility == NameVisibility.FullVisible && clinic.HideShape == false && clinic.MapSymbol == "ja/hospital",
                "Legacy AllVisible, explicit shape visibility or hospital symbol migration lost behavior");
            var overrideAsset = config.BuildingPreferences.Single(p => p.SteamId == "44");
            Check(overrideAsset.NameVisibility == NameVisibility.FullVisible && overrideAsset.HideShape == true && overrideAsset.MapSymbol == "maki/fire-station",
                "Migration changed an existing native preference");
            foreach (var key in legacyKeys)
            {
                var migrated = config.BuildingPreferences.Single(p => p.Name == key).MapSymbol!;
                var svg = MapSymbolPictureManager.Instance.GetSvg(migrated);
                Check(svg?.Picture != null && svg.Picture.CullRect.Width > 0 && svg.Picture.CullRect.Height > 0,
                    "Legacy map symbol no longer resolves to a drawable published SVG: " + key);
                Check(ReferenceEquals(svg, MapSymbolPictureManager.Instance.GetSvg(key)),
                    "A legacy key loaded directly by the native viewer differs from its migrated SVG: " + key);
            }
            Check(config.BuildingPreferences.Single(p => p.Name == "ja.powerStation").MapSymbol == "ja/powerPlant"
                && config.BuildingPreferences.Single(p => p.Name == "ja.cemetery").MapSymbol == "ja/graveyard"
                && config.BuildingPreferences.Single(p => p.Name == "maki.railMetro").MapSymbol == "maki/rail-metro",
                "Renamed legacy symbols changed their category");
            using (var stream = File.Create(native)) new XmlSerializer(config.GetType()).Serialize(stream, config);
            var restored = BetaSettings.ImportConfig(native);
            Check(restored.BuildingPreferences.Count == config.BuildingPreferences.Count
                && restored.BuildingPreferences.Single(p => p.SteamId == "custom").MapSymbol == "custom.symbol.with.dots"
                && restored.BuildingPreferences.Single(p => p.SteamId == "custom").NameVisibility == NameVisibility.Invisible
                && restored.BuildingPreferences.Single(p => p.SteamId == "43").MapSymbol == "system.none"
                && MapSymbolPictureManager.Instance.GetSvg("system.none") == null,
                "Native settings round-trip changed unknown custom preferences or explicit symbol suppression");
            Check(LegacyMapSymbolKeys.Normalize("ja/custom.symbol") == "ja/custom.symbol"
                && LegacyMapSymbolKeys.Normalize("ja.custom-symbol") == "ja.custom-symbol"
                && LegacyMapSymbolKeys.Normalize(null) == null && LegacyMapSymbolKeys.Normalize("") == "",
                "Legacy normalization changed an unknown/native symbol or default selection");
            CheckUnnamedBuildingLabels(config);
            Console.WriteLine($"PASS building preference migration: AllVisible renders unnamed assets, {legacyKeys.Length} legacy symbols resolve to published SVGs, native/custom/suppression prefs round-trip unchanged");
        }
        finally { File.Delete(source); File.Delete(native); }
    }
    private static void CheckUnnamedBuildingLabels(CS2MapView.CS1.Config.ApplicationConfig config)
    {
        XMLBuilding Building(int id, string name, float x) => new()
        {
            Id = id, Name = name, Service = "HealthCare", SubService = "HealthCareHospital", ItemClass = "Hospital",
            Points = [new(x-10,0,-10),new(x+10,0,-10),new(x+10,0,10),new(x-10,0,10)]
        };
        var raw = new CSLExportXML { FileVersion = "4.1", BuildingList =
            [Building(1,"42.Migration clinic_Data",-1000),Building(2,"43.Legacy visible_Data",1000)] };
        var settings = UserSettings.Default;
        BetaOutput.ApplyFeatureSettings(settings, config);
        using var host = new BetaOutputHost(settings, config);
        var vc = host.Context.ViewContext;
        vc.WorldRect = CS1MapType.CS1WorldRect; vc.ViewLeftTop = (-8640,-8640);
        var session = BetaMapSession.Get(host, raw);
        var manager = new LabelContentsManager(vc.TextWorldRect);
        new CS1LabelContentBuilder(host, raw, manager, session.Config).BuildAsync(vc).GetAwaiter().GetResult();
        var labels = manager.Contents.GetAllOrderedObjects().ToList();
        Check(labels.OfType<BuildingNameLabel>().Select(l => l.Text).SequenceEqual(new[] { "Migration clinic" }),
            "Migrated AllVisible did not display an unnamed asset label, or changed the old Visible behavior");
        Check(labels.OfType<MapSymbol>().Count() == 1, "Migrated hospital SVG or explicit suppression did not reach the actual native label builder");
    }
}
