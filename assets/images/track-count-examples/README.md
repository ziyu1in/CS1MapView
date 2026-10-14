# Track-count examples

These two SVG images are direct exports from the native CS1MapView renderer.
They show the current physical track geometry around splitting junctions.
No track geometry or endpoint connections were added to the images.

The source is the private `Springdale-20260927-124147.cslmap.gz` export.
Its SHA-256 is
`C9EBCB5BA1371BAE962FB536649AFD482F91BCC523B4B7471B91E01F3921F718`.
The save is not included in this repository.
The images were exported on 2026-10-13 from a Release build of the application source.

## What the images show

| Export | Crop in game metres: x,y,width,height | Junction node | Track counts on the three incident segments |
| --- | --- | ---: | --- |
| [Two-track split](two-track-split.svg) | `10,395,405,380` | 18221 | 2, 1, 1 |
| [Four-track split](four-track-split.svg) | `-3090,-225,500,550` | 31773 | 4, 2, 2 |

The first crop includes a parallel two-track network beside the split.
The four-track approach in the second crop enters from the upper left.
Its two-track branches lead toward a larger station-track layout.
Both crops retain the current gaps and mismatched endpoints.
Reconnect tracks does not work yet.

Track counts come from distinct usable train-lane positions in the export.
They are not taken from the asset names or the network width.
One line represents one physical track, including a track that carries trains in both directions.

| Junction | Segment IDs | Exported asset | Distinct train-lane positions (m) | Lines per segment |
| --- | --- | --- | --- | ---: |
| 18221 | 12610 | `2610252544.R2USA W GrWo 2x2_Data` | -2, 2 | 2 |
| 18221 | 8546, 32232 | `2610252544.R2USA W GrWo 1x1_Data` | 0 | 1 |
| 31773 | 31217 | `2610252544.R2USA W GrWo 4x2 N_Data` | -6, -2, 2, 6 | 4 |
| 31773 | 1160, 8454 | `2610252544.R2USA W GrWo 2x1 N_Data` | -2, 2 | 2 |

The common asset pack is [RAILWAY 2 - American Wooden](https://steamcommunity.com/sharedfiles/filedetails/?id=2610252544).
Exact switch paths are not part of the exported lane metadata.

## Export settings

[settings.xml](settings.xml) uses `apple-light`, the Apple track appearance,
Separate tracks on, Reconnect tracks off, and zero rotation.
The longest image dimension is 1,000 pixels.
Terrain, forest, buildings, roads, and train infrastructure are enabled.
Other rail modes, route lines, station markers, station areas, labels, symbols,
and the grid are disabled so the track lines are visible.

Build the viewer from the repository root.
Supply the private reference export at your own local path.

```powershell
./build.ps1
./scripts/render-map.ps1 -InputFile 'C:/path/to/Springdale-20260927-124147.cslmap.gz' -OutputFile 'artifacts/two-track-split.svg' -CanvasSize 1000 -Style apple-light -Config 'assets/images/track-count-examples/settings.xml' -Crop '10,395,405,380' -SkipBuild
./scripts/render-map.ps1 -InputFile 'C:/path/to/Springdale-20260927-124147.cslmap.gz' -OutputFile 'artifacts/four-track-split.svg' -CanvasSize 1000 -Style apple-light -Config 'assets/images/track-count-examples/settings.xml' -Crop '-3090,-225,500,550' -SkipBuild
```

If the normal build output is locked by an open viewer, build to a separate directory:

```powershell
dotnet build src/CS2MapView.Form/CS2MapView.Form.csproj -c Release -p:OutputPath="C:/path/to/CS1MapView/artifacts/track-count-runtime/"
```

Run that directory's `CS1MapView.exe` with `--render`, `--output`, `--size 1000`,
`--style apple-light`, `--config`, and the crop above.
Keep the complete build output together.

Only the two requested regional SVG examples and their settings are included in source control.
Full-map renders, PNG previews, build output, and verification logs remain local.
