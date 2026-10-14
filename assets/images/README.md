# README gallery

These eleven images were exported as SVG by this repository's native CS1MapView
renderer. They recreate the supplied screenshot views with approximate map crops,
without the application window or export-dialog frame. The original PNGs remain
local references and are ignored by Git.

All views use **apple-light**, a **90 m cluster radius**, a **150 m transfer-link
limit**, zero rotation and a 1,000 px longest dimension. These are the gallery's
settings; the application's default transfer-link limit remains 100 m.

The source is the separately supplied private export
`Springdale-20261012-141026.cslmap.gz`, SHA-256
`8E540B7FFFC5B2AF37E1A5767241A0B5D7AF64E462E0BCFC1571E58A0D292208`.
The city export is not included in this repository. The older September reference
used by the acceptance checks is a different export.

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

| Original screenshot | SVG | Routes | Markers | Physical rail | Transit modal tags | Additional visibility choice |
| --- | --- | --- | --- | --- | --- | --- |
| `Station area.png` | [station-area.svg](station-area.svg) | Off | Off | Off | — | All services available |
| `Rail, Station area.png` | [rail-station-area.svg](rail-station-area.svg) | Off | Off | On | — | All services available |
| `Uni-tag, Rail, Station area.png` | [Combined transit modal tags, rail, and station areas](combined-badges-rail-station-area.svg) | Off | On | On | Combined | All services available |
| `Routes, Uni-tag, Rail, Station area.png` | [Routes, combined transit modal tags, rail, and station areas](routes-combined-badges-rail-station-area.svg) | On | On | On | Combined | All services available |
| `Routes, Multi-tag, Rail, Station area.png` | [Routes, split transit modal tags, rail, and station areas](routes-split-badges-rail-station-area.svg) | On | On | On | Split | All services available |
| `Complex linking, Uni-tag.png` | [Complex links with combined transit modal tags](complex-linking-combined-badges.svg) | On | On | On | Combined | All services available |
| `Complex linking, Multi-tag.png` | [Complex links with split transit modal tags](complex-linking-split-badges.svg) | On | On | On | Split | All services available |
| `Complex linking 1-1.png` | [complex-linking-1-1.svg](complex-linking-1-1.svg) | On | On | On | Split | Both metro lines and train markers |
| `Complex linking 1-2.png` | [complex-linking-1-2.svg](complex-linking-1-2.svg) | On | On | On | Split | Orange metro line hidden |
| `Complex linking 1-3.png` | [complex-linking-1-3.svg](complex-linking-1-3.svg) | On | On | On | Split | Teal metro line hidden |
| `Complex linking 1-4.png` | [complex-linking-1-4.svg](complex-linking-1-4.svg) | On | On | On | Split | Both metro lines restored; train markers off, train routes on |

The numbered sequence demonstrates how changing the stops available for linkage
changes the displayed symbol and connectors. The two individual service choices
were matched to the remaining stop positions in the screenshots.

## Crops and reproduction

The CLI crop is `x,y,width,height` in metres, where x increases east and y is
negative game Z. Each group uses one identical crop so its layer changes can be
compared directly:

| Views | Crop |
| --- | --- |
| Station-area and main station examples (first five) | `500,1480,980,960` |
| Coastal complex linking (combined and split) | `1070,2300,650,640` |
| Numbered complex linking (1-1 through 1-4) | `-2490,65,650,580` |

[gallery.json](gallery.json) records every crop and visibility choice. Each view
also has an explicit application configuration and service-visibility override
under [settings/](settings/). An empty `.routes.xml` intentionally leaves all
services visible; the two nonempty files hide the named service using its city,
ID and name.

Build from the repository root. Copy the **complete** build output to a temporary
runtime directory under `artifacts/`, excluding the current user's
`CS1MapView.config.xml`, `CS1MapView.routes.xml`,
`CS1MapView.settings` and log files. This keeps personal viewer overrides
out of the gallery. For each view, copy its `.routes.xml` to that isolated
runtime as `CS1MapView.routes.xml`, then render with its matching config.
For example, after preparing `artifacts/gallery-runtime/`:

```powershell
$runtime = (Resolve-Path 'artifacts/gallery-runtime').Path
$config = (Resolve-Path 'assets/images/settings/complex-linking-1-2.xml').Path
Copy-Item 'assets/images/settings/complex-linking-1-2.routes.xml' "$runtime/CS1MapView.routes.xml" -Force
& "$runtime/CS1MapView.exe" --render 'C:/path/to/Springdale-20261012-141026.cslmap.gz' --output 'artifacts/complex-linking-1-2.svg' --size 1000 --style apple-light --rotate 0 --config $config --crop '-2490,65,650,580'
```

Repeat with the other view names, crops and configs. Run views sequentially
because they share the isolated runtime's service-override filename. Do not add
`--transit`: that convenience switch enables transit layers and overrides the
specific off/on choices used here. Changing the output extension to `.png`
produces a local raster preview with the same settings.

SVG retains the actual renderer's geometry; these files are not vectorized
screenshots. GitHub can display static SVG images in a README using relative
links. See [GitHub's image-rendering documentation](https://docs.github.com/en/repositories/working-with-files/using-files/working-with-non-code-files#rendering-and-diffing-images).
