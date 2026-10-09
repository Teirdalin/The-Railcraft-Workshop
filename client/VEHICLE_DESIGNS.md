# Vehicle design policy

Original, post-wheel/pre-graphics models are the production design. Newer artwork is an experimental, per-vehicle opt-in. Do not remove it or silently replace a failed experimental assembly with an original.

Maintain gameplay in the common server vehicle/controller systems. Design choice changes native replicated presentation only, without changing object/item identities, storage, fuel, coupling or vehicle capabilities. Minecarts remain manual carts; a visual variant does not grant train controls or routing.

`RailVehicleDesignBuilder` reads independent original prefabs in `LegacyVehicleSources`. Never change those sources while fitting experimental artwork. It generates `ModernDesign` and `LegacyDesign` native fit clips plus renderer-state callbacks on one vehicle rig. `RailVehicleModernFit` and `RailPreparedVehicleFit` own experimental geometry-specific fitting. Original meshes, textures and archived native fit remain separately referenced. Shared wheel, rod, dumping, restraint and cab-control pivots supply both variants; bind equivalent artwork when adding a feature.

The serialized shipping prefab starts in the original design, including its mount/collision fit. On rebuilding, restore the modern fit clip before recapturing it. Always check both directions and repeated rebuilds: transform names must be unique for Unity animation bindings. Never add a second vehicle, network sync or movement controller to a visual branch.

Release checks must cover original defaults (including missing settings), explicit opt-in, native cargo/fuel/component/coupling preservation, both collision/mount/exit fits, running wheel/LOD visibility, and shared movement/route/dumping regressions. Exported checks are not live human seating or multiplayer acceptance; record those limits separately.

Native rider-role enums are shared constants, never float animation curves. Dynamic cab controls use design-specific anchor fits on shared animation hosts; their hit colliders follow the moving handles. Cab travel gates retain their moving/parked boolean authority and are excluded from design visibility. Isolate mesh-only children before hiding artwork on nodes that also own native colliders/interactions.

Production icons and placement previews show originals. An opted-in placed vehicle receives its experimental artwork through the replicated design state; this preview limitation is documented rather than silently presenting it as independent preview selection.

`RailPresentationPolish` applies repairs to temporary original instances without rewriting archived sources. Middle tram chairs and rider corrections therefore survive regeneration. The handcar pump uses a separate controller layer; do not replace or disable the shared root design Animator. Pump and dump artwork belongs on mesh-only children of shared animated transforms.
