using CS2MapView.Util.CS1;
using CSLMapView.XML;
using SkiaSharp;
using System.Diagnostics;

namespace CS2MapView.Drawing.Roads.CS1;

/// <summary>Road-only, vector crossing corrections, prepared once with the road layer.</summary>
internal sealed class CS1RoadCrossingPostProcessor : IDisposable
{
    internal const double CellSizeMetres = 100;
    internal const double HeightToleranceMetres = 0.5;
    private const double Flatness = 0.05;
    [ThreadStatic] private static int maskFallbacks;
    internal static Action<string>? DiagnosticProgress { get; set; }
    private readonly List<Road> roads;
    private readonly double refinementLengthSquared;
    private readonly RoadCrossingReport report = new();

    // Consumes sources. Recorded pictures own the resulting native geometry;
    // all temporary paths and masks are disposed before returning.
    internal static RoadCrossingReport Append(IEnumerable<RoadCrossingSource> sources, DrawCommandLookup commands,
        double cellSize = CellSizeMetres, double refinementMetres = 1)
    {
        var timer = Stopwatch.StartNew();
        int previousFallbacks = maskFallbacks;
        using var process = new CS1RoadCrossingPostProcessor(sources, refinementMetres);
        process.FindContacts(cellSize);
        DiagnosticProgress?.Invoke($"Crossing search complete: {process.report.ContactMasks} masks; {timer.Elapsed.TotalMilliseconds:F1} ms");
        process.Record(commands);
        process.report.MaskFallbacks = maskFallbacks - previousFallbacks;
        process.report.ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
        return process.report;
    }

    private CS1RoadCrossingPostProcessor(IEnumerable<RoadCrossingSource> sources, double refinementMetres)
    {
        if (!double.IsFinite(refinementMetres) || refinementMetres <= 0) throw new ArgumentOutOfRangeException(nameof(refinementMetres));
        refinementLengthSquared = refinementMetres * refinementMetres;
        roads = sources.OrderBy(s => s.Segment.Id).Select(s => new Road(s)).Where(r => r.Radius > 0).ToList();
        report.Roads = roads.Count;
    }

    private void FindContacts(double size)
    {
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var grid = new Dictionary<(int X, int Y), List<int>>();
        var cells = new (int MinX, int MinY, int MaxX, int MaxY)[roads.Count];
        for (int i = 0; i < roads.Count; i++)
        {
            var b = roads[i].Bounds;
            var range = ((int)Math.Floor(b.Left / size), (int)Math.Floor(b.Top / size),
                (int)Math.Floor(b.Right / size), (int)Math.Floor(b.Bottom / size));
            cells[i] = range;
            for (int x = range.Item1; x <= range.Item3; x++)
            for (int y = range.Item2; y <= range.Item4; y++)
            {
                if (!grid.TryGetValue((x, y), out var bucket)) grid[(x, y)] = bucket = [];
                bucket.Add(i);
            }
        }
        foreach (var (cell, bucket) in grid)
        for (int i = 0; i < bucket.Count; i++)
        for (int j = i + 1; j < bucket.Count; j++)
        {
            report.LocalPairVisits++;
            int a = bucket[i], b = bucket[j];
            // This pair belongs to exactly its first shared cell, including
            // negative coordinates and rectangles touching cell boundaries.
            if (cell.X != Math.Max(cells[a].MinX, cells[b].MinX)
                || cell.Y != Math.Max(cells[a].MinY, cells[b].MinY)) continue;
            report.OwnedPairs++;
            if (!Overlaps(roads[a].Bounds, roads[b].Bounds)) continue;
            report.CandidatePairs++;
            if (report.CandidatePairs % 100 == 0) DiagnosticProgress?.Invoke($"Crossing candidates {report.CandidatePairs}; masks {report.ContactMasks}; fallbacks {maskFallbacks}");
            ProcessPair(roads[a], roads[b]);
        }
    }

    private static bool Overlaps(SKRect a, SKRect b)
        => a.Left <= b.Right && a.Right >= b.Left && a.Top <= b.Bottom && a.Bottom >= b.Top;

