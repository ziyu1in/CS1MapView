# Collinear station clusters

Implemented 2026-10-09; transit modal tag leaders added 2026-10-10. The station-name proposals and manually named design previews are superseded by this implementation.

Use the [README glossary](../README.md#glossary) for network and transit terms.
Existing identifiers such as `StationBadgeLayout`, `ShowStationModeBadges`, and
`SplitStationBadgesByCluster` retain their spelling for compatibility.

## Controls

Open **View > CS1 features** and expand **RouteMapConfig**.

- **Cluster search radius (m)**: default 90, range 0–512. A circle is searched around every visible physical stop. All members of a candidate must fit in at least one of these circles. Several clusters may occupy a circle. Zero keeps distinct positions as separate dots.
- **Transfer link distance (m)**: default 100, range 0–512. Measured between the original stop positions, independently of the cluster radius. Zero disables links between distinct positions.
- **Show mode letters**: enabled by default. Turn it off to hide the fallback mode letters. Explicit line shorthands remain visible.
- **One transit modal tag per cluster**: enabled by default. Each displayed cluster gets its own transit modal tag and a thin black leader. Modes sharing that cluster remain together. Turn it off for one combined transit modal tag per local transfer group, connected to the nearest member that offers suitable placement. This also applies to explicit line shorthands.

The two distances and both toggles persist in the viewer configuration. **Transfers** chooses eligible mode combinations. **Highlight station** selects the routes serving the local station group. Escape or Clear restores the prior visibility settings.

Station names are omitted, including the previous transport-building name cleanup and name-based grouping. District, street and other building labels remain available. Line shorthands still come from a viewer override or the existing configurable route-number patterns. No station name is invented or inferred from a nearby building.

## Grouping and links

1. Collect visible stops, retaining all service memberships. Repeated visits to one physical node are deduplicated by service. Compatible stops at effectively identical positions (under 1 cm apart) share a dot.
2. Search every stop's radius neighborhood, including overlapping neighborhoods. Consider pairs, trios, quartets and larger subsets. A candidate must satisfy the selected mode policy for every pair of members.
3. Fit a straight axis by orthogonal least squares (PCA). Every member must be within the applicable maximum perpendicular distance: **5 m normally; 6 m (1.2x) for a single-mode group; 7 m (1.4x) for a single-mode group sharing a line and containing opposite-direction platforms**. These multipliers do not compound or change symbol width. Mixed-mode groups keep the 5 m tolerance. Exactly straight, dense rows use contiguous intervals because skipping intermediate dots would cause overlapping symbols.
4. Assign every point once. Rank partitions by opposite-direction same-line pairs kept together first, then same-mode pairs, then fewer symbols, shorter total span and lower fitting error. Direction comes from the first 20 m of each service's outgoing path; headings must oppose within 30 degrees. Missing paths do not establish opposite directions. Several groups can survive in one radius. Reject overlapping bars. Partition optimisation stops after 100,000 states for unusually large components, retaining the best valid partition found.
5. Build the legal link candidates using actual stop-to-stop distances and the selected mode policy. Internal edges are represented by cluster bars. Each pair of displayed symbols has one possible connector, at the nearest points on their axes. White interiors cover the hidden ends so the visible connector meets each symbol edge.
6. **Keep the shortest links without loops**, the user's final clarification. Sort connectors by length, with stable IDs resolving ties. Accept an edge only when it joins two separate components. This is Kruskal's minimum spanning forest: every reachable group remains connected, branches are permitted, and total link length is minimal. A 30/40/50 m triangle keeps its 30 m and 40 m sides. A 5x5 lattice with 50 m spacing retains 24 cardinal edges and no diagonals or cycles. This supersedes previous all-pairs and one-outgoing-link interpretations.

The constant symbol thickness is independent of either distance. At a 16384 canvas, cluster outer/inner widths are 14.4/11.8 pixels; link widths are 5.2/3.0 pixels. All dark outlines are painted before white interiors, removing black seams at joins.

Transit modal tags group serving lines by mode. The letters use one vertical color slice per distinct line, not per platform or direction. Modes are T (train), M (metro), MR (monorail), Tr (tram), B (bus), Tb (trolleybus), F (ferry), A (air). Transit modal tags use solid clipped glyph geometry, so PNG and SVG exports retain the same colors.

## Transit modal tag leaders

`StationBadgeLayout` measures the text and chooses short leader lines that avoid station symbols, transfer links, earlier transit modal tags and earlier leaders. It tests both ends of a bar and several outward placements; dots use radial placements. This is a local placement heuristic, not a guarantee of an overlap-free layout in every possible city. All Springdale transit modal tags pass the station-symbol and modal tag overlap checks.

Leaders are solid black, 0.3 render-scale units thick (1.2 pixels on a 16384 canvas), compared with 5.2 pixels for transfer-link outlines. Dot attachment is radial, perpendicular to the tangent of the circle. Bar attachment is at the closest rounded tip, using the actual cubic cap extent. The other endpoint meets the rounded transit modal tag border. Leaders are painted behind station symbols and transit modal tags; they never run through a symbol's white interior.

With splitting on, transit modal tag membership comes only from the visible routes stopping at that cluster. For example, Pennsylvania has separate teal M, train T and purple/gold M transit modal tags, plus a combined M/MR transit modal tag for the shared metro/monorail bar. Turning splitting off restores one transit modal tag for the local selection group with one leader to a nearby member. Both modes keep the existing multimodal highlight group. Clicking a transit modal tag or its leader selects that same group. Hiding fallback mode letters removes their leaders too; explicit line shorthands retain their transit modal tags and leaders.

The split toggle redraws labels without rebuilding routes or clusters. Older configuration files default to splitting on. To render the off state, set `RouteMapConfig.SplitStationBadgesByCluster` to false in a render configuration and pass it to `scripts/render-map.ps1 -Config <configuration-file>`.

## Route integrity and selection

`StationDisplayLayout` runs after route construction. It does not modify logical `MergedStations`, any route stop, physical track or combined-direction geometry. The existing one-stop-per-line safeguard remains in the logical route builder. Nearby opposite-direction platforms can therefore share a display symbol without deleting a journey.

Linked symbols share a highlight target when all their modes are mutually eligible and every pair of points is within twice the larger distance setting. They also share a transit modal tag when splitting is off. This bounds selection ownership on a long city-wide chain; links across the bound remain visible. Clicking either half of such a connecting link selects the corresponding local group. This selection bound never removes a transfer edge.

## Validation and renders

`build.ps1 -NoRestore -SmokeTest -MapFile samples/Springdale-20260927-124147.cslmap.gz` passes.

Checks cover subset enumeration, the three tolerances, same-line/direction and same-mode ranking, missing directions, multiple groups in one circle, input-order independence, dense straight rows, setting persistence/live apply, nearest-edge attachment and mode exclusions. Forest checks prove connectivity and absence of cycles, and verify that no discarded edge is shorter than the longest retained edge on the corresponding path. Existing click tests cover four rotations, three zoom levels and two crop sizes. All 16 Springdale monorail legs retain their full asset paths.

With all Springdale transit modes enabled: 114 physical positions, 188 candidate groups, 79 displayed symbols, 57 eligible physical stop-pair candidates, 6 displayed connectors and 73 selectable groups. Internal candidates are represented by bars. Pennsylvania has same-mode train and metro pairs; Primrose prioritizes the opposite-direction teal-line pair.

Transit modal tag checks also verify serving-route ownership, radial dot attachment, nearest bar tips, modal tag/leader hit targets, persistence, legacy defaults, mode letters off and the split-only live redraw. Springdale produces 79 transit modal tags with splitting on and 73 with it off.

Fresh Apple-light exports are under `renders/Springdale/badge-leaders-20261010/`: full-map PNG/SVG, Pennsylvania, Primrose and Prospect details, and a Pennsylvania comparison with splitting off. Close-ups preserve the actual viewer SVG drawing and change only its root viewport/output size. Station-area tint is enabled. Earlier renders remain in their previous directory.
