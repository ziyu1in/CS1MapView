<!-- Writing reference: ASD-STE100, Issue 9. https://www.asd-ste100.org/assets/files/ASD-STE100_ISSUE9.pdf -->

# CS1MapView

CS1MapView is a Windows map viewer for Cities: Skylines 1 exports.
The viewer opens `.cslmap` and `.cslmap.gz` files.
It does not open `.crp` files or Cities: Skylines 2 exports.

## Development and credits

The author developed CS1MapView entirely through prompts to GPT-6 Astra and GPT-6.1 Sol.
The design is based on gansaku's original [CSLMapView](https://steamcommunity.com/sharedfiles/filedetails/?id=845665815).
The source code is based on gansaku's [CS2MapView](https://github.com/gansaku/CS2MapView).
We thank gansaku for the original projects.
The upstream copyright notice and MIT license are included.

See the [development declaration](DEVELOPMENT.md).

[![Routes, station markers, tracks, and station areas](assets/images/routes-split-badges-rail-station-area.svg)](assets/images/routes-split-badges-rail-station-area.svg)

## Station areas

The station area layer shows building lots, station tracks, platform networks, and underground pedestrian networks.
The layer includes some BM station tracks and pathways.
Station areas have a red or blue fill.
Station areas are approximate 2D shapes.

| Station areas | Station areas and tracks |
| --- | --- |
| ![Station areas](assets/images/station-area.svg) | ![Station areas and tracks](assets/images/rail-station-area.svg) |

Limits:

- The layer does not show platforms that are only part of building meshes.
- The layer does not include all custom platform or pathway assets.
- Areas at different heights can have an overlap on the map.
- The layer does not include airport or tram station buildings.
- The layer does not include building groups with `yard` or `depot` in their names.
- The map does not show if passengers can use each passage.

## Rail tracks

Train, metro, and tram tracks use exported lane positions.
The Apple style uses continuous track lines.
The Google style adds cross-ties.
Underground tracks use dashed lines.
Monorail and cable car tracks use one centerline.

The **Infrastructure only** option hides route lines and station markers.
The **Reconnect tracks** option can adjust track connections.

Limits:

- Each line shows one track.
- The viewer shows one centerline when lane data is missing.
- Switch geometry is approximate.
- The **Reconnect tracks** option does not repair all junctions.

## Station clusters and transfer links

A station cluster is a group of aligned physical stops.
The viewer shows a cluster with two or more positions as one long station marker.
Other stops have circular station markers.

The viewer searches a circle around each visible physical stop.
The search includes pairs and larger groups.
One circle can contain more than one cluster.
Each stop is in one cluster.

Opposite-direction stop pairs on the same line have the first priority.
Stops with the same transport mode have the next priority.
The viewer then selects the smallest number of markers and the shortest total marker length.
The viewer calculates travel directions from the exported outgoing service paths.

The alignment limit is the maximum perpendicular distance between a stop and the fitted cluster axis.
The viewer uses the largest applicable alignment limit.

| Cluster members | Alignment limit |
| --- | --- |
| Different transport modes | 5 m |
| One transport mode | 6 m |
| One transport mode, a line common to all stops, and an opposite-direction pair | 7 m |

The selected transport modes control which stops can have a transfer link.
The distance limit uses the physical stop positions.
The viewer measures link length between the nearest points on the marker axes.
The viewer keeps the shortest links that keep each reachable group connected.
The links can have branches, but have no loops.
The viewer shows a maximum of one link for each marker pair.

Route stop data does not change.

| Setting | Function | Default |
| --- | --- | --- |
| Cluster search radius | Sets the radius of each search circle. | 90 m |
| Transfer link distance | Sets the maximum distance between stops for a link. | 100 m |

Marker width does not change these distances.
The viewer saves the two distance settings.
The feature gallery uses a 90 m cluster search radius and a 150 m transfer link distance.

Limits:

- Stops without direction data do not receive the opposite-direction priority.
- Clusters use 2D stop positions. Station area boundaries do not control clusters.
- Transfers beyond the distance limit do not have direct links.
- Links do not show walking paths or fare rules.

The examples below show the same map area with different distance settings.
The green circle shows the cluster search radius for stop A.
The purple circle shows the transfer link distance from stop A.
The top image pair changes the cluster radius.
The bottom image pair changes the link distance.

[![The same area with different cluster radii and transfer link distances](assets/images/distance-examples/distance-comparison.svg)](assets/images/distance-examples/distance-comparison.svg)

See the [distance examples](assets/images/distance-examples/README.md) for individual images and export settings.

