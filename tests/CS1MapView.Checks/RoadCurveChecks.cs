using CS2MapView.Beta;
using CS2MapView.Config;
using CS2MapView.CS1.Config;
using CS2MapView.Drawing;
using CS2MapView.Drawing.Layer;
using CS2MapView.Drawing.Roads.CS1;
using CS2MapView.Form;
using CS2MapView.Import.CS1;
using CS2MapView.Util;
using CS2MapView.Util.CS1;
using CSLMapView.XML;
using SkiaSharp;

internal static class RoadCurveChecks
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static XMLVector3 Evaluate(XMLVector3 a, XMLVector3 b, XMLVector3 c, XMLVector3 d, double t)
    {
        double s = 1 - t;
        float Axis(float av, float bv, float cv, float dv)
            => (float)(s * s * s * av + 3 * s * s * t * bv + 3 * s * t * t * cv + t * t * t * dv);
        return new(Axis(a.X, b.X, c.X, d.X), Axis(a.Y, b.Y, c.Y, d.Y), Axis(a.Z, b.Z, c.Z, d.Z));
    }

    private static List<XMLVector3> Samples(XMLVector3 a, XMLVector3 b, XMLVector3 c, XMLVector3 d)
        => Enumerable.Range(0, 5).Select(i => Evaluate(a, b, c, d, i / 4d)).ToList();

    private static SKPoint Map(XMLVector3 p) => new(p.X, -p.Z);
    private static double Distance(SKPoint a, SKPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static int VerbCount(SKPath path, SKPathVerb target)
    {
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        int count = 0;
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done) if (verb == target) count++;
        return count;
    }

    internal static void RunPolicies()
    {
        foreach (var controls in new XMLVector3[][] {
            [new(0,0,0), new(0,0,300), new(300,0,300), new(300,0,0)],
            [new(-100,12,0), new(-40,40,100), new(60,-12,-100), new(100,0,0)],
            [new(0,0,0), new(-100,0,300), new(100,0,300), new(0,0,0)],
            [new(7000,5,-7000), new(7100,10,-7100), new(7200,15,-6900), new(7300,20,-7000)] })
        {
            var samples = Samples(controls[0], controls[1], controls[2], controls[3]);
            Check(CS1RoadGeometry.TryRecover(samples, out var recovered), "Exporter-compatible cubic was rejected");
            Check(recovered.Start == Map(samples[0]) && recovered.End == Map(samples[4]), "Curve moved an endpoint node");
            for (int i = 0; i <= 100; i++)
            {
                var expected = Map(Evaluate(controls[0], controls[1], controls[2], controls[3], i / 100d));
                var actual = Map(Evaluate(new(recovered.Start.X, 0, -recovered.Start.Y),
                    new(recovered.Control1.X, 0, -recovered.Control1.Y), new(recovered.Control2.X, 0, -recovered.Control2.Y),
                    new(recovered.End.X, 0, -recovered.End.Y), i / 100d));
                Check(Distance(expected, actual) < .002, "Recovered curve deviated from the known source cubic between samples");
            }
            samples[2].Y += .02f;
            Check(!CS1RoadGeometry.TryRecover(samples, out _), "Midpoint elevation mismatch passed the one-centimetre guard");
        }
        var corner = new List<XMLVector3> { new(0,0,0), new(30,0,0), new(30,0,30), new(60,0,30), new(60,0,60) };
        Check(!CS1RoadGeometry.TryRecover(corner, out _), "Unrelated five-point corners were smoothed");
        var malformed = Samples(new(0,0,0), new(0,0,100), new(100,0,100), new(100,0,0));
        malformed[1].X = float.NaN;
        Check(!CS1RoadGeometry.TryRecover(malformed, out _), "Non-finite curve was accepted");
        Check(!CS1RoadGeometry.TryRecover([new(0,0,0), new(1,0,1)], out _), "Two-point line was treated as a sampled cubic");
        CheckJoining();
        CheckNativeRendering();
        Console.WriteLine("PASS road curves: dense source-cubic agreement, midpoint guard, corners/lines, reversed multi-segment joins, native ground/tunnel/elevated strokes and curved junction fills");
    }

    private static XMLNode Node(int id, XMLVector3 point, int mode = 0)
        => new() { Id = id, Position = point, Service = "Road", OnGround = mode == 0, UnderGround = mode == 1 };

    private static XMLSegment Segment(int id, XMLNode start, XMLNode end, List<XMLVector3> points, string name = "Road")
        => new() { Id = id, Name = name, ItemClass = "Road", Width = 16, StartNodeId = start.Id, EndNodeId = end.Id,
            StartNode = start, EndNode = end, Points = points };

    private static void CheckJoining()
    {
        // Exercise both traversal directions, every original segment orientation,
        // and the deliberate corner between the second cubic and the straight road.
        foreach (bool hierarchy in new[] { false, true })
        for (int mask = 0; mask < 8; mask++)
        foreach (bool reorder in new[] { false, true })
        {
            var nodes = new[] { Node(1,new(0,0,0)), Node(2,new(100,0,0)), Node(3,new(200,0,0)), Node(4,new(280,0,100)) };
            var first = Segment(1,nodes[0],nodes[1],Samples(nodes[0].Position,new(0,0,80),new(100,0,80),nodes[1].Position));
            var second = Segment(2,nodes[1],nodes[2],Samples(nodes[1].Position,new(100,0,-80),new(200,0,-80),nodes[2].Position));
            var last = Segment(3,nodes[2],nodes[3],[nodes[2].Position,nodes[3].Position]);
            var segments = new[] { first, second, last };
            foreach (var segment in segments.Where((_, i) => (mask & (1 << i)) != 0))
            {
                segment.Points.Reverse();
                (segment.StartNodeId,segment.EndNodeId)=(segment.EndNodeId,segment.StartNodeId);
                (segment.StartNode,segment.EndNode)=(segment.EndNode,segment.StartNode);
            }
            var originals = segments.Select(s => s.Points.Select(p => (p.X,p.Y,p.Z)).ToArray()).ToArray();
            var xml = new CSLExportXML { NodeList = nodes.ToList(), SegmentList = reorder ? [second,last,first] : segments.ToList() };
            var geometry = new CS1RoadGeometry();
            var view = CS1XMLImporter.CreateLabelView(xml, hierarchy, geometry);
            Check(view.SegmentList.Count == 1, "Compatible road chain no longer joined");
            var joined = view.SegmentList[0];
            var pieces = geometry.Pieces(joined);
            Check(pieces.Count == 3 && pieces.Count(p => p.IsCubic) == 2, "Joining lost original cubic/line boundaries");
            Check(pieces[0].Start == Map(joined.Points[0]) && pieces[^1].End == Map(joined.Points[^1]), "Joining/reversal moved curve endpoints");
            Check(pieces.Zip(pieces.Skip(1)).All(pair => pair.First.End == pair.Second.Start), "Joined curve pieces were disconnected");
            foreach (var segment in segments.Take(2))
            {
                Check(CS1RoadGeometry.TryRecover(segment.Points, out var original), "Source cubic was lost");
                Check(pieces.Contains(original) || pieces.Contains(original.Reversed()), "Joining changed an original curve's handles");
            }
            Check(pieces.Any(p => !p.IsCubic && (p.Start == Map(nodes[2].Position) || p.End == Map(nodes[2].Position))), "Deliberate corner was smoothed across segments");
            using var path = geometry.CreatePath(joined);
            Check(VerbCount(path, SKPathVerb.Cubic) == 2 && VerbCount(path, SKPathVerb.Line) == 1 && VerbCount(path, SKPathVerb.Move) == 1,
                "Native path flattened or refitted the joined chain");
            for (int i = 0; i < segments.Length; i++)
                Check(originals[i].SequenceEqual(segments[i].Points.Select(p => (p.X,p.Y,p.Z))), "Display curve construction mutated source points");
        }
    }

    private static void CheckNativeRendering()
    {
        var xml = new CSLExportXML();
        for (int mode = 0; mode < 3; mode++)
        {
            float z = -600 * mode, y = mode == 1 ? -10 : mode == 2 ? 10 : 0;
            var start = Node(mode * 2 + 1, new(0,y,z),mode);
            var end = Node(mode * 2 + 2, new(300,y,z),mode);
            xml.NodeList.AddRange([start,end]);
            xml.SegmentList.Add(Segment(mode+1,start,end,Samples(start.Position,new(0,y,z-300),new(300,y,z-300),end.Position)));
        }
        // A surface highway/local junction forces the road fill-repair pass.
        var stubEnd = Node(7,new(-100,0,0));
        xml.NodeList.Add(stubEnd);
        xml.SegmentTypeList.Add(new XMLSegmentType { Name = "Highway", Highway = true });
        xml.SegmentList.Add(Segment(4,xml.NodeList[0],stubEnd,[xml.NodeList[0].Position,stubEnd.Position],"Highway"));
        var geometry = new CS1RoadGeometry();
        var view = CS1XMLImporter.CreateLabelView(xml,true,geometry);
        var road = view.SegmentList.Single(s => s.Id == 1);
        foreach (bool fromStart in new[] { true, false })
        {
            using var patch = geometry.CreateJunctionPath(road,fromStart);
            Check(VerbCount(patch, SKPathVerb.Cubic) == 1 && VerbCount(patch, SKPathVerb.Line) == 0, "Junction patch reintroduced a straight chord");
            Check(fromStart ? patch.Points[0] == Map(road.Points[0]) : patch.LastPoint == Map(road.Points[^1]), "Junction patch left its node");
            using var measure = new SKPathMeasure(patch,false,4);
            Check(Math.Abs(measure.Length - 30) < .1, "Junction patch extent changed unexpectedly");
        }
        foreach (var styleName in new[] { "apple-light", "carto-voyager", "carto-positron" })
        {
            using var host = new BetaOutputHost(UserSettings.Default,new ApplicationConfig { SelectedStyle = styleName, CanvasSize = 4096 });
            using var layer = (BasicLayer)new CS1RoadLayerBuilder(host,xml).BuildAsync(null).GetAwaiter().GetResult();
            var commands = layer.DrawCommands.GetAllOrderedObjects().OfType<PathDrawCommand>().ToArray();
            Check(commands.Count(c => VerbCount(c.Path!, SKPathVerb.Cubic) > 0) >= 7, "Native builder missed a curved road mode or junction fill");
            for (int mode = 0; mode < 3; mode++)
            {
                // The true cubic is visibly separated from the old first chord.
                Check(PixelAt(new(12.890625f,mode*600+98.4375f)).Alpha > 240, "Native stroke missed the reconstructed curve on " + styleName);
                Check(PixelAt(new(23.4375f,mode*600+84.375f)).Alpha == 0, "Old polygonal chord remains in the rendered road on " + styleName);
            }
            SKColor PixelAt(SKPoint point)
            {
                using var pixel = new SKBitmap(1,1,SKColorType.Bgra8888,SKAlphaType.Premul);
                using var canvas = new SKCanvas(pixel);
                canvas.Clear(SKColors.Transparent); canvas.Translate(.5f-point.X,.5f-point.Y);
                foreach (var command in commands) command.Draw(canvas,1,1);
                return pixel.GetPixel(0,0);
            }
        }
    }

    internal static void RunSpringdale(CSLExportXML raw, string reports)
    {
        var manager = new CS1SegmentManager(raw);
        var roads = raw.SegmentList.Where(s => manager.GetWayType(s).HasBit(WayType.Road)).ToArray();
        var original = raw.SegmentList.Select(s => s.Points.Select(p => (p.X,p.Y,p.Z)).ToArray()).ToArray();
        int curves = roads.Count(s => CS1RoadGeometry.TryRecover(s.Points,out _));
        Check(curves > 3000 && roads.Where(s => s.Points.Count == 5).All(s => CS1RoadGeometry.TryRecover(s.Points,out _)), "Springdale sampled road coverage is incomplete");
        foreach (bool hierarchy in new[] { false, true })
        {
            var geometry = new CS1RoadGeometry();
            var view = CS1XMLImporter.CreateLabelView(raw,hierarchy,geometry);
            var displayed = view.SegmentList.Where(s => manager.GetWayType(s).HasBit(WayType.Road)).ToArray();
            Check(displayed.Sum(s => geometry.Pieces(s).Count(p => p.IsCubic)) == curves, "Springdale display joining dropped reconstructed curves");
            foreach (var segment in displayed)
            {
                var pieces = geometry.Pieces(segment);
                Check(pieces.Count > 0 && pieces[0].Start == Map(segment.Points[0]) && pieces[^1].End == Map(segment.Points[^1]), "Springdale curve left its original endpoint nodes");
                Check(pieces.Zip(pieces.Skip(1)).All(pair => pair.First.End == pair.Second.Start), "Springdale joined road has a curve discontinuity");
            }
        }
        for (int i = 0; i < raw.SegmentList.Count; i++)
            Check(original[i].SequenceEqual(raw.SegmentList[i].Points.Select(p => (p.X,p.Y,p.Z))), "Springdale raw geometry changed during curve recovery");
        string report = $"PASS Springdale road curves: {curves} recovered cubics; original endpoints and piece continuity preserved through both display-joining policies; source geometry unchanged.";
        File.WriteAllText(Path.Combine(reports,"road-curve-reconstruction.txt"),report);
        Console.WriteLine(report);
    }
}
