# Map rendering comparisons

The comparison panels are direct exports from the actual viewers. Locations are
identified as Interchange A and Interchange B. City names, local place names,
export filenames, source coordinates and service records are omitted.

## Viewer versions

| Panel | Viewer |
| --- | --- |
| Original | Original CSLMapView, assembly version 4.4.7.0. |
| Curves: before | CS1MapView v0.1.2.2, before curve recovery. |
| Heights: before | CS1MapView v0.1.2.2, with smooth curves but before height-based crossing correction. |
| After and style comparisons | CS1MapView v0.1.3. |

The original viewer also differs in road widths, railway symbols, contours and
terrain detail. The curve comparison includes later crossing corrections as well;
inspect the ramp bends to compare smoothness. The height comparison starts with
smooth curves in both versions.

## Display settings

Each comparison uses the same map area and scale across its panels. The longest
image dimension is 1,200 pixels; the original viewer can round an edge one pixel
shorter. Images can be opened at full size from the main README.

The road improvement panels use Apple Light. The style panels compare Apple
Light, CARTO Voyager and CARTO Positron with the same enabled layers and network.
Terrain, roads and buildings are shown. Text labels, forests, hillshading, the
grid, transit routes, transit markers and station-area overlays are hidden.
Interchange B additionally shows contour lines and railway tracks.

The height correction applies to road-to-road overlaps. Railway tracks provide
context; road-to-rail stacking is unchanged. The images retain each viewer's
actual geometry and drawing output.
