# Seacrest Route Maker

!!! THE APP AS IT IS NOW, WOULD NOT BRICK YOUR GAME, BUT JUST TO STAY IN THE SAFE ZONE, IT'S RECOMMENDED TO SAVE A COPY OF YOUR GAME FILES FIRST. !!!

If all this setup process is too hard for you, you can reach out to me on discord and I can send you a prebuilt app in a zip. discord id: shbaum

A project to create and organize custom race routes on the existing Seacrest County roads in **Need for Speed Hot Pursuit Remastered**.

The beta is a Windows app where players can design a course on a map, keep a library of saved races, install a selected route, and restore the original game archive.

In app Screenshot:
<img width="1919" height="1040" alt="image" src="https://github.com/user-attachments/assets/9db87ca0-cc99-4c90-a03f-dcee5e8f6e29" />



## The idea

Seacrest County already has a varied road network. This project explores new race itineraries through those roads by combining existing starting grids, route markers, and finish locations.

It changes race routes within existing events. Creating new terrain, road geometry, or separate event-menu entries is outside the current scope.

## Windows beta features

This beta supports:

- A map-based road and marker picker, with up to eight total installed markers (start and finish included).
- Race Library with named routes, previews, estimated lengths, recipe importing and retained revisions.
- Multiple stock-event replacements at once: 10 Offline Race, 16 Online Race and 15 Online Arms Race targets.
- A Replaced routes tab with each installed course preview and individual restoration.
- Direct online installation without an offline-test prerequisite. Arms Race is online only.
- Archive validation, save backups, and restoration of the complete stock archive.

## Windows beta v0.5.0-beta.1

Download **SeacrestRouteMaker-v0.5.0-beta.1-Windows.zip** from [GitHub Releases](https://github.com/yeedough/seacrest-route-maker/releases/tag/v0.5.0-beta.1). Extract into a new writable folder outside the game. Install Python 3.10+ and run **Setup.cmd**, or run:

```powershell
py -3 setup_beta.py
```

Paste the supported stock game folder containing NFS11Remastered.exe. Setup generates the map from your own installation. Open **route-maker/SeacrestRouteMaker.exe**. Save a course, open **Race Library**, choose Offline/Online, Race/Arms Race and the stock target, then **Install route**. **Replaced routes** shows installed assignments and restores them individually. **Game installation** offers full restoration. Use matching complete replacement sets on both PCs for private multiplayer.

Keep your previous app folder, recipes, build evidence and backups. Restore replacements through the previous app before setting up this beta against stock files. Import your saved recipe JSON files into the new library. Do not delete the folder holding an active installation's build manifests or backups.

The editor EXE is included in both download paths. Windows .NET Framework is required; its compiler is also used for portable helper exports. To rebuild the editor, run `py -3 scripts/build_route_maker_app.py`. See [the app guide](route-maker/README.md) for details.

## Current limits

**Eight total markers maximum for installation.** Longer saved recipes can be opened and edited, but builds above eight are refused because of a confirmed native event-preview buffer overflow. This is crash prevention, not expanded waypoint support. An eight-marker control passed preview; every new course still needs gameplay testing.

The editor uses existing race markers rather than arbitrary checkpoint placement. Its map preview is an estimate: direction, restrictions, and junction traversal can make the in-game route differ. Estimated world-unit lengths are displayed separately from reported in-game distances.

The current approach replaces the route of an existing event, retaining its event identity. Controlled multiplayer testing uses matching route builds in a private session; entering a session successfully does not by itself establish synchronized race results.

## Credits

Game archive packing uses DGIorio's [bundle_packer_unpacker](https://github.com/DGIorio/bundle_packer_unpacker), distributed with its MIT license.

Special thanks to Zeroday for helping me with the tests, having his first custom made race with me. 

This is an independent community project and is not affiliated with or endorsed by Electronic Arts or Criterion Games.
