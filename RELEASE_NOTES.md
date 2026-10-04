# Railcraft 0.1.0 — prerelease

For Eco 0.14.1.1 beta release-1079. Install the matching DLL and Unity bundle from `Railcraft-0-1-0.zip`.

## Changes since beta.16

- One-block coaster stations with held-cart placement and automatic queueing/coupling behind the front cart.
- Left/right corkscrews and descending coils; compact crest, dip, and flat/slope transition pieces.
- Front and rear coaster targets support both Shift + left-click coupling and Shift + E shoving. Coasters slow near sharp turns.
- Hammer-only chain-direction arrows: green for a powered circuit and red for unpowered, refreshed once per second within six blocks, capped at 72 arrows per player.
- Explicit material-based map colors and distant textures for all 568 custom rail/support block definitions, replacing the bright-green defaults.
- Native curved surface shader test, seated riders raised 0.10 m in the affected passenger vehicles, and cab panel overlap corrections.
- Railcraft Workbench appearance, placement/occupancy, Crafting map category, and wood table interface improvements.
- Vehicle Access controls for granting/revoking direct operator access without opening the property map. Native ownership and group permissions are retained.
- Vehicle recipe/fabric and rail-economy updates; server-controlled vehicle motion and self-collision adjustments.

## Installation

1. Back up the world and the existing mod pair outside the server's `Mods` directory.
2. Stop the server. Replace `Eco.Minecarts.dll` and `EcoMinecarts.unity3d` together in `Mods/UserCode`.
3. Keep only one copy of each file anywhere under `Mods`. Restart and reconnect.

For a locally hosted Eco game, the folder is `Eco_Data/Server/Mods/UserCode` inside the game installation. Close Eco before replacing the files. The ZIP contains exactly these two files; documentation and release records remain in the repository.

## Validation and known limitations

- Server DLL and Unity bundle builds passed. Distribution and installed-file hashes were verified.
- Exported bundle inspection confirmed all 568 custom blocks have explicit non-green map colors and valid distant textures.
- The latest coupling targets and chain indicator prefabs were checked in the exported bundle. Full gameplay/regression testing was not repeated for the latest updates.
- The newest map/distant rendering, hammer overlay, and coupling behavior still need in-game acceptance.
- A Linux server with an existing modded world failed to finish startup with the Coil Update and booted with the mod removed. The cause is unresolved; this package does not claim to fix it.
- Vehicle paint colors do not currently display during the native `Curved/Standard` shader test. Saved paint data is retained.
- Preserve the prior mod pair and world backup for rollback. Loading/saving a world with the mod removed can lose its objects and items.
