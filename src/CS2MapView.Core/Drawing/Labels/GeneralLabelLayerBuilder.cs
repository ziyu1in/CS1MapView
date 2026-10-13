using CS2MapView.Data;
using CS2MapView.Drawing.Layer;
using CS2MapView.Theme;
using CS2MapView.Util;
using Gfw.Common;
using SkiaSharp;

namespace CS2MapView.Drawing.Labels
{
    public class GeneralLabelLayerBuilder : IRebuildOnResizeLayerBuilder, IRebuildOnRotateLayerBuilder
    {
        private ICS2MapViewRoot AppRoot { get; init; }
        private RotatedLayer ResultLayer { get; init; }

        private LabelContentsManager LabelContentManager { get; init; }
        private ViewContext BuildViewContext;

        private readonly bool IsDebug = false;

        public LoadProgressInfo.Process ProcessType => LoadProgressInfo.Process.BuildLabels;

        internal GeneralLabelLayerBuilder(ICS2MapViewRoot appRoot, ViewContext vc, LabelContentsManager manager)
        {


            AppRoot = appRoot;

            LabelContentManager = manager;
            BuildViewContext = vc.Clone();
            ResultLayer = new RotatedLayer(AppRoot, ILayer.LayerNameLabels, vc.TextWorldRect, this);

        }


        public Task<ILayer> BuildAsync(LoadProgressInfo? loadProgressInfo)
        {
            return Task.Run<ILayer>(() =>
            {
                lock (ResultLayer.DrawObjectsLock)
                {
                    Build();
                }
                loadProgressInfo?.Progress(this, LoadProgressInfo.Process.BuildLabels, 1f, null);
                return ResultLayer;

            });
        }

        public Task ResizeAsync(LoadProgressInfo? loadProgressInfo, ViewContext vc)
        {
            return Task.Run<ILayer>(() =>
            {
                lock (ResultLayer.DrawObjectsLock)
                {
                    BuildViewContext = vc.Clone();
                    ResultLayer.ClearAndResizeCommandsList(vc.TextWorldRect, true);

                    Build();
                }

                loadProgressInfo?.Progress(this, LoadProgressInfo.Process.BuildLabels, 1f, null);
                return ResultLayer;

            });
        }

        public Task RotateAsync(LoadProgressInfo? loadProgressInfo, ViewContext vc) => ResizeAsync(loadProgressInfo, vc);


        private void DisplayDebugBounds(AbstractMapLabel label)
        {
            if (IsDebug)
            {
                var debugCmd = new PathDrawCommand
                {
                    Path = SKPathEx.RectToPath(label.Bounds),
                    StrokePaintFunc = (f, g) => new SKPaintCache.StrokeKey(1f, SKColors.Red, StrokeType.Flat)
                };
                ResultLayer.DrawCommands.Add(debugCmd);
            }
        }

        private void Build()
        {

            //コマンド生成
            lock (ResultLayer.DrawObjectsLock)
            {
                lock (LabelContentManager.ContentsLock)
                {
                    var allObj = LabelContentManager.Contents.GetAllOrderedObjects().Where(t => !t.Yielded);
                    var stringTheme = AppRoot.Context.StringTheme!;
                    var colors = AppRoot.Context.Theme.Colors!.StringColors!;
                    SetStreetNameDrawCommands(allObj.SelectNotNull(t => t as StreetNameLabel)!, stringTheme, colors);
                    SetBuildingDrawCommands(allObj.SelectNotNull(t => t as BuildingNameLabel)!, stringTheme, colors);
                    SetMapSymbolDrawCommands(allObj.SelectNotNull(t => t as MapSymbol)!);
                    SetDistrictNameDrawCommands(allObj.SelectNotNull(t => t as DistrictNameLabel)!, stringTheme, colors);
                    SetParkNameDrawCommands(allObj.SelectNotNull(t => t as ParkNameLabel)!, stringTheme, colors);

                }
            }
        }

        // Labels live in the upstream rotated/text layer, whose coordinates already
        // contain the view scale. Match their actual measured pixel size before
        // drawing, including the CS1 metre-based profile, instead of using a 1:1
        // style that disagrees with the measured bounds at zoom/export scales.
        private StringStyle ToDisplayStyle(StringStyle style)
        {
            var vc = BuildViewContext;
            Width Pixels(Width value)
            {
                var size = (value.StrokeWidthByScale(vc.ScaleFactor, vc.WorldScaleFactor) ?? 0) * vc.ViewScaleFromWorld;
                return new Width(size, 0, size, size, 0);
            }
            return new StringStyle
            {
                FontFamily = style.FontFamily,
                FontSize = Pixels(style.FontSize),
                Stroke = style.Stroke is null ? null : new StrokeStyle(Pixels(style.Stroke.Width), style.Stroke.DashEffect, style.Stroke.StrokeType)
            };
        }

