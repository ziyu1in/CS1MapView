# Road crossings

CS1MapView 0.1.3 adds a final road crossing correction after the existing road
and junction drawing passes. It uses the exported elevation along each original
segment. A ramp can pass below one road and above another without assigning one
average height to the entire ramp. All map styles use this correction.

The correction covers overlapping road strokes, including their outlines. It
also handles curved, tangential and parallel overlaps whose centerlines do not
intersect. Contacts must also fall within the physical network footprint;
separate streets touching only through exaggerated preview widths do not create
crossings. Ordinary shared-node junction cores retain their existing appearance.
Height differences of 0.5 m or less retain the original drawing order to avoid
treating near-level contacts as bridges.

## Finding nearby overlaps

The renderer places width-expanded segment bounds in a 100 m spatial grid.
Only pairs sharing a cell proceed to the bounding-rectangle test. A pair spanning
several cells belongs to its first shared cell, so detailed analysis runs once.
Cell boundaries and negative map coordinates use the same ownership rule.

Adaptive short spans screen curve proximity. Native stroke footprints determine
the actual painted overlap. Compatible five-point exports recover cubic geometry
and its elevation controls; other point lists use linear interpolation between
their original samples. Local height bounds establish ordering where possible.
Ambiguous spans are subdivided, then their actual nearby curve parameters are
evaluated. Refinement is bounded at approximately half an output pixel, with a
0.25 m minimum, to avoid invisible fragments around height ties. Road strokes
retain their native geometry. Consistent local height orders share one pair mask.

## Drawing and cost

Each higher road receives a local repair mask. Areas covered by still higher
roads are removed from that mask. This permits several levels and changing
height order along the same pair of segments without imposing one global order.
Corrected outlines are drawn before corrected interiors to preserve connected
upper-road junctions. A small guard around repair and occlusion masks covers old
stroke antialias fringes without changing the road geometry.

The resulting native drawing commands are cached with the road layer. Panning
and repainting use those commands without repeating the crossing search. Changes
that rebuild the road layer, including style changes, rebuild its corrections.
Raster and SVG output use the same native curves and clipping masks. If native
curve boolean operations fail numerically, only the affected clipping masks are
retried as fine local polygons; the road strokes retain their native curves.

The live OpenGL map disables Skia's reusable path-mask cache. With caching
enabled, repeated frames after zooming or panning could omit parts of road
fills and outlines, especially around the crossing clips. The map control
creates its native OpenGL context with `AllowPathMaskCaching = false` and
recreates the context when its window handle changes. Road drawing commands
remain cached; screen drawing retains native paths and clipping.

An opt-in hardware regression check opens the actual `MainForm`, runs its
message loop and wheel/drag handlers, and compares live OpenGL readbacks with
software renders at the same view. It covers Voyager and Positron, repeated
frames, resizing and context recreation. Run it on an interactive Windows
desktop after building (the export is private and supplied separately):

```powershell
tests/CS1MapView.Checks/bin/Release/net8.0-windows/CS1MapView.Checks.exe . <local-test-export> --road-opengl-only
```

The spatial grid restricts detailed work to nearby road segments. Correction
preparation depends on output scale, network density and hardware. Cached road
commands are reused for subsequent frames.

## Scope

This version corrects road-to-road overlaps. Railway-only and pedestrian-only
segments do not participate. Road-to-rail stacking remains deferred; a road
containing tram lanes still participates as a road. Source nodes, segments,
heights, widths, lane-based colors and transit route paths are unchanged.

The result depends on exported elevation data. Missing bridge or height data is
not inferred from appearance, road class or traffic direction.
