# Vehicle design policy

Version 0.2.66 and later ship the original vehicle models only. Preserve newer artwork and its authoring data outside the public bundle for future updates. `NewDesign` remains in editable settings for compatibility; a stale true value still displays originals in the current bundle. Reintroducing the deferred models requires a deliberate release update.

Maintain gameplay in the common server vehicle/controller systems. Design choice changes native replicated presentation only, without changing object/item identities, storage, fuel, coupling or vehicle capabilities. Minecarts remain manual carts; a visual variant does not grant train controls or routing.

For the current release, use `OriginalVehicleReleaseBuilder` through `MinecartAssetBuilder.BuildOriginalVehiclesBundle`. It retains the original fits and native mechanism rig, removes deferred render dependencies, and makes both design-state names select the original fit. Original artwork repairs may update original authoring geometry while preserving native seats, colliders, wheels and saved identities. The full visible coaster restraints are batched separately from the fixed seat/frame and attached by `OriginalCoasterRestraintBuilder` to the existing replicated animation host.

For future dual-design authoring, `RailVehicleDesignBuilder` reads independent original prefabs in `LegacyVehicleSources` and generates `ModernDesign` and `LegacyDesign` native fit clips plus renderer-state callbacks on one vehicle rig. Do not modify original fits merely to accommodate experimental artwork. `RailVehicleModernFit` and `RailPreparedVehicleFit` own experimental geometry-specific fitting. Shared wheel, rod, dumping, restraint and cab-control pivots supply both variants; bind equivalent artwork when adding a feature.

The serialized shipping prefab starts in the original design, including its mount/collision fit. The originals-only release path must not regenerate or recapture modern models. Future dual-design rebuilding restores the modern fit clip before recapturing it. Always check repeated rebuilds: transform names must be unique for Unity animation bindings. Never add a second vehicle, network sync or movement controller to a visual branch.

Current release checks cover original defaults (including missing settings and stale true values), native cargo/fuel/component/coupling preservation, original collision/mount/exit fits, wheels, mechanisms and shared movement/route/dumping regressions. A future dual-design release must additionally test explicit opt-in and both variants' fits and LOD visibility. Exported checks are not live human seating or multiplayer acceptance; record those limits separately.

Native rider-role enums are shared constants, never float animation curves. Dynamic cab controls use design-specific anchor fits on shared animation hosts; their hit colliders follow the moving handles. Cab travel gates retain their moving/parked boolean authority and are excluded from design visibility. Isolate mesh-only children before hiding artwork on nodes that also own native colliders/interactions.

Production icons, placement previews and placed vehicles show originals in the current bundle. The optional BepInEx camera remains separate from these server-delivered models and animations.

`RailPresentationPolish` applies shared original presentation repairs. Middle tram chairs and rider corrections must survive regeneration. The handcar pump uses an independent child Animator; do not replace or disable the shared root design Animator. Pump, dump and restraint artwork belongs on mesh-only children of shared animated transforms. Replicated pose transitions must accept the latest state even when it interrupts earlier motion.
