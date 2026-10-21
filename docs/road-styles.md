# Road styles

CS1MapView 0.1.2.2 adds `carto-voyager` and `carto-positron`. Choose either in the
View menu or the Options style selector. Both also work with the command-line
renderer's `--style` option. Existing styles and the `apple-light` default remain
available.

The designs take inspiration from [CARTO's Voyager and Positron comparison](https://carto.com/blog/new-voyager-basemap/).
They use CS1 city exports and locally defined colors.

## Width and visual importance

Road width and importance are independent. A segment's full exported width in
metres determines its drawing width using the viewer's existing scale conversion.
That footprint can include sidewalks, medians and verges. Missing or invalid
widths retain the existing lane-span and legacy asset fallbacks.

Importance changes colors and outline contrast, without adding width. Count the
total physical motor-traffic lanes across both directions within each segment:

| Total lanes | Class | Examples |
| --- | --- | --- |
| 1–3 lanes | Minor | 1 + 1 or 2 + 1 two-way street, two- or three-lane one-way street |
| 4–5 lanes | Intermediate | 2 + 2 or 3 + 2 avenue, four- or five-lane one-way street |
| 6 or more lanes | Major | 3 + 3 or 4 + 2 boulevard, six-lane one-way street |
| Exported highway flag | Highway | Highway carriageways and ramps, regardless of lane count |

A 32 m two-lane street remains wider than a 24 m four-lane avenue, while the
avenue gets more visual emphasis. A one-lane highway ramp stays narrow and
retains highway colors. This is a lane-based visual hierarchy, not a claim about
actual traffic volume or a road's official administrative class.

Count each physical motor-traffic lane once, including identifiable dedicated
bus lanes. A mixed car/tram lane contributes one road lane. Parking lanes,
pedestrian paths, cycle-only lanes, rail-only lanes, empty helper entries and
invalid dimensions are excluded. Duplicate mode entries at the same lateral
position are combined. A bidirectional lane contributes one to the total, even
though it permits traffic in either direction. Lane-based importance starts at
four total lanes. Positron's separate gray-versus-white distinction uses physical
width, not this importance class.
Unknown lane directions are not guessed; missing usable lane metadata uses the
minor class while preserving any known highway classification.

Parking-lot networks and their service roads use the minor road palette even
when their internal driving aisles expose four or more traffic lanes. The
exclusion recognizes parking-lot/car-park network asset names, including the
Parking Lot Roads family. It preserves physical widths and lane metadata.
Ordinary streets with curbside parking remain eligible for lane-based emphasis;
custom street names do not identify a parking-lot network.

## Appearance

Voyager uses white minor streets, light yellow intermediate roads, stronger yellow
major roads and deeper yellow surface highways. Intermediate and major streets
have stronger yellow fills than highway tunnels. Gray and warm-gray outlines become more
distinct as importance increases. Its land, water, buildings and greenery use
a warm, light palette.

Positron draws physically narrow roads up to 12 m wide as muted gray lines, with
matching fill and outline colors. Wider roads remain white even when they have
only two lanes or run one-way. For example, a 16 m wide two-lane one-way
street remains white. Lane count controls the outline hierarchy of
the wider roads. Highways retain their separate white, outlined styling at every
width. Its land, water, buildings and greenery form a cooler, quieter
background. Tunnel fills are slightly muted in both styles; tunnels retain
dashed outlines and elevated roads retain stronger outlines.

Ground-road strokes are batched by both width and importance. Display joining
preserves class boundaries between equal-width roads, and tunnel, elevated and
junction strokes use the same class colors. Original nodes, segments and transit
route paths remain unchanged. Lane markings and individual road-lane geometry
are not part of this version.

## Road curves

All map styles draw compatible five-point road segments as native cubic Bézier
curves. The exporter stores the endpoints and samples at curve parameters
0.25, 0.5 and 0.75. The viewer recovers two control handles from the quarter
samples and fixed endpoints, then checks the midpoint in three dimensions
against a 0.01 m tolerance. Two-point roads remain straight; other point lists
and inconsistent five-point samples retain their original polylines.

Recovery happens on a copied network before display joining or point
simplification. Joined roads retain their original curve pieces and deliberate
corners; reversing a segment reverses its pieces and exchanges their control
handles. Road interiors and outlines share the same native curve. Junction
fill patches use clipped portions of that path, with the established local
extent limits, instead of reintroducing straight chords near the nodes.

The same geometry is used on screen and in raster and SVG exports, including
ground, elevated and tunnel roads. Widths, lane-based importance, source
coordinates and transit route paths are independent of this rendering change.

## Custom style files

Each preset is a self-contained `.cmvstyle` file. The optional `RoadHierarchy`
section enables lane-based styling and supplies `Intermediate` and `Major`
palettes. Each palette defines `Fill`, `Border`, `TunnelFill`, `TunnelBorder`,
`ElevatedFill` and `ElevatedBorder`. Minor and highway colors use the existing
`Colors` entries. The optional `NarrowRoadMaxWidth` cutoff, in metres, selects a
separate `Narrow` palette independently of importance; zero disables it. Styles
without this section keep their previous road colors and width rules.