    private void ProcessPair(Road a, Road b)
    {
        if (SameLevel(a.MinHeight, a.MaxHeight, b.MinHeight, b.MaxHeight)) return;
        int order = Order(a.MinHeight, a.MaxHeight, b.MinHeight, b.MaxHeight);
        SKPath? pairMask = null;
        bool maskBuilt = false, repaired = false;
        bool hasTies = false;
        var localContacts = new List<(Span First, Span Second, int Order)>();
        try
        {
            foreach (var x in a.Spans)
            foreach (var y in b.Spans)
            {
                report.SpanTests++;
                if (!Near(x, y, a.ContactRadius + b.ContactRadius)) continue;
                if (order != 0)
                {
                    var mask = PairMask();
                    if (!mask.IsEmpty) Apply(a, b, order, mask, x, y);
                    return;
                }
                Local(x, y, 0);
            }
            if (localContacts.Count == 0) return;
            var first = localContacts[0];
            // Most slopes have overlapping overall height ranges but one
            // consistent local order. Certify that order before constructing
            // masks, then record one footprint instead of many small fragments.
            if (!hasTies && localContacts.All(c => c.Order == first.Order))
            {
                var mask = PairMask();
                if (!mask.IsEmpty) Apply(a, b, first.Order, mask, first.First, first.Second);
                return;
            }
            var whole = PairMask();
            if (whole.IsEmpty) return;
            foreach (var contact in localContacts)
            {
                using var xf = contact.First.Footprint(a.Radius);
                using var yf = contact.Second.Footprint(b.Radius);
                using var overlap = Op(xf, yf, SKPathOp.Intersect);
                if (overlap.IsEmpty) continue;
                using var mask = Op(overlap, whole, SKPathOp.Intersect);
                if (!mask.IsEmpty) Apply(a, b, contact.Order, mask, contact.First, contact.Second);
            }
        }
        finally
        {
            if (repaired) report.RepairedPairs++;
            pairMask?.Dispose();
        }

        SKPath PairMask()
        {
            if (maskBuilt) return pairMask!;
            maskBuilt = true;
            pairMask = Op(a.Footprint, b.Footprint, SKPathOp.Intersect);
            // Preserve the core of real shared-node junctions. A connected
            // pair can still cross elsewhere, so do not discard the whole pair.
            foreach (var node in new[] { a.Source.Segment.StartNode, a.Source.Segment.EndNode }.DistinctBy(n => n.Id))
            {
                if (node.Id != b.Source.Segment.StartNodeId && node.Id != b.Source.Segment.EndNodeId) continue;
                using var junction = new SKPath();
                junction.AddCircle(node.Position.X, -node.Position.Z, (float)(a.Radius + b.Radius + 1));
                var remainder = Op(pairMask, junction, SKPathOp.Difference);
                pairMask.Dispose(); pairMask = remainder;
            }
            return pairMask;
        }

        void Local(Span x, Span y, int depth)
        {
            if (!Near(x, y, a.ContactRadius + b.ContactRadius)) return;
            if (SameLevel(x.MinHeight, x.MaxHeight, y.MinHeight, y.MaxHeight)) { hasTies = true; return; }
            int localOrder = Order(x.MinHeight, x.MaxHeight, y.MinHeight, y.MaxHeight);
            if (localOrder != 0)
            {
                localContacts.Add((x, y, localOrder));
                return;
            }
            // Resolve actual contact heights at the output's useful precision.
            // Native stroke geometry stays exact; low-resolution previews must
            // not build thousands of invisible fragments around a height tie.
            if (depth >= 8 || Math.Max((x.D - x.A).LengthSquared, (y.D - y.A).LengthSquared) <= refinementLengthSquared)
            {
                var (u, v) = Refine(x, y);
                double difference = x.Evaluate(u).H - y.Evaluate(v).H;
                if (Math.Abs(difference) <= HeightToleranceMetres) { hasTies = true; return; }
                localOrder = difference > 0 ? 1 : -1;
                localContacts.Add((x, y, localOrder));
                return;
            }
            if (x.MaxHeight - x.MinHeight >= y.MaxHeight - y.MinHeight)
            {
                var (left, right) = x.Split(); Local(left, y, depth + 1); Local(right, y, depth + 1);
            }
            else
            {
                var (left, right) = y.Split(); Local(x, left, depth + 1); Local(x, right, depth + 1);
            }
        }

        void Apply(Road first, Road second, int direction, SKPath mask, Span x, Span y)
        {
            var upper = direction > 0 ? first : second;
            var lower = direction > 0 ? second : first;
            upper.Repairs.Add(new SKPath(mask));
            lower.Occlusions.Add(new SKPath(mask));
            report.ContactMasks++;
            repaired = true;
            if (report.Contacts.Count < 10000)
            {
                var (u, v) = Refine(x, y);
                var p = x.Evaluate(u); var q = y.Evaluate(v);
                report.Contacts.Add(new(first.Source.Segment.Id, second.Source.Segment.Id,
                    (p.X + q.X) / 2, (p.Y + q.Y) / 2, p.H, q.H, upper.Source.Segment.Id));
            }
        }
    }

