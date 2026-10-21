using CS2MapView.Beta;
using CS2MapView.Config;
using CS2MapView.CS1.Config;
using CS2MapView.Data;
using CS2MapView.Drawing;
using CS2MapView.Drawing.Layer;
using CS2MapView.Drawing.Roads.CS1;
using CS2MapView.Form;
using CS2MapView.Theme;
using CS2MapView.Util.CS1;
using CSLMapView.XML;
using SkiaSharp;
using System.Text.Json;
using System.Xml.Linq;

internal static class RoadCrossingChecks
{
    private static readonly SKColor Red = new(210,40,30), Blue = new(30,70,220), Green = new(40,160,70);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static XMLVector3 Point(double x, double y, double height) => new((float)x,(float)height,(float)-y);
    private static XMLVector3 Evaluate(XMLVector3 a, XMLVector3 b, XMLVector3 c, XMLVector3 d, double t)
    {
        double s=1-t;
        float Axis(float av,float bv,float cv,float dv) => (float)(s*s*s*av+3*s*s*t*bv+3*s*t*t*cv+t*t*t*dv);
        return new(Axis(a.X,b.X,c.X,d.X),Axis(a.Y,b.Y,c.Y,d.Y),Axis(a.Z,b.Z,c.Z,d.Z));
    }
    private static RoadCrossingSource Road(int id, SKColor color, params XMLVector3[] points)
    {
        var start=new XMLNode {Id=id*2,Position=points[0],Service="Road",OnGround=true};
        var end=new XMLNode {Id=id*2+1,Position=points[^1],Service="Road",OnGround=true};
        var segment=new XMLSegment {Id=id,Name="Road",ItemClass="Road",Width=16,StartNode=start,EndNode=end,
            StartNodeId=start.Id,EndNodeId=end.Id,Points=points.ToList()};
        return new(segment,new(12,SKColors.Black,StrokeType.Round),new(8,color,StrokeType.Round));
    }
    private static RoadCrossingSource Cubic(int id, SKColor color, XMLVector3 a, XMLVector3 b, XMLVector3 c, XMLVector3 d)
        => Road(id,color,Enumerable.Range(0,5).Select(i=>Evaluate(a,b,c,d,i/4d)).ToArray());

