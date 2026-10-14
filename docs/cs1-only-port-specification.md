# CS1MapView: derivative of gansaku's CS2MapView

**Final scope document — 2026-10-10**

Terminology follows the [README glossary](../README.md#glossary), including
network nodes and segments, mode-specific tracks and lanes, route paths, and
transit modal tags.

Status: implemented for private beta development, with native replacements on `codex/cs1-native-skia`; see [native beta acceptance](cs1-native-beta-acceptance.md). The first beta is preserved on `codex/cs1-skia-beta`. This document supersedes the earlier source-comparison migration analysis wherever the requirements differ.

## 1. Product and source foundation

Develop a CS1-only derivative using Gansaku's published CS2MapView Core, Forms application and rendering infrastructure as the actual foundation. Preserve our current CS1 map appearance and transit/infrastructure features, subject to the explicit exceptions below. The outer UI, settings-dialog arrangement and normal mouse navigation can follow upstream.

Upstream source baseline: [gansaku/CS2MapView](https://github.com/gansaku/CS2MapView), commit `ecba18732777acb3de18a8378f06ab5047bc0e23`.

Retain meaningful, functioning upstream components: layer interfaces, drawing commands, Skia paint caches, view transforms, regional lookup, asynchronous layer rebuilding, generic terrain machinery, theme/settings infrastructure, SVG symbol loading, Forms host and the adapted transport renderer. Preserve upstream history or source provenance and MIT notices. Code lineage, rather than a target similarity percentage, supports describing the result as a derivative.

The current CS1 application supplies the feature and visual reference. It remains unchanged during planning. Existing custom algorithms should be adapted to the new core; recovered code and independently added code must have their origins recorded separately. The upstream license does not automatically change the license of other recovered implementation.

## 2. Explicit decisions and permitted differences

| Decision | Final requirement |
| --- | --- |
| Game compatibility | Support CS1 only. Remove CS2 game import/export and its game-specific drawing code. |
| Map appearance | Preserve our palette, network geometry, line/border widths, station symbols, transit modal tags, leaders, infrastructure colours and station-area treatment. |
| UI and navigation | Keep upstream Forms layout and mouse controls where they do not cause incorrect rendering, export or selection. Matching the old zoom steps and wheel gestures is unnecessary. |
| Street labels | Keep upstream path-following street-name rendering. Straight labels identical to the current WPF viewer are not required. |
| Transport-building names | Keep upstream name cleanup and grouping of same-named transport-building labels. This is an approved exception to the current viewer's omitted station-name labels. |
| Transit rendering foundation | Adapt the active CS2 transport renderer into a renderer that consumes CS1 data. Do not base the work on enabling the inactive experimental CS1 renderer. |
| Opposite-direction route merging | Defer it entirely from the initial derivative: no direction-merging algorithm, controls or active configuration option. Draw the original CS1 service paths. |
| Station direction rules | Keep opposite-direction platform evidence and priority in station clustering. This is independent of merging route paths. |

The name-label decision restores upstream labels, not custom invented station names or manual naming proposals. Label grouping is visual only: a shared name must not merge physical stops, services, transfer membership or highlight ownership.

### Verified historical detail

The preserved raw CS1 recovery has transport-building name cleanup and grouping by cleaned name at the mean label position. Our current code excludes public-transport building names, matching the later station-display requirements. The original street renderer builds a straight text outline and rotates it to the road direction; upstream CS2 renders text along the road path. Both upstream behaviours are now permitted in this derivative, regardless of those historical differences.

## 3. What to keep, change and remove

| Area | Keep | Change or add | Remove or defer |
| --- | --- | --- | --- |
| Core and UI | Published Core, Forms host, Skia drawing infrastructure, caches and layer lifecycle | Extend them for the required CS1 features | CS2 game branches and misleading game-specific UI entries |
| Input | CS1 XML/GZip schema, original IDs, lane metadata, older-export fallbacks | Preserve raw data before simplification and add the CS1 transit adapter | CS2 ZIP map parser and exporter |
| Base map | Current Apple-light palette and road/building/terrain presentation | Translate styles and scale formulas; add missing forest/park-label support | Unrequested changes to network appearance |
| Roads | Correct exported-width precedence and validated usable-lane fallback | Apply those rules to the upstream CS1 segment manager and road builder | Reverting to rounded widths inferred from every lane |
| Routes | Colours, visibility, overrides, shared-service widths, arrows, route integrity | Adapt the active upstream transport renderer and reconstruct CS1 paths | Opposite-direction route merging |
| Stations | Current cluster selection, transfer forest and selectable local groups | Translate their drawing/hit geometry into the upstream renderer | Using display clusters as logical route stops |
| Transit modal tags | Mode-letter slices, explicit shorthands, split/combined transit modal tags and leaders | Match drawing, placement and hit ownership in Skia | Name-based changes to transit modal tag or route membership |
| Rail infrastructure | Separate tracks, reconnect behaviour, colours, tunnels and both appearances | Implement these inside upstream's CS1 railway layer | CS2 rail-data dependency |
| Station areas | Selection rules, platform tint, passage/entrance treatment and red/blue controls | Port geometry operations and layer composition | Changed asset-selection rules without a separate request |
| General labels | Upstream curved street labels, transport-building label grouping and SVG symbols | Match colours/fonts/halos; add missing CS1 park labels | Mandatory emulation of the old straight-text layout |
| Interaction | Upstream normal navigation; current station-selection semantics | Add correct hit testing, highlight/clear behaviour and feature controls | Mandatory replication of old navigation gestures |
| Settings | Current saved choices, defaults and per-route/building overrides | Add migration into the new settings/theme model | Active direction-merging settings |
| Output | Current required image, crop, highlighted-view and automation behaviour | Adapt CLI, export and supporting utilities to the new application | CS2 output-map generation |

## 4. Make the data pipeline CS1-only

### Project pruning

Remove the CS2 Exporter and Serialization projects from the final build after their remaining consumers have been adapted. Remove CS2 map deserialization, map types, game-specific geometry helpers and the CS2 builders for roads, buildings, railways, terrain, districts, grid and labels. The transport renderer's useful implementation is retained and adapted before its CS2 dependencies are removed.

Use `.cslmap` and `.cslmap.gz` in open dialogs, drag-and-drop and automation. Retain the old-file compatibility paths. Names such as `ICS2MapViewRoot` refer to reusable infrastructure and do not imply a requirement for CS2 game compatibility; namespace/product renaming can follow once the functioning derivative is established.

Resolve the source repository's build references and package required dependencies. For example, the inspected Core project points at `oldver/CSLMapViewXML.dll` while its published DLL is under `lib/`. That DLL is byte-for-byte identical to our original CS1 XML library. Include the required managed/native dependencies and applicable notices in the runnable package.

### Preserve source data

The published CS1 importer simplifies segments twice before returning the imported map. Refactor that sequence so original segments, node IDs, PathUnit references and asset identities survive long enough for transit, track metadata and station-premises collection.

Maintain separate raw, transport and base-map views, or equivalent immutable source data plus derived models. Base-map simplification must not truncate transit paths or discard asymmetric track layouts. Keep geometry calculations in game metres and retain double precision for fitting, matching and reconnection until drawing conversion.

## 5. Adapt his active transport renderer directly

The starting implementation is [CS2TransportLineLayerBuilder](https://github.com/gansaku/CS2MapView/blob/ecba18732777acb3de18a8378f06ab5047bc0e23/CS2MapView.Core/Drawing/Transport/CS2/CS2TransportLineLayerBuilder.cs).

Keep its layer lifecycle, asynchronous build/resize/rotate integration, view transformation, drawing-command construction, style/cache usage and visibility dispatch. Replace its `CS2MapDataSet` input and CS2 serialization types with a CS1-compatible internal transit model. The internal model is a viewer model, not a new map-file format.

| Renderer input | CS1 source and required adaptation |
| --- | --- |
| Service identity, name and colour | XMLTransport ID/name/UserColor plus viewer overrides |
| Stop identity and position | XMLStop node IDs, original node data and the current stop-position rules |
| Stop-to-stop paths | Reconstruct ordered paths from the original PathUnit segment references, orienting connected segments correctly |
| Route geometry | Accept the exported point sequences directly; the CS2 renderer currently expects cubic curves. Do not invent smoothing that changes CS1 paths |
| Mode | Map CS1 mode semantics explicitly, including trolleybus, monorail and the game's blimp/ferry representations; do not depend on CS2 enum values |
| Membership | Keep every service serving each physical stop and each path portion, including repeated visits and shared stops |
| Visibility | Apply per-mode switches, manual route visibility and transient highlight filtering together |
| Direction | Retain service-specific arrows and original paths; omit the deferred route-direction merger |

This adaptation still needs CS1 route reconstruction. A working drawing loop cannot infer those paths from stop coordinates alone.

Extend the inherited drawing behaviour to match our route colours, opacity, joins, caps and shared-service widths. Replace the stock stop-circle drawing with the required station display. Keep custom station symbols and their hit targets anchored; the general label arranger must not independently move or suppress them. The transit modal tag layout owns modal tag/leader placement.

Keep the full-path and terminal-loop safeguards, especially for monorail. Preserve logical stops independently of all visual grouping. Ship/air paths and missing-data fallback must follow the current supported behaviour rather than being guessed from another mode.

## 6. Preserve our station and infrastructure features

### Station clusters and transfers

Retain the current [station rules](/C:/Users/Admin/Documents/ChatGPT/CSLM/docs/collinear-station-clusters.md):

- Independent persisted search radius and transfer-link distance, defaults 90 m and 100 m.
- Overlapping neighbourhood searches, aligned subsets and multiple non-overlapping clusters.
- Perpendicular fitting limits of 5 m, 6 m for same-mode groups and 7 m for qualifying opposite-direction same-line groups.
- Same-line opposite-direction platform priority from outgoing-path evidence, then same-mode priority.
- Minimum spanning forest over legal symbol pairs, nearest-axis connector cost and stable tie breaking.
- Constant symbol thickness, dark outlines followed by white interiors, and selection ownership independent of route construction.

Dropping route-direction merging does not remove these station rules: headings can be read from the original outgoing route paths.

### Transit modal tags and selection

Retain optional mode letters with vertical serving-line-colour slices, explicit shorthands, split-by-cluster default, combined modal tag option and thin black leaders. Leaders attach radially to dots and to rounded bar tips, stopping at both outlines. Transit modal tag, leader, cluster and connector hits keep their current local transfer-group ownership.

Retain highlight mode, Clear/Escape, click-again/empty-space clearing, nearest-group selection for overlapping shapes and preservation of manually hidden routes. Name labels introduced by the approved upstream policy must not change these memberships.

### Physical tracks

Retain [rail infrastructure behaviour](/C:/Users/Admin/Documents/ChatGPT/CSLM/docs/rail-infrastructure.md) and [track reconnection constraints](/C:/Users/Admin/Documents/ChatGPT/CSLM/docs/track-reconnection.md): lane-derived separate train/metro/tram tracks, monorail/cable-car centerlines, colours, tunnels, Apple/Google appearances, cross-ties, conservative reconnection and unchanged fallback. Infrastructure-only mode hides service overlays and markers, disables highlighting and restores previous layer choices when turned off.

### Station premises

Retain [station-area selection and presentation](/C:/Users/Admin/Documents/ChatGPT/CSLM/docs/station-areas.md): station lots, optional full stop tracks, entrances/passages, decorative platforms, asset exclusions, cargo handling and red/blue tint. Adapt unions, buffering, subtraction and shared borders to Skia while preserving their visible result.

## 7. Rendering compatibility

Create a current-appearance profile using the Apple-light colours, fonts, halos, building/road borders, rail styles, station widths, transit modal tag clipping, leader widths and layer order. Translate our fixed-plus-scaled style measurements into upstream's world/view/export coordinates explicitly. Export size must not silently change stored metre distances or logical groups.

Carry forward corrected road-width precedence, fractional widths and usable-lane fallback. Preserve rail asset identities across simplification. Add the current forest-density overlay and independent visibility; the inspected upstream parses the forest data without drawing it. Add CS1 park labels, which its inspected label builder omits.

Keep upstream path-following street labels and transport-building label grouping as approved. Match their typography, colours and halos to the current style; exact current label positions are not required where upstream placement differs. Keep these name labels independent from the custom transit modal tag system.

Differences in antialiasing and font metrics must be evaluated in actual renders. Visual acceptance is not a byte-identical PNG promise. The permitted label and original-direction-path differences are intentional; changes elsewhere must be checked against the current viewer.

## 8. UI, settings and output compatibility

Keep the inherited Forms mouse controls, wheel scaling and rotation gestures. Change navigation only where it creates incorrect coordinates, clipping, selection or render state. Remove CS2-specific map-expansion assumptions as part of making the application CS1-only.

Add our feature controls to upstream's UI: infrastructure appearance/tracks/reconnection, station highlighting, transfer modes, separate cluster distances, transit modal tags, station areas, route visibility/colour/shorthand editing and building preferences. Preserve live apply, correct busy-state handling and cancellation or versioning of obsolete asynchronous rebuilds. Menu and dialog layout may adapt.

Hit testing must use the inverse of the actual view, crop, rotation and DPI transformation and the same geometry as the displayed shapes. Ordinary dragging still pans; an intended station click selects the correct local group.

Import the existing configuration, styles, building preferences and per-route overrides into the new settings model. Preserve independent settings and established defaults. An old `CombineNearbyDirections` choice or merge distance has no active effect in the initial derivative and must not create a hidden merger or visible control. Keep the current viewer's settings unchanged.

Support the required PNG/JPEG/BMP/SVG/SVGZ output, crop/drawing-area selection, highlighted exports, heightmap saving, printing and automated rendering. Add the CLI/render-helper integration absent from the inspected upstream startup. Screen and export must share route, station and infrastructure geometry; export-specific rebuilds must not leave interactive state altered.

## 9. Compatibility and acceptance checklist

| Contract | Acceptance evidence |
| --- | --- |
| CS1 files | Load plain and GZip exports, including supported older versions; no CS2 parser required |
| Route integrity | All Springdale services and complete paths survive; stop order/membership and monorail safeguards pass |
| Deferred merger | Original service paths are drawn; no direction-merging controls or algorithm run |
| Stations | Cluster/link rules, independent distances, stable results and logical/display separation pass |
| Transit modal tags | Both split states, letters on/off, explicit shorthands, colour slices, leader attachment and click ownership pass |
| Rail and premises | Track counts/spacing, reconnection fallback, both appearances, tunnels, colours and station-area selection pass |
| Navigation and highlighting | Upstream controls work; transformed hits, dragging, clearing and visibility restoration pass |
| Labels | Curved street names and grouped transport-building labels work; grouping never changes route or transfer membership |
| Appearance | Palette, strokes, symbols, areas and composition match the reference apart from approved differences |
| Settings/output | Configuration migration and live apply pass; required output and automation behave consistently |
| Provenance/package | Functional upstream ancestry, retained notices and a runnable dependency package are recorded |

Use the unchanged Springdale save at [Springdale-20260927-124147.cslmap.gz](/C:/Users/Admin/Documents/ChatGPT/CSLM/samples/Springdale-20260927-124147.cslmap.gz), SHA-256 `C9EBCB5BA1371BAE962FB536649AFD482F91BCC523B4B7471B91E01F3921F718`.

Adapt the relevant existing [behaviour checks](/C:/Users/Admin/Documents/ChatGPT/CSLM/tests/CSLMapView.SmokeCheck). Compare the current viewer with direction merging disabled, so its merged-path view is not the wrong reference for the deferred feature. Test full-map output and Pennsylvania, Primrose and Prospect details, modal tag/letter variants, highlighted routes, infrastructure modes, crops and rotation/zoom/DPI hit transforms. Treat the approved label changes as explicit exceptions when comparing images; retain geometry and membership checks alongside image review.

Future delivered code changes still require a fresh actual Springdale render, including affected layers and relevant details. Planning/document-only work does not require a new render. Deliver source through the project's GitHub repository and attach runnable packages to GitHub Releases when a release is requested. Keep renders and supporting reports local unless requested.

## 10. Implementation sequence

1. Establish a reproducible upstream build and preserve its source baseline/notices. Separate the CS1 raw-data stage from base-map simplification.
2. Adapt the active transport renderer to a CS1 internal model, then remove the remaining CS2 game projects and import branches.
3. Match the base-map profile: styles, widths, terrain/forest, footprints, icons and approved upstream labels.
4. Complete route reconstruction and rendering, station clusters/transfers, transit modal tags/leaders and highlighting. Keep direction merging out of this version.
5. Add physical-track infrastructure, reconnection, station premises and the required controls/state restoration.
6. Complete settings migration, editors, exports/automation and packaging; pass the compatibility checks and actual render comparisons.

The largest work remains CS1 path reconstruction, the custom station/infrastructure renderer and visual validation. Reusing the active transport layer avoids rebuilding its drawing infrastructure, but does not remove those CS1-specific requirements. No build, render-parity result or performance improvement has been established for the derivative yet.