    private static bool SameLevel(double amin, double amax, double bmin, double bmax)
        => amax - bmin <= HeightToleranceMetres && bmax - amin <= HeightToleranceMetres;
    private static int Order(double amin, double amax, double bmin, double bmax)
        => amin > bmax + HeightToleranceMetres ? 1 : bmin > amax + HeightToleranceMetres ? -1 : 0;

    private static bool Near(Span a, Span b, double radius)
    {
        var ab = a.Bounds; ab.Inflate((float)(radius + 2 * Flatness), (float)(radius + 2 * Flatness));
        if (!Overlaps(ab, b.Bounds)) return false;
        var (u, v) = Closest(a.A, a.D, b.A, b.D);
        var delta = (a.A + (a.D - a.A) * u) - (b.A + (b.D - b.A) * v);
        return delta.LengthSquared <= Math.Pow(radius + 2 * Flatness, 2);
    }

    private static (double U, double V) Closest(V a, V b, V c, V d)
    {
        var p = b - a; var q = d - c; var w = c - a;
        double det = Cross(p, q);
        if (Math.Abs(det) > 1e-12)
        {
            double u = Cross(w, q) / det, v = Cross(w, p) / det;
            if (u >= 0 && u <= 1 && v >= 0 && v <= 1) return (u, v);
        }
        double Project(V point, V start, V delta) => delta.LengthSquared == 0 ? 0
            : Math.Clamp(Dot(point - start, delta) / delta.LengthSquared, 0, 1);
        var choices = new[] { (0d, Project(a, c, q)), (1d, Project(b, c, q)), (Project(c, a, p), 0d), (Project(d, a, p), 1d) };
        return choices.MinBy(t => ((a + p * t.Item1) - (c + q * t.Item2)).LengthSquared);
    }

    private static (double U, double V) Refine(Span a, Span b)
    {
        var (u, v) = Closest(a.A, a.D, b.A, b.D);
        for (int i = 0; i < 10; i++)
        {
            var difference = a.Evaluate(u) - b.Evaluate(v);
            if (difference.LengthSquared < .0001) break;
            var da = a.Derivative(u); var db = b.Derivative(v);
            double det = Cross(da, db);
            if (Math.Abs(det) < 1e-12) break; // Parallel footprint overlaps use their closest-point parameters.
            double nextU = u - Cross(difference, db) / det;
            double nextV = v - Cross(difference, da) / det;
            if (nextU < 0 || nextU > 1 || nextV < 0 || nextV > 1) break;
            u = nextU; v = nextV;
        }
        return (u, v);
    }

    private static double Dot(V a, V b) => a.X * b.X + a.Y * b.Y;
    private static double Cross(V a, V b) => a.X * b.Y - a.Y * b.X;
    private static SKPath Op(SKPath a, SKPath b, SKPathOp operation)
    {
        var result = a.Op(b, operation);
        if (result is not null) return result;
        // Nearly coincident stroke edges can defeat native curve booleans at
        // city-scale coordinates. Retry just this mask with local, snapped
        // polygon coordinates; the actual road drawing remains native curves.
        maskFallbacks++;
        var bounds = SKRect.Union(a.Bounds, b.Bounds);
        var origin = new SKPoint(bounds.MidX, bounds.MidY);
        foreach (double scale in new[] { 1024d, 64d })
        {
            using var first = PolygonMask(a, origin, scale);
            using var second = PolygonMask(b, origin, scale);
            result = first.Op(second, operation);
            if (result is null) continue;
            result.Transform(SKMatrix.CreateScale((float)(1 / scale), (float)(1 / scale))
                .PostConcat(SKMatrix.CreateTranslation(origin.X, origin.Y)));
            return result;
        }
        throw new InvalidDataException("Unable to construct road crossing mask: " + operation);
    }

