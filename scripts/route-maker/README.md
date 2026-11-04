# Seacrest Route Maker v0.3

## Install, switch, or restore

1. Close the game. In **Saved races**, select a course and its offline or online build. Click **Game installation…**. You can also open the **Game installation** tab directly, including when you only want to restore.
2. Check the game folder and current save folder. **Inspect** identifies the installed route and whether the game is running. Choose **Install selected build** to install it or switch directly from a recognized route. The app backs up current saves, retains the previous archive, and verifies the replacement. If Windows needs administrator permission for the game folder, the app carries the same operation through the standard Windows prompt.
3. After testing, close the game and choose **Restore original** in the same tab. Restore works independently of the selected build. It verifies the original archive and preserves current save progress. **View last operation log** shows the result from the latest operation in this session.

The app recognizes verified local maker exports, the supported Seacrest Tour and Grand Ocean Sprint builds, and earlier diagnostic archives for restoration. It refuses an unknown archive. Keep the local project, its baseline files, and `working/route-deployment` with this installation. That directory contains private verified originals, route receipts, save snapshots and logs. The previous archive for each replacement is also retained beside the game archive. The separate package tool remains available for portable test exports.

## Saved races

Open the **Saved races** tab to browse existing exports and saved courses. Your
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
build link. Choose a valid matching offline export to continue online export.

November 4 cosmetic update (v0.1.1): the map is reflected vertically across its
horizontal axis, keeping left/right unchanged. Roads, markers and route previews
share the corrected orientation. Existing recipe coordinates are unchanged.

Open **SeacrestRouteMaker.exe** in the project's `route-maker` folder. This Windows
app uses the project's captured game data, Python runtime and verified packer;
it is not a standalone portable release. Keep the project and config in place.
No packages, game changes or background services are installed.

## Make a course

1. Start with **Tested 6 km preset**, or choose **New route**.
2. Choose the marker role: start, waypoint or finish. Click a road on the map to
   see its existing compatible markers. Click a marker dot directly, or select
   it in the list and press **Use selected marker**.
3. Set one existing starting grid, up to four existing waypoint markers, and one
   existing finish. Remove or reorder waypoints with the course-order controls.
4. Choose a stock restriction set. The default is Roadsters Reborn. This is an
   experimental choice for other courses: restricted section/direction semantics
   are not fully decoded. V0.1 offers stock sets containing exactly two records.
5. **Preview** validates marker roles, references, distinct IDs and undirected
   connectivity. Wheel zooms; right-drag pans; **Fit map** restores the full view.
6. **Export offline** creates a new folder under `working/route-maker-builds` with
   recipe, map estimate, candidate, manifest, re-extraction checks and Route-Test.zip.
   The export appears in **Game installation**. Select **Install selected build** when ready to test. For a separately shared package, its README describes the portable test tool.
7. After completing this exact offline course, **Export online after offline
   success** records your observations and builds the matching online event.
   To resume later, use **Saved races > Edit a copy** on that course. The library
   selects a valid matching offline export, preferring one with completion evidence.
   The already-tested 6 km preset unlocks online export by reproducing the exact
   successful offline archive; a new recipe does not inherit that success.

All exports replace ROADSTERS REBORN under its existing name and event identity:
career for offline exports, online Race for multiplayer exports. Use matching
online candidates on both PCs in a private session. Completing career tests can
update that event's saved result; never roll back saves automatically.

The export ZIP contains only delta/helper/hash/README files, not full game archives
or personal data. Its local helper checks the supported original, backs up current
saves/archive, stages and verifies replacement, retains another atomic backup,
and restores the exact original. Do not use the Seacrest Tour switcher to restore
an arbitrary maker candidate. Keep each PC's state/backups private and local.

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
geometry editing are outside v0.1. Each new course needs offline and online proof.

## Reproduction

Sources: `scripts/route_map.py`, `scripts/route_maker.py`,
`scripts/route-maker/SeacrestRouteMaker.cs`. Build with
`scripts/build_route_maker_app.py` using the project's existing Python runtime.
The app is compiled with the installed Windows .NET Framework compiler.

Command-line backend actions: library, save-library, catalog, preview, build-offline, build-online and
record-offline. See `route_maker.py --help`. Recipes identify catalog Locations by
event ID and index, not arbitrary offsets or executable code. Build directories
are confined to `working/route-maker-builds` and never overwritten.
