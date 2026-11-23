# Seacrest Route Maker — Windows beta 0.5

## Install multiple races or restore one

1. Close the game. In **Game installation**, check the game folder and current save folder, then **Inspect**.
2. In **Race Library**, select a course, choose **Offline** or **Online**, then **Race** or **Arms Race**, and choose the stock route in the bottom dropdown. Click **Install route**. Online installation does not require an offline build, test, or completion report.
3. Confirm the custom course, target stock race, game and save folder. Other installed replacements are retained. Installing another course into the same stock event replaces that one assignment.
4. Open the **Replaced routes** tab (between Race Library and Game installation) to see `Custom course => Stock race (mode)` for the current game folder. Selecting a row previews its custom route on the right, with start/finish markers, intermediate markers and an estimated path. Select a row and **Restore selected to stock** to revert only that event. **Restore all to stock** restores the whole original archive. Both preserve current save progress.

This revision exposes 41 supported stock targets: 10 Offline Race, 16 Online Race and 15 Online Arms Race. All use the supported two-record restriction layout. The game-type selector is between Online/Offline and Install route. Offline forces Race and disables the game-type selector; switching from online Arms Race to offline also resets the stock-target list. Other event layouts remain excluded.

Arms Race replacements retain the original OnlineArmsRaceDefinition and non-route event fields, including the existing mode configuration. Only route locations and restriction data change. Static/build and copied-game tests pass, but Arms Race gameplay and weapon behavior still need a private multiplayer test. An ordinary Race event named ARMS RACE is distinct from the Arms Race mode.

Each operation rebuilds the complete replacement set from the verified stock archive, checks all 423 events and 232 resources, backs up current saves before an install, retains the previous archive, and verifies the replacement. An individual restore that leaves other replacements follows this same guarded install path. Standard Windows elevation is requested only when needed. No offline-test dialog is shown.

Installed assignments are recovered from the actual archive hash and matching build manifests, including supported legacy single-route installs. Keep `working/route-maker-builds` and `working/route-deployment`: they contain the replacement recipes, verified archive evidence, private originals, receipts, save snapshots and logs. Do not delete them while using replacements. Unknown or damaged state is refused rather than silently losing another route. Older diagnostic archives may require **Restore all to stock** before adding courses.

Prepared combined builds are internal installation snapshots, not standalone saved-course variants or portable ZIP releases. **Open latest export** opens the latest build folder. The older **Install selected build** control also merges a supported single build into the installed replacement set; it does not discard other assignments.

## Race Library

Open the **Race Library** tab to browse existing exports and saved courses. Your
six-marker course and Grand Ocean Sprint preset appear automatically. Select a
course to see its route, name, marker count, estimated length and separate offline/
online completion status. The build dropdown keeps each export variant accessible;
**Open selected build** opens its folder.

**Edit a copy** loads the selected course into the editor. Browsing alone leaves
your current draft intact; loading a copy preserves that draft in the session
folder. **Save to library** saves the editor's course as a new file under
`working/route-library`, then selects it in the library. Different course revisions
remain separate even with the same name. Identical routes are grouped together.
Original recipes and export files are never overwritten by library saving.

Use **Import recipe** for JSON recipes saved elsewhere. Imports are validated and
copied into the library. Existing maker exports and recipe-shaped JSON files saved
directly under `working` are discovered automatically; transient session drafts
are excluded. **Refresh** rescans files. Missing/corrupt items are reported while
intact courses remain usable. Restarting the app rebuilds the same local index.

Lengths labelled **world units** are estimates, not kilometres. A separately
reported game distance appears only where recorded. Completion status is based
on reports tied to the exact recipe/build; editing a route clears its old offline
build link. Offline completion reports remain historical evidence, not an online installation requirement.

November 4 cosmetic update (v0.1.1): the map is reflected vertically across its
horizontal axis, keeping left/right unchanged. Roads, markers and route previews
share the corrected orientation. Existing recipe coordinates are unchanged.

Run **Setup.cmd** first to generate local data from your own supported stock game. Then open **route-maker/SeacrestRouteMaker.exe**. Keep the extracted beta folder and its generated config, recipes and backups together.