    private static SKPath PolygonMask(SKPath source, SKPoint origin, double scale)
    {
        var result = new SKPath { FillType = source.FillType };
        using var iterator = source.CreateRawIterator();
        var p = new SKPoint[4];
        SKPoint previous = default;
        SKPoint Snap(SKPoint point) => new((float)Math.Round((point.X - (double)origin.X) * scale),
            (float)Math.Round((point.Y - (double)origin.Y) * scale));
        void Line(SKPoint point)
        {
            var snapped = Snap(point);
            if (snapped != previous) result.LineTo(snapped);
            previous = snapped;
        }
        void Curve(Func<double, SKPoint> evaluate, double t0, double t1, int depth)
        {
            var start = evaluate(t0); var end = evaluate(t1); double middle = (t0 + t1) / 2;
            double Distance(SKPoint point) => PointDistance(new(point.X, point.Y, 0), new(start.X, start.Y, 0), new(end.X, end.Y, 0));
            if (depth >= 16 || Math.Max(Distance(evaluate(middle)),
                Math.Max(Distance(evaluate((3 * t0 + t1) / 4)), Distance(evaluate((t0 + 3 * t1) / 4)))) < .000025)
            { Line(end); return; }
            Curve(evaluate, t0, middle, depth + 1); Curve(evaluate, middle, t1, depth + 1);
        }
        SKPathVerb verb;
        while ((verb = iterator.Next(p)) != SKPathVerb.Done)
        {
            if (verb == SKPathVerb.Move) { previous = Snap(p[0]); result.MoveTo(previous); }
            else if (verb == SKPathVerb.Line) Line(p[1]);
            else if (verb == SKPathVerb.Close) result.Close();
            else if (verb == SKPathVerb.Cubic)
            {
                var a = p[0]; var b = p[1]; var c = p[2]; var d = p[3];
                Curve(t => { double s = 1 - t; return new((float)(s*s*s*a.X+3*s*s*t*b.X+3*s*t*t*c.X+t*t*t*d.X),
                    (float)(s*s*s*a.Y+3*s*s*t*b.Y+3*s*t*t*c.Y+t*t*t*d.Y)); }, 0, 1, 0);
            }
            else if (verb is SKPathVerb.Quad or SKPathVerb.Conic)
            {
                var a = p[0]; var b = p[1]; var c = p[2]; double weight = verb == SKPathVerb.Conic ? iterator.ConicWeight() : 1;
                Curve(t => { double s = 1 - t, denominator = s*s+2*weight*s*t+t*t;
                    return new((float)((s*s*a.X+2*weight*s*t*b.X+t*t*c.X)/denominator),
                        (float)((s*s*a.Y+2*weight*s*t*b.Y+t*t*c.Y)/denominator)); }, 0, 1, 0);
            }
        }
        return result;
    }
    private static SKPath? UnionMasks(List<SKPath> source)
    {
        if (source.Count == 0) return null;
        var level = source.OrderBy(p => p.Bounds.Left).ThenBy(p => p.Bounds.Top).ToList();
        source.Clear();
        while (level.Count > 1)
        {
            var next = new List<SKPath>();
            for (int i = 0; i < level.Count; i += 2)
            {
                if (i + 1 == level.Count) { next.Add(level[i]); continue; }
                var union = Op(level[i], level[i + 1], SKPathOp.Union);
                level[i].Dispose(); level[i + 1].Dispose(); next.Add(union);
            }
            level = next;
        }
        return level[0];
    }

    private static SKPath? GuardMask(SKPath? mask)
    {
        if (mask is null || mask.IsEmpty) return mask;
        // Cover the antialias fringe of the old stroke as well as its exact
        // mathematical footprint. Guard both repair and occlusion masks so a
        // lower correction cannot intrude into a higher road's fringe.
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 2,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var edge = new SKPath();
        paint.GetFillPath(mask, edge);
        var guarded = Op(mask, edge, SKPathOp.Union);
        mask.Dispose();
        return guarded;
    }

