# Editable Railworks vehicle files

Each vehicle has its own `.cs` file in `Vehicles`. Eco compiles these files at server startup; no .NET SDK or source build is required.

Install the matching `Railworks.dll` and `Railworks.unity3d` alongside the `Vehicles` folder in `Mods/UserCode`. Stop the server before changing files, edit the numeric constants, save, and restart.

- `CarriedItemWeightGrams`: weight of the vehicle item in a player's inventory (grams).
- `NewDesign`: defaults to `false` for all fourteen vehicles. Version 0.2.66 and later ship the original vehicle artwork only; newer vehicle meshes and textures are deferred for future updates. The field remains for compatibility with existing settings. A stale `true` value still displays the originals in this bundle. Capacity, movement, networking, cargo/fuel, coupling and mechanical animations keep their existing shared systems.

- `EmptyMassKg`: mass of the placed empty vehicle used by rail physics (kilograms).
- `CargoCapacityKg`: vehicle inventory weight limit (kilograms).
- `StorageSlots`: inventory slot count. Existing inventories grow to match; shrinking relocates cargo into surviving slots only when there is room. If the inventory is too full, existing slots and cargo are retained until a later initialization after unloading.
- `MaximumSpeedMetresPerSecond`: server rail-guidance/autopilot speed limit. Native handcar/pulled-cart client limits still apply; raising this value does not rebuild their client controller.
- `PowerWatts`, `TractionNewtons`, `BrakingNewtons`, `IntendedTrainMassKg`: vehicle performance values. Cargo, passengers and fuel still add mass. Trams still require track power; unpowered cars remain unpowered vehicle types.
- `DurabilityHours`: maintenance lifetime.
- `AllowDumping`: automatically unload cargo at a Minecart Dumping Rail into authorized linked output storage. Defaults to `true` for standard and wooden minecarts, `false` for other vehicles; set to `true` to enable trains or other cargo cars. Fuel inventories are excluded; rejected cargo stays aboard.

Keep the filename, namespace, class name and `VehicleKey` intact, with one file per vehicle. Do not copy the full server source into UserCode: that duplicates existing types. These files edit vehicle balance; shared movement logic, recipes and visual geometry remain in the core DLL/bundle.

Defaults match the release balance. Existing saved vehicle types and cargo persist. Back up before lowering storage limits or changing balance; lowering a capacity does not discard existing cargo. Preserve your customized `.cs` files during upgrades instead of overwriting them with release defaults. Deleting a vehicle's settings file restores its built-in defaults on the next restart.
