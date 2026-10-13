using CS2MapView.CS1.Config;
using CS2MapView.CS1.Config.Styles;
using CS2MapView.CS1.Transit;
using CSLMapView.XML;
using SkiaSharp;

internal static class NativeRouteChecks
{
    private static void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    private static XMLVector3 P(float x,float z=0)=>new(x,0,z);
    private static XMLNode Node(int id,float x)=>new(){Id=id,Position=P(x),OnGround=true,SubService="PublicTransportMetro"};
    private static XMLSegment Segment(int id,params float[] x)=>new(){Id=id,Name="Metro",ItemClass="Metro Track",Points=x.Select(v=>P(v)).ToList()};
    private static XMLSegment Leg(int id,int from,int to,params int[] physical)=>new(){Id=id,StartNodeId=from,EndNodeId=to,Name="Metro Line",ItemClass="Metro Line",PathUnit=new(){SegmentIdList=physical.ToList()},Points=[P(from==1?10:90),P(to==1?10:90)]};
    private static XMLTransport Service(string mode,params int[] stops)=>new(){Id=1,TransportType=mode,Name="Example",UserColor=new(){A=255,R=10,G=100,B=180},Stops=stops.Select(id=>new XMLStop{Node=id}).ToList()};
    private static string SourceSignature(CSLExportXML raw)=>string.Join("|",raw.SegmentList.Select(s=>$"{s.Id}:{string.Join(',',s.PathUnit?.SegmentIdList??[])}:{string.Join(';',s.Points.Select(p=>$"{p.X},{p.Z}"))}"));
    private static TransportManager Load(CSLExportXML raw,ApplicationConfig? config=null)
    {var manager=new TransportManager();manager.LoadInitialData(raw,config??new(),new MapStyle());return manager;}