    private void Record(DrawCommandLookup commands)
    {
        int prepared = 0;
        foreach (var road in roads)
        {
            if (++prepared % 500 == 0) DiagnosticProgress?.Invoke($"Preparing crossing masks: {prepared}/{roads.Count}");
            road.Repair = GuardMask(UnionMasks(road.Repairs));
            road.Occluded = GuardMask(UnionMasks(road.Occlusions));
            if (road.Repair is null) continue;
            if (road.Occluded is not null)
            {
                var visible = Op(road.Repair, road.Occluded, SKPathOp.Difference);
                road.Repair.Dispose(); road.Repair = visible;
            }
            if (!road.Repair.IsEmpty) report.CorrectedRoads++;
        }
        // Visibility masks exclude every higher road. Borders can therefore be
        // recorded first and interiors second, preserving same-level joins
        // without a lower patch ever painting over a higher bridge.
        foreach (bool border in new[] { true, false })
        foreach (var road in roads)
        {
            var mask = road.Repair;
            var key = border ? road.Source.Border : road.Source.Fill;
            if (mask is null || mask.IsEmpty || key is null || key.Color.Alpha == 0) continue;
            var bounds = mask.TightBounds;
            bounds.Inflate(.05f, .05f);
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(bounds);
            // The road stroke supplies its own antialiased boundary. A second
            // fractional clip would leave the previous lower-road outline
            // showing through at the interior edge of the repair.
            canvas.ClipPath(mask, SKClipOperation.Intersect, false);
            canvas.DrawPath(road.Path, SKPaintCache.Instance.GetStroke(key));
            commands.Add(new NativePictureDrawCommand(recorder.EndRecording(), bounds));
            report.RecordedCommands++;
        }
    }

    public void Dispose() { foreach (var road in roads) road.Dispose(); }

    private sealed class Road : IDisposable
    {
        internal readonly RoadCrossingSource Source;
        internal readonly List<Span> Spans = [];
        internal readonly double Radius, ContactRadius, MinHeight, MaxHeight;
        internal readonly SKRect Bounds;
        internal SKPath? Repair, Occluded;
        internal readonly List<SKPath> Repairs = [], Occlusions = [];
        private SKPath? path, footprint;
        internal Road(RoadCrossingSource source)
        {
            Source = source;
            Radius = Math.Max(source.Border?.Color.Alpha > 0 ? source.Border.Width / 2 : 0,
                source.Fill?.Color.Alpha > 0 ? source.Fill.Width / 2 : 0);
            // At city-wide preview scales the fixed pixel width exaggerates
            // roads well beyond their physical network footprint. Nearby,
            // separate streets touching only through that exaggeration are
            // not vertical crossings and must not generate correction masks.
            double physicalWidth = source.PhysicalWidth > 0 ? source.PhysicalWidth : source.Segment.Width;
            ContactRadius = double.IsFinite(physicalWidth) && physicalWidth > 0 ? Math.Min(Radius, physicalWidth / 2) : Radius;
            var pieces = CS1RoadGeometry.OriginalPieces(source.Segment.Points);
            var initial = pieces.Select(Span.FromPiece).ToArray();
            MinHeight = initial.Min(s => s.MinHeight); MaxHeight = initial.Max(s => s.MaxHeight);
            Bounds = initial.Select(s => s.Bounds).Aggregate(SKRect.Union);
            var expanded = Bounds; expanded.Inflate((float)(Radius + Flatness), (float)(Radius + Flatness)); Bounds = expanded;
            foreach (var span in initial) Flatten(span, 0);
            void Flatten(Span span, int depth)
            {
                if (depth >= 20 || ((span.D - span.A).LengthSquared <= 400
                    && Math.Max(PointDistance(span.B, span.A, span.D), PointDistance(span.C, span.A, span.D)) <= Flatness * Flatness))
                { Spans.Add(span); return; }
                var (left, right) = span.Split(); Flatten(left, depth + 1); Flatten(right, depth + 1);
            }
        }
        internal SKPath Path
        {
            get
            {
                if (path is not null) return path;
                path = new SKPath();
                foreach (var piece in CS1RoadGeometry.OriginalPieces(Source.Segment.Points))
                {
                    if (path.IsEmpty) path.MoveTo(piece.Start);
                    if (piece.IsCubic) path.CubicTo(piece.Control1, piece.Control2, piece.End); else path.LineTo(piece.End);
                }
                return path;
            }
        }
        internal SKPath Footprint
        {
            get
            {
                if (footprint is not null) return footprint;
                footprint = new SKPath();
                foreach (var key in new[] { Source.Border, Source.Fill })
                {
                    if (key is null || key.Color.Alpha == 0) continue;
                    using var filled = new SKPath();
                    SKPaintCache.Instance.GetStroke(key).GetFillPath(Path, filled);
                    if (footprint.IsEmpty) footprint.AddPath(filled);
                    else { var union = Op(footprint, filled, SKPathOp.Union); footprint.Dispose(); footprint = union; }
                }
                return footprint;
            }
        }
        public void Dispose()
        {
            path?.Dispose(); footprint?.Dispose(); Repair?.Dispose(); Occluded?.Dispose();
            foreach (var mask in Repairs) mask.Dispose(); foreach (var mask in Occlusions) mask.Dispose();
        }
    }

