using CS2MapView.Config;
using CS2MapView.Data;
using CS2MapView.Drawing.Layer;
using CS2MapView.Theme;
using CS2MapView.Util;
using CS2MapView.Util.CS1;
using CSLMapView.XML;
using SkiaSharp;
using CS2MapView.Beta;
using CS2MapView.CS1.Config.Styles;
using CS2MapView.Import.CS1;

namespace CS2MapView.Drawing.Roads.CS1;
/// <summary>
/// 道路描画オブジェクト構築(CS1)
/// </summary>
public class CS1RoadLayerBuilder
{
    private BetaMapSession Session => BetaMapSession.Get(AppRoot, RawXML);
    private CSLExportXML RawXML { get; init; }
    private ICS2MapViewRoot AppRoot { get; init; }
    private CSLExportXML ExportXML { get; init; }

    private CS1SegmentManager SegmentManager { get; init; }
    private CS1RoadGeometry RoadGeometry { get; } = new();
    internal RoadCrossingReport CrossingReport { get; private set; } = new();
    private BasicLayer ResultLayer { get; init; }
    private Dictionary<(WayType, float, bool, RoadImportance, bool), Func<float, float, SKPaintCache.StrokeKey?>> StrokeKeyFuncs { get; init; }
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="appRoot"></param>
    /// <param name="xml"></param>
    public CS1RoadLayerBuilder(ICS2MapViewRoot appRoot, CSLExportXML xml)
    {
        AppRoot = appRoot;
        RawXML = xml;
        // Recover original curves on the copied network before its segments
        // are reversed, joined or simplified for display.
        ExportXML = CS1XMLImporter.CreateLabelView(xml, Session.CurrentMapStyle.RoadHierarchy.Enabled, RoadGeometry);
        SegmentManager = new CS1SegmentManager(ExportXML);
        ResultLayer = new BasicLayer(AppRoot, ILayer.LayerNameRoads, CS1MapType.CS1WorldRect);
        StrokeKeyFuncs = [];

    }

