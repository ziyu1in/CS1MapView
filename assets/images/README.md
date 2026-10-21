# README gallery

These gallery images are native CS1MapView exports. They illustrate layer and
station-display options without application windows, city names or local place
labels. The diagrams use anonymous locations.

All views use **apple-light**, a **90 m cluster radius**, a **150 m transfer-link
limit**, zero rotation and a 1,000 px longest dimension. These are the gallery's
settings; the application's default transfer-link limit remains 100 m.

## Views and layer choices

Station areas, terrain, buildings, roads and forest are enabled throughout.
District, park, building and road labels, map symbols, the grid and contours are
disabled. Physical rail uses the Apple appearance, separate tracks, tunnels and
colors by type, with infrastructure-only mode off. Train, metro and monorail
station modes participate; other station modes are off. Train, metro, tram and
monorail service routes are available wherever the route layer is enabled.

Use the [README glossary](../../README.md#glossary) for network and transit terms.

"Combined" means one transit modal tag per local transfer selection group. "Split" means
one transit modal tag per visible cluster. Mode letters are enabled in all marker views.

| Example | SVG | Routes | Markers | Physical rail | Transit modal tags | Additional visibility choice |
| --- | --- | --- | --- | --- | --- | --- |
| `Station area.png` | [station-area.svg](station-area-v2.svg) | Off | Off | Off | — | All services available |
| `Rail, Station area.png` | [rail-station-area.svg](rail-station-area-v2.svg) | Off | Off | On | — | All services available |
| `Uni-tag, Rail, Station area.png` | [Combined transit modal tags, rail, and station areas](combined-badges-rail-station-area.svg) | Off | On | On | Combined | All services available |
| `Routes, Uni-tag, Rail, Station area.png` | [Routes, combined transit modal tags, rail, and station areas](routes-combined-badges-rail-station-area.svg) | On | On | On | Combined | All services available |
| `Routes, Multi-tag, Rail, Station area.png` | [Routes, split transit modal tags, rail, and station areas](routes-split-badges-rail-station-area-v2.svg) | On | On | On | Split | All services available |
| `Complex linking, Uni-tag.png` | [Complex links with combined transit modal tags](complex-linking-combined-badges.svg) | On | On | On | Combined | All services available |
| `Complex linking, Multi-tag.png` | [Complex links with split transit modal tags](complex-linking-split-badges.svg) | On | On | On | Split | All services available |
| `Complex linking 1-1.png` | [complex-linking-1-1.svg](complex-linking-1-1.svg) | On | On | On | Split | Both metro lines and train markers |
| `Complex linking 1-2.png` | [complex-linking-1-2.svg](complex-linking-1-2.svg) | On | On | On | Split | Orange metro line hidden |
| `Complex linking 1-3.png` | [complex-linking-1-3.svg](complex-linking-1-3.svg) | On | On | On | Split | Teal metro line hidden |
| `Complex linking 1-4.png` | [complex-linking-1-4.svg](complex-linking-1-4.svg) | On | On | On | Split | Both metro lines restored; train markers off, train routes on |

The numbered sequence demonstrates how changing the stops available for linkage
changes the displayed symbol and connectors. Each panel demonstrates a different service-visibility choice.

## Reading the gallery

The images show how layer choices and route visibility affect station markers,
transfer links and transit modal tags. Hidden-service examples change which
stops can participate in grouping. Colors distinguish services without exposing
private service names or record IDs. Local reproduction records and source
exports are not distributed with the gallery.

See the [map style and road comparisons](road-rendering-comparisons/README.md)
for newer rendering examples.