    private static double PointDistance(V point, V start, V end)
    {
        var delta = end - start;
        double t = delta.LengthSquared == 0 ? 0 : Math.Clamp(Dot(point - start, delta) / delta.LengthSquared, 0, 1);
        return (point - start - delta * t).LengthSquared;
    }

    private readonly record struct V(double X, double Y, double H)
    {
        internal double LengthSquared => X * X + Y * Y;
        internal SKPoint Point => new((float)X, (float)Y);
        public static V operator +(V a, V b) => new(a.X + b.X, a.Y + b.Y, a.H + b.H);
        public static V operator -(V a, V b) => new(a.X - b.X, a.Y - b.Y, a.H - b.H);
        public static V operator *(V a, double f) => new(a.X * f, a.Y * f, a.H * f);
    }

    private readonly record struct Span(V A, V B, V C, V D, bool Cubic)
    {
        internal double MinHeight => Math.Min(Math.Min(A.H, B.H), Math.Min(C.H, D.H));
        internal double MaxHeight => Math.Max(Math.Max(A.H, B.H), Math.Max(C.H, D.H));
        internal SKRect Bounds => new((float)Math.Min(Math.Min(A.X, B.X), Math.Min(C.X, D.X)),
            (float)Math.Min(Math.Min(A.Y, B.Y), Math.Min(C.Y, D.Y)),
            (float)Math.Max(Math.Max(A.X, B.X), Math.Max(C.X, D.X)), (float)Math.Max(Math.Max(A.Y, B.Y), Math.Max(C.Y, D.Y)));
        internal static Span FromPiece(RoadCurvePiece p)
        {
            var a = new V(p.Start.X, p.Start.Y, p.StartHeight); var d = new V(p.End.X, p.End.Y, p.EndHeight);
            return new(a, p.IsCubic ? new(p.Control1.X, p.Control1.Y, p.Control1Height) : a + (d - a) * (1d / 3),
                p.IsCubic ? new(p.Control2.X, p.Control2.Y, p.Control2Height) : a + (d - a) * (2d / 3), d, p.IsCubic);
        }
        internal (Span Left, Span Right) Split()
        {
            var ab = (A + B) * .5; var bc = (B + C) * .5; var cd = (C + D) * .5;
            var abc = (ab + bc) * .5; var bcd = (bc + cd) * .5; var m = (abc + bcd) * .5;
            return (new(A, ab, abc, m, Cubic), new(m, bcd, cd, D, Cubic));
        }
        internal V Evaluate(double t) { double s = 1 - t; return A * (s * s * s) + B * (3 * s * s * t) + C * (3 * s * t * t) + D * (t * t * t); }
        internal V Derivative(double t) => (B - A) * (3 * (1 - t) * (1 - t)) + (C - B) * (6 * (1 - t) * t) + (D - C) * (3 * t * t);
        internal SKPath Footprint(double radius)
        {
            using var path = new SKPath(); path.MoveTo(A.Point);
            if (Cubic) path.CubicTo(B.Point, C.Point, D.Point); else path.LineTo(D.Point);
            using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)(2 * radius), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
            var result = new SKPath(); paint.GetFillPath(path, result); return result;
        }
    }
}

internal sealed record RoadCrossingSource(XMLSegment Segment, SKPaintCache.StrokeKey? Border, SKPaintCache.StrokeKey? Fill, double PhysicalWidth = 0);
internal sealed record RoadCrossingContact(int FirstId, int SecondId, double X, double Y, double FirstHeight, double SecondHeight, int UpperId);
internal sealed class RoadCrossingReport
{
    internal int Roads, CandidatePairs, RepairedPairs, ContactMasks, CorrectedRoads, RecordedCommands, MaskFallbacks;
    internal long LocalPairVisits, OwnedPairs, SpanTests;
    internal double ElapsedMilliseconds;
    internal List<RoadCrossingContact> Contacts { get; } = [];
}
