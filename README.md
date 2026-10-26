# Railworks Workshop

Trains, trams, minecarts, roller coasters and rail infrastructure for **Eco 0.14.2.1 release-1106**.

**Latest release: [0.2.118 prerelease](https://github.com/Teirdalin/The-Railcraft-Workshop/releases/tag/v0.2.118).** Download [Railworks-Workshop-Server-0-2-118.zip](https://github.com/Teirdalin/The-Railcraft-Workshop/releases/download/v0.2.118/Railworks-Workshop-Server-0-2-118.zip).

## What's included

- Trains, passenger and cargo cars, trams, handcars, minecarts and roller coasters.
- Stations, routing and conditional automation, passenger fares and station traffic rules.
- Gradual braking into tight corners, coupled vehicle handling and coaster station guardrails.
- Rail supports with central standing surfaces, crossings and bell assets in one Unity bundle.
- Assembly Line train recipes, iron train components and editable vehicle/rail settings.
- Hidden administrator-spawned Thunderfury with a recreated model and cosmetic lightning impact bindings.

Version 0.2.118 prevents stale station departures when relevant vehicle state, conditions, rail routes or tram settings change while a reservation is waiting. See [release notes](RELEASE_NOTES.md).

## Installation

1. Close Eco and its hosted server, or stop the dedicated server.
2. Extract the matching `Railworks.dll`, `Railworks.unity3d`, `Vehicles` and `Rails` into server `Mods/UserCode`. Hosted games use `Eco_Data/Server/Mods/UserCode`.
3. On upgrades, preserve customized settings and add missing files. Remove obsolete `Eco.Minecarts.dll` / `EcoMinecarts.unity3d` and duplicate Railworks installations from `Mods`.
4. Restart Eco. Always use the DLL and bundle from the same release.

The ZIP contains 24 runtime/settings files: one DLL, one unified bundle, fourteen vehicle settings and eight rail settings. Crossing assets are already in the unified bundle. Joining players receive these assets normally and need no third-party loader for the server mod.

Each settings `.cs` file can be edited before restarting Eco. Keep its class, filename and key intact. Large Cargo Cars allow dumping by default; customized opt-outs can be retained. See [customization](customization/README.md).

## Optional coaster camera

The [optional camera 0.2.66](https://github.com/Teirdalin/The-Railcraft-Workshop/releases/download/v0.2.68/Optional.Railworks-BepInEx-Rollercoaster-Camera-0-2-66.zip) is independently versioned and remains a separate player download. It requires BepInEx 6 IL2CPP on each participating client. Its plugin belongs in `BepInEx/plugins`; toggle with **Shift+F9**. Server updates do not require reinstalling this unchanged optional plugin.

## Source and validation

`src/Eco.Minecarts` contains the exact authored server source corresponding to 0.2.118. `client` and `models` retain the public authoring material; the complete Unity project and deferred assets are maintained separately. Use the packaged release for installation.

Matched disposable native server runs passed 2,011 selected cases per build, with 31,731 baseline / 32,583 candidate assertions. The newest cases reduced stale dispatches from 140 to zero; all recovered after fresh observations. All 1,048 saved definitions were unchanged from 0.2.116. Archive CRC, all 24 entry hashes and the release-content guard passed. Live connected gameplay, multiplayer, natural service after reload and whole-server performance remain unverified for this prerelease.

Original project material uses [JDL-1](LICENSE); see [third-party notices](THIRD_PARTY_NOTICES.md). This is an unofficial Eco mod.