    /// <summary>
    /// 構築の実行
    /// </summary>
    /// <param name="loadProgressInfo"></param>
    /// <returns></returns>
    public Task<ILayer> BuildAsync(LoadProgressInfo? loadProgressInfo)
    {
        return Task.Run<ILayer>(() =>
        {
            List<XMLSegment> tunnelList = [];
            List<XMLSegment> wayGroundList = [];
            List<XMLSegment> highwayGroundList = [];
            List<XMLSegment> beautificationGroundList = [];
            List<XMLSegment> elevatedList = [];

            foreach (var seg in ExportXML.SegmentList)
            {
                if (seg.Points.Count < 2 || !seg.Points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z))) continue;
                WayType wt = SegmentManager.GetWayType(seg);
                if (wt.HasBit(WayType.Road) || wt.HasBit(WayType.Beautification))
                {
                    if (Session.Config.IgnoreSegments.IsHidden(seg.Name, seg.ItemClass))
                    {
                        continue;
                    }

                    if (wt.HasBit(WayType.Tunnel))
                    {
                        tunnelList.Add(seg);
                    }
                    else if (wt.HasBit(WayType.Elevated))
                    {
                        elevatedList.Add(seg);
                    }
                    else
                    {
                        if (wt.HasBit(WayType.Highway))
                        {
                            highwayGroundList.Add(seg);
                        }
                        else if (wt.HasBit(WayType.Beautification))
                        {
                            beautificationGroundList.Add(seg);
                        }
                        else
                        {
                            wayGroundList.Add(seg);
                        }
                    }

                }

            }
            List<Junction> junctionList = Junction.GetJunctions(SegmentManager, ExportXML.NodeList);


            //トンネル

            DrawWaysSequentially(tunnelList, junctionList);

            loadProgressInfo?.Progress(this, LoadProgressInfo.Process.BuildRoads, 0.2f, null);

            List<Junction> renderTarget =
            [
                .. DrawWaysAll(wayGroundList, junctionList, WayType.Road | WayType.Ground),
                .. DrawWaysAll(highwayGroundList, junctionList, WayType.Road | WayType.Highway | WayType.Ground),
                .. DrawWaysAll(beautificationGroundList, junctionList, WayType.Beautification | WayType.Ground),
            ];
            loadProgressInfo?.Progress(this, LoadProgressInfo.Process.BuildRoads, 0.8f, null);
            foreach (Junction j in renderTarget)
            {
                DrawJunctionFigure(j);
            }

            DrawWaysSequentially(elevatedList, junctionList);

            // Append road-only corrections after every original road and
            // junction pass. Keep raw segment boundaries and their Y profiles.
            var crossingRoads = new List<RoadCrossingSource>();
            foreach (var segment in RawXML.SegmentList)
            {
                if (segment.Points.Count < 2 || !segment.Points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z))) continue;
                var way = SegmentManager.GetWayType(segment);
                if (!way.HasBit(WayType.Road) || Session.Config.IgnoreSegments.IsHidden(segment.Name, segment.ItemClass)) continue;
                var width = GetDrawingWidth(segment);
                if (width <= 0) continue;
                crossingRoads.Add(new(segment,
                    GetStrokeFunc(way, width, true, GetImportance(segment), IsNarrow(segment))(1, 1),
                    GetStrokeFunc(way, width, false, GetImportance(segment), IsNarrow(segment))(1, 1),
                    SegmentManager.GetWidth(segment)));
            }
            CrossingReport = CS1RoadCrossingPostProcessor.Append(crossingRoads, ResultLayer.DrawCommands,
                refinementMetres: Math.Max(0.25, 17280d / Session.Config.CanvasSize / 2));

            loadProgressInfo?.Progress(this, LoadProgressInfo.Process.BuildRoads, 1f, null);


            return ResultLayer;
        });
    }
    private static float GetAverageHeight(XMLSegment s) => s.Points.Select(t => t.Y).Average();




    // The native upstream road/junction builder retains its pass ordering, while
    // these measurements preserve the established fixed-plus-scaled CS1 style.
    internal float GetDrawingWidth(XMLSegment segment)
    {
        double canvas = Session.Config.CanvasSize;
        double width = SegmentManager.GetWidth(segment);
        return width <= 0 ? 0 : (float)((0.5 + width * 0.15 * canvas / 4096d) * 17280d / canvas);
    }

    private BorderConfig GetBorder(WayType wt)
    {
        var b = Session.CurrentMapStyle.WayBorder;
        if (wt.HasBit(WayType.Highway))
            return wt.HasBit(WayType.Tunnel) ? b.HighwayTunnelBorder : wt.HasBit(WayType.Elevated) ? b.HighwayElevatedBorder : b.HighwayBorder;
        if (wt.HasBit(WayType.Road))
            return wt.HasBit(WayType.Tunnel) ? b.RoadTunnelBorder : wt.HasBit(WayType.Elevated) ? b.RoadElevatedBorder : b.RoadBorder;
        return wt.HasBit(WayType.Tunnel) ? b.BeautificationTunnelBorder : wt.HasBit(WayType.Elevated) ? b.BeautificationElevatedBorder : b.BeautificationBorder;
    }

    private RoadImportance GetImportance(XMLSegment segment) => Session.CurrentMapStyle.RoadHierarchy.Enabled
        ? SegmentManager.GetRoadImportance(segment) : RoadImportance.Minor;

    private bool IsNarrow(XMLSegment segment)
    {
        var hierarchy = Session.CurrentMapStyle.RoadHierarchy;
        var way = SegmentManager.GetWayType(segment);
        return hierarchy.Enabled && hierarchy.NarrowRoadMaxWidth > 0 && way.HasBit(WayType.Road)
            && !way.HasBit(WayType.Highway) && SegmentManager.GetWidth(segment) <= hierarchy.NarrowRoadMaxWidth;
    }

    private SKColor? GetDrawingColor(WayType wt, RoadImportance importance, bool isBorder, bool narrow)
    {
        var hierarchy = Session.CurrentMapStyle.RoadHierarchy;
        if (hierarchy.Enabled && wt.HasBit(WayType.Road) && !wt.HasBit(WayType.Highway)
            && (narrow || importance is RoadImportance.Intermediate or RoadImportance.Major))
        {
            var palette = narrow ? hierarchy.Narrow : importance == RoadImportance.Major ? hierarchy.Major : hierarchy.Intermediate;
            return wt.HasBit(WayType.Tunnel) ? isBorder ? palette.TunnelBorder : palette.TunnelFill
                : wt.HasBit(WayType.Elevated) ? isBorder ? palette.ElevatedBorder : palette.ElevatedFill
                : isBorder ? palette.Border : palette.Fill;
        }
        var color = WayTypeToColor(wt, isBorder);
        return color.HasValue ? (SKColor)color.Value : null;
    }

    private Func<float, float, SKPaintCache.StrokeKey?> CreateStrokeFunc(WayType wt, float meterWidth, bool isBorder, RoadImportance importance, bool narrow)
    {
        var color = GetDrawingColor(wt, importance, isBorder, narrow);
        if (color is null || meterWidth <= 0 || !float.IsFinite(meterWidth)) return (_, _) => null;
        var strokeWidth = meterWidth;
        float[]? dash = null;
        if (isBorder)
        {
            var border = GetBorder(wt);
            double canvas = Session.Config.CanvasSize;
            strokeWidth += (float)(border.Size.CalcSize(canvas / 4096d) * 17280d / canvas);
            if (border.DashStyle?.Dashes?.Any() == true)
            {
                float fraction = meterWidth / strokeWidth;
                dash = border.DashStyle.Dashes.Select((d, i) => (float)d * fraction / (i % 2 == 0 ? 3 : 1)).ToArray();
            }
        }
        return (_, _) => new SKPaintCache.StrokeKey(strokeWidth, (SKColor)color, StrokeType.Round, dash);
    }
    private Func<float, float, SKPaintCache.StrokeKey?> GetStrokeFunc(WayType wt, float meterWidth, bool isBorder, RoadImportance importance, bool narrow)
    {
        var key = (wt, meterWidth, isBorder, importance, narrow);
        if (StrokeKeyFuncs.TryGetValue(key, out var value))
        {
            return value;
        }
        else
        {
            StrokeKeyFuncs.Add(key, CreateStrokeFunc(wt, meterWidth, isBorder, importance, narrow));
            return StrokeKeyFuncs[key];
        }
    }

    private void DrawWaysSequentially(List<XMLSegment> roadList, List<Junction> junctionList)
    {

        roadList.Sort((a, b) =>
        {
            double diff = GetAverageHeight(a) - GetAverageHeight(b);
            return diff < 0 ? -1 : diff == 0 ? 0 : 1;
        });

        foreach (XMLSegment seg in roadList)
        {
            WayType wt = SegmentManager.GetWayType(seg);
            var width = GetDrawingWidth(seg);
            SKPath path = RoadGeometry.CreatePath(seg);
            var cmdBorder = new PathDrawCommand
            {
                Path = path,
                StrokePaintFunc = GetStrokeFunc(wt, width, true, GetImportance(seg), IsNarrow(seg))
            };

            var cmdFill = new PathDrawCommand
            {
                Path = path,
                StrokePaintFunc = GetStrokeFunc(wt, width, false, GetImportance(seg), IsNarrow(seg))
            };

            ResultLayer.DrawCommands.Add(cmdBorder);
            ResultLayer.DrawCommands.Add(cmdFill);


            List<Junction> renderTarget = [];
            Junction? temp = junctionList.FirstOrDefault(j => j.Id == seg.StartNodeId);
            if (temp?.AddRenderedSegment(seg) ?? false)
            {
                renderTarget.Add(temp);
            }
            temp = junctionList.FirstOrDefault(j => j.Id == seg.EndNodeId);
            if (temp?.AddRenderedSegment(seg) ?? false)
            {
                renderTarget.Add(temp);
            }

            foreach (Junction j in renderTarget)
            {
                DrawJunctionFigure(j);

            }


        }
    }


    private void DrawJunctionFigure(Junction junction)
    {

        junction.Connection.Sort((a, b) =>
        {
            WayType atype = SegmentManager.GetWayType(a).ToGroundModeOnly();
            WayType btype = SegmentManager.GetWayType(b).ToGroundModeOnly();
            int asort = (atype == WayType.Ground ? 2 : atype == WayType.Elevated ? 1 : 0);
            int bsort = (btype == WayType.Ground ? 2 : btype == WayType.Elevated ? 1 : 0);
            int result = asort - bsort;
            if (result != 0)
            {
                return result;
            }
            else
            {
                asort = (atype == WayType.Highway ? 1 : 0);
                bsort = (btype == WayType.Highway ? 1 : 0);
                return asort - bsort;
            }
        });

        foreach (XMLSegment s in junction.Connection)
        {
            if (s.Points.Count < 2) continue;
            if (s.StartNodeId == junction.Id) DrawEnd(true);
            if (s.EndNodeId == junction.Id) DrawEnd(false);

            void DrawEnd(bool fromStart)
            {
                SKPath path = RoadGeometry.CreateJunctionPath(s, fromStart);
                if (path.IsEmpty)
                {
                    path.Dispose();
                    return;
                }
                ResultLayer.DrawCommands.Add(new PathDrawCommand
                {
                    Path = path,
                    StrokePaintFunc = GetStrokeFunc(SegmentManager.GetWayType(s), GetDrawingWidth(s), false, GetImportance(s), IsNarrow(s))
                });
            }
        }
    }

    private static void AddToLineDic(Dictionary<(RoadImportance Importance, float Width, bool Narrow), List<SKPath>> lineDic,
        (RoadImportance Importance, float Width, bool Narrow) key, SKPath pf)
    {
        if (lineDic.TryGetValue(key, out var list))
        {
            list.Add(pf);
        }
        else
        {
            lineDic.Add(key, [pf]);
        }
    }

    private List<Junction> DrawWaysAll(List<XMLSegment> list, List<Junction> junctionList, WayType wt)
    {
        List<Junction> renderTarget = [];
        Dictionary<(RoadImportance Importance, float Width, bool Narrow), List<SKPath>> lineDic = [];
        foreach (XMLSegment seg in list)
        {
            AddToLineDic(lineDic, (GetImportance(seg), GetDrawingWidth(seg), IsNarrow(seg)), RoadGeometry.CreatePath(seg));

            Junction? temp = junctionList.FirstOrDefault(j => j.Id == seg.StartNodeId);
            if (temp?.AddRenderedSegment(seg) ?? false)
            {
                renderTarget.Add(temp);
            }
            temp = junctionList.FirstOrDefault(j => j.Id == seg.EndNodeId);
            if (temp?.AddRenderedSegment(seg) ?? false)
            {
                renderTarget.Add(temp);
            }
        }

        foreach (var t in lineDic.OrderBy(t => t.Key.Importance).ThenBy(t => t.Key.Width))
        {
            var width = t.Key.Width;
            var pathes = t.Value;
            foreach (var p in pathes)
            {
                var dc = new PathDrawCommand { Path = p, StrokePaintFunc = GetStrokeFunc(wt, width, true, t.Key.Importance, t.Key.Narrow) };
                ResultLayer.DrawCommands.Add(dc);
            }
        }
        foreach (var t in lineDic.OrderBy(t => t.Key.Importance).ThenBy(t => t.Key.Width))
        {
            var width = t.Key.Width;
            var pathes = t.Value;
            foreach (var p in pathes)
            {
                var dc = new PathDrawCommand { Path = p, StrokePaintFunc = GetStrokeFunc(wt, width, false, t.Key.Importance, t.Key.Narrow) };
                ResultLayer.DrawCommands.Add(dc);
            }
        }

        return renderTarget;
    }


    private SerializableColor? WayTypeToColor(WayType wt, bool isBorder)
    {
        var rule = AppRoot.Context.Theme.Colors!;
        if (wt.HasBit(WayType.Road))
        {
            if (wt.HasBit(WayType.Highway))
            {
                if (wt.HasBit(WayType.Tunnel))
                {
                    return isBorder ? rule.HighwayTunnelBorder : rule.HighwayTunnel;
                }
                else if (wt.HasBit(WayType.Elevated))
                {
                    return isBorder ? rule.HighwayElevatedBorder : rule.HighwayElevated;
                }
                else
                {
                    return isBorder ? rule.HighwayBorder : rule.Highway;
                }
            }
            else
            {
                if (wt.HasBit(WayType.Tunnel))
                {
                    return isBorder ? rule.RoadTunnelBorder : rule.RoadTunnel;
                }
                else if (wt.HasBit(WayType.Elevated))
                {
                    return isBorder ? rule.RoadElevatedBorder : rule.RoadElevated;
                }
                else
                {
                    return isBorder ? rule.RoadBorder : rule.Road;
                }
            }
        }
        else if (wt.HasBit(WayType.Beautification))
        {
            if (wt.HasBit(WayType.Tunnel))
            {
                return isBorder ? rule.BeautificationTunnelBorder : rule.BeautificationTunnel;
            }
            else if (wt.HasBit(WayType.Elevated))
            {
                return isBorder ? rule.BeautificationElevatedBorder : rule.BeautificationElevated;
            }
            else
            {
                return isBorder ? rule.BeautificationBorder : rule.Beautification;
            }
        }
        else
        {
            return null;
        }

    }
}
