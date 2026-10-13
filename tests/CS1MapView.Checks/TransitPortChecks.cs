using System.Xml.Serialization;
using CS2MapView.Beta;
using CS2MapView.CS1.Config;
using CS2MapView.CS1.Geometry;
using CS2MapView.CS1.Transit;
using CS2MapView.Data;
using CS2MapView.Drawing;
using CS2MapView.Drawing.Layer;
using CS2MapView.Drawing.Transport.CS1;
using CS2MapView.Form;
using CSLMapView.XML;
using SkiaSharp;

internal static class TransitPortChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void RunPolicies()
    {
        StationClusterChecks.Run(); CollinearStationChecks.Run(); NativeRouteChecks.RunPolicies();
        var serializer = new XmlSerializer(typeof(TransportRouteMapConfig));
        var migrated = (TransportRouteMapConfig)serializer.Deserialize(new StringReader(
            "<TransportRouteMapConfig><CombineNearbyDirections>true</CombineNearbyDirections><StationClusterRadius>123.5</StationClusterRadius><StationLinkDistance>77</StationLinkDistance></TransportRouteMapConfig>"))!;
        Check(migrated.StationClusterRadius == 123.5 && migrated.StationLinkDistance == 77, "Independent station distances failed migration");
        using var writer = new StringWriter(); serializer.Serialize(writer, migrated);
        Check(!writer.ToString().Contains("CombineNearbyDirections"), "Deferred route merger became active");
        Check(TransportMapBuilder.GetLinePenWidth(1, 0, false, .5) == 2 && TransportMapBuilder.GetLinePenWidth(1, 0, false, 2) == 3
            && Math.Abs(TransportMapBuilder.GetLinePenWidth(3, 2, true, 1) - 4.8) < 1e-9, "Transit stroke sizing changed");
    }
    internal static void RunSpringdale(BetaMapSession session, CSLExportXML raw, string reports)
    {
        var manager = session.TransportManager;
        Check(manager.TransportList.Count == 26 && manager.TransportList.Count == raw.TransportList.Count, "Service count changed");
        int legs = 0;
        foreach (var route in manager.TransportList)
        {
            var source = raw.TransportList.Single(t => t.Id == route.Id);
            Check(!route.HadLoadError, $"Service {route.Id} failed: {string.Join(";",manager.Diagnostics)}");
            Check(route.Stops.Select(s => s.NodeId).SequenceEqual(source.Stops.Select(s => s.Node)), $"Service {route.Id} reordered logical stops");
            for (int i = 0; i < route.Stops.Count; i++)
            {
                var stop = route.Stops[i]; var next = route.Stops[(i + 1) % route.Stops.Count];
                var original = raw.SegmentList.First(s => s.PathUnit != null && s.StartNodeId == stop.NodeId && s.EndNodeId == next.NodeId).PathUnit.SegmentIdList;
                var ids = original.Where((id,j) => j == 0 || id != original[j-1]);
                Check(stop.DrawRoute.Select(s => s.SegmentId).SequenceEqual(ids), $"Service {route.Id} truncated exported path {stop.NodeId}->{next.NodeId}");
                Check(stop.PointsToNextStop.Count > 0 && stop.PointsToNextStop.All(p => p.Points.Count >= 2), "Journey lost point geometry");
                Check(stop.PointsToNextStop.SelectMany(p => p.Points).All(p => float.IsFinite(p.X) && float.IsFinite(p.Z)), "Journey has non-finite points");
                legs++;
            }
        }
        StationClusterChecks.CheckSpringdale(manager, raw, reports);
        CollinearStationChecks.Springdale(manager, reports);
        var layout = manager.StationLayout;
        Check(layout.Points.Count == 114 && layout.Clusters.Count == 79 && layout.CandidateCount == 188 && layout.Links.Count == 6 && layout.Stations.Count == 73,
            $"Station layout changed: {layout.Points.Count} points, {layout.CandidateCount} candidates, {layout.Clusters.Count} symbols, {layout.Links.Count} links, {layout.Stations.Count} groups");
        StationBadgeChecks.Run(session);
        CheckHits(session);
        Console.WriteLine($"PASS native transit: 26 services, {legs} complete journeys, 16 monorail legs, 79 symbols/6 links/73 selectable groups");
    }
    private static void CheckHits(BetaMapSession session)
    {
        session.ClearHighlight();
        using (session.BuildStationCommand(new ViewContext())) { }
        var scale = session.Config.CanvasSize / 17280;
        var samples = session.StationHitTargets.SelectMany(t => t.Shapes.Select(shape => (Target:t, Point:new MapPoint((shape.Bounds.Left+shape.Bounds.Right)/2,(shape.Bounds.Top+shape.Bounds.Bottom)/2))))
            .Where(x => x.Target.Contains(x.Point,0) && session.StationHitTargets.Count(t => t.Contains(x.Point,0)) == 1).Take(30).ToArray();
        Check(samples.Length >= 15, "Actual hit-shape sample is incomplete"); int count = 0;
        foreach (float angle in new[] { 0f, .37f, MathF.PI/2 })
        foreach (float zoom in new[] { .4f, 1f, 2f })
        foreach (float dpi in new[] { 1f, 1.5f, 2f })
        foreach (var sample in samples)
        {
            var view = new ViewContext { WorldScaleFactor=.237f, ScaleFactor=zoom, Angle=angle, ViewLeftTop=(217,-384) };
            var world = new SKPoint((float)(sample.Point.X/scale-8640),(float)(sample.Point.Y/scale-8640));
            var surface = SKMatrix.CreateScale(dpi,dpi).MapPoint(view.ViewTransform.MapPoint(world));
            var restored = view.ViewTransform.Invert().MapPoint(new(surface.X/dpi,surface.Y/dpi));
            session.ClearHighlight(); session.SelectAtWorld(restored);
            Check(session.SelectedGroupId == sample.Target.Station.Id, "Hit ownership moved under crop/rotation/zoom/DPI");
            session.SelectAtWorld(restored); Check(session.SelectedGroupId == null, "Repeat click did not clear highlight"); count++;
        }
        session.ClearHighlight(); var hidden = session.TransportManager.TransportList[0]; var before = hidden.ViewerVisible;
        hidden.ViewerVisible=false; session.HighlightGroup(session.TransportManager.SelectableStations[0].Id); session.ClearHighlight();
        Check(!hidden.ViewerVisible && session.TransportManager.TransportList.All(r=>r.HighlightVisible), "Clear restored a manually hidden service"); hidden.ViewerVisible=before;
        session.Config.Railway.InfrastructureOnly=true; session.RefreshTransport();
        Check(session.SelectedGroupId==null && session.StationHitTargets.Count==0 && !session.SelectAtWorld(new SKPoint(0,0)), "Infrastructure-only mode kept station selection");
        session.Config.Railway.InfrastructureOnly=false;
        Console.WriteLine($"PASS {count} actual native station/badge/leader hits with repeated-click clearing and manual visibility preservation");
    }
    internal static void CheckTransitDisplaySwitches(BetaOutputHost host)
    {
        var session = BetaMapSession.Get(host, ((CS1MapType)host.Context.SourceMapType!).GetImportData());
        var config = session.Config;
        bool savedRoutes = config.RenderTransportRoute, savedStations = config.RenderTransport, savedInfrastructure = config.Railway.InfrastructureOnly;
        var savedModes = BetaSettings.Clone(config).RouteMapConfig;
        var view = host.Context.ViewContext.Clone(); view.Angle = 0; view.ScaleFactor = 3;
        string LogicalStops() => string.Join("|", session.TransportManager.TransportList.Select(r => r.Id + ":" + string.Join(",",r.Stops.Select(s => s.NodeId))));
        string StationMembers() => string.Join("|",session.TransportManager.StationLayout!.Clusters.Select(c=>string.Join(",",c.Points.SelectMany(p=>p.Stops).Select(s=>$"{s.TransportId}:{s.NodeId}"))));
        string logical = LogicalStops(), members = StationMembers();
        var dot = session.TransportManager.StationLayout!.Clusters.First(c => c.Span < .001);
        var world = new SKPoint((float)dot.A.X,(float)-dot.A.Y);
        view.ViewLeftTop = (world.X - 128/view.ViewScaleFromWorld,world.Y - 128/view.ViewScaleFromWorld);
        void BuildAndCheck(bool routes, bool stations, bool infrastructure = false)
        {
            config.RenderTransportRoute = routes; config.RenderTransport = stations; config.Railway.InfrastructureOnly = infrastructure;
            session.ClearHighlight(); session.RefreshTransport();
            using var layer = new CS1TransportLineLayerBuilder(host,session.Raw).BuildAsync(null,view).GetAwaiter().GetResult();
            var commands = ((BasicLayer)layer).DrawCommands.GetAllOrderedObjects().ToList();
            bool pathsExpected = routes && !infrastructure, markersExpected = stations && !infrastructure;
            Check(commands.OfType<PathDrawCommand>().Any() == pathsExpected,"Route visibility did not independently control the native path commands");
            Check(commands.OfType<NativePictureDrawCommand>().Any() == markersExpected,"Station visibility did not independently control native marker commands");
            Check((session.StationHitTargets.Count > 0) == markersExpected,"Station visibility did not independently control hit targets");
            Check(LogicalStops() == logical && StationMembers() == members,"Global route/station switches changed logical or display membership");
            using var bitmap = new SKBitmap(256,256); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
            layer.DrawLayer(canvas,view,view.ViewTransform.Invert().MapRect(new SKRect(0,0,256,256)),new SKRect(0,0,256,256));
            var center = bitmap.GetPixel(128,128);
            if (markersExpected) Check(center.Alpha > 220 && center.Red > 220 && center.Green > 220 && center.Blue > 220,
                "A visible native station marker was absent at its physical position");
            if (!pathsExpected && !markersExpected) Check(bitmap.Pixels.All(p => p.Alpha == 0),"The hidden transit layer still painted pixels");
            bool selected = session.SelectAtWorld(world);
            Check(selected == markersExpected && session.SelectedGroupId == (markersExpected ? (int?)dot.Station.Id : null),
                "Station selection did not follow marker visibility independently of routes");
            session.ClearHighlight();

        }
        try
        {
            foreach(var state in new[]{(Routes:true,Stations:true),(Routes:true,Stations:false),(Routes:false,Stations:true),(Routes:false,Stations:false)})
                BuildAndCheck(state.Routes,state.Stations);
            BuildAndCheck(true,true,true);
            config.Railway.InfrastructureOnly = false; config.RenderTransportRoute = false; config.RenderTransport = true;
            foreach(var property in config.RouteMapConfig.GetType().GetProperties().Where(p=>p.Name.StartsWith("Render")&&p.Name.EndsWith("Line")&&p.PropertyType==typeof(bool)))
                property.SetValue(config.RouteMapConfig,false);
            session.RefreshTransport();
            using(var stationsOnly = new CS1TransportLineLayerBuilder(host,session.Raw).BuildAsync(null,view).GetAwaiter().GetResult())
            {
                Check(session.StationHitTargets.Count > 0 && !((BasicLayer)stationsOnly).DrawCommands.GetAllOrderedObjects().OfType<PathDrawCommand>().Any(),
                    "Per-mode line switches incorrectly hid independent station symbols");
            }
            foreach(var property in config.RouteMapConfig.GetType().GetProperties().Where(p=>p.Name.StartsWith("Render")&&p.PropertyType==typeof(bool)&&!p.Name.EndsWith("Line")))
                property.SetValue(config.RouteMapConfig,false);
            config.RouteMapConfig.RenderTrainStation = true; session.RefreshTransport();
            using(var trainStops = new CS1TransportLineLayerBuilder(host,session.Raw).BuildAsync(null,view).GetAwaiter().GetResult())
                Check(session.StationHitTargets.Count > 0 && session.StationHitTargets.SelectMany(h=>h.Station.Stops).All(s=>s.TransportType=="Train") && LogicalStops()==logical,
                    "Per-mode stop switches lost logical routes or included disabled station modes");
            config.RenderTransport = false; config.RenderTransportRoute = true; config.RouteMapConfig.RenderMetroLine = true; session.RefreshTransport();
            using(var metroLines = new CS1TransportLineLayerBuilder(host,session.Raw).BuildAsync(null,view).GetAwaiter().GetResult())
                Check(((BasicLayer)metroLines).DrawCommands.GetAllOrderedObjects().OfType<PathDrawCommand>().Any() && session.StationHitTargets.Count==0 &&
                    session.TransportManager.TransportList.Where(r=>r.IsLineRenderingTarget).All(r=>r.TransportType=="Metro"),
                    "Per-mode route switches lost line-only rendering or enabled another mode");
        }
        finally
        {
            config.RenderTransportRoute = savedRoutes; config.RenderTransport = savedStations; config.Railway.InfrastructureOnly = savedInfrastructure;
            config.RouteMapConfig = savedModes; session.ClearHighlight(); session.RefreshTransport();
            using(session.BuildStationCommand(host.Context.ViewContext)) { }
        }
        Console.WriteLine("PASS independent transit display: four route/station states, actual native paths/markers/pixels/hits, marker-only selection, infrastructure override and per-mode line/stop switches");
    }
    internal static void CheckManualRouteVisibility(BetaOutputHost host)
    {
        var session = BetaMapSession.Get(host, ((CS1MapType)host.Context.SourceMapType!).GetImportData());
        var config = session.Config;
        bool savedRoutes = config.RenderTransportRoute, savedStations = config.RenderTransport, savedInfrastructure = config.Railway.InfrastructureOnly;
        var savedVisibility = session.TransportManager.TransportList.ToDictionary(r=>r.Id,r=>r.ViewerVisible);
        string LogicalPaths() => string.Join("|",session.TransportManager.TransportList.Select(r=>r.Id+":"+string.Join(";",r.Stops.Select(s=>s.NodeId+":"+string.Join(",",s.OriginalSegmentIds)))));
        string logical = LogicalPaths();
        void Build()
        {
            session.RefreshTransport();
            using var layer = new CS1TransportLineLayerBuilder(host,session.Raw).BuildAsync(null,host.Context.ViewContext).GetAwaiter().GetResult();
        }
        try
        {
            config.RenderTransportRoute = config.RenderTransport = true; config.Railway.InfrastructureOnly = false;
            var id = session.TransportManager.TransportList.First(r=>StationGrouping.ShowStops(r.TransportType,config.RouteMapConfig)&&r.Stops.Count>0).Id;
            foreach(var route in session.TransportManager.TransportList) route.ViewerVisible=false;
            Build();
            Check(session.TransportManager.StationLayout!.Points.Count==0 && session.TransportManager.SelectableStations.Count==0 && session.StationHitTargets.Count==0,
                "Bulk hide retained station membership or hit targets from manually hidden services");
            Check(LogicalPaths()==logical,"Bulk hide removed original logical stops or paths");
            var hiddenManager = session.TransportManager; session.RefreshTransport();
            Check(ReferenceEquals(hiddenManager,session.TransportManager),"Unchanged manual visibility invalidated the transport cache");
            session.TransportManager.TransportList.Single(r=>r.Id==id).ViewerVisible=true;
            Build();
            Check(session.StationHitTargets.Count>0 && session.TransportManager.StationLayout!.Points.SelectMany(p=>p.Stops).All(s=>s.TransportId==id) &&
                session.StationHitTargets.SelectMany(h=>h.Station.Stops).All(s=>s.TransportId==id),"Showing one service retained hidden-service station or badge ownership");
            Check(LogicalPaths()==logical,"Subset visibility changed original logical stops or paths");
            var selectedNode = session.TransportManager.SelectableStations[0].Stops[0].NodeId;
            var selectedLayout = session.TransportManager.StationLayout; var selectedManager = session.TransportManager;
            Check(session.HighlightStopNode(selectedNode),"A visible subset station could not be highlighted");
            session.RefreshTransport();
            Check(ReferenceEquals(selectedManager,session.TransportManager) && ReferenceEquals(selectedLayout,session.TransportManager.StationLayout),
                "Transient highlight filtering changed station membership or invalidated the manual-visibility cache");
            session.ClearHighlight();
            foreach(var route in session.TransportManager.TransportList) route.ViewerVisible=savedVisibility[route.Id];
            Build();
            var group = session.TransportManager.SelectableStations.First(station =>
                station.Stops.Select(s=>s.TransportId).Distinct().Count()>1 &&
                station.Stops.Where(s=>s.NodeId==station.Stops[0].NodeId).Select(s=>s.TransportId).Distinct().Count()==1);
            var previousNodes = group.Stops.Select(s=>s.NodeId).ToHashSet(); var firstNode = group.Stops[0].NodeId;
            var removedServices = session.TransportManager.TransportList.Where(r=>r.Stops.Any(s=>s.NodeId==firstNode)).Select(r=>r.Id).ToHashSet();
            Check(session.HighlightStopNode(firstNode),"The physical-stop highlight fixture was not selectable");
            foreach(var route in session.TransportManager.TransportList.Where(r=>removedServices.Contains(r.Id))) route.ViewerVisible=false;
            Build();
            Check(session.TransportManager.HighlightedStation!=null && session.TransportManager.HighlightedStation.Stops.Any(s=>previousNodes.Contains(s.NodeId)) &&
                session.TransportManager.HighlightedStation.Stops.All(s=>s.NodeId!=firstNode),"Hiding the first station member discarded the highlight instead of keeping a surviving physical stop");
            Check(session.TransportManager.TransportList.Where(r=>removedServices.Contains(r.Id)).All(r=>!r.ViewerVisible&&!r.EffectiveVisible),
                "Highlight restoration restored a manually hidden service");
            Check(LogicalPaths()==logical,"Highlight restoration changed original logical stops or paths");
        }
        finally
        {
            config.RenderTransportRoute = savedRoutes; config.RenderTransport = savedStations; config.Railway.InfrastructureOnly = savedInfrastructure;
            foreach(var route in session.TransportManager.TransportList) route.ViewerVisible=savedVisibility[route.Id];
            session.ClearHighlight(); session.RefreshTransport(); using(session.BuildStationCommand(host.Context.ViewContext)) { }
        }
        Console.WriteLine("PASS manual service visibility: bulk hide/subset show update station and hit membership, unchanged visibility keeps cache, transient highlights preserve geometry, surviving physical stops retain selection and logical routes stay complete");
    }
    internal static void CheckStaleLayerAnchor(BetaOutputHost host)
    {
        CheckTransitDisplaySwitches(host);
        CheckManualRouteVisibility(host);
        var session = BetaMapSession.Get(host, ((CS1MapType)host.Context.SourceMapType!).GetImportData());
        var original = host.Context.ViewContext.Clone();
        original.Angle = 0; original.ScaleFactor = .7f;
        session.ClearHighlight();
        var cluster = session.TransportManager.StationLayout.Clusters.First(c=>c.Span<.001);
        var world = new SKPoint((float)cluster.A.X,(float)-cluster.A.Y);
        using var layer = new CS1TransportLineLayerBuilder(host, session.Raw).BuildAsync(null, original).GetAwaiter().GetResult();
        try
        {
            foreach(float angle in new[]{.3f,MathF.PI/2,MathF.PI}) foreach(float zoom in new[]{1.4f,3f})
            {
                var vc = original.Clone();
                vc.Angle=angle; vc.ScaleFactor=zoom;
                var rotated=SKMatrix.CreateRotation(angle).MapPoint(world); var scale=vc.ViewScaleFromWorld;
                vc.ViewLeftTop=(rotated.X-150/scale,rotated.Y-150/scale);
                using var bitmap=new SKBitmap(300,300); using var canvas=new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
                layer.DrawLayer(canvas,vc,vc.ViewTransform.Invert().MapRect(new SKRect(0,0,300,300)),new SKRect(0,0,300,300));
                var anchor=vc.ViewTransform.MapPoint(world);
                Check(Math.Abs(anchor.X-150)<.01 && Math.Abs(anchor.Y-150)<.01,"The stale-view fixture placed its marker incorrectly");
                var center=bitmap.GetPixel(150,150);
                Check(center.Alpha>220 && center.Red>220 && center.Green>220 && center.Blue>220,
                    "Station interior drifted while using stale layer commands before rebuild");
            }
        }
        finally { using (session.BuildStationCommand(host.Context.ViewContext)) { } }
        Console.WriteLine("PASS native station anchors under 6 changed views before asynchronous rebuild");
    }
}