    internal static void RunPolicies()
    {
        var raw=new CSLExportXML {NodeList=[Node(1,10),Node(2,90)],SegmentList=[Segment(10,0,20),Segment(20,80,50,20),Segment(30,80,100),Leg(100,1,2,10,10,20,30),Leg(101,2,1,30,20,10),Leg(102,1,1,10,20,30,20,10)],TransportList=[Service("Metro",1,2,1)]};
        string before=SourceSignature(raw);var config=new ApplicationConfig();config.RouteMapConfig.StopsMergingRange=25;config.RouteMapConfig.DetectEndLoop=true;
        var manager=Load(raw,config);var route=manager.TransportList.Single();
        Check(!route.HadLoadError&&route.Stops.Select(s=>s.NodeId).SequenceEqual(new[]{1,2,1}),"Native route decoder lost repeated logical stops");
        var first=route.Stops[0];var second=route.Stops[1];
        Check(first.DrawRoute.Select(s=>s.SegmentId).SequenceEqual(new[]{10,20,30}),"Native decoder changed physical segment order/duplicate handling");
        Check(first.PointsToNextStop.SelectMany(p=>p.Points).Select(p=>p.X).SequenceEqual(new float[]{10,20,20,50,80,80,90}),"Native path orientation or platform clipping failed");
        Check(second.PointsToNextStop.SelectMany(p=>p.Points).Select(p=>p.X).SequenceEqual(new float[]{90,80,80,50,20,20,10}),"Native return journey did not reverse the physical segment traversal");
        Check(first.DrawRoute[1].ForwardTransports.Contains(1)&&first.DrawRoute[1].BackwardTransports.Contains(1),"Shared segment direction ownership lost a return traversal");
        Check(!route.Stops[2].IsTerminalLoopHidden&&route.Stops[2].DrawRoute.Select(s=>s.SegmentId).SequenceEqual(new[]{10,20,30,20,10}),"Terminal-loop control truncated a large closed journey");
        Check(SourceSignature(raw)==before,"Native route decoding mutated raw path units/points");

        var small=new CSLExportXML {NodeList=[Node(1,10)],SegmentList=[Segment(10,0,20),Leg(100,1,1,10)],TransportList=[Service("Metro",1)]};
        var visible=Load(small).TransportList.Single().Stops.Single();
        Check(visible.PointsToNextStop.SelectMany(p=>p.Points).Any(p=>p.X!=10)&&!visible.IsTerminalLoopHidden,"Closed terminal traversal vanished while its switch was disabled");
        var hidden=Load(small,config).TransportList.Single().Stops.Single();
        Check(hidden.IsTerminalLoopHidden&&hidden.PointsToNextStop.SelectMany(p=>p.Points).All(p=>p.X==10)&&hidden.DrawRoute.Single().SegmentId==10&&hidden.OriginalSegmentIds.SequenceEqual(new[]{10}),"Safe terminal-loop suppression lost source identities or drew the hidden loop");

        foreach(string mode in new[]{"Ship","Airplane"})
        {
            var direct=new CSLExportXML {NodeList=[Node(1,10),Node(2,90)],SegmentList=[new(){Id=100,StartNodeId=1,EndNodeId=2,Name=mode+" Line",ItemClass=mode+" Line",Points=[P(10),P(50,20),P(90)]},new(){Id=101,StartNodeId=2,EndNodeId=1,Name=mode+" Line",ItemClass=mode+" Line",PathUnit=new(),Points=[P(90),P(50,-20),P(10)]}],TransportList=[Service(mode,1,2)]};
            var decoded=Load(direct).TransportList.Single();
            Check(!decoded.HadLoadError&&decoded.Stops.All(s=>s.PointsToNextStop.Single().Points.Count==3),"Direct "+mode+" journey without physical references was lost");
            Check(decoded.Stops[0].PointsToNextStop[0].Points[1].Z==20&&decoded.Stops[1].PointsToNextStop[0].Points[1].Z==-20,"Direct air/sea route curves were flattened");
        }
        var missing=new CSLExportXML {NodeList=[Node(1,10)],SegmentList=[Leg(100,1,1,999)],TransportList=[Service("Metro",1)]};
        var malformed=Load(missing);Check(malformed.TransportList.Single().HadLoadError&&malformed.Diagnostics.Any(s=>s.Contains("999")),"Missing physical segments were silently accepted");
        missing.SegmentList.Insert(0,Segment(999,0,float.NaN));Check(Load(missing).TransportList.Single().HadLoadError,"Non-finite route geometry was accepted");

        var firstCode=new TransportInfo{Id=1,Name="A",TransportType="Metro",ViewerDefinedLineNo="X1"};var secondCode=new TransportInfo{Id=2,Name="B",TransportType="Metro",ViewerDefinedLineNo="X1"};
        config.RouteMapConfig.ShowStationModeBadges=false;config.RouteMapConfig.UseMergedRouteNumberings=true;
        Check(StationDisplayRenderer.BadgeEntries([firstCode,secondCode],config).Single().Routes.Count==2,"Merged route codes lost serving services");
        config.RouteMapConfig.UseMergedRouteNumberings=false;
        Check(StationDisplayRenderer.BadgeEntries([firstCode,secondCode],config).Count==2,"Unmerged route codes remained merged");
        var routeColors=new MapStyle();routeColors.Colors.RouteMapColors=[SKColors.Red,SKColors.Blue];
        raw.TransportList.Add(new(){Id=2,Name="Other",TransportType="Metro",Stops=[new(){Node=1},new(){Node=2}]});
        var colors=new TransportManager();colors.LoadInitialData(raw,config,routeColors);
        Check(colors.TransportList[0].AutoColor!=colors.TransportList[1].AutoColor,"Automatic palette gave shared services the same available colour");
        Console.WriteLine("PASS native route adapter: repeated stops, ordered/reversed segments, exact platform clipping, untouched XML, direct ferry/air curves, malformed paths, safe terminal loops, route-code merging and shared palette colours");
    }
}