        private void SetMapSymbolDrawCommands(IEnumerable<MapSymbol> mapSymbols)
        {
            foreach (var m in mapSymbols)
            {
                var cmd = new SKPictureDrawCommand
                {
                    BoundingRect = m.Bounds,
                    Location = new SKPoint(m.DisplayPosition.X - m.Size.width / 2f, m.DisplayPosition.Y - m.Size.height / 2f),
                    Picture = m.Svg.Picture!,
                    DisposeRequired = false,
                    Scale = m.ImageScale
                };
                ResultLayer.DrawCommands.Add(cmd);
                DisplayDebugBounds(m);
            }

        }

        private void SetStreetNameDrawCommands(IEnumerable<StreetNameLabel> labels, StringTheme theme, StringColors colors)
        {
            var textStyle = theme.StreetName;
            var textStylec = textStyle is null ? null
                : new TextStyleWithColor(ToDisplayStyle(textStyle), colors.StreetNameFill, colors.StreetNameStroke);

            if (textStylec is null)
            {
                return;
            }
            SKPaintCache.TextFillKey? fillPaintFunc(float s, float w) => textStylec!.ToFillCacheKey(1f, 1f);
            SKPaintCache.TextStrokeKey? strokePaintFunc(float s, float w) => textStylec!.ToStrokeCacheKey(1f, 1f);

            foreach (var l in labels)
            {
                if (string.IsNullOrEmpty(l.Text) || l.PathPoints is null)
                {
                    continue;
                }
                SKPath p = new();
                if (l.PathType == AbstractTextOnPathLabel.PathPointType.CubicBesier)
                {
                    p.MoveTo(l.PathPoints[0]);
                    p.CubicTo(l.PathPoints[1], l.PathPoints[2], l.PathPoints[3]);
                }
                else
                {
                    p.MoveTo(l.PathPoints[0]);
                    l.PathPoints.TakeLast(l.PathPoints.Length - 1).ForEach(p.LineTo);

                }
                var cmd = new TextOnPathDrawCommand
                {
                    Path = p,
                    Text = l.Text,
                    TextFillPaintFunc = fillPaintFunc,
                    TextStrokePaintFunc = strokePaintFunc,
                    Ascent = l.Ascent,
                    TextOffset = l.TextOffset
                };
                ResultLayer.DrawCommands.Add(cmd);

                DisplayDebugBounds(l);

            }

        }

        private void SetTextDrawCommands(IEnumerable<AbstractTextLabel> labels, TextStyleWithColor twc)
        {
            SKPaintCache.TextFillKey? fillPaintFunc(float s, float w) => twc.ToFillCacheKey(1f, 1f);
            SKPaintCache.TextStrokeKey? strokePaintFunc(float s, float w) => twc.ToStrokeCacheKey(1f, 1f);

            foreach (var b in labels)
            {
                if (string.IsNullOrEmpty(b.Text))
                {
                    continue;
                }
                var cmd = new TextDrawCommand
                {
                    Location = new SKPoint(b.Bounds.Left, b.Bounds.Top - b.Ascent),
                    Text = b.Text,
                    BoundingRect = b.Bounds,
                    TextFillPaintFunc = fillPaintFunc,
                    TextStrokePaintFunc = strokePaintFunc,
                };
                ResultLayer.DrawCommands.Add(cmd);
                DisplayDebugBounds(b);
            }
        }

        private void SetBuildingDrawCommands(IEnumerable<BuildingNameLabel> buildings, StringTheme stringTheme, StringColors colors)
        {
            var buildingTextStyle = stringTheme.BuildingName;
            var buildingTextStylec = buildingTextStyle is null ? null
                : new TextStyleWithColor(ToDisplayStyle(buildingTextStyle), colors.BuildingNameFill, colors.BuildingNameStroke);
            if (buildingTextStylec is null) { return; }
            SetTextDrawCommands(buildings, buildingTextStylec);
        }

        private void SetDistrictNameDrawCommands(IEnumerable<DistrictNameLabel> districts, StringTheme stringTheme, StringColors colors)
        {
            var tw = stringTheme.DistrictName;
            var twc = tw is null ? null :
                new TextStyleWithColor(ToDisplayStyle(tw), colors.DistrictNameFill, colors.DistrictNameStroke);
            if (twc is null)
            {
                return;
            }
            SetTextDrawCommands(districts, twc);
        }

        private void SetParkNameDrawCommands(IEnumerable<ParkNameLabel> parks, StringTheme stringTheme, StringColors colors)
        {
            var style = stringTheme.ParkName ?? stringTheme.DistrictName;
            if (style is null)
                return;
            SetTextDrawCommands(parks, new TextStyleWithColor(ToDisplayStyle(style),
                colors.ParkNameFill ?? colors.DistrictNameFill,
                colors.ParkNameStroke ?? colors.DistrictNameStroke));
        }

    }
}
