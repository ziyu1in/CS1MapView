using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.CS1.Transit;

/// <summary>Viewer metadata is separate from the upstream XML export objects.</summary>
public sealed class TransportInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TransportType { get; set; } = "";
    public List<TransportStop> Stops { get; set; } = [];
    public bool HadLoadError { get; set; }
    public SKColor? UserDefinedColor { get; set; }
    public SKColor? AutoColor { get; set; }
    public bool IsLineRenderingTarget { get; set; }
    public string? ViewerDefinedLineNo { get; set; }
    public bool ViewerVisible { get; set; } = true;
    public bool HighlightVisible { get; set; } = true;
    public bool EffectiveVisible => ViewerVisible && HighlightVisible && !HadLoadError;
}

public sealed class TransportStop
{
    public int TransportId { get; set; }
    public string TransportType { get; set; } = "";
    public int NodeId { get; set; }
    public XMLNode? Node { get; set; }
    private XMLVector3? position;
    public XMLVector3 PositionOnSegment { get => position ?? Node?.Position ?? new XMLVector3(); set => position = value; }
    public List<RouteSegment> DrawRoute { get; set; } = [];
    public List<int> OriginalSegmentIds { get; set; } = [];
    public bool IsTerminalLoopHidden { get; set; }
    public List<PointsWithWidth> PointsToNextStop { get; set; } = [];
}

public sealed class RouteSegment
{
    public int SegmentId { get; set; }
    public List<XMLVector3> Points { get; set; } = [];
    public List<int> ForwardTransports { get; set; } = [];
    public List<int> BackwardTransports { get; set; } = [];
    public int NetworkLevel { get; set; }
    public bool IsOneWayFor(int id) => ForwardTransports.Contains(id) ^ BackwardTransports.Contains(id);
}

public sealed class PointsWithWidth
{
    public List<XMLVector3> Points { get; set; } = [];
    public RouteSegment? RefSegment { get; set; }
    public bool IsOneWayFor(int id) => RefSegment?.IsOneWayFor(id) == true;
    public void GetLaneAndPosition(List<TransportInfo> routes, int id, out int lanes, out int position)
    {
        var active = routes.Where(r => r.IsLineRenderingTarget && r.EffectiveVisible).Select(r => r.Id).ToHashSet();
        var ids = (RefSegment?.ForwardTransports ?? []).Concat(RefSegment?.BackwardTransports ?? [])
            .Where(active.Contains).Distinct().Order().ToList();
        lanes = Math.Max(1, ids.Count); position = Math.Max(0, ids.IndexOf(id));
    }
}

public sealed class MergedStation
{
    public int Id { get; set; }
    public List<TransportStop> Stops { get; set; } = [];
    public string NodeIds => string.Join(",", Stops.Select(s => s.NodeId));
    public XMLVector3 Position => new((float)Stops.Average(s => s.PositionOnSegment.X),
        (float)Stops.Average(s => s.PositionOnSegment.Y), (float)Stops.Average(s => s.PositionOnSegment.Z));
}
