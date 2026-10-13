using CS2MapView.CS1.Config;
using CS2MapView.Drawing.Railways.CS1;
using System.Xml.Serialization;
using CSLMapView.XML;
using Point = CS2MapView.CS1.Geometry.MapPoint;
using Color = SkiaSharp.SKColor;

internal static class RailMetadataPortChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static double Lightness(Color color)
    {
        Func<byte, double> linear = channel => { double v = channel / 255.0; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); };
        double r = linear(color.Red), g = linear(color.Green), b = linear(color.Blue);
        double l = Math.Pow(.4122214708 * r + .5363325363 * g + .0514459929 * b, 1.0 / 3);
        double m = Math.Pow(.2119034982 * r + .6806995451 * g + .1073969566 * b, 1.0 / 3);
        double s = Math.Pow(.0883024619 * r + .2817188376 * g + .6299787005 * b, 1.0 / 3);
        return .2104542553 * l + .793617785 * m - .0040720468 * s;
    }
    private static XMLLaneType Lane(float position, string direction = "Both", string type = "Vehicle", string vehicle = "Train")
        => new XMLLaneType { Position = position, Width = 4, Type = type, VehicleType = vehicle, Direction = direction };

    internal static void RunPolicies()
    {
        RailJunctionChecks.Run();
        var asset = new XMLSegmentType { Name = "Mod" };
        asset.Lanes.Add(Lane(0));
        Check(RailTrackMetadata.Read(asset).Count == 1, "Bidirectional lane was counted twice");
        asset.Lanes.Add(Lane(0, "Forward"));
        asset.Lanes.Add(Lane(50, "None", "None", "None"));
        asset.Lanes.Add(Lane(70, "Forward", "Vehicle", "Car"));
        asset.Lanes.Add(Lane(float.NaN));
        asset.Lanes.Add(Lane(float.PositiveInfinity));
        Check(RailTrackMetadata.Read(asset).Count == 1, "Helpers, duplicate directions or invalid positions became tracks");
        asset.Lanes.Clear();
        foreach (float position in new[] { -6f, -2f, 2f, 6f }) asset.Lanes.Add(Lane(position, "AvoidForward", "Vehicle, TransportVehicle"));
        Check(RailTrackMetadata.Read(asset).Select(t => t.Position).SequenceEqual(new[] { -6.0, -2, 2, 6 }), "Four-track metadata failed");
        asset.Lanes.Clear(); asset.Lanes.Add(Lane(2, "Forward", "Vehicle", "Car, Tram"));
        Check(RailTrackMetadata.Read(asset).Single().Mode == WayType.Tram, "Road-embedded tram track omitted");
        var options = new RailwayConfig { Appearance = RailwayAppearance.Google, InfrastructureOnly = true, SeparateTracks = false, ReconnectTracks = false, ShowTunnels = false, ColorByType = false };
        var serializer = new XmlSerializer(typeof(RailwayConfig));
        using (var writer = new StringWriter()) {
            serializer.Serialize(writer, options);
            var restored = (RailwayConfig)serializer.Deserialize(new StringReader(writer.ToString()));
            Check(restored.Appearance == RailwayAppearance.Google && restored.InfrastructureOnly && !restored.SeparateTracks && !restored.ReconnectTracks && !restored.ShowTunnels && !restored.ColorByType, "Rail preferences did not persist");
        }
        var modes = new[] { WayType.Rail, WayType.Metro, WayType.Tram, WayType.Monorail, WayType.CableCar };
        Check(modes.Select(m => CS1RailWayLayerBuilder.TrackColor(m, false, true, false)).Distinct().Count() == 5, "Track types share a color");
        foreach (bool googleStyle in new[] { false, true })
        foreach (bool tunnelStyle in new[] { false, true }) {
            var gray = CS1RailWayLayerBuilder.TrackColor(WayType.Rail, tunnelStyle, false, googleStyle);
            Check(gray == CS1RailWayLayerBuilder.TrackColor(WayType.Rail, tunnelStyle, true, googleStyle), "Train did not retain its legacy gray");
            foreach (var mode in new[] { WayType.Metro, WayType.Tram }) {
                var tint = CS1RailWayLayerBuilder.TrackColor(mode, tunnelStyle, true, googleStyle);
                Check(tint.Alpha == gray.Alpha && Math.Abs(Lightness(tint) - Lightness(gray)) < .002, "Metro/tram intensity does not match train gray");
                Check(mode == WayType.Metro ? tint.Blue > tint.Red && tint.Blue > tint.Green : tint.Green > tint.Red && tint.Green > tint.Blue, "Metro/tram sample hue lost");
            }
        }
        foreach (var mode in new[] { WayType.Monorail, WayType.CableCar }) {
            var surface = CS1RailWayLayerBuilder.TrackColor(mode, false, true, false);
            var underground = CS1RailWayLayerBuilder.TrackColor(mode, true, true, false);
            Check(surface.Alpha == 255 && underground.Alpha == 160 && surface.Red == underground.Red && surface.Green == underground.Green && surface.Blue == underground.Blue, "Tunnel changed type hue or tint strength");
            Check(surface == CS1RailWayLayerBuilder.TrackColor(mode, false, true, true), "Apple and Google appearances changed the type palette");
        }
        var polyline = CS1RailTrackGeometry.CleanPoints(new[] { new Point(0, 0), new Point(0, 0), new Point(10, 0), new Point(10, 10) });
        var shifted = CS1RailTrackGeometry.OffsetPath(polyline, 2);
        Check(shifted.Count == 3 && Math.Abs(shifted[0].Y - 2) < 0.001 && Math.Abs(shifted[2].X - 8) < 0.001, "Track offset geometry incorrect");
        Check(CS1RailTrackGeometry.OffsetPath(new List<Point> { new Point(0, 0), new Point(1, 0), new Point(0, 0) }, 6).All(p => !double.IsNaN(p.X) && !double.IsNaN(p.Y)), "Hairpin offset produced invalid geometry");
        Console.WriteLine("PASS rail metadata, settings, curved offsets, legacy train gray and matched metro/tram intensity in both appearances and tunnels");

    }
}
