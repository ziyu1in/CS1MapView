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

## Display settings

The panels use Apple Light with zero rotation. Route lines and map-name labels
are hidden. Station markers, railways and station areas are shown. The circles
are explanatory annotations; the viewer calculates the displayed markers and
transfer links. A and B identify stops only within this illustration.
