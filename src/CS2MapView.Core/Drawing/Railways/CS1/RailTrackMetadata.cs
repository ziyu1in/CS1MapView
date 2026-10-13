using System;
using System.Collections.Generic;
using System.Linq;
using CSLMapView.XML;

namespace CS2MapView.Drawing.Railways.CS1
{
    internal sealed class RailTrack
    {
        internal WayType Mode { get; set; }
        internal double Position { get; set; }
    }

    internal static class RailTrackMetadata
    {
        internal const WayType RailModes = WayType.Rail | WayType.Metro | WayType.Tram | WayType.Monorail | WayType.CableCar;

        internal static List<RailTrack> Read(XMLSegmentType asset)
        {
            var result = new List<RailTrack>();
            foreach (var lane in asset?.Lanes ?? new List<XMLLaneType>())
            {
                if (!(HasFlag(lane.Type, "Vehicle") || HasFlag(lane.Type, "TransportVehicle")) ||
                    !Finite(lane.Position) || !Finite(lane.Width) || lane.Width <= 0) continue;
                foreach (var pair in new[] {
                    new { Name = "Train", Mode = WayType.Rail }, new { Name = "Metro", Mode = WayType.Metro },
                    new { Name = "Tram", Mode = WayType.Tram }, new { Name = "Monorail", Mode = WayType.Monorail },
                    new { Name = "CableCar", Mode = WayType.CableCar } })
                {
                    // Both/AvoidForward describe travel direction, not additional physical tracks.
                    if (HasFlag(lane.VehicleType, pair.Name) && !result.Any(t => t.Mode == pair.Mode && Math.Abs(t.Position - lane.Position) < 0.01))
                        result.Add(new RailTrack { Mode = pair.Mode, Position = lane.Position });
                }
            }
            return result.OrderBy(t => t.Position).ThenBy(t => t.Mode).ToList();
        }

        private static bool HasFlag(string flags, string flag) => (flags ?? "").Split(',').Any(f => f.Trim() == flag);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
