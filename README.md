# Seacrest Route Maker

A project to create and organize custom race routes on the existing Seacrest County roads in **Need for Speed Hot Pursuit Remastered**.

The goal is a single Windows app where players can design a course on a map, keep a library of saved races, install a selected route, and restore the original game archive.

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
- Dedicated package tools for installation and restoration, using archive checks and save backups.

Custom routes have been completed offline and in two-PC private multiplayer tests without reported issues. These results apply to the tested routes and game setup; each new course still needs its own validation.

## Next steps

- Bring route editing, building, installation, and restoration into the same app.
- Provide one restore-original action across supported project route variants.
- Continue investigating road direction, restrictions, and route guidance.
- Improve the library and map experience as the workflow develops.

## Current limits

The editor uses existing race markers rather than arbitrary checkpoint placement. Its map preview is an estimate: direction, restrictions, and junction traversal can make the in-game route differ. Estimated world-unit lengths are displayed separately from reported in-game distances.

The current approach replaces the route of an existing event, retaining its event identity. Controlled multiplayer testing uses matching route builds in a private session; entering a session successfully does not by itself establish synchronized race results.

## Repository status

This repository currently contains only the project concept. The application and its implementation are still being developed locally. Source code, executables, route packages, extracted game resources, and save data are not included in this initial publication.

This is an independent community project and is not affiliated with or endorsed by Electronic Arts or Criterion Games.