## Make a course

1. Start with **Tested 6 km preset**, or choose **New route**.
2. Choose the marker role: start, waypoint or finish. Click a road on the map to
   see its existing compatible markers. Click a marker dot directly, or select
   it in the list and press **Use selected marker**.
3. Set one existing starting grid, existing waypoint markers, and one
   existing finish. Remove or reorder waypoints with the course-order controls.
4. Choose a stock restriction set. The default is Roadsters Reborn. This is an
   experimental choice for other courses: restricted section/direction semantics
   are not fully decoded. V0.1 offers stock sets containing exactly two records.
5. **Preview** validates marker roles, references, distinct IDs and undirected
   connectivity. Wheel zooms; right-drag pans; **Fit map** restores the full view.
6. Select **Save to library**. Choose the mode and stock target in **Race Library**, then **Install route**.
7. For multiplayer, use the same complete replacement set on both PCs and verify matching archive hashes before a private session.
8. **Delete** removes a course from the saved library only. Installed replacements and their build evidence are retained; use **Replaced routes** to restore installed events individually.

Green arrows estimate spawn facing; they do not rotate cars. Builds and installations are limited to **8 total markers, including start and finish** (at most six intermediate waypoints). Longer recipes remain readable and editable, but cannot be installed. A confirmed native preview-buffer overflow affects longer routes; this beta prevents those builds and does not expand the game buffer. The eight-marker control passed event preview; arbitrary courses still require gameplay checks.

CLI single-route exports still produce their own delta/helper ZIP and default to Roadsters Reborn unless `--target-id` is supplied. Their standalone helpers accept stock or that exact single candidate; use the app for multi-replacement installs. Combined snapshots contain only local build evidence and a delta, and are not advertised as portable export packages. Never distribute full archives or private save backups.

## What the map means

Road lines are decoded sampled centerlines from the owned game data: 115 road
groups, 332 sections, 2,735 points. Junction membership resolves completely at 225
junctions, with two junction references per section. The points/tangents are not
a newly exported terrain or collision mesh.

The **amber dashed line is an estimate** over an undirected graph. It ignores
direction, game restrictions and distance through junction gaps, and connects
the supplied sample points with straight segments rather than decoded splines.
It can choose a different branch from the game's actual path. Its numeric length
is in world-coordinate units, not a validated race distance. The tested preset
was reported as **6 km in the game**; the graph estimate is about 5,370 units.

Clicking a road selects available existing markers on it. It does not create a
new marker at the clicked point or enforce every highlighted road segment. Free
placement, arbitrary direction, road exclusion, exact path solving, new menus and
geometry editing are outside this revision. Static validation does not prove gameplay compatibility.

## Reproduction

Sources: `scripts/route_map.py`, `scripts/route_maker.py`,
`scripts/route-maker/SeacrestRouteMaker.cs`. Build with
`scripts/build_route_maker_app.py` using the project's existing Python runtime.
The app is compiled with the installed Windows .NET Framework compiler.

Command-line backend actions: library, save-library, catalog, preview, build-offline, build-online, build-arms-race and
record-offline, targets, installed-replacements and prepare-replacements. See `route_maker.py --help`. Recipes identify catalog Locations by
event ID and index, not arbitrary offsets or executable code. Build directories
are confined to `working/route-maker-builds` and never overwritten.

## Stock restriction set

This copies the selected stock event's `RestrictedRoadSections` records into the custom event. These are separate from the start, waypoints and finish; the selection changes the built game data, not just the map display. The 26 source choices contain 13 distinct record sets. Our successful short route retained the Roadsters set, while the Seacrest Tour conversion copied Seacrest's set.

The exact meanings of the record flags and their effect on allowed roads, route selection and barriers are not fully decoded. The preview does not simulate them. There is no test establishing that removing the records is harmless, so this update retains both the selector and its data. A future UI simplification could move the selector under Advanced while preserving existing recipe choices and a default for new courses.

`build-arms-race` defaults to Arms Race ROADSTERS REBORN (1104032); use `--target-id` to select another supported Arms Race event. It cannot target an Offline Race or ordinary Online Race definition.