The next examples show the alignment test and the selection priorities.
The bands show the alignment limits around the fitted axes.
The blue arrows show outgoing travel directions.
The white station markers show the viewer results.

[![Alignment limits and station selection priorities](assets/images/station-rule-examples/station-rule-comparison.svg)](assets/images/station-rule-examples/station-rule-comparison.svg)

See the [station rule examples](assets/images/station-rule-examples/README.md) for individual images and measurements.

## Station badges

Badges show line colors and short line names.
Mode letters are optional: `T` for train, `M` for metro, and `MR` for monorail.
Leaders connect badges to station markers.
Leaders stop at the edges of the badges and markers.

| "One badge per cluster" | Result |
| --- | --- |
| On | Each station cluster has one badge. |
| Off | Each transfer group has one badge. |

| One badge for each transfer group | One badge for each station cluster |
| --- | --- |
| ![One badge for each transfer group](assets/images/complex-linking-combined-badges.svg) | ![One badge for each station cluster](assets/images/complex-linking-split-badges.svg) |

## Layer and line controls

Route lines, station markers, tracks, and station areas have different layer switches.
Station clusters and transfer links do not include hidden lines.
The route layer switch does not remove station markers.
Train route lines can stay visible without train markers.

| Route lines off | Route lines on |
| --- | --- |
| ![Route lines off](assets/images/combined-badges-rail-station-area.svg) | ![Route lines on](assets/images/routes-combined-badges-rail-station-area.svg) |

| The two metro lines | Orange metro line hidden |
| --- | --- |
| ![The two metro lines and train markers](assets/images/complex-linking-1-1.svg) | ![Orange metro line hidden](assets/images/complex-linking-1-2.svg) |

| Teal metro line hidden | The two metro lines, train markers hidden |
| --- | --- |
| ![Teal metro line hidden](assets/images/complex-linking-1-3.svg) | ![The two metro lines, train markers hidden](assets/images/complex-linking-1-4.svg) |

## Other features

- The `apple-light` style sets the map colors.
- The viewer uses exported road widths.
- Road widths use defaults when export data is missing.
- The editors change line colors, short line names, and building settings.
- The viewer exports PNG, JPEG, BMP, SVG, SVGZ, and 16-bit heightmaps.
- The command-line renderer exports maps without the application window.
- The Windows print dialog controls printer output.

The project has no printer test results.

## Build and start

Build requirements:

- Windows x64.
- .NET SDK 8 or a subsequent version with Windows desktop components.
- Network access for NuGet package downloads.

.NET Desktop Runtime 8 is necessary to start the viewer.

1. Build the viewer from the repository root.

   ```powershell
   ./build.ps1
   ```

2. Start the viewer.

   ```powershell
   ./src/CS2MapView.Form/bin/Release/net8.0-windows/CS1MapView.exe
   ```

Keep all files in the build output directory together.
Keep the dependency DLLs in `src/*/lib/`.

## Map controls

| Task | Instruction |
| --- | --- |
| Open a map | Open a city export. |
| Pan | Drag the map. |
| Zoom | Turn the mouse wheel. |
| Turn the map | Hold Ctrl while you turn the mouse wheel. |
| Highlight transfers | Click a station marker, badge, or leader. |
| Clear the highlight | Press Escape. |

The **View** menu has the layer and style controls.
The **Lists** menu has the line and building editors.

## Checks and exports

The private reference export is necessary for the full checks.
See the [fixture requirements](samples/README.md).

```powershell
./build.ps1 -SmokeTest -MapFile 'C:/path/to/reference.cslmap.gz'
./scripts/render-map.ps1 -InputFile 'C:/path/to/city.cslmap.gz' -OutputFile 'renders/city.svg' -Style apple-light -Transit
```

See the [gallery settings](assets/images/README.md) for image crops and layer settings.
Private city exports and generated builds stay outside source control.

## Source and licenses

The repository includes the upstream MIT license and its original copyright notice.
See the [source provenance](SOURCE-PROVENANCE.md).
Keep the licenses, notices, and SHA-256 manifests with the source.

Internal `CS2MapView` names come from the upstream source.
Legacy `CSLMapView` names identify the export format and migration files.

- [Upstream MIT license](LICENSE.upstream).
- [Forms MIT license](src/CS2MapView.Form/LICENSE).
- [Dependency notices](NOTICES/).
- [Source manifest](UPSTREAM-BASELINE-SHA256.csv).
- [Recovery input manifest](RECOVERY-INPUT-SHA256.csv).
