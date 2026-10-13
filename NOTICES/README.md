# Beta dependency and asset notices

Keep this directory and `LICENSE.upstream` with both runnable and source
deliveries. Package subdirectories contain the original restored licence/notice
files and `.nuspec` metadata. No metadata or licence text has been rewritten.
The source/copyright distinction for historical recovery inputs and native extensions is recorded in
`../SOURCE-PROVENANCE.md`.

| Dependency | Version | Licence / retained notice |
| --- | --- | --- |
| log4net | 3.3.1 | Apache-2.0; `log4net-LICENSE.txt`, `log4net-NOTICE.txt` and package metadata. |
| SkiaSharp and native/View libraries | 2.88.7 | MIT and their bundled third-party notices; separate original package notices retained. |
| SkiaSharp.HarfBuzz | 2.88.6 | MIT; original package licence retained. |
| HarfBuzzSharp and native assets | 7.3.0 | MIT wrapper and bundled HarfBuzz notices retained. |
| Svg.Skia, Svg.Model, ShimSkiaSharp | 1.0.0.13 | MIT; exact repository commit licence in `Svg.Skia-LICENSE.txt`, package copyright metadata retained. |
| Svg.Custom | 1.0.0.13 | MS-PL; `SVG-Custom-MS-PL.txt` and package copyright metadata retained. |
| ExCSS | 4.2.3 | MIT; `ExCSS-LICENSE.txt` and package attribution retained. |
| OpenTK and OpenTK.GLControl | 3.1.0 | OpenTK/third-party licence in `OpenTK-3.1-LICENSE.txt` and package attribution retained. |
| Microsoft System/Platform helper packages | Versions in their package subdirectories | Original package licence and third-party notice texts retained. |
| .NET and Windows Desktop win-x64 runtime | 8.0.29 | Original restored runtime licence and third-party notices retained in `microsoft.netcore.app.runtime.win-x64-8.0.29` and `microsoft.windowsdesktop.app.runtime.win-x64-8.0.29`. |
| Maki SVG map symbols | Copied published baseline | Mapbox Maki CC0-1.0 notice in `Maki-CC0.txt`. |
| Japanese SVG map symbols | Copied published baseline | Published source provenance and Gansaku MIT notice retained; no additional notice was supplied with those files. |
| Gfw.Common / Gfw.Ui.WindowsForms | Copied published baseline | Preserved upstream-distributed binaries and hashes; baseline supplies no separate library licence file. |
| CSLMapViewXML.dll | Copied published baseline | Byte-preserved upstream schema binary; hash/attribution recorded in SOURCE-PROVENANCE. No recovered XML source is compiled or included. |

`license-sources.csv` lists the primary URLs and SHA-256 hashes for additional
licence files. The Svg.Skia licence comes from the package's exact source commit;
OpenTK's from the corresponding `3.1` branch. The ExCSS, SVG and Maki licence texts
were fetched from their official repositories; package `.nuspec` files independently
record the licences of the restored versions.

The .NET Desktop Runtime is supplied and licensed by Microsoft. The self-contained
beta distribution retains the restored runtime licence/notice files above.
