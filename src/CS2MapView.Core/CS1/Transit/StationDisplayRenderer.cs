using System.Text.RegularExpressions;
using CS2MapView.CS1.Config;
using CS2MapView.CS1.Config.Styles;
using CS2MapView.CS1.Geometry;
using CS2MapView.Drawing;
using SkiaSharp;
namespace CS2MapView.CS1.Transit;

/// <summary>Station vectors and selectable ownership recorded directly in native Skia.</summary>
public static class StationDisplayRenderer
{
    public sealed class BadgeEntry { public string Text = ""; public List<TransportInfo> Routes = []; }
    public static List<BadgeEntry> BadgeEntries(IEnumerable<TransportInfo> routes, ApplicationConfig config)
    {
        var expressions = config.SampleRouteNumbering.Select(pattern => {
            try { return new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)); } catch (ArgumentException) { return null; }
        }).Where(regex => regex != null).ToList();
        var entries = new List<BadgeEntry>();
        foreach (var mode in routes.Where(r=>r.EffectiveVisible).GroupBy(r=>r.TransportType).OrderBy(g=>ModeOrder(g.Key)))
        {
            var fallback = new List<TransportInfo>();
            foreach (var route in mode.GroupBy(r=>r.Id).Select(g=>g.First()).OrderBy(r=>r.Id))
            {
                string? code=route.ViewerDefinedLineNo;
                if (string.IsNullOrWhiteSpace(code)) foreach (var regex in expressions)
                {
                    try { var match=regex!.Match(route.Name); if (match.Success && match.Groups.Count>1) {code=match.Groups[1].Value; break;} }
                    catch (RegexMatchTimeoutException) { }
                }
                if (string.IsNullOrWhiteSpace(code)) { fallback.Add(route); continue; }
                var entry=config.RouteMapConfig.UseMergedRouteNumberings ? entries.FirstOrDefault(e=>e.Text==code && e.Routes[0].TransportType==mode.Key) : null;
                if (entry==null) { entry=new BadgeEntry {Text=code}; entries.Add(entry); }
                entry.Routes.Add(route);
            }
            if (fallback.Count>0 && config.RouteMapConfig.ShowStationModeBadges) entries.Add(new BadgeEntry {Text=ModeCode(mode.Key),Routes=fallback});
        }
        return entries;
    }
    private static int ModeOrder(string mode)=>Array.IndexOf(new[]{"Train","Metro","Monorail","Tram","Bus","Trolleybus","Ship","Airplane"},mode);
    private static string ModeCode(string mode)=>mode switch {"Train"=>"T","Metro"=>"M","Monorail"=>"MR","Tram"=>"Tr","Bus"=>"B","Trolleybus"=>"Tb","Ship"=>"F","Airplane"=>"A",_=>mode};

    public static (IDrawCommand Command,List<StationHitTarget> Hits) Build(ApplicationConfig config,MapStyle style,TransportManager manager,SKMatrix canvasToLayer)
    {
        var layout=manager.StationLayout ??= StationDisplayLayout.Build(manager.TransportList,config.RouteMapConfig);
        var routes=manager.TransportList.ToDictionary(r=>r.Id);
        var clusters=config.ShowTransitLabels ? layout.Clusters.Where(c=>c.Points.Any(p=>p.Stops.Any(s=>routes[s.TransportId].EffectiveVisible))).ToList() : [];
        var visible=clusters.ToHashSet(); var links=layout.Links.Where(l=>visible.Contains(l.First)&&visible.Contains(l.Second)).ToList();
        double scale=config.CanvasSize/4096;
        var hits=clusters.Select(c=>c.Station).Distinct().ToDictionary(s=>s,s=>new StationHitTarget{Station=s});
        MapPoint Translate(MapPoint p)=>new((p.X+8640)*config.CanvasSize/17280,(8640-p.Y)*config.CanvasSize/17280);
        var symbols=clusters.ToDictionary(c=>c,c=>StationShape.Stroke(Translate(c.A),Translate(c.B),3.6*scale));
        var connectors=links.ToDictionary(l=>l,l=>StationShape.Stroke(Translate(l.A),Translate(l.B),1.3*scale));
        foreach(var cluster in clusters) hits[cluster.Station].Shapes.Add(symbols[cluster].Clone());
        foreach(var link in links)
        {
            if(link.First.Station==link.Second.Station) hits[link.First.Station].Shapes.Add(connectors[link].Clone());
            else {var middle=link.A+(link.B-link.A)*.5;
                hits[link.First.Station].Shapes.Add(StationShape.Stroke(Translate(link.A),Translate(middle),1.3*scale));
                hits[link.Second.Station].Shapes.Add(StationShape.Stroke(Translate(middle),Translate(link.B),1.3*scale));}
        }
        var badges=StationBadgeLayout.Build(clusters,manager.TransportList,config,symbols,connectors.Values);
        foreach(var badge in badges)
        {
            hits[badge.Station].Shapes.Add(StationShape.RoundedBox(badge.Position.Box,scale));
            hits[badge.Station].Shapes.Add(StationShape.Stroke(badge.Position.SymbolEnd,badge.Position.BadgeEnd,.9*scale));
        }
        var bounds=canvasToLayer.MapRect(new SKRect(0,0,(float)config.CanvasSize,(float)config.CanvasSize));
        using var recorder=new SKPictureRecorder(); var canvas=recorder.BeginRecording(bounds); canvas.SetMatrix(canvasToLayer);
        using var fill=new SKPaint {IsAntialias=true,Style=SKPaintStyle.Fill};
        using var pen=new SKPaint {IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeJoin=SKStrokeJoin.Round};
        if(manager.HighlightedStation!=null && hits.TryGetValue(manager.HighlightedStation,out var selected))
        {pen.Color=SKColors.DodgerBlue;pen.StrokeWidth=(float)(1.8*scale); foreach(var shape in selected.Shapes) canvas.DrawPath(shape.Path,pen);}
        pen.Color=SKColors.Black;pen.StrokeWidth=(float)(.3*scale);
        foreach(var badge in badges) canvas.DrawLine((float)badge.Position.SymbolEnd.X,(float)badge.Position.SymbolEnd.Y,(float)badge.Position.BadgeEnd.X,(float)badge.Position.BadgeEnd.Y,pen);
        fill.Color=new SKColor(41,44,48);
        foreach(var connector in connectors.Values) canvas.DrawPath(connector.Path,fill);
        foreach(var symbol in symbols.Values) canvas.DrawPath(symbol.Path,fill);
        fill.Color=SKColors.White;
        foreach(var link in links) {using var inner=StationShape.Stroke(Translate(link.A),Translate(link.B),.75*scale);canvas.DrawPath(inner.Path,fill);}
        foreach(var cluster in clusters) {using var inner=StationShape.Stroke(Translate(cluster.A),Translate(cluster.B),2.95*scale);canvas.DrawPath(inner.Path,fill);}
        foreach(var badge in badges) DrawBadge(canvas,badge,config,fill,pen);
        var picture=recorder.EndRecording();
        foreach(var badge in badges) badge.Dispose();
        foreach(var shape in symbols.Values.Concat(connectors.Values)) shape.Dispose();
        return (new NativePictureDrawCommand(picture,bounds),hits.Values.ToList());
    }
    private static void DrawBadge(SKCanvas canvas,StationBadgeLayout.Badge badge,ApplicationConfig config,SKPaint fill,SKPaint pen)
    {
        double scale=config.CanvasSize/4096;var box=badge.Position.Box;
        using var outline=StationShape.RoundedBox(box,scale);fill.Color=SKColors.White;canvas.DrawPath(outline.Path,fill);
        pen.Color=new SKColor(171,179,187);pen.StrokeWidth=(float)(.17*scale);canvas.DrawPath(outline.Path,pen);
        double x=box.Left+scale;
        for(int i=0;i<badge.Entries.Count;i++)
        {
            var label=badge.Labels[i];using var glyph=new SKPath(label.Path);var bounds=glyph.Bounds;
            glyph.Transform(SKMatrix.CreateTranslation((float)(x+(badge.Widths[i]-label.Width)/2),(float)((box.Top+box.Bottom-bounds.Top-bounds.Bottom)/2)));
            var colors=badge.Entries[i].Routes.Select(r=>(config.RouteMapConfig.AutoColoring?r.AutoColor:r.UserDefinedColor)??SKColors.Black).ToList();
            bounds=glyph.Bounds;
            for(int j=0;j<colors.Count;j++)
            {
                using var stripe=new SKPath();stripe.AddRect(new SKRect(bounds.Left+bounds.Width*j/colors.Count,bounds.Top-1,bounds.Left+bounds.Width*(j+1)/colors.Count,bounds.Bottom+1));
                using var piece=glyph.Op(stripe,SKPathOp.Intersect);fill.Color=colors[j];if(piece!=null)canvas.DrawPath(piece,fill);
            }
            x+=badge.Widths[i];
            if(i+1<badge.Entries.Count) {pen.Color=new SKColor(230,232,234);pen.StrokeWidth=(float)(.15*scale);canvas.DrawLine((float)x,(float)(box.Top+scale),(float)x,(float)(box.Bottom-scale),pen);}
        }
    }
}