    private sealed class Render : IDisposable
    {
        private readonly DrawCommandLookup commands=new(CS1MapType.CS1WorldRect);
        internal readonly RoadCrossingReport Report;
        internal Render(RoadCrossingSource[] sources, double cell=100, double refinement=1)
        {
            foreach(var source in sources)
            {
                var path=new SKPath();
                foreach(var piece in CS1RoadGeometry.OriginalPieces(source.Segment.Points))
                {
                    if(path.IsEmpty)path.MoveTo(piece.Start);
                    if(piece.IsCubic)path.CubicTo(piece.Control1,piece.Control2,piece.End);else path.LineTo(piece.End);
                }
                commands.Add(new PathDrawCommand {Path=path,StrokePaintFunc=(_,_)=>source.Border});
                commands.Add(new PathDrawCommand {Path=path,StrokePaintFunc=(_,_)=>source.Fill});
            }
            Report=CS1RoadCrossingPostProcessor.Append(sources,commands,cell,refinement);
        }
        internal SKColor Pixel(double x,double y,bool corrections=true)
        {
            using var bitmap=new SKBitmap(1,1,SKColorType.Bgra8888,SKAlphaType.Premul);
            using var canvas=new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);canvas.Translate(.5f-(float)x,.5f-(float)y);
            foreach(var command in commands.GetAllOrderedObjects())
                if(command is PathDrawCommand path)path.Draw(canvas,1,1);
                else if(corrections && command is NativePictureDrawCommand picture)picture.Draw(canvas);
            return bitmap.GetPixel(0,0);
        }
        internal void CheckVectorExport()
        {
            using var stream=new MemoryStream();
            using(var canvas=SKSvgCanvas.Create(new SKRect(-150,-150,150,150),stream))
                foreach(var command in commands.GetAllOrderedObjects())
                    if(command is PathDrawCommand path)path.Draw(canvas,1,1);
                    else if(command is NativePictureDrawCommand picture)picture.Draw(canvas);
            stream.Position=0;
            var svg=XDocument.Load(stream);
            Check(svg.Descendants().Any(e=>e.Name.LocalName=="clipPath")
                && svg.Descendants().Any(e=>e.Name.LocalName=="path")
                && !svg.Descendants().Any(e=>e.Name.LocalName=="image"),
                "Crossing corrections did not remain clipped vectors in native SVG export");
        }
        public void Dispose()=>commands.ClearContent(true);
    }

    internal static void RunPolicies()
    {
        // The ramp's average is 50 m, but it is only 10 m high at the left crossing.
        var ramp=Road(1,Red,Point(-100,0,0),Point(900,0,100));
        var left=Road(2,Blue,Point(0,-100,20),Point(0,100,20));
        var right=Road(3,Green,Point(800,-100,30),Point(800,100,30));
        using(var image=new Render([left,right,ramp]))
        {
            Check(image.Pixel(0,0,false)==Red,"Slope fixture does not expose the wrong baseline order");
            Check(image.Pixel(0,0)==Blue && image.Pixel(800,0)==Red,"Crossing correction used average height or redrew an entire ramp");
            Check(image.Report.RepairedPairs==2,"Distant ramp crossings were not handled independently");
        }
        // A cubic's elevation profile is nonlinear even though all five samples are available.
        var curved=Cubic(4,Red,Point(-120,0,0),Point(-120,-100,0),Point(120,100,0),Point(120,0,100));
        using(var image=new Render([left,curved]))
            Check(image.Pixel(0,0)==Blue,"Recovered cubic height was not evaluated locally");

        var a=Road(5,Red,Point(-100,0,0),Point(100,0,0));
        var b=Road(6,Green,Point(0,-100,10),Point(0,100,10));
        var c=Road(7,Blue,Point(-100,-100,20),Point(100,100,20));
        foreach(var sources in new[]{new[]{c,b,a},new[]{a,c,b},new[]{b,a,c}})
        using(var image=new Render(sources))
        {
            Check(image.Pixel(0,0)==Blue && image.Pixel(0,25)==Green && image.Pixel(25,0)==Red,
                "An overlapping correction painted over a higher road in a three-level crossing");
            image.CheckVectorExport();
        }
        using(var image=new Render([b,a]))
            Check(new[]{-6.2,-6.0,6.0,6.2}.All(y=>image.Pixel(0,y)==Green),
                "A previous lower-road antialias fringe remains across the upper road interior");
        // The same two segments exchange which one is higher at two separate crossings.
        var rising=Road(8,Red,Point(-100,0,0),Point(100,0,40));
        var arch=Cubic(9,Blue,Point(-100,-60,20),Point(-100,100,20),Point(100,100,20),Point(100,-60,20));
        foreach(double refinement in new[]{1d,30d})
        using(var image=new Render([arch,rising],refinement:refinement))
        {
            double t=(1-Math.Sqrt(.5))/2;
            var crossing=Evaluate(arch.Segment.Points[0],Point(-100,100,20),Point(100,100,20),arch.Segment.Points[^1],t);
            Check(image.Pixel(crossing.X,0)==Blue && image.Pixel(-crossing.X,0)==Red,
                "Opposite local orders for the same pair were collapsed into one global order");
        }

        foreach(double cell in new[]{50d,100d,200d})
        {
            var lower=Road(10,Red,Point(-250,0,0),Point(250,0,0));
            var upper=Road(11,Blue,Point(-250,2,12),Point(250,2,12));
            using var image=new Render([upper,lower],cell);
            Check(image.Report.OwnedPairs==1 && image.Report.CandidatePairs==1 && image.Report.LocalPairVisits>1,
                "A pair spanning several/negative cells was checked more than once or lost");
            Check(image.Pixel(0,1)==Blue,"Parallel road footprint overlap was missed");
        }
        var exaggeratedLower=Road(21,Red,Point(-100,0,0),Point(100,0,0))
            with {Border=new(60,SKColors.Black,StrokeType.Round),Fill=new(56,Red,StrokeType.Round)};
        var separateHigher=Road(22,Blue,Point(-100,25,10),Point(100,25,10))
            with {Border=new(60,SKColors.Black,StrokeType.Round),Fill=new(56,Blue,StrokeType.Round)};
        using(var image=new Render([separateHigher,exaggeratedLower],refinement:30))
            Check(image.Report.RepairedPairs==0,"Exaggerated preview widths turned physically separate streets into vertical crossings");
        var realHigher=Road(23,Blue,Point(0,-100,10),Point(0,100,10))
            with {Border=new(60,SKColors.Black,StrokeType.Round),Fill=new(56,Blue,StrokeType.Round)};
        using(var image=new Render([realHigher,exaggeratedLower],refinement:30))
            Check(image.Pixel(0,0)==Blue,"Preview filtering lost a real vertical crossing");
        var tangent=Cubic(12,Red,Point(-100,100,0),Point(-100d/3,-100d/3,0),Point(100d/3,-100d/3,0),Point(100,100,0));
        var tangentTop=Road(13,Blue,Point(-80,0,10),Point(80,0,10));
        using(var image=new Render([tangentTop,tangent])) Check(image.Pixel(0,0)==Blue,"Tangential curve contact was missed");

        var joinLeft=Road(14,Blue,Point(-100,0,10),Point(0,0,10));
        var joinRight=Road(15,Blue,Point(0,0,10),Point(100,0,10));
        joinRight.Segment.StartNode=joinLeft.Segment.EndNode;joinRight.Segment.StartNodeId=joinLeft.Segment.EndNodeId;
        var underJoin=Road(16,Red,Point(0,-100,0),Point(0,100,0));
        using(var image=new Render([joinLeft,joinRight,underJoin]))
            Check(new[]{-2,0,2}.All(x=>image.Pixel(x,0)==Blue),"Correction introduced an outline seam at a connected upper junction");
        var flat=Road(17,Blue,Point(0,-100,10.2),Point(0,100,10.2));
        using(var image=new Render([joinLeft,joinRight,flat]))
            Check(image.Report.RecordedCommands==0,"Near-level ordinary junctions received bridge treatment");

        CheckNativeBuilder();
        Console.WriteLine("PASS crossing post-process: local ramp/cubic heights, three levels, changing pair order, parallel/tangent footprints, unique cell ownership, native junction seams, near-level ties and road-only scope");
    }

    private static void CheckNativeBuilder()
    {
        var lower=Road(18,Red,Point(-100,0,0),Point(100,0,0)).Segment;
        var upper=Road(19,Blue,Point(0,-100,12),Point(0,100,12)).Segment;
        var rail=Road(20,Green,Point(-100,-100,24),Point(100,100,24)).Segment;
        rail.Name="Rail";rail.ItemClass="Train Line";
        lower.Name="Highway";
        var xml=new CSLExportXML {FileVersion="4.1",SegmentList=[upper,lower,rail],NodeList=[upper.StartNode,upper.EndNode,lower.StartNode,lower.EndNode,rail.StartNode,rail.EndNode]};
        xml.SegmentTypeList.Add(new XMLSegmentType {Name="Highway",Highway=true,Lanes=[new XMLLaneType {Type="Vehicle",VehicleType="Car",Direction="Forward",Width=3,Position=0}]});
        xml.SegmentTypeList.Add(new XMLSegmentType {Name="Road",Lanes=[new XMLLaneType {Type="Vehicle",VehicleType="Car",Direction="Forward",Width=3,Position=0}]});
        xml.SegmentTypeList.Add(new XMLSegmentType {Name="Rail",Lanes=[new XMLLaneType {Type="Vehicle",VehicleType="Train",Direction="Forward",Width=3,Position=0}]});
        var snapshot=xml.SegmentList.Select(s=>s.Points.Select(p=>(p.X,p.Y,p.Z)).ToArray()).ToArray();
        using var host=new BetaOutputHost(UserSettings.Default,new ApplicationConfig {SelectedStyle="carto-voyager",CanvasSize=4096});
        var style=BetaMapSession.Get(host,xml).CurrentMapStyle;
        var builder=new CS1RoadLayerBuilder(host,xml);
        using var layer=(BasicLayer)builder.BuildAsync(null).GetAwaiter().GetResult();
        using var bitmap=new SKBitmap(1,1);using var canvas=new SKCanvas(bitmap);
        canvas.Translate(.5f,.5f);
        foreach(var command in layer.DrawCommands.GetAllOrderedObjects())
            if(command is PathDrawCommand path)path.Draw(canvas,1,1);else if(command is NativePictureDrawCommand picture)picture.Draw(canvas);
        Check(bitmap.GetPixel(0,0)==style.Colors.Road,"Actual road builder did not append its crossing correction after highway/junction drawing");
        Check(builder.CrossingReport.RepairedPairs==1 && builder.CrossingReport.RecordedCommands>0,"Actual road builder omitted its post-process");
        Check(builder.CrossingReport.Roads==2 && builder.CrossingReport.Contacts.All(c=>c.FirstId!=rail.Id && c.SecondId!=rail.Id),
            "Road-only post-process included a railway segment");
        for(int i=0;i<xml.SegmentList.Count;i++)Check(snapshot[i].SequenceEqual(xml.SegmentList[i].Points.Select(p=>(p.X,p.Y,p.Z))),"Post-process changed source geometry");
    }

    internal static void RunReferenceMap(CSLExportXML raw,string reports,double canvasSize=4096)
    {
        var snapshot=raw.SegmentList.Select(s=>(s.Id,s.Points.Select(p=>(p.X,p.Y,p.Z)).ToArray())).ToArray();
        var measurements=new List<object>();
        var cases=canvasSize==4096
            ? new[]{("apple-light",4096d),("carto-voyager",4096d),("carto-positron",4096d),("apple-light",300d)}
            : new[]{("apple-light",canvasSize)};
        foreach(var (styleName,size) in cases)
        {
            using var host=new BetaOutputHost(UserSettings.Default,new ApplicationConfig {SelectedStyle=styleName,CanvasSize=size});
            var builder=new CS1RoadLayerBuilder(host,raw);
            using var layer=(BasicLayer)builder.BuildAsync(null).GetAwaiter().GetResult();
            var r=builder.CrossingReport;
            long possible=(long)r.Roads*(r.Roads-1)/2;
            Check(r.Roads>5000 && r.CandidatePairs<possible/50 && r.RepairedPairs>100 && r.RecordedCommands>0,
                "Reference map spatial filtering or road-crossing coverage failed");
            Check(size!=300 || r.ContactMasks<20000,"Low-resolution preview generated excessive subpixel crossing fragments");
            measurements.Add(new {Style=styleName,CanvasSize=size,r.Roads,r.LocalPairVisits,r.OwnedPairs,r.CandidatePairs,r.SpanTests,r.RepairedPairs,r.ContactMasks,r.CorrectedRoads,r.RecordedCommands,r.MaskFallbacks,r.ElapsedMilliseconds,
                Contacts=r.Contacts.Select(c=>new {c.FirstId,c.SecondId,c.X,c.Y,c.FirstHeight,c.SecondHeight,c.UpperId}).ToArray()});
            Console.WriteLine($"PASS Reference map road post-process {styleName} at {size}px: {r.CandidatePairs} candidate pairs, {r.RepairedPairs} repaired pairs, {r.RecordedCommands} cached commands; {r.ElapsedMilliseconds:F1} ms build");
        }
        for(int i=0;i<raw.SegmentList.Count;i++)Check(snapshot[i].Id==raw.SegmentList[i].Id && snapshot[i].Item2.SequenceEqual(raw.SegmentList[i].Points.Select(p=>(p.X,p.Y,p.Z))),"Reference map post-process mutated source IDs/heights/geometry");
        File.WriteAllText(Path.Combine(reports,canvasSize==4096 ? "road-crossing-performance.json" : "road-crossing-preview-performance.json"),JsonSerializer.Serialize(measurements,new JsonSerializerOptions {WriteIndented=true}));
    }
}
