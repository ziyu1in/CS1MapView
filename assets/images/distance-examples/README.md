# Distance examples

These SVG files contain viewer exports with circle annotations.
All four examples use the same crop and the `apple-light` style.
Route lines are off.
Station markers, rail tracks, and station areas are on.

[![Cluster radius and transfer link distance comparisons](distance-comparison.svg)](distance-comparison.svg)

The green circle shows the cluster search radius for stop A.
The purple circle shows the transfer link distance from stop A.
Stop B is the second metro stop.
The viewer searches around every visible physical stop.

| Example | Cluster radius | Link distance | Local markers | Local links |
| --- | --- | --- | --- | --- |
| [Smaller cluster radius](radius-30-link-150.svg) | 30 m | 150 m | 3 | 2 |
| [Larger cluster radius](radius-90-link-150.svg) | 90 m | 150 m | 2 | 1 |
| [Shorter link distance](radius-90-link-25.svg) | 90 m | 25 m | 2 | 0 |
| [Longer link distance](radius-90-link-50.svg) | 90 m | 50 m | 2 | 1 |

The top image pair changes the cluster radius.
The bottom image pair changes the link distance.
The viewer calculates the station markers and transfer links in each example.

## Export settings

The crop is `-2330,120,400,400` in meters.
The map export has a width of 1,000 pixels and no rotation.
The circle annotations use the world-to-SVG scale from each export.
The figure identifies physical stops at these map positions:

| Stop | East | South |
| --- | --- | --- |
| A | -2132.69384765625 m | 316.49249267578125 m |
| B | -2186.456298828125 m | 351.885498046875 m |

See [examples.json](examples.json) for the measurements and layer selections.
See the [source gallery record](../gallery.json) for the private export and its hash.
The repository does not include the city export.

Each example has a configuration in [settings/](settings/).
The service override file leaves all services active.
Use an isolated copy of the complete build output for these commands.

```powershell
$runtime = (Resolve-Path 'artifacts/gallery-runtime').Path
$config = (Resolve-Path 'assets/images/distance-examples/settings/radius-30-link-150.xml').Path
Copy-Item 'assets/images/distance-examples/settings/all-services.routes.xml' "$runtime/CS1MapView.routes.xml" -Force
& "$runtime/CS1MapView.exe" --render 'C:/path/to/city.cslmap.gz' --output 'artifacts/radius-30-link-150.svg' --size 1000 --style apple-light --rotate 0 --config $config --crop '-2330,120,400,400'
```

Repeat with the other configurations.
Do not add `--transit` to these commands.
The viewer exports the map without the circle annotations.
