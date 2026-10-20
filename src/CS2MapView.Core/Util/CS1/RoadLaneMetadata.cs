using CSLMapView.XML;
using System.Text.RegularExpressions;

namespace CS2MapView.Util.CS1;

internal enum RoadImportance { Minor, Intermediate, Major, Highway }

/// <summary>Motor-traffic lanes, independent of the full exported network width.</summary>
internal readonly record struct RoadLaneCounts(int Forward, int Backward, int Bidirectional = 0)
{
    internal int BusiestDirection => Math.Max(Forward, Backward);
    // A lane that permits both directions is still one physical lane.
    internal int Total => Forward + Backward - Bidirectional;
    internal RoadImportance Importance => Total >= 6 ? RoadImportance.Major
        : Total >= 4 ? RoadImportance.Intermediate : RoadImportance.Minor;
}

internal static partial class RoadLaneMetadata
{
    // Use the network asset's internal name, never a street's custom name or
    // the presence of ordinary curbside parking lanes.
    internal static bool IsParkingLotNetwork(string? assetName)
        => assetName is not null && ParkingLotNetworkRegex().IsMatch(assetName);

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:parking[\s_-]*lots?|car[\s_-]*parks?)(?:road)?(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParkingLotNetworkRegex();

    internal static RoadLaneCounts Read(XMLSegmentType type)
    {
        // A shared car/tram lane or duplicate mode entries at one lateral
        // position describe one physical road lane, not several road lanes.
        Dictionary<double, (bool Forward, bool Backward)> positions = [];
        foreach (var lane in type.Lanes)
        {
            if (!float.IsFinite(lane.Width) || lane.Width <= 0 || !float.IsFinite(lane.Position)) continue;
            var laneTypes = Flags(lane.Type);
            if (laneTypes.Contains("Parking") || !laneTypes.Overlaps(["Vehicle", "TransportVehicle"])) continue;
            if (!Flags(lane.VehicleType).Overlaps(["Car", "Bus", "Taxi", "Trolleybus", "Truck", "Emergency", "Service"])) continue;
            var directions = Flags(lane.Direction);
            bool both = directions.Overlaps(["Both", "AvoidBoth"]);
            bool forward = both || directions.Overlaps(["Forward", "AvoidForward"]);
            bool backward = both || directions.Overlaps(["Backward", "AvoidBackward"]);
            if (!forward && !backward) continue;
            var position = Math.Round(lane.Position, 3);
            var previous = positions.GetValueOrDefault(position);
            positions[position] = (previous.Forward || forward, previous.Backward || backward);
        }
        return new(positions.Values.Count(p => p.Forward), positions.Values.Count(p => p.Backward),
            positions.Values.Count(p => p.Forward && p.Backward));
    }

    private static HashSet<string> Flags(string? value) => (value ?? "").Split(',')
        .Select(s => s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
