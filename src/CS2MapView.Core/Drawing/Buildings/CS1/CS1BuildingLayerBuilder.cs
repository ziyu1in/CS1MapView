using CS2MapView.Data;
using CS2MapView.Beta;
using CS2MapView.Drawing.Layer;
using CS2MapView.Theme;
using CS2MapView.Util;
using CS2MapView.Util.CS1;
using CSLMapView.XML;
using SkiaSharp;

namespace CS2MapView.Drawing.Buildings.CS1;
/// <summary>
/// CS1の建物レイヤー構築
/// </summary>
/// <param name="appRoot"></param>
/// <param name="xml"></param>
public class CS1BuildingLayerBuilder(ICS2MapViewRoot appRoot, CSLExportXML xml)
{
    private BasicLayer ResultLayer { get; init; } = new BasicLayer(appRoot, ILayer.LayerNameBuildings, CS1MapType.CS1WorldRect);
    private CS1BuildingRenderingManager RenderingManager { get; init; } = new CS1BuildingRenderingManager(appRoot);
    /// <summary>
    /// 実行
    /// </summary>
    /// <param name="loadProgressInfo"></param>
    /// <returns></returns>
    public Task<ILayer> BuildAsync(LoadProgressInfo? loadProgressInfo)
    {
        return Task.Run<ILayer>(() =>
        {
            if (xml != null)
            {
                var config = BetaMapSession.Get(appRoot, xml).Config;
                RenderingManager.Config = config;
                SKColor borderColor = appRoot.Context.Theme.Colors?.BuildingBorder ?? SKColors.Black;

                SKPaintCache.StrokeKey? borderKeyGetter(float scale, float worldScale) => appRoot.Context.Theme.Strokes!.Building!.WithColor(borderColor).ToCacheKey(scale, worldScale);

                List<List<XMLBuilding>> groupedList = GroupBySubBuilding(xml.BuildingList);

                foreach (List<XMLBuilding> subList in groupedList)
                {
                    XMLBuilding b = subList[0];

                    if (IsTargetBuilding(b) && b.Points.Count >= 3)
                    {
                        if (subList.Count > 1)
                        {

                            var pathes = subList.Where(s => s.Points.Count >= 3 && s.Points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Z))).Select(s =>
                            {
                                var p = new SKPath();
                                p.MoveTo(s.Points[0].ToMapSpace());
                                foreach (var point in s.Points.Skip(1)) p.LineTo(point.ToMapSpace());
                                p.Close();
                                return p;
                            });

                            SKPath retPath = CS2MapView.CS1.Geometry.NativeGeometry.Union(pathes);

                            var pdc = new PathDrawCommand() { Path = retPath };
                            if (!config.DisableBuildingBorder)
                            {
                                pdc.StrokePaintFunc = borderKeyGetter;

                            }
                            pdc.FillColor = ServiceToFillColor(RenderingManager.GetShapeRenderingParam(b.SteamId, b.ShortName, b.ItemClass, b.Service).ServiceForRender);
                            ResultLayer.DrawCommands.Add(pdc);
                        }
                        else
                        {
                            SKPath path = new() { FillType = SKPathFillType.Winding };
                            path.MoveTo(b.Points[0].ToMapSpace());
                            foreach (var point in b.Points.Skip(1)) path.LineTo(point.ToMapSpace());
                            path.Close();

                            var pdc = new PathDrawCommand() { Path = path };
                            if (!config.DisableBuildingBorder)
                            {
                                pdc.StrokePaintFunc = borderKeyGetter;
                            }
                            pdc.FillColor = ServiceToFillColor(RenderingManager.GetShapeRenderingParam(b.SteamId, b.ShortName, b.ItemClass, b.Service).ServiceForRender);
                            ResultLayer.DrawCommands.Add(pdc);
                        }
                    }

                }
                loadProgressInfo?.Progress(this, LoadProgressInfo.Process.BuildBuildings, 1f, null);

            }

            return ResultLayer;
        });
    }

    private static readonly string[] RICO_CATEGORY = ["Residential", "Commercial", "Industrial", "Office"];

    private SKColor ServiceToFillColor(string serviceNameInternal)
    {
        if (appRoot.Context.Theme.Colors is null)
        {
            return SKColors.Gray;
        }
        ColorRules colors = appRoot.Context.Theme.Colors;

        if (serviceNameInternal == "Residential")
        {
            return colors.ResidentialBuilding;
        }
        else if (serviceNameInternal == "Commercial")
        {
            return colors.CommercialBuilding;
        }
        else if (serviceNameInternal == "Industrial" || serviceNameInternal == "PlayerIndustry")
        {
            return colors.IndustrialBuilding;
        }
        else if (serviceNameInternal == "Office")
        {
            return colors.OfficeBuilding;
        }
        else if (serviceNameInternal == "PublicTransport")
        {
            return colors.PublicTransportBuilding;
        }
        else if (serviceNameInternal == "Beautification" || serviceNameInternal == "Monument")
        {
            return colors.BeautificationBuilding;
        }
        else
        {
            return colors.PublicBuilding;
        }

    }

    private bool IsTargetBuilding(XMLBuilding b)
    {

        BuildingShapeRenderingParam param = RenderingManager.GetShapeRenderingParam(b.SteamId, b.ShortName, b.ItemClass, b.Service);

        if (BetaMapSession.Get(appRoot, xml).Config.HideRICO)
        {
            if (RICO_CATEGORY.Contains(param.ServiceForRender))
            {
                return false;
            }
        }

        return param.Visible;

    }



    internal static List<List<XMLBuilding>> GroupBySubBuilding(List<XMLBuilding> list)
    {
        List<List<XMLBuilding>> result = [];
        List<XMLBuilding> tempList = new(list);

        int passed = 0;
        while (tempList.Count > 0 && passed < tempList.Count)
        {

            XMLBuilding b = tempList[passed];

            if (string.IsNullOrEmpty(b.SubBuildingId) && string.IsNullOrEmpty(b.ParentBuildingId))
            {
                tempList.Remove(b);
                result.Add([b]);
                continue;
            }
            else if (string.IsNullOrEmpty(b.ParentBuildingId) && !string.IsNullOrEmpty(b.SubBuildingId))
            {
                tempList.Remove(b);
                List<XMLBuilding> addList = [b];

                List<XMLBuilding> newList = new(FindChild(tempList, b.Id)); ;
                foreach (XMLBuilding x in newList)
                {
                    addList.Add(x);
                    tempList.Remove(x);
                }
                passed -= newList.Count;
                result.Add(addList);
            }
            else
            {
                passed++;
            }

            if (passed < 0)
            {
                passed = 0;
            }
            if (passed >= tempList.Count)
            {
                break;
            }
        }
        if (tempList.Count != 0)
        {
            //  Log.Error(Properties.Resources.MsgSubBuildingRenderError);
            foreach (XMLBuilding b in tempList)
            {
                result.Add([b]);
                //      Log.Warn($"Parent not found Building Id={b.Id}");
            }
        }
        return result;
    }

    private static List<XMLBuilding> FindChild(List<XMLBuilding> list, int parentId)
    {
        List<XMLBuilding> result = [];
        foreach (XMLBuilding x in list)
        {
            if (x.ParentBuildingId == parentId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                result.Add(x);
                if (!string.IsNullOrEmpty(x.SubBuildingId))
                {
                    List<XMLBuilding> tempList = new(list);
                    result.AddRange(FindChild(tempList, x.Id));
                }
            }
        }
        return result;
    }
}
