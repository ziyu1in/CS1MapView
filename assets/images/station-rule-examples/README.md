# Station rule examples

These SVG files contain viewer exports with measured annotations.
The white station markers show the viewer results.
The bands, axes, stop labels, and blue arrows are annotations.
The dashed candidate axes show the groups in the alignment tests.

[![Collinearity and station selection priorities](station-rule-comparison.svg)](station-rule-comparison.svg)

## Collinearity

The viewer fits an axis through each candidate group.
The alignment error is the largest perpendicular distance from a stop position to that axis.
Each band extends the specified distance on each side of the axis.

| Example | Candidate | Maximum error | Alignment limit | Result |
| --- | --- | --- | --- | --- |
| [Accepted alignment](collinearity-accepted.svg) | Train stops A+B+C | 1.94 m | 6 m | One station marker. |
| [Rejected alignment](collinearity-rejected.svg) | Metro stops A+B and train stop C | 18.80 m | 5 m | Separate metro and train markers. |

The red segment from stop C to the candidate axis shows the largest alignment error.
The train marker includes other train stop positions in this view.

## Same-mode priority

[Open the same-mode example](same-mode-priority.svg).

A and B are metro stops.
C and D are train stops.
Candidate A+C+D passes the 5 m alignment limit with an error of 4.59 m.
The candidate layout keeps B as a separate marker.

| Layout | Station markers | Opposite-direction pairs on the same line | Same-mode pairs | Total marker axis length |
| --- | --- | --- | --- | --- |
| Selected: A+B, C+D | 2 | 1 | 2 | 75.15 m |
| Candidate: A+C+D, B | 2 | 1 | 1 | 40.18 m |

The two layouts have the same number of opposite-direction pairs.
The viewer selects the layout with more same-mode pairs.
This priority comes before the marker length priority.
Pair counts use distinct physical stop positions.

## Opposite-direction priority

[Open the opposite-direction example](opposite-direction-priority.svg).

A, B, and C are train stops.
A and B serve the same line and have opposite outgoing directions.
A and C serve different lines and have similar outgoing directions.
The blue arrows show the directions from the exported service paths.

| Layout | Station markers | Opposite-direction pairs on the same line | Same-mode pairs | Total marker axis length |
| --- | --- | --- | --- | --- |
| Selected: A+B, C | 2 | 1 | 1 | 84.64 m |
| Candidate: A+C, B | 2 | 0 | 1 | 21.68 m |

The viewer selects the layout with the opposite-direction pair on the same line.
This priority comes before the same-mode and marker length priorities.
The selected pair has a 7 m alignment limit.
The candidate pair has a 6 m alignment limit.

## Export settings

All examples use a 90 m cluster search radius and a 0 m transfer link distance.
Route lines are off.
Station markers, rail tracks, and station areas are on.
Map name labels are off.
The style is `apple-light`.
The map exports have a width of 1,000 pixels and no rotation.

| Example | Crop in meters | Visible services |
| --- | --- | --- |
| Accepted alignment | `-3680,-2360,220,220` | Train services 76 and 105. |
| Rejected alignment | `-2330,120,400,400` | All services. |
| Same-mode priority | `-2330,120,400,400` | All services except train service 76. |
| Opposite-direction priority | `-4120,-3740,240,240` | Train services 76 and 234. |

Service numbers identify export records.
See [examples.json](examples.json) for the physical positions, fitted axes, alignment errors, and pair counts.
See the [source gallery record](../gallery.json) for the private export and its hash.
The repository does not include the city export.

Each example has a configuration and a service override file in [settings/](settings/).
Use an isolated copy of the complete build output for these commands.

```powershell
$runtime = (Resolve-Path 'artifacts/gallery-runtime').Path
$config = (Resolve-Path 'assets/images/station-rule-examples/settings/same-mode-priority.xml').Path
Copy-Item 'assets/images/station-rule-examples/settings/same-mode-priority.routes.xml' "$runtime/CS1MapView.routes.xml" -Force
& "$runtime/CS1MapView.exe" --render 'C:/path/to/city.cslmap.gz' --output 'artifacts/same-mode-priority.svg' --size 1000 --style apple-light --rotate 0 --config $config --crop '-2330,120,400,400'
```

Repeat with the other configurations, override files, and crops.
Do not add `--transit` to these commands.
The viewer exports the map without the annotations.
