using System.Xml.Serialization;
using CS2MapView.Beta;
using CS2MapView.CS1.Config;
using CS2MapView.CS1.Geometry;
using CS2MapView.CS1.Transit;
using SkiaSharp;

internal static class StationBadgeChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    internal static void Run(BetaMapSession session)
    {
        var config=session.Config;var manager=session.TransportManager;var layout=manager.StationLayout!;double scale=config.CanvasSize/4096;
        MapPoint Translate(MapPoint p)=>new((p.X+8640)*config.CanvasSize/17280,(8640-p.Y)*config.CanvasSize/17280);
        var symbols=layout.Clusters.ToDictionary(c=>c,c=>StationShape.Stroke(Translate(c.A),Translate(c.B),3.6*scale));
        var links=layout.Links.Select(l=>StationShape.Stroke(Translate(l.A),Translate(l.B),1.3*scale)).ToList();
        bool splitSetting=config.RouteMapConfig.SplitStationBadgesByCluster;bool letters=config.RouteMapConfig.ShowStationModeBadges;
        string membership=string.Join("|",layout.Clusters.Select(c=>string.Join(",",c.Points.SelectMany(p=>p.Stops).Select(s=>$"{s.TransportId}:{s.NodeId}"))));
        try
        {
            config.RouteMapConfig.SplitStationBadgesByCluster=config.RouteMapConfig.ShowStationModeBadges=true;
            var badges=StationBadgeLayout.Build(layout.Clusters,manager.TransportList,config,symbols,links);
            try
            {
                Check(badges.Count==79,"Native split badge count changed");
                var build=StationDisplayRenderer.Build(config,session.CurrentMapStyle,manager,SKMatrix.Identity);
                using var command=build.Command;
                try
                {
                    foreach(var badge in badges)
                    {
                        var cluster=badge.Clusters.Single();var a=Translate(cluster.A);var b=Translate(cluster.B);var p=badge.Position;var axis=b-a;
                        var ids=cluster.Points.SelectMany(point=>point.Stops).Select(stop=>stop.TransportId).Distinct().Order();
                        Check(ids.SequenceEqual(badge.Entries.SelectMany(e=>e.Routes).Select(r=>r.Id).Order()),"Native badge advertises wrong service");
                        if(axis.Length<1e-6)
                        {var radius=p.SymbolEnd-a;var leader=p.BadgeEnd-p.SymbolEnd;Check(Math.Abs(radius.Length-1.8*scale)<1e-6&&Math.Abs(MapVector.CrossProduct(radius,leader))<1e-6&&radius*leader>0,"Dot leader is not radial/perpendicular");}
                        else
                        {axis.Normalize();var first=a-axis*(1.35*scale);var last=b+axis*(1.35*scale);bool atFirst=(p.SymbolEnd-first).Length<1e-6;
                         Check(atFirst||(p.SymbolEnd-last).Length<1e-6,"Bar leader misses actual rounded tip");
                         Check((p.BadgeEnd-p.SymbolEnd).Length<=(p.BadgeEnd-(atFirst?last:first)).Length+1e-6,"Bar leader chooses farther rounded tip");}
                        Check(symbols[cluster].Contains(p.SymbolEnd,.01*scale),"Detached native leader endpoint");
                        using var box=StationShape.Box(p.Box);Check(!symbols.Values.Any(shape=>shape.Intersects(box)),"Native badge covers a station symbol");
                        var owner=build.Hits.Single(hit=>hit.Station==badge.Station);
                        Check(owner.Contains(new((p.Box.Left+p.Box.Right)/2,(p.Box.Top+p.Box.Bottom)/2),0),"Native badge misses its selectable owner");
                        Check(owner.Contains(p.SymbolEnd+(p.BadgeEnd-p.SymbolEnd)*.5,0),"Native leader misses its selectable owner");
                    }
                }
                finally{foreach(var hit in build.Hits)hit.Dispose();}
                for(int i=0;i<badges.Count;i++)for(int j=i+1;j<badges.Count;j++)Check(!badges[i].Position.Box.IntersectsWith(badges[j].Position.Box),"Native station badges overlap");
            }
            finally{foreach(var badge in badges)badge.Dispose();}
            config.RouteMapConfig.SplitStationBadgesByCluster=false;
            var combined=StationBadgeLayout.Build(layout.Clusters,manager.TransportList,config,symbols,links);
            try{Check(combined.Count==73,"Native combined badge count changed");foreach(var badge in combined)Check(badge.Clusters.All(c=>c.Station==badge.Station)&&badge.Clusters.Contains(badge.Position.Cluster),"Combined badge attaches to unrelated local group");}
            finally{foreach(var badge in combined)badge.Dispose();}
            config.RouteMapConfig.ShowStationModeBadges=false;
            var hidden=StationBadgeLayout.Build(layout.Clusters,manager.TransportList,config,symbols,links);try{Check(hidden.Count==0,"Hidden mode letters left badges/leaders");}finally{foreach(var badge in hidden)badge.Dispose();}
            var route=manager.TransportList.First(r=>layout.Points.SelectMany(p=>p.Stops).Any(s=>s.TransportId==r.Id));var oldCode=route.ViewerDefinedLineNo;route.ViewerDefinedLineNo="X1";
            try{var explicitCodes=StationBadgeLayout.Build(layout.Clusters,manager.TransportList,config,symbols,links);try{Check(explicitCodes.Count>0&&explicitCodes.All(b=>b.Entries.All(e=>e.Text=="X1")),"Explicit code vanished with mode letters disabled");}finally{foreach(var badge in explicitCodes)badge.Dispose();}}
            finally{route.ViewerDefinedLineNo=oldCode;}
            Check(membership==string.Join("|",layout.Clusters.Select(c=>string.Join(",",c.Points.SelectMany(p=>p.Stops).Select(s=>$"{s.TransportId}:{s.NodeId}")))),"Badge layout changed cluster/route membership");
            using var writer=new StringWriter();new XmlSerializer(typeof(TransportRouteMapConfig)).Serialize(writer,config.RouteMapConfig);
            var restored=(TransportRouteMapConfig)new XmlSerializer(typeof(TransportRouteMapConfig)).Deserialize(new StringReader(writer.ToString()))!;
            Check(!restored.SplitStationBadgesByCluster&&!restored.ShowStationModeBadges,"Native badge switches did not persist independently");
        }
        finally{config.RouteMapConfig.SplitStationBadgesByCluster=splitSetting;config.RouteMapConfig.ShowStationModeBadges=letters;foreach(var shape in symbols.Values.Concat(links))shape.Dispose();}
        Console.WriteLine("PASS native badge geometry: 79 split/73 combined, exact serving routes, radial/tip and nearest leaders, native outline endpoints, no overlaps, clickable badge/leader ownership, mode-letter and explicit-code switches");
    }
}
