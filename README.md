# Railworks Workshop

Rails, minecarts, trains, trams, and roller coasters for **Eco 0.14.1.1 beta release-1079**.

**Latest packaged version: [0.2.40 prerelease](https://github.com/Teirdalin/The-Railcraft-Workshop/releases/tag/v0.2.40).**

Download `Railworks-Workshop-0-2-40.zip`. It contains the matching `Railworks.dll` and `Railworks.unity3d`, plus a `Vehicles` folder with 14 editable C# files and a customization guide.

## Installation

1. Back up your world and current mod files outside `Mods`.
2. Stop the Eco server or close the hosted game.
3. Install the matching pair and the `Vehicles` folder together in `Eco_Data/Server/Mods/UserCode` (or your dedicated server's `Mods/UserCode`). Remove the previous `Eco.Minecarts.dll` and `EcoMinecarts.unity3d` when upgrading from an older release. Keep only one copy of the mod under `Mods`.
4. Restart the server. Use both files from the same release.

Internal assembly, object, recipe and prefab identifiers remain compatible with existing saves. The latest schema check preserves all 929 saved definitions from 0.2.38.

## Changes in 0.2.40

- One editable C# balance file per vehicle, with automatic Eco startup compilation.
- Safe inventory resizing preserves cargo when a configured slot reduction cannot fit it.

Included from 0.2.39:

- Smaller packet-time corner corrections for handcars and manually pulled minecarts; refresh corrected poses for observers.
- Handcar maximum speed increased to 8 m/s (28.8 km/h).
- Explicit native client physics ownership for guided vehicles and coupled followers, restoring native physics when released.
- Mass-weighted consist gravity across slopes and transitions, with wheelbase-aware shared follower speed.

Updates since 0.1.0 also include blueprint export/import and station improvements, drive connection controls, electrical drives, conditional routing, performance profiling support, vehicle access/text fixes, support climbing and power transmission, smoother coaster jump landings, speed-sensitive brake effects, and braking chains. See [release notes](RELEASE_NOTES.md).

## Source and verification

Each vehicle has its own editable `.cs` file in `Vehicles`. Change inventory-item weight, physical mass, cargo capacity, slot count and performance/maintenance constants, then save and restart Eco. Eco compiles these standalone files automatically; no full source download, SDK or build project is required. Keep the file/class/key names intact. Preserve customized files during upgrades. See the [vehicle customization guide](customization/README.md).

`src/Eco.Minecarts` contains the current server source. `client` contains Unity authoring code and supporting assets; `models` contains model source assets. Building Unity visuals also requires the matching Eco ModKit and Unity environment. Installation uses the distribution ZIP.

**Testing status:** build, private native-server regressions, exported-bundle ownership callbacks and vehicle model checks passed. Native Eco startup checks compile all 14 editable files and verify changed weights, capacities and slots; cargo-preserving slot resizing is also checked. Live multiplayer driving, heavy-rear-car downhill behavior and Linux operation remain unverified in this prerelease. Keep a matching rollback backup.

The optional Shift+F11 profiler is a separate Windows client add-on; keep it out of server UserCode. This release's distribution does not include that add-on.

Original project material is licensed under [JDL-1](LICENSE). Third-party and supplied-material notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). This is an unofficial Eco mod.
