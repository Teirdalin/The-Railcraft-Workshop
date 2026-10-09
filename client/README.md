# Client bundle workspace

## Current release export

Version 0.2.67 ships fourteen original vehicle designs, including the complete
animated coaster guardrails. Use `MinecartAssetBuilder.BuildOriginalVehiclesBundle`
with the matching authored Unity project. It preserves native rig components,
removes deferred vehicle-model dependencies and evaluates boarding/secured
animation through the existing replicated station states. General modern-model
exports are not the originals-only release path.

The generated Unity project and preserved deferred model library are outside
this repository. The `models` directory contains the repository's original
model sources. `prefab-contract.json` describes the initial prototype subset,
not the full current vehicle/track inventory. Exported asset checks remain
separate from connected-player rendering and multiplayer acceptance.

Public mod name: **Railworks Workshop**. Version 0.2.30+ distributes the
unchanged authored bundle bytes as **Railworks.unity3d**, paired with
**Railworks.dll**. Authoring output, prefab names and internal bundle identity
retain their existing names for compatibility; Stage-RailworksBuild.ps1 maps
the verified artifacts to the public filenames.

This folder records the exact prefab contract for the Eco ModKit side of the project. The source
models remain in `../Assets/Minecart`; do not rescale or rebake their functional pivots before
import.

Verified dependency:

- Compatible Eco ModKit archive: `EcoModKit_v0.14.1.1-beta.zip`
- Import its `EcoModKit.unitypackage` into the authoring project.
- Required editor: Unity `6000.3.6f1`

Initial prototype setup (historical):

1. Install Unity 6.3 compatible with the package's `ProjectVersion.txt` (`6000.3.6f1` for the
   inspected client).
2. Create a Built-In Render Pipeline 3D project and import `EcoModKit.unitypackage`.
3. Import the FBX models and PNG textures from `../Assets/Minecart`.
4. Build the five world objects and five item prefabs named in `prefab-contract.json` under a
   `ModkitPrefabContainer`.
5. Build `EcoMinecarts.unity3d` through **Eco Tools > Mod Kit > Build Current Bundle**.

The first live prototype deliberately uses Eco's compiled vehicle stack and physical guide
colliders. Do not add an arbitrary custom client C# assembly: the shipping client is IL2CPP and a
standard ModKit bundle is an asset/prefab bundle.

## Vehicle paint authoring

Copy `client/*.cs` into the project's `Assets/EcoMinecarts/Editor` folder and
`client/Shaders/*.shader` into `Assets/EcoMinecarts/Shaders` before rebuilding.
The full content build and surface refresh apply `RailVehiclePaintBuilder`
automatically. `RailVehiclePaintBuilder.Refresh` updates the existing vehicle
prefabs and their unpainted icons without regenerating track geometry.
The normal exported-bundle audit also checks the native paint shader contract
and renders full/partial/removed coats. The shader is asset-only and uses the
installed WorldObject/PaintableComponent; no additional client assembly is needed.
