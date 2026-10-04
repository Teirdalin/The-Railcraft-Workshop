# The Railcraft Workshop

Rails, minecarts, trains, trams, and roller coasters for **Eco 0.14.1.1 beta release-1079**.

**Latest packaged version:** [0.1.0 prerelease](https://github.com/Teirdalin/The-Railcraft-Workshop/releases/tag/v0.1.0). Download `Railcraft-0-1-0.zip`; it contains only the server DLL and matching Unity asset bundle. Back up the world and current mod pair, stop the Eco server, and copy `Eco.Minecarts.dll` and `EcoMinecarts.unity3d` together into `Mods/UserCode`. Keep only one copy of each under `Mods`, then restart the server. Keep backups outside `Mods`.

This build includes one-block coaster stations and station cart placement, corkscrews and descending coils, compact coaster transitions, front/rear shove and coupling controls, hammer-only chain power/direction indicators, workbench improvements, and material-based map colors and distant textures for all 568 custom blocks.

**Testing status:** server and bundle builds and package checks passed. The newest overlays and distant appearance still need in-game verification. A Linux server with an existing modded world failed to finish startup with the Coil Update; the cause remains unresolved in this package. Vehicle paint colors are currently unavailable during the native `Curved/Standard` shader test. The [previous beta.16 release](https://github.com/Teirdalin/The-Railcraft-Workshop/releases/tag/v0.1.0-beta.16) remains available. Restore a matching backup when comparing releases rather than loading and saving a world without the mod.

`src/Eco.Minecarts` is the server mod source. `client` contains the Unity model/bundle authoring code and assets. `models` contains the Blender source, FBX exports, textures, and collision meshes. The main source snapshot may be ahead of the latest packaged release; use the release ZIP when installing the mod. Existing `EcoMinecarts` package filenames are retained for compatibility.

Original project material is licensed under [JDL-1](LICENSE). Third-party and supplied-material notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). This is an unofficial Eco mod.
