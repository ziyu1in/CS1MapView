namespace CS2MapView.Drawing;

/// <summary>Known stable-viewer symbol identifiers mapped to the published native SVG assets.</summary>
public static class LegacyMapSymbolKeys
{
    private static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ja.castle"] = "ja/castle",
        ["ja.cemetery"] = "ja/graveyard",
        ["ja.powerStation"] = "ja/powerPlant",
        ["ja.fireStation"] = "ja/fireStation",
        ["ja.highSchool"] = "ja/highSchool",
        ["ja.highTower"] = "ja/highTower",
        ["ja.historicSite"] = "ja/historicalSite",
        ["ja.hospital"] = "ja/hospital",
        ["ja.meteorologicalAgency"] = "ja/meteorologicalObservatory",
        ["ja.policeStation"] = "ja/policeStation",
        ["ja.port"] = "ja/localPort",
        ["ja.radioTower"] = "ja/electricWaveTower",
        ["ja.school"] = "ja/elementarySchool",
        ["ja.university"] = "ja/university",
        ["ja.windTurbine"] = "ja/windmill",
        ["ja.cityHall"] = "ja/cityHall",
        ["ja.localGovernment"] = "ja/governmentOffice",
        ["ja.shintoShrine"] = "ja/shrine",
        ["ja.lighthouse"] = "ja/lighthouse",
        ["ja.buddhistTemple"] = "ja/temple",
        ["ja.onsen"] = "ja/spa",
        ["ja.ferry"] = "ja/ferry",
        ["ja.postOffice"] = "ja/postOffice",
        ["maki.castle"] = "maki/castle",
        ["maki.cemetery"] = "maki/cemetery",
        ["maki.college"] = "maki/college",
        ["maki.hospital"] = "maki/hospital",
        ["maki.harbor"] = "maki/harbor",
        ["maki.fireStation"] = "maki/fire-station",
        ["maki.police"] = "maki/police",
        ["maki.prison"] = "maki/prison",
        ["maki.school"] = "maki/school",
        ["maki.airport"] = "maki/airport",
        ["maki.doctor"] = "maki/doctor",
        ["maki.ferry"] = "maki/ferry",
        ["maki.townHall"] = "maki/town-hall",
        ["maki.recycling"] = "maki/recycling",
        ["maki.post"] = "maki/post",
        ["maki.parking"] = "maki/parking",
        ["maki.park"] = "maki/park",
        ["maki.bus"] = "maki/bus",
        ["maki.car"] = "maki/car",
        ["maki.rail"] = "maki/rail",
        ["maki.railLight"] = "maki/rail-light",
        ["maki.railMetro"] = "maki/rail-metro",
        ["maki.aerialway"] = "maki/aerialway",
        ["maki.lighthouse"] = "maki/lighthouse",
        ["maki.monument"] = "maki/monument",
        ["maki.heliport"] = "maki/heliport",
        ["maki.attraction"] = "maki/attraction",
        ["maki.danger"] = "maki/danger"
    };

    /// <summary>Preserves native identifiers, explicit suppression and unknown custom keys.</summary>
    public static string? Normalize(string? key) => key != null && Aliases.TryGetValue(key, out var native) ? native : key;
}
