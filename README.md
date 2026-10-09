# Railworks Workshop

Rails, minecarts, trains, trams and roller coasters for **Eco 0.14.1.1 beta release-1079**.

**Latest packaged version: [0.2.68 prerelease](https://github.com/Teirdalin/The-Railcraft-Workshop/releases/tag/v0.2.68).**

Download [Railworks-Workshop-Server-0-2-68.zip](https://github.com/Teirdalin/The-Railcraft-Workshop/releases/download/v0.2.68/Railworks-Workshop-Server-0-2-68.zip) for the server. It contains the matching `Railworks.dll` and `Railworks.unity3d` plus fourteen editable C# files in `Vehicles`. There are no READMEs, installers or client plugins in the server ZIP.

## Server installation

1. Back up your world and current mod files outside `Mods`, then stop the server or close the hosted game.
2. Copy `Railworks.dll` and `Railworks.unity3d` together into `Eco_Data/Server/Mods/UserCode` (or your dedicated server's `Mods/UserCode`). On a fresh installation, copy the `Vehicles` folder there as well.
3. On upgrades, preserve customized vehicle files and add missing settings files. Remove old `Eco.Minecarts.dll` / `EcoMinecarts.unity3d` copies and duplicate Railworks installations from `Mods` after backing them up outside that folder.
4. Restart the server. Always use the DLL and bundle from the same release.

Joining players receive the vehicles, tracks, models and animations through the normal server asset bundle. They need no third-party loader for those features. Existing saved object, item, recipe and prefab identities are retained.

Each vehicle has its own editable `.cs` file. Change capacity, slots, carried-item weight, physical mass and performance constants, then restart Eco. Eco compiles these settings automatically; no SDK or full source build is required. Keep the filenames, classes and keys intact. See the [vehicle customization guide](customization/README.md).

## Original vehicles and coaster guardrails

Version 0.2.67 animates the original coaster's full padded guardrails and swing arms: they rise for boarding and lower before departure. Cancelled departures can reopen them smoothly. This uses the existing replicated station animation system.

Version 0.2.68 docks the leading coaster car at the loading marker regardless of which coupled car owns the simulation or carries the rider. It retains the departure state until the entire train clears the station, including gaps between car centres.

All fourteen vehicles ship their original artwork. Newer vehicle meshes and textures are deferred for future updates and excluded from this bundle. `NewDesign` defaults to `false` and remains for configuration compatibility; a stale `true` setting still shows the originals. The server ZIP is approximately **42.5 MiB**.

Included updates since 0.2.40 improve independent coupled-car landing/contact, station queue progression and chain power, handcar curves and stop/dismount animation, lighting, seating and station presentation. Train lamps turn on at night or under high tunnel/cave cover. Coaster stations have editable signs and a **Return carts to station** button available once per station per server restart. See [release notes](RELEASE_NOTES.md).

Automatic positional resets on coaster loading are removed. Do not rely on momentum always resuming correctly after a restart; live restart behavior remains a validation task.

## Optional coaster camera

The improved loop camera is a separate, per-player download: [Railworks-Workshop-BepInEx-Camera-0-2-67.zip](https://github.com/Teirdalin/The-Railcraft-Workshop/releases/download/v0.2.67/Railworks-Workshop-BepInEx-Camera-0-2-67.zip).

Camera versions are independent of server releases. The unchanged 0.2.67 camera works with server 0.2.68; station, physics and model updates do not require reinstalling it. A new camera ZIP is published when the plugin itself changes.

Each player who wants it must install a compatible **BepInEx 6 IL2CPP** loader on their own Eco client, then extract the camera ZIP into the folder containing `Eco.exe`. The plugin belongs in `BepInEx/plugins`. The tested Windows x64 loader is BepInEx bleeding-edge build 788. The camera ZIP contains the plugin and its README; it does not include the loader or Eco assemblies.

Joining a server does not install the camera plugin. Players without it use Eco's normal camera. Keep the plugin out of server `Mods/UserCode`. The optional camera supports mouse look, follows the occupied car through loops and banks, and toggles with **Shift+F9**. See its [installation guide](development/Eco.Railcraft.CoasterCamera.Client/README.md).

## Source and verification

`src/Eco.Minecarts` contains the server source; `client` contains Unity authoring and exported-asset checks; `models` contains the repository's original model source assets. `development/Eco.Railcraft.CoasterCamera.Client` contains the optional camera source. The preserved deferred model library and generated Unity project are not uploaded to this repository.

Building Unity visuals requires the compatible Eco ModKit, Unity editor and matching authored project/assets. The shipping original-only export uses `MinecartAssetBuilder.BuildOriginalVehiclesBundle`; general modern-model exports are not the release path. Players and server owners should install the release ZIPs rather than copying full source into `Mods`.

**Verification:** clean server/client builds, private native Eco-server regressions, saved-schema compatibility checks and exported Unity animation/asset checks passed. These cover visible original guardrails, interrupted transitions, seat clearance, native physics preservation, wheels, running gear, dumping, handcar movement and station state replication. Live connected-player visuals, multiplayer riding and Linux operation remain unverified for this prerelease. Keep a matching rollback backup.

Original project material is licensed under [JDL-1](LICENSE). Dependency and supplied-material notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). This is an unofficial Eco mod.
