# Track-count examples

These images are direct exports from CS1MapView. They show physical railway
tracks around two splitting junctions. Each segment has two endpoint nodes,
regardless of how many tracks it contains.

| Example | Tracks on the incident segments | Distinct track positions across the approach |
| --- | --- | --- |
| [Two-track split](two-track-split.svg) | 2, 1, 1 | -2 m, +2 m |
| [Four-track split](four-track-split.svg) | 4, 2, 2 | -6 m, -2 m, +2 m, +6 m |

The first view also contains a parallel two-track railway. The four-track
approach in the second view enters from the upper left. Track counts come from
distinct exported railway-lane positions. A track carrying trains in both
directions remains one physical track.

The network examples use [RAILWAY 2 — American Wooden](https://steamcommunity.com/sharedfiles/filedetails/?id=2610252544).
Exact switch paths are not exported. The gaps and mismatched endpoints remain
visible; Reconnect tracks does not currently repair them.

## Display settings

The examples use Apple Light, separate tracks, the Apple railway appearance,
zero rotation and a 1,000 px longest dimension. Reconnect tracks is off.
Terrain, forest, buildings, roads and train infrastructure are shown. Other rail
modes, routes, station markers, station areas, text labels, symbols and the grid
are hidden.
