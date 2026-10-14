# CS1MapView UI and output

The native application retains the upstream Forms layout and uses the upstream Skia layer pipeline. Launch `src/CS2MapView.Form/bin/Release/net8.0-windows/CS1MapView.exe` after building the root solution.

Use the [README glossary](../README.md#glossary) for network nodes, segments,
mode-specific tracks and lanes, route paths, station markers, and transit modal tags.

## Navigation and selection

New preferences use `CS1MapView.config.xml`, `CS1MapView.settings`, and `CS1MapView.routes.xml`.
When a new file is absent, the viewer reads the corresponding `CSLMapView.Beta` file.
The viewer saves changes to the new filenames and leaves the earlier files intact.

Open or drop `.cslmap` and `.cslmap.gz` files. Drag with the left mouse button to pan, use the wheel to zoom and Ctrl+wheel to rotate. A click with no drag selects the nearest local transfer group under the station symbol, connector, transit modal tag or leader. Clicking it again, clicking empty space, choosing Clear route highlight or pressing Escape clears the highlight. Route visibility choices survive highlighting. The click position is converted from control coordinates to the Skia backing surface and then through the inverse view transform.

View → CS1 features contains expandable map, railway, route and station settings, a per-service visibility/colour/shorthand editor, and asset preferences for the loaded city's buildings. Changes apply live. The cluster search radius and transfer link distance are directly visible at the top, in game metres, with defaults of 90 and 100. The radius finds aligned stop clusters; the link distance limits connections between separate symbols. Both still follow the active transfer-mode rules. Press Enter or Apply to commit a typed numeric edit; closing the dialog also commits pending edits and waits for the map update.

In RouteMapConfig, Use selected transfer modes (`UseCustomTransferModes`) enables the individual transfer modes. With it off, the legacy bus/trolleybus/tram and optional train/metro connection rules apply instead. The inactive choices are greyed out, and the status line identifies the active policy. A larger distance cannot connect a mode pair excluded by that policy. The old StopsMergingRange property is hidden because it does not control the visible collinear cluster layout.

View → Station areas controls visibility, framing of stop tracks, and red/blue tint. Station areas remain visible when rail infrastructure, route lines and station symbols are all hidden. The four supported BM underground station types project darker platform bands from their exported pedestrian path offsets and widths. The two center-platform types join their inner pedestrian paths into a continuous island, while side platforms remain separate. The full station footprint retains its lighter tint and shared outer border. These are pedestrian-width estimates, not architectural mesh outlines. Other underground metro station/stop assets, and BM assets without usable pedestrian metadata, retain their existing full-footprint tint. Ordinary metro tunnel tracks retain their rail appearance. The station-area preference also remains independent in Infrastructure only mode.

Transit route lines and station symbols with transit modal tags have separate switches. Symbols, transit modal tags, leaders and station selection work with route lines hidden; route lines can remain visible with all station markers hidden. The station layer menu is **Station symbols and transit modal tags**. **One transit modal tag per cluster** controls whether each cluster has its own tag or the local transfer group shares a combined tag. Railway → InfrastructureOnly clears and disables station highlighting while retaining the saved transit choices for restoration when switched off. Direction-path merging is omitted.

View → Options retains upstream layer order, terrain, SVG-symbol and font settings. Feature settings use `CS1MapView.config.xml`; navigation and layer preferences use `CS1MapView.settings`; per-city service overrides use `CS1MapView.routes.xml`. These files are beside `CS1MapView.exe`. Earlier settings files are read-only migration inputs.

View → Layers and labels provides direct visibility switches; Transit and Railway infrastructure provide mode and appearance controls. Lists opens the service/building editors. View → Map area, Rotate and Zoom provide drawing-size and navigation commands. File → Use current view as print/export area stores a geographic selection; rotation preserves its world positions, and Save image opens with that selection. Clear print/export area restores full-map image selection and current-view printing.

View → Map style selects any installed CS1 style directly. The Zoom menu includes the old viewer's intermediate percentages. File → Choose print/export area opens the actual map preview to draw or enter a rectangle without exporting a file; the selection keeps its geographic position through rotation.

The service list can sort by stop count and route length, show/hide all services, redraw, choose colours and edit shorthands. Sorting affects the table only. Manual visibility changes rebuild station display membership while preserving complete logical routes and surviving station highlights. The building list shows asset counts, services, subservices and item classes; its context menus set shape/name visibility and installed map symbols with previews. Default-symbol selection and resetting building overrides are available above the grid. Existing `AllVisible` name preferences and the old registered dot-separated SVG keys migrate to their native equivalents.

Help → Debug exports building/segment lists, segment/node details, a simplified inspection map, the native transport overview, and detailed service paths. These exports use a separate copy and leave the loaded map unchanged. The simplified map retains terrain, water and forest data; the overview uses JSON and the tabular reports use TSV.

## Images, crop, print and heightmap

Save image uses the inherited drawing-area selection dialog and supports PNG, JPEG, BMP, SVG and SVGZ. Its preview and final output own separate map layers, so resizing or rotating for export cannot change the interactive map. The selected station group's highlight is carried into the export. File → Print current view sends an independently rendered current view through the standard Windows print dialog. File → Save heightmap writes the original 1081×1081, 16-bit grayscale CS1 height samples with the established vertical orientation and 1/64 metre quantization.

## Automation

Build with `build.ps1`, or include `-SmokeTest -MapFile <reference-save>` to run the full checks with the separately supplied private fixture. `.NET SDK 8` or newer is required to build; `.NET Desktop Runtime 8` is required for the framework-dependent application.

```powershell
scripts/render-map.ps1 -InputFile samples/Springdale-20260927-124147.cslmap.gz `
    -OutputFile renders/Springdale/beta-full.png -CanvasSize 4096 -Transit

scripts/render-map.ps1 -InputFile samples/Springdale-20260927-124147.cslmap.gz `
    -OutputFile renders/Springdale/beta-detail.svgz -CanvasSize 1800 -Transit `
    -Crop '200,1300,1500,1500' -Rotate 15 -Config artifacts/beta-renders/acceptance-config.xml
```

Equivalent executable arguments:

```text
CS1MapView.exe --render map.cslmap.gz --output map.png --size 4096 --style apple-light --transit
CS1MapView.exe --render map.cslmap.gz --output detail.svgz --size 1800 --crop 200,1300,1500,1500 --rotate 15 --highlight 12345
CS1MapView.exe --heightmap map.cslmap.gz --output heights.png
```

`--size` is the longest image dimension. `--crop x,y,width,height` uses game metres, with X east and Y equal to negative game Z (south). Styles use the full-map virtual canvas implied by the crop's view scale, preserving the established fixed-plus-scaled widths. `--rotate` uses degrees. `--highlight` identifies an original physical stop node, selecting its existing local transfer group. `--config` reads a chosen legacy/beta configuration without changing it. `--transit` enables all supported route and stop modes for that render; other choices, including transfer-mode rules, come from the configuration. CLI errors return a nonzero exit code. The render helper captures stdout and stderr under `artifacts/map-renders`.
