# Seacrest Route Maker

IMPORTANT: THE EXECUTABLE IS NOT BEING CREATED, IF YOU WANT TO HAVE A WORKING COPY MESSAGE ME ON DISCORD AND I CAN SHARE MY WHOLE PROJECT FILE THAT'S LIKE 7 GBS LOL. (DC: shbaum)

A project to create and organize custom race routes on the existing Seacrest County roads in **Need for Speed Hot Pursuit Remastered**.

The beta is a Windows app where players can design a course on a map, keep a library of saved races, install a selected route, and restore the original game archive.

In app Screenshot:
<img width="1304" height="892" alt="image" src="https://github.com/user-attachments/assets/441a847b-2c35-4b88-b64f-228bc5e37e3b" />


## The idea

Seacrest County already has a varied road network. This project explores new race itineraries through those roads by combining existing starting grids, route markers, and finish locations.

It changes race routes within existing events. Creating new terrain, road geometry, or separate event-menu entries is outside the current scope.

## Local prototype

The prototype currently supports:

- A map-based road and marker picker.
- Courses with a start, up to four intermediate markers, and a finish.
- A saved-race library with course names, route previews, estimated lengths, and separate offline/online export information.
- Versioned recipe saving and importing, with earlier course revisions retained.
- Offline and online test-package generation, with online export tied to a completed offline test.
- Integrated installation, direct route switching, and shared restoration, with archive checks and save backups.

Custom routes have been completed offline and in two-PC private multiplayer tests without reported issues. These results apply to the tested routes and game setup; each new course still needs its own validation.

## Windows beta v0.3.0-beta.1

Download **SeacrestRouteMaker-v0.3.0-beta.1-Windows.zip** from [GitHub Releases](https://github.com/yeedough/seacrest-route-maker/releases). Extract it into a new writable folder outside the game. With Windows, Python 3.10+ and the supported stock PC game installed, open a terminal in the extracted folder and run:

```powershell
py -3 setup_beta.py
```

Paste the folder containing NFS11Remastered.exe. Setup reads your game files to generate the map locally. Then launch **route-maker/SeacrestRouteMaker.exe**. Build a route offline first. Under **Game installation**, inspect the game folder, install a selected build, switch between supported routes, or restore the original. Keep your beta folder and its local backups. The classic desktop .NET Framework compiler is needed to export portable packages. To build the editor from source after setup, run `py -3 scripts/build_route_maker_app.py`.

## Current limits

The editor uses existing race markers rather than arbitrary checkpoint placement. Its map preview is an estimate: direction, restrictions, and junction traversal can make the in-game route differ. Estimated world-unit lengths are displayed separately from reported in-game distances.

The current approach replaces the route of an existing event, retaining its event identity. Controlled multiplayer testing uses matching route builds in a private session; entering a session successfully does not by itself establish synchronized race results.

## Credits

Game archive packing uses DGIorio’s [bundle_packer_unpacker](https://github.com/DGIorio/bundle_packer_unpacker), distributed with its MIT license.

This is an independent community project and is not affiliated with or endorsed by Electronic Arts or Criterion Games.
