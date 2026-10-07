# Eco Minecarts server project

This project targets the installed Eco `0.14.1.1 beta release-1079` reference package on .NET 10.

The inspected reference package marks its server API surface as Windows-only. The project suppresses the resulting `CA1416` analyzer noise because this build targets the installed Windows server. Cross-platform server packaging needs a separate validation pass before release.

`Content/MinecartContent.cs` defines the initial item, recipe, storage vehicle, and world object. It intentionally follows Eco's supported `PhysicsWorldObject` and `VehicleComponent` pattern. It is not ready to deploy until the matching client prefab bundle exists; spawning a new world-object type without that prefab would leave the client with missing presentation or control behavior.

`Physics` and `Track` contain engine-independent rail dynamics. Distances are in block-relative units, time in seconds, mass in kilograms, and forces in newtons. `MinecartTuning.MetersPerBlock` performs the physical unit conversion once the ModKit confirms Eco's import scale. The solver models cargo mass, grade, rolling resistance, aerodynamic loss, handbraking, curve warnings, and time-filtered derailment. `RailPath` carries overshoot across segment boundaries instead of discarding it at voxel edges.

The client integration gate remains the Eco 0.14.1.1 ModKit for Unity `6000.3.6f1`. Once available, import the model GLBs/FBXs, create the `Minecart` prefab, connect the serialized anchors and collision proxies, and determine whether the rail controller can run as a supported client component or requires server-driven presentation.
