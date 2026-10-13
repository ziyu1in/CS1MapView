using CS2MapView.Beta;
using CS2MapView.Import.CS1;
using CSLMapView.XML;
using System.Globalization;
using System.Text.Json;
using System.Xml.Serialization;

namespace CS2MapView.Form;

internal enum BetaDiagnosticKind { Buildings, Segments, SegmentDetails, SimplifiedMap, TransportOverview, TransportSegments }

/// <summary>Read-only exports of the loaded map and native derived models.</summary>
internal static class BetaDiagnostics
{
    internal static void Write(BetaMapSession session, BetaDiagnosticKind kind, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var raw = session.Raw;
        if (kind == BetaDiagnosticKind.SimplifiedMap)
        {
            var copy = PrepareSimplifiedCopy(raw);
            using var file = File.Create(path);
            new XmlSerializer(typeof(CSLExportXML)).Serialize(file, copy);
            return;
        }
        using var output = new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
        string Cell(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture)?.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ") ?? "";
        void Row(params object?[] values) => output.WriteLine(string.Join("\t", values.Select(Cell)));
        switch (kind)
        {
            case BetaDiagnosticKind.Buildings:
                Row("Workshop", "Asset", "Service", "SubService", "ItemClass", "Count");
                foreach (var group in raw.BuildingList.GroupBy(b => (b.SteamId, b.ShortName)).OrderBy(g => g.Key.ShortName))
                { var b = group.First(); Row(b.SteamId, b.ShortName, b.Service, b.SubService, b.ItemClass, group.Count()); }
                break;
            case BetaDiagnosticKind.Segments:
                Row("Asset", "ItemClass", "SourceWidthMetres", "Count");
                foreach (var group in raw.SegmentList.GroupBy(s => (s.Name, s.ItemClass, s.Width)).OrderBy(g => g.Key.Name))
                    Row(group.Key.Name, group.Key.ItemClass, group.Key.Width, group.Count());
                break;
            case BetaDiagnosticKind.SegmentDetails:
                Row("Kind", "Id", "Name", "ItemClass", "StartNode", "EndNode", "WidthMetres", "PointsXYZ");
                foreach (var s in raw.SegmentList)
                    Row("Segment", s.Id, s.Name, s.ItemClass, s.StartNodeId, s.EndNodeId, s.Width, string.Join(";", s.Points.Select(p => FormattableString.Invariant($"{p.X},{p.Y},{p.Z}"))));
                Row("Kind", "Id", "Service", "SubService", "Underground", "OnGround", "PositionXYZ");
                foreach (var n in raw.NodeList)
                    Row("Node", n.Id, n.Service, n.SubService, n.UnderGround, n.OnGround, FormattableString.Invariant($"{n.Position.X},{n.Position.Y},{n.Position.Z}"));
                break;
            case BetaDiagnosticKind.TransportOverview:
                var manager = session.TransportManager;
                output.Write(JsonSerializer.Serialize(new {
                    raw.City,
                    Routes = manager.TransportList.Select(r => new { r.Id, r.Name, Mode = r.TransportType, Visible = r.ViewerVisible,
                        Shorthand = r.ViewerDefinedLineNo, Stops = r.Stops.Count, r.HadLoadError,
                        Color = r.UserDefinedColor?.ToString(),
                        StopNodes = r.Stops.Select(s => s.NodeId).ToArray() }).ToArray(),
                    Clusters = manager.StationLayout.Clusters.Select(c => new { c.Id, SelectionGroup = c.Station.Id,
                        From = new { c.A.X, c.A.Y }, To = new { c.B.X, c.B.Y }, Stops = c.Points.SelectMany(p => p.Stops).Select(s => new { s.TransportId, s.NodeId }).ToArray() }).ToArray(),
                    Links = manager.StationLayout.Links.Select(l => new { First = l.First.Id, Second = l.Second.Id, l.Length }).ToArray(),
                    manager.Diagnostics
                }, new JsonSerializerOptions { WriteIndented = true }));
                break;
            case BetaDiagnosticKind.TransportSegments:
                Row("RouteId", "RouteName", "Mode", "StopNode", "NextStopNode", "SourceSegmentIds", "RenderedPointsXYZ");
                foreach (var route in session.TransportManager.TransportList)
                    for (var index = 0; index < route.Stops.Count; index++)
                    {
                        var stop = route.Stops[index]; var next = route.Stops[(index + 1) % route.Stops.Count];
                        Row(route.Id, route.Name, route.TransportType, stop.NodeId, next.NodeId,
                            string.Join(",", stop.OriginalSegmentIds),
                            string.Join(";", stop.PointsToNextStop.SelectMany(p => p.Points).Select(p => FormattableString.Invariant($"{p.X},{p.Y},{p.Z}"))));
                    }
                break;
        }
    }
    internal static CSLExportXML PrepareSimplifiedCopy(CSLExportXML raw)
    {
        var labels = CS1XMLImporter.CreateLabelView(raw);
        var copy = BetaOutput.Clone(raw);
        copy.NodeList = labels.NodeList; copy.SegmentList = labels.SegmentList; copy.SegmentTypeList = labels.SegmentTypeList;
        // Derived arrays are XmlIgnore in the distributed XML contract. Re-create
        // its row format on this copy if the serialized source was discarded.
        const int terrainWidth = CSLExportXML.TERRAIN_RESOLUTION;
        if (copy.TerrainLines.Count == 0 && raw.LandArray?.Length == terrainWidth * terrainWidth && raw.WaterArray?.Length == raw.LandArray.Length)
            for (var offset = 0; offset < raw.LandArray.Length; offset += terrainWidth)
                copy.TerrainLines.Add(string.Join(",", Enumerable.Range(offset, terrainWidth).Select(index =>
                    QuantizedHeight(raw.LandArray[index]) + ":" + QuantizedHeight(raw.WaterArray[index]))));
        if (copy.ForestList.Count == 0 && raw.ForestByteData is { Length: > 0 } forest)
            for (var offset = 0; offset < forest.Length; offset += 512)
                copy.ForestList.Add(string.Join(",", forest.Skip(offset).Take(Math.Min(512, forest.Length - offset)).Select(value => value.ToString(CultureInfo.InvariantCulture))));
        return copy;
    }

    private static string QuantizedHeight(float height)
        => checked((int)Math.Round(height * 64d)).ToString(CultureInfo.InvariantCulture);
}
