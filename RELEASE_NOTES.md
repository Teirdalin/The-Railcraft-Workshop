# 0.2.68 prerelease - leading-car station docking

- Dock the physical leading car at the station centre regardless of which car is occupied or selected as simulation leader. Determine the endpoint through reciprocal couplers, including backwards-facing cars and reverse travel.
- Use shared rail/coupling geometry for current progress between physics substeps. Brake and creep through the existing force integrator, then cap travel at the marker without teleporting the train.
- Keep arrival/dwell/dispatch identity stable through a change of simulation owner. Preserve dispatch until the whole consist passes the station, including the gap between car centres. Record departure/recovery order from the physical leading endpoint.
- Ship a server-only update. The optional camera remains at 0.2.67; it only needs a new release when its plugin changes. Packaging the camera is now explicit and uses the plugin's own version.
- Private native checks cover single-, two- and three-car trains, front/middle/rear simulation owners, reverse-facing coupling, both directions, 2/8 m/s arrivals, multiple physics substeps, stopping at the centre, owner handoff and complete-train clearance. Live connected-player visual acceptance remains pending.

# 0.2.67 prerelease - original coaster guardrails

- Animate the original coaster's complete visible padded bars and swing arms through the existing replicated boarding/secured controller. The bars rise for boarding and lower before station departure. Their opening arc clears the original seat backs.
- Allow the latest restraint state to interrupt an earlier transition regardless of controller transition ordering, including reopening after a cancelled departure. Seats, wheels and vehicle physics stay fixed while the bars move.
- Exported-bundle checks cover raising, lowering, interrupted motion, rotated car frames, seat clearance and native rig stability. Private Eco-server checks cover departure gating and state replication. Connected-player visual and multiplayer acceptance remain pending.

## Included updates since the previous GitHub release (0.2.40)

- Ship original artwork for all fourteen vehicles. Newer vehicle models are preserved outside the public bundle for future updates; `NewDesign` remains compatible with old settings but does not load those deferred meshes. The server ZIP is 42.5 MiB.
- Use each coupled coaster car's own position/contact and airborne state for landing rotation within the shared movement/coupling systems. Improve queued-car departure, chain traction across a consist, station power continuity, and slopes/loops.
- Remove automatic positional recovery resets on loading. Coaster stations provide **Return carts to station**, available once per station per server restart. Do not assume that saved momentum always resumes correctly on load; live restart behavior remains a validation task.
- Keep the improved mouse-controlled coaster camera in a separate optional BepInEx package. Each player installs the compatible BepInEx 6 IL2CPP loader and plugin locally; joining a server does not install it.
- Correct handcar curve guidance and stop/dismount animation state; retain real-motion wheel and pump animation and original rider fitting.
- Add automatic train lighting at night and under high tunnel/cave cover, face-mounted tram lights, original coach roof/seating clearance, engine latch clearance, and station deck/chain-drive presentation repairs.
- Provide an editable coaster-station sign, corrected station occupancy, and the missing component icons. Keep cargo, fuel, recipes, owner-editable balance and existing saved identities compatible.
- The server ZIP contains only `Railworks.dll`, `Railworks.unity3d` and fourteen editable `Vehicles/*.cs` files. Installation documentation stays on GitHub, outside server data folders. The optional camera ZIP contains its plugin and a local-installation README.

# 0.2.54 test build - lamp faces and presentation corrections

- Put the tram's two warm spotlights and illuminated lenses at the actual headlamp faces, following Eco's steam-truck lamp settings. Remove the small lower lamp cylinders; retain replicated night control in both designs.
- Move the handcar pump and pushrod to an independent child Animator driven by the same real movement updates as the working wheels. Smoothly park the mechanism when stopped.
- Replace the perforated imported station deck with a closed timber platform at the existing boarding height. Inset smaller chain-drive backing plates inside both housings.
- Bridge the original large-engine boiler and cab with a matching firebox neck.
- Preserve original-design defaults, customized vehicle settings and all saved identities. Private-server and exported asset checks remain separate from live Eco acceptance.

# 0.2.53 test build - seating and presentation repairs

- Add two physical wooden chairs under the original tram's middle passenger positions; retain all six passenger places.
- Raise coaster and passenger-car rider attachments by 20 cm, with additional original coach roof clearance. Preserve independent design fits and current design choices.
- Remove protruding decorative dump-hinge cylinders; dumping pivots, wheels and cargo transfer remain intact.
- Restore handcar pumping and pushrod animation on a separate shared-controller layer, with smooth return to the parked pose.
- Back both chain-drive faces with dark interior plates and close the coaster station timber floor gap.
- Correct zero station prefab sizes and register icons for the components reported in client logs.
- Private server and exported asset checks remain separate from live seating, multiplayer and rendering acceptance.

# 0.2.52 test build - original designs for release

- All fourteen release vehicle configurations default NewDesign to false. Unconfigured/older settings also default to originals; individual true overrides retain the experimental designs.
- Shared gameplay and saved identities are retained. Design fits now include collision scales, exits, attachment emitters and unique collision paths. Experimental cab details no longer overlay original artwork.
- Experimental collision fitting uses its own model/deck geometry instead of the inherited short minecart underframe and original roof/deck elevations. Original collision fitting comes independently from the archived post-wheel vehicle assets.
- Original coaster restraints now follow the shared replicated boarding/securing hinges.
- Native state-preservation and exported original/experimental collision, seating, exit and moving visibility checks gate staging. Human riding, live multiplayer rendering and actual world acceptance remain unverified.

# 0.2.51 test build - per-vehicle designs and cart corrections

- NewDesign defaults to true in all fourteen editable vehicle files. False selects post-wheel/pre-graphics artwork and rider/interaction fitting while retaining shared current physics and animation. Older files without the field default to true; upgrades preserve existing balances and choices.
- Regenerate the wooden cart from raw standard-cart construction and shared native builders. No LOD switching; lower capacities remain 400 kg / 8 slots.
- Lower coaster seat assemblies by 12 cm and reduce center spacing from 62 to 50 cm. Move rider anchors and restraints with the seats.
- Extend whole-consist chain traction to trailing coaster axles after the leader crosses the crest, budgeting power once per drive/consist.
- Preserve all 936 saved definitions, recipes, cargo and vehicle identities. Live gameplay and multiplayer acceptance remain separate from private-server and exported checks.

# 0.2.49 test build - integrated vehicle models and native animation

- Replace the meshes on all fourteen existing vehicle identities. Original source files, recipes, owner-editable balance, fuel/cargo rules and saved types remain intact. Raw library parts have three LODs, packed surface textures and shared wheel assets. Updated inventory icons use the new models.
- **Mine Train:** compact boiler fitted ahead of the usable native cab; shared wheel/rod mechanism and independently fitted operator chair.
- **Passenger Locomotive, Freight Locomotive and Large Train Engine:** individually fitted new boiler/chassis bodies; reconstructed playable cabs, curved roofs, framed rear windows, backheads, gauge panels and native physical controls. Throttle, brake and reverser levers reflect replicated commands with smooth transitions. Gauges are decorative, not false live instruments.
- **Standard Minecart and Wooden Minecart:** separate new chassis, flanged wheels and steel/wooden buckets on visible tipping bearings. Retain manual cart control and shared guided movement; no station autopilot is added.
- **Coal Tender, Large Coal Tender and Large Cargo Car:** fitted hopper bodies and reinforced underframes, shared wheel animation and side-tipping buckets. Dumping remains subject to each editable `AllowDumping` setting and successful cargo transfer.
- **Small Passenger Car and Large Passenger Car:** new covered coach bodies with inward-facing bench places. Cushion heights and deck collision follow the meshes; small-car riders are staggered to reduce opposing-leg overlap. Existing passenger capacities remain unchanged.
- **Heritage Tram:** retain the open-sided trolley, with six outward-facing passenger places on measured bench surfaces. Align the deck collider and remove obsolete bench blockers.
- **Railroad Handcar:** new chassis and pump handle on the existing mechanical pump pivot; real-motion wheels and native input remain shared with the current vehicle framework.
- **Roller Coaster Cart:** lower the two separately modeled seats into the body, remove the added seat pedestals, preserve upstop rollers and add two hinged restraints. Station conditions initiate a smooth close; dispatch waits for it, cancelled departure reopens at rest, and motion keeps the bars secured. Startup recovery restores boarding state.
- Replace the generated locomotive rear interiors where they obstructed the verified cab space. Cab body cuts interpolate triangle attributes, preserve source meshes and rebuild from clean geometry on every export. Batch new static instrument details by material. Side dump hinges now meet the new bucket bottoms without lifting the chassis or wheels.
- Clear older restraint/control triggers before applying each replicated pose, including repeated default states and several updates before a rendered frame. Both locomotive smoke emitters follow the replacement chimney geometry.
- All 20 prepared source assets passed geometry checks: 23,333,735 source triangles reduced to 281,953 combined LOD0 triangles, with all 100 original source files unchanged. This is the complete parts library total, not the triangles drawn by one vehicle. The alternate Minetrain remains an authoring alternative; the separate stake flatbed has no existing wagon identity.
- The 0.2.49 server compiled with zero warnings/errors and passed private Eco-server motion, coupling, grade, routing, departure, timed dumping and restraint/control regressions. Saved-schema comparison against 0.2.48 preserves all 936 definitions with no additions. Final exported-bundle and installation receipts live in `validation/models-0.2.49` and `validation/railworks-0.2.49`; a build result alone is not live gameplay acceptance.
- Final exported Unity checks passed: 60 native wheel Animators, eight quartered slider-cranks, five dump buckets, smooth cab controls, both restraint hinges after rapid state changes, four cabs' entry clearance/control rays, and all fourteen native mount configurations. Installed with a persistent backup; ZIP contents and installed DLL/bundle hashes match. Connected-player gameplay and multiplayer rendering have not been tested.

Manual acceptance: board every vehicle, check third-person hip/leg fit and first-person visibility, use cab controls and both exits, accelerate/reverse/stop and negotiate curves/slopes, watch from another client, test coaster cancellation/departure/return, and dump in both approach directions including full/restricted receiving storage. Passenger dismount must not freeze the vehicle. Native camera behavior and the existing sitting pose still constrain individual avatar fit; no unsupported camera patch is included.

# 0.2.42 test build - remove competing guided movement snaps

- Remove the recurring hard position sync introduced in 0.2.41. Coupled/shoved vehicles now publish only the interpolated native physics stream during continuous movement; recurring absolute snaps no longer compete with buffered poses.
- Preserve explicit absolute synchronization at coaster recovery, placement and native ownership handoff. Manual pulling behavior, client assets, saved definitions, cargo and editable balance settings are unchanged.
- Native regression checks assert no forced sync during guided movement across the former one-second interval, while current poses and kinematic ownership still reach outgoing packets. Live coupled/shove multiplayer smoothness remains acceptance testing.
# 0.2.41 test build - coaster startup recovery and guided pose repair

- Persist each coaster cart's home loading station, latest departure sequence and body orientation. On server startup, wait for world/object loading and coupling reconciliation, then return assigned carts to straight, level connected loading track behind their station in departure order. Preserve saved couplings and inventories, reset flight/derail motion and explicitly publish the restored poses.
- Validate the whole station queue before moving it. If approach track is short, obstructed, or changed couplings conflict with departure order, retain all positions and report the reason in Startup Recovery. Legacy unassigned carts use a single station as fallback with deterministic order; when multiple stations exist, they need a recorded station visit/departure before recovery can identify their home. Existing saves cannot supply historical departure order retroactively.
- Add rate-bounded absolute pose repair during server-guided movement, complementing interpolated physics packets when a client retains an old pose after ownership/coupling/shove changes. Native pulling ownership is excluded. Live multiplayer acceptance remains required.
- Restore Minecart Dumping Rail at the Railworks workbench. Authorized linked output inventories receive cargo using native capacity/item restrictions; rejected cargo remains aboard. Add AllowDumping to each editable vehicle file (standard/wooden minecarts enabled by default, other vehicles opt in).
- Anchor all fourteen native snap-placement volumes at their wheel-contact pivots instead of subtracting an elevated preview bottom into the ground. Widen initial vertical rail capture only; live placement acceptance remains required.
- Preserve all 929 previous saved definitions; seven additive definitions bring the schema to 936. Preserve custom vehicle settings while adding the new missing dumping option.
# 0.2.40 prerelease - editable vehicle C# balance files

- Supply one standalone Eco UserCode `.cs` file for each of the 14 vehicles. Server owners can edit storage slots, cargo capacity, inventory-item weight, physical empty mass and performance/maintenance values, then restart Eco without a source-build project.
- Read the compiled constants once and cache resolved vehicle specifications. Route Minecart and Mine Train storage setup through the same specifications used by other cars. Apply carried-item weights to Eco's cached native attributes after item initialization.
- Preserve the core assembly and all saved Item/WorldObject/component identifiers; the editable files introduce no replacement saved vehicle types. Default values preserve 0.2.39 balance and its Unity bundle.
- Keep shared rail movement, recipes and geometry in the core DLL/bundle. Preserve customized vehicle files during upgrades. Native handcar/pulling controller limits still apply to configurable server speed limits.
# 0.2.39 test build - corner guidance and coupled slope ownership

- Replace coarse native heading corrections with packet-time easing and earlier, smaller owner corrections. Refresh accepted native pose snapshots for remote observers and coupled followers.
- Raise the handcar speed cap from 5 to 8 m/s (28.8 km/h) in both server specifications and the native client controller/limiter; retain calorie controls, traction and suspension.
- Bind guided ownership to the native client Rigidbody kinematic state on every vehicle prefab. Coupled followers follow the shared rail solver while a manually driven leader retains native physics; release restores native ownership.
- Calculate longitudinal gravity from each coupled car's rail slope and loaded mass, with coupler endpoint orientation respected. Cache that aggregate once per simulation frame using existing load/rail data. Preserve common follower speed through wheelbase-aware bends and grade transitions.
- Preserve saved identifiers, inventories, recipes, rail layout and couplings. Matching source and two-file distribution packages are supplied for testing. Live multiplayer downhill and mounted driving acceptance remain required.
# 0.2.38 test build - handcar drive and vehicle rail fit

- Use the public release narrow gauge in the client catalogue by default, matching the production DLL. Move all four large-vehicle wheel/collider pairs, axles, bearings, springs and engine rods onto that gauge; preserve broad bodies, contact-plane pivots and saved object identifiers.
- Keep large vehicle collision clearance at their actual 2.2 metre body width rather than deriving it from their track gauge.
- Configure the handcar explicitly instead of inheriting a heavier minetrain suspension. Use rated payload springs, empty-mass damping, four-wheel drive, a fixed single gear and a horizontal acceleration/speed limiter. Preserve native calorie-based operation, mount controls and gravity.
- Native placement and Unity geometry/suspension checks are separate from live multiplayer rendering and mounted driving acceptance. No save definitions change.
# 0.2.37 test build - smooth jump landings and braking chains

- Blend the incoming coaster pitch/roll back to the live rail frame over 0.45 seconds with smooth easing. Keep rail position, signed landing momentum, braking and route physics authoritative.
- Apply the blend to individual carts and coupled followers; finish alignment even when stopped. New flights reset the transient blend; its state is not serialized.
- Add hammer-buildable Braking Chain - Station Approach, using the existing steel coaster rail item and chain-drive connection. Passive friction is bounded and tapers toward powered chain speed, then normal traction carries the cart onward. Without power it slows toward rest; ordinary chain rails are unchanged. Place enough braking track for the approach speed.
- Add four terrain rotations, a warm brake-shoe finish and a blueprint preview. All 924 prior saved definitions remain intact, with five additive block/preview types.
- Native checks cover landing orientation, bounded hot-arrival braking, powered creep, restart from rest and unpowered friction. Offline asset/private-server acceptance is separate from live visual verification.

# 0.2.36 test build - speed-sensitive brake squeal

- Suppress brake sound and sparks at or below 0.35 m/s, including stationary corrections and a one-foot-per-second crawl. Above that, a squared speed envelope gradually increases brake loudness to the existing 0.85 cap at 8 m/s. At 1 m/s, full-brake volume stays below one percent.
- Preserve proportional service-brake effort, reverse-speed symmetry, normal recording pitch and high-speed brake warnings. The shared sound calculation applies to coasters, carts, trains and trams; no braking forces or stopping behavior change.
- Native checks exercise the actual coaster sound-update method at stationary, crawl, slow, fast and reverse speeds and verify the envelope bounds. Existing tram service-braking, slopes, station turnaround, support power, export and pulling checks remain separate from live listening acceptance.
- Saved schema and the verified 0.2.34 Unity bundle remain unchanged; distribution includes all preceding staged fixes.

# 0.2.35 test build - consolidate equivalent coaster slope choices

- The station blueprint picker groups 26 unpowered slope/transition definitions into 13 shapes. Gentle and steep numbered sections pair in reverse order; 45-degree grades share one slope choice; bottom/crest transitions pair an uphill entrance/exit with the corresponding downhill exit/entrance. Single-block transitions follow the same rule.
- Filter compatibility before grouping, automatically choosing the direction when only one fits. When both fit, the second dropdown explicitly offers Uphill/Downhill. Numbered height slices are measured from the low end of the slope.
- Keep the directional hammer forms, NextRamp height/order, catalogue/blueprint keys, placed blocks, recipes and saved definitions. Those directions still control construction sequencing. Chain forms keep their pull direction; the previously hidden downhill chain shapes stay hidden. This is a blueprint-menu cleanup, with no geometry or physics changes.
- Native geometric checks sample each pair's points, tangents, upright frames and length under 180-degree rotation with reversed traversal. The verified 0.2.34 station/switch client bundle is unchanged. Live picker interaction remains separate from offline checks.

# 0.2.34 test build - grounded coaster station and tram switch finish

- Extend the Roller Coaster Station platform and its collider down to the ground plane (-0.5 m relative to the object pivot), removing the previous 15 cm gap. The boarding surface stays at -0.25 m; rail sockets, cabinet, signs, placement pivot and saved station positions are unchanged.
- All six tram switch variants use the regular tram street-bed finish on their ties and mounting sleeper, keeping the rail-steel material on the running rails. Existing point animations, collision and route connections are unchanged.
- Refresh the existing authored prefab without regenerating unrelated assets. Verify bundled station geometry in all four yaw rotations and retain text, climbing and placement asset checks. Existing saved types remain unchanged from 0.2.33.
- Includes the terminal tram and support-conducted drive connections from 0.2.33. Offline asset/private-server checks are separate from live gameplay acceptance.

# 0.2.33 test build - terminal tram departure and support power connections

- Trams reverse on departure from an end-of-line station when the connected rail ahead ends at a Tram Endpiece and no other station intervenes. Departure conditions and dwell still apply; loops and track ends without an endpiece do not trigger this station reversal. Conditional routing evaluates the departing direction.
- Place a chain drive adjacent to a support base, with an uninterrupted same-material base/middle/top column and chain rail at its top, then press Connect. Wood, Iron and Steel columns conduct the connection; mechanical and electrical chain drives retain their original power source, demand and speed limits. Tram cable drives can use the same support connection.
- Discovery runs only on Connect. Connected supports are saved with the rail network; removal or material/segment replacement stalls the drive and requires explicit reconnection after repairs. Automatic top slope/rotation adjustments keep the conductive segment intact. Existing direct rail connections remain valid.
- All 917 previous serialized definitions are preserved, with seven additive definitions for support membership. Existing placed assets, recipes, blueprint keys and Unity bundle are unchanged. Private-server checks and live gameplay acceptance are reported separately.

# 0.2.32 test build - coaster picker families and tram station comfort

- The station blueprint picker shows Bend, Bank and Loop once, then asks for a compatible Left/Right direction when both fit. Existing placed shapes, items, crafting recipes and exported blueprint keys remain unchanged. Both loop directions remain because their lateral exits differ.
- Tram station approach uses a 0.65 m/s² comfort envelope and proportional service braking before the hard stopping-distance boundary. Manual brakes, power loss, repair stops and emergency/route safety braking retain full authority; station departure conditions and exact stopping limits remain unchanged.
- Guided motion integrates service brake effort. Brake sound volume follows actual effort, and light braking no longer generates heavy-braking sparks. Existing full-brake behavior on other vehicles is retained.
- Includes the 0.2.31 uphill/crawl-speed traction fix. Saved schema and Unity bundle remain unchanged. Native checks are separate from live arrival smoothness and audio acceptance.

# 0.2.31 test build - tram uphill and crest traction

- Tram pulling force now includes opposing gravity before applying the passenger comfort acceleration cap. The old cap limited an empty tram to 1875 N, below approximately 2973 N of gravity on a quarter slope, requiring momentum to get uphill. Available track power, motor watts and adhesion still cap actual force.
- Use the same wheelbase-aware grade as guided motion through the crest, including reverse travel. Flat/downhill comfort limits and other locomotives remain unchanged; no changes to fuel, drive power requirements, track geometry or saved fields.
- Tram target-speed control includes uphill holding effort within its existing 0..1 throttle range, so a low requested speed does not require a run-up. Insufficient allocated power still limits or stalls the tram.
- Runtime changes are server-side; the verified Unity bundle is unchanged. Native force and powered-tram crest checks are distinct from live client testing.

# 0.2.30 test build - Railworks Workshop branding

- Rename the mod to Railworks Workshop and the crafting bench to Railworks Workbench. Runtime distribution is Railworks-Workshop-0-2-30.zip, containing only Railworks.dll and Railworks.unity3d. Blueprint export filenames use Railworks Workshop.
- Preserve internal assembly, serialized types, prefab/bundle identities and the legacy workbench recipe key. Existing vehicles, workbenches, queued recipes and saved layouts retain their identities.
- Upgrade installation backs up the old pair outside Mods, installs the renamed matching pair and removes the old filenames to avoid duplicate loading. Earlier ZIPs remain available for rollback.

# 0.2.29 test build - export files, lettering lifecycle and coaster recovery

- Local Windows blueprint exports write complete, import-compatible JSON to a new uniquely named file on the Desktop and display its location. Empty layouts and errors are explicit; exporting leaves the active plan and saved library unchanged. Remote players retain the native copyable multiline popup, with literal JSON and the station window closed before opening it. A private actual-client check displayed 306141 characters, but remote station clicks still require live acceptance.
- Keep lettering objects active for Eco's binding coroutine, and toggle text and backing/frame renderers instead. Blank tram and vehicle plates hide their frames. Curved text shaders remain unchanged.
- Add missing UV0 data to 87 support collision meshes required by Eco's native CustomBuilder; preserve vertices, normals, triangle indices and ladder corridors.
- Raise temporary vehicle snap-placement volumes above flat rail voxel cells. Physical vehicle colliders remain unchanged. Actual client snap placement still needs gameplay verification.
- Unsupported coasters resume server gravity instead of remaining parked in the air. Landing uses rail capture rather than treating rail voxels as solid flat floors, and immediately aligns a captured cart. Full jump behavior remains a live gameplay check.
- Avoid path sampling in collision sweeps whose broad-phase obstacle list is empty. The new report's 1.53-second TravelFraction peak motivated this change; it does not establish that the entire measured pause was CPU work or that all lag is resolved.
- Native server checks verify complete 2048-piece export/import, retained jumps, unchanged source documents, unsupported-cart gravity and prior pulling/coupling synchronization. Unity checks verify blank/filled/cleared plates, active text objects, support UV reads and flat snap volumes at four rotations. Saved schema is unchanged; distribution contains only the DLL and Unity bundle.

# 0.2.28 test build - pulling suspension, release synchronization and blueprint export

- Reproduced spontaneous empty-cart launching in Unity wheel physics with no player, engine torque or networking. The iron cart reached 14.29 m/s vertical speed and 10.55 m of height variation; the wooden cart reached 11.60 m/s and 6.71 m. Rated-load tests settled, exposing the empty-load instability in suspension damping introduced for ramp pulling.
- Retain rated-payload springs and bound pulling-cart damping by empty supported wheel mass at the native 50 Hz physics step: iron 1750 and wooden 500, replacing maximum-cargo damping. The candidate settled at both empty and rated loads below 0.001 m/s vertical speed. Final authored-prefab acceptance also permits pitch/roll and starts tilted.
- Release explicitly aligns to the retained rail, publishes its final pose, revokes native ownership and sends a forced position update to the former driver. Native-server checks verify final alignment, a forced update, reciprocal coupling links and guided handcart packets without dynamic velocity.
- Keeps 0.2.27 walking limits and coupling-end fixes. Saved schema and unrelated assets are preserved. Connected-player pulling and coupling collision behavior remain live acceptance tasks; this is not a full Eco gameplay reproduction.

- Expose Export Blueprint as a direct station-page button rather than opening a second dialog from the saved-layout selector. Send JSON as nonlocalized literal text to Eco's native multiline input dialog, snapshot under the existing blueprint gate, reject empty/station-only layouts and show explicit access/busy/size/dialog errors. Export never commits edited text or changes saved plans. The JSON schema and Import Blueprint remain unchanged; native format/preservation checks do not establish live popup rendering or clipboard acceptance.

# 0.2.27 test build - native pulling and coupling ownership

- Limit native minecart/wooden-minecart pulling to 1.5 m/s (5.4 km/h), with lower torque and a matching one-gear curve. Gravity/ramp motion retains its vertical component.
- Project accepted native poses once per packet; remove repeated between-packet rail corrections. Guided carts, including coupled minecart followers, omit native dynamic-velocity packets while under server control.
- Grabbing the opposite free handle swaps persistent coupling ends and reciprocal partner references when the symmetric cart turns around. Occupied coupling ends cannot be used as player grips.
- Preserves all saved types, existing recipes, coaster geometry, curved shaders and prior station changes. Actual player pulling, ramp starts and coupled client collision behavior still require live gameplay testing.

# 0.2.26 staged test build - coaster menu cleanup and snap placement

- Hide all 13 downhill chain forms from hammer and blueprint pickers, retaining 52 saved rotated block types and the complete physics/blueprint catalog for existing worlds. The chain single-block crest now advances into unpowered downhill rail. Keep numbered phases and genuinely different slope lengths/angles rather than deleting their different sockets. No duplicate named forms remain among the 47 selectable terrain choices.
- Mark flat straight, chain-straight and sharp-corner forms as native partial floor fills. Coaster item placement previously used the tight ordinary rail-capture tolerances (0.24 m horizontal / 0.32 m vertical), while placement alignment used different tolerances. Both placement-only checks now agree on 0.5 m horizontal / 1.05 m vertical and align a new cart to the actual rail; normal movement, save-load capture, towing and collision checks retain their existing bounds. Live client snapping remains to be tested.
- Includes the 0.2.25 softer chained/unchained compact transition meshes, matching server guidance, all prior station-routing changes and unchanged saved schema. ZIP contains only the two runtime files; client profiler 0.2.21 remains compatible.

# 0.2.25 staged test build - softened compact grade transitions

- Soften the shared profile for chained and unchained single-block flat-to-uphill, flat-to-downhill and mirrored grade-to-flat transitions. The former quintic steepened to 56.52 degrees before returning to the adjoining 45-degree grade; the new integrated smoothstep profile limits this to 51.34 degrees and spreads the return to the slope across the piece. Endpoint heights, tangents and zero endpoint curvature remain compatible with existing placed rails.
- Increase these eight paths from 65 to 129 exported samples and rebuild their 32 rotated terrain meshes and collision meshes. Other terrain shapes, supports, vehicles, materials and shaders remain unchanged. Server guidance and client geometry use the same path source. This is a curve and asset change with unchanged saved schema; live riding and appearance remain to be checked.
- Includes 0.2.24 station routing and passenger conditions. The runtime ZIP still contains only the DLL and Unity bundle; optional profiler 0.2.21 remains compatible.

# 0.2.24 staged test build - station destinations and passenger conditions

- Target Station is visible on train/tram autopilot and Tram Route. Ordinary autopilot activation starts tram service. Configured Station Departure conditions control tram dispatch; the old tram timer is a fallback for unconfigured stops.
- Optional ordered IF conditions -> THEN destination rules share the station departure page and Eco's native condition editor. Connected-station selection stores stable GUIDs; priority up/down, edit and deletion use one management menu. Matching reachable rules authorize departure; missing/self/unreachable destinations try the next rule before ordinary departure conditions.
- Fuel conditions compare burner storage fill percent, available MJ or reserve minutes using strict/inclusive thresholds. Track-powered trams report fuel unavailable. Native character conditions use mounted passengers across the consist, with explicit Any / All / At least X / None comparisons; empty trains and unavailable character information fail closed, including under negation. Saved fixed-citizen subjects are rebound to the passenger.
- Temporary destinations preserve the next normal scheduled stop and tram route position. Real directed track paths obey vehicle clearance and switch access; automatic points retain the occupied-switch safety lock. A stationary train may reverse to reach a destination; automatic shunting is outside this implementation. Removed destinations or impossible return paths report the issue and resume ordinary operation.
- Destination searches are bounded, run at departure/topology changes, and are cached during travel. Station conditions evaluate at most once per second while holding. All 903 prior saved definitions are preserved; new routing state and enums are additive. Existing vehicles, recipes and save data are retained.
- Includes the 0.2.22 pulling collider and side ladder asset changes below. The two-file ZIP contains no developer probes or profiler loader; optional client profiler 0.2.21 remains compatible. Native fixture and focused algorithm verification do not replace connected-client gameplay or Linux acceptance.

# 0.2.22 staged test build - pulling collider and side ladders

- Minecart and Wooden Minecart pulling colliders were absent from Vehicle.AllVehicleColliders because an earlier refresh discarded disabled colliders. Retain the occupied collider used by MountSpotPulled so Eco's native player/vehicle exclusions can include it. The material refresh also retains disabled colliders to preserve this fix on future exports. This addresses a concrete configuration defect; live pulling is required to verify the reported bouncing is resolved.
- Move support rungs perpendicular to the track, onto the local +X side. Native climb facing now follows that side for every rotation, and the physical approach corridor is cut on the same side through the top saddle. Verify climbing from the visible rungs and exiting onto the track in game.
- Export inspection checks both pulling collider lists, all 87 installed support variants and their landings, unchanged loose-stack ladder flags, 1447 unchanged unrelated mesh payloads and 36 unchanged shader bindings. Production saved schema remains unchanged. Optional background profiler 0.2.21 remains installed and compatible.

# 0.2.21 staged test build - legacy tram load fix and background profiler

- Fix the 0.2.20 save-load crash in Eco's FuelSupplyComponent.Initialize: existing trams keep optional fuel storage, so their nonserialized fuel tags must still be initialized. Tram traction remains powered by Tram Rail only; new trams still have no fuel component.
- Optional client profiler 0.2.21 keeps recording when Shift+F11 hides the panel. Stop recording and New session are explicit actions. Save report retains each method/entity's session maxima and the complete window containing the worst individual call, alongside the latest live window, with bounded storage. Observation timestamps identify window receipt, not exact server event times; overlapping windows are not summed.

# 0.2.20 staged test build - tram access and single-station return

- Trams run only on powered Tram Rail. New trams have no fuel supply, fuel consumption or air pollution component; existing fuel inventories may remain for recovery but supply no traction and receive no tender refills. Standard-rail burner fallback is removed. Track Power and autopilot status identify missing rail power; native generator tests cover both mechanical and electrical drives, actual movement, stop/restart and power outage/restoration.
- Both tram end bulkheads have solid interaction colliders. E opens tram settings directly without dismounting seated passengers. Chassis ownership, disabled client wheel simulation, and native self-collision exclusions are retained.
- Departure clearance now measures actual track travel rather than straight-line displacement. Clearing the departed station refreshes the cached journey, and the departure station is part of the cache context. This fixes single-station circuits that stopped on the initial trip but skipped subsequent laps. Native checks covered three same-station arrival/dispatch cycles and movement clamping at the station stopping point.
- Train stations retain their side-of-track footprint and include no track piece. Their geometry and colliders are shifted to native block-bottom height to remove the half-block snap gap.
- Serialized mod type/member definitions are unchanged. Exported mesh payloads and shader bindings are unchanged; connected-client interaction, station placement and live loop acceptance remain to be tested. The ZIP contains only the matching DLL and asset bundle; profiler client 0.2.12 remains unchanged.

# 0.2.19 staged test build - autopilot stop and support access

- Explicitly switching autopilot off clears automatic throttle and applies brakes for locomotives and trams. Vehicles loaded with an old off-mode throttle also brake when there is no manual operator. Physical cab controls retain manual driving. Shift+E now says Start / stop autopilot.
- Control requests account for wrapped world positions and explain access, distance or operator restrictions instead of silently rejecting the action. Native start/resume and station approach, arrival and hold were checked on the installed Eco server runtime. Live menu interaction remains to be verified.
- All 87 installed support variants supply their actual ladder-facing rotation to Eco. Visible rungs mark the approach face and continue through raised top sections. The support-only collider leaves a clear corridor through collars and saddle overhangs, retaining the rear structure and landing; visual LODs keep the full shape. Loose carried stacks remain ordinary blocks.
- Build and exported-bundle checks cover facing, rung geometry and ladder-side collider clearance. Climbing onto an actual placed track still needs a connected-client playtest; native ladders provide one selected approach face, chosen by hammer rotation.
- Existing serialized gameplay schema and all 0.2.18 recipe changes are preserved. The ZIP contains only the matching DLL and bundle. Client profiler 0.2.12 is unchanged; Linux and connected-client acceptance remain pending.

# 0.2.18 staged test build - profession crafting tables

- Six compact vehicle recipes now use Wainwright / Basic Engineering: Wooden Minecart, Minecart, Railroad Handcar, Passenger Car, Coal Tender and Mine Train. Their required specialty levels stay 1, 2, 2, 3, 3 and 4 respectively. Requirement, ingredient, labor and crafting-time modifiers agree.
- Six full-size stock recipes now use Machinist / Mechanics: Passenger Locomotive, Heavy-Haul Locomotive, Large Train Engine, Large Cargo Car, Large Passenger Car and Large Coal Tender. Existing Mechanics levels are preserved.
- Heritage Tram, Roller Coaster Cart, Electrical Rail Chain Drive and Electrical Tram Cable Drive now use Electric Machinist / Industry 3. Standard Rail Chain Drive and Tram Cable Drive stay at the Railcraft Workbench with their existing Basic Engineering / Industry requirements. All rails, track shapes, supports, switches and stations remain at Railcraft.
- Item descriptions and native recipe crafting-table views identify the new locations. Each family registers only once; the previous table no longer advertises moved recipes. Native profession-specific module support is retained.
- Native Eco checks cover all 56 recipe families, including 16 moves, valid output lookup, exactly one crafting table, matching specialty/module support and UI location. Before/after snapshots preserve recipe names, ingredients and quantities, outputs, base labor/time, XP and required levels. The 903 saved type/member definitions match 0.2.17; no vehicle data migration or world reset is introduced. Live crafting, pre-existing work orders and Linux server acceptance remain unverified.
- The ZIP contains only Eco.Minecarts.dll and the unchanged 0.2.17 EcoMinecarts.unity3d bundle. Client profiler 0.2.12 remains compatible.

# 0.2.17 staged test build - Industry/Mechanics crafting and electrical drives

- Roller coaster carts, rails, complete shapes and station recipes now use Industry. Trams, tram rails, switches, cable drives and stops also use Industry. Locomotives, passenger/cargo cars, tenders, the railroad handcar and train station use Mechanics. Existing skill levels remain the same; the coaster material changes below are included. Requirements, labor, crafting time and skill-sensitive ingredient discounts agree. Shared minecart rails/drives/supports retain their existing skills.
- Coaster rails, shapes and stations use Steel Bars in place of Iron Bars. Coaster carts use 14 Steel Bars, eight Wood Boards, four Fabric, one Steel Gear and one Lubricant, with the four Fabric replacing four former bars. One Lubricant restores 25% cart condition; other vehicle maintenance remains unchanged.
- Added Electrical Rail Chain Drive and Electrical Tram Cable Drive, requiring Industry 3 at the Railcraft Workbench. Each upgrade recipe consumes one matching mechanical drive, one Electric Motor, four Steel Bars and two Basic Circuits; 200 base calories and six base minutes. Existing recipe identifiers remain intact.
- Electrical chain drives use Eco's electrical grid and allow 0.25x–5x speed. Mechanical chain drives remain at 0.25x–3x. At 1x both cost 2 W per connected rail; power demand and available lift power scale with speed. Electrical tram cable drives change the energy source, preserving tram traction and maximum speed. Both variants retain manual Connect and immediate stop/off on removal, requiring explicit reconnection after repairs.
- Native Eco checks verified all 39 affected registered recipes and four drive variants, including actual energy types, connection/removal/reconnection, persisted speed caps, demand, supply allocation and lift targets. The exported bundle includes both new prefabs/icons with native running/stalled state events. Existing mesh payloads remain unchanged. Live powered movement, rendering and Linux server acceptance remain for testing.
- Two-file ZIP includes all prior 0.2.16 journey planning improvements. No saved vehicle, station or crafting identifiers were renamed. Client profiler 0.2.12 remains compatible.

# 0.2.16 staged test build - cached station journeys

- Autopilot discovers its next applicable station and track constraints once when activated, including midway along a line, and again at station departure. Remaining distance follows actual rail progress; motion steps reuse planned connections and compressed geometry instead of repeatedly scanning ahead.
- Current speed, cargo weight, braking force and nose offset still determine safe braking. Curve limits, incompatible routes, buffers and physical track ends remain protected. A station-free circuit continues running until stopped or unable to run; the planning limit does not create an artificial stop.
- Relevant rail edits, turnouts, station/tram settings, reversals and coupling changes refresh the plan. A bounded change journal lets unrelated terrain edits preserve it. Very long routes are planned in chunks of up to 4,096 pieces and extend before reaching the chunk boundary. Wrapped world seams preserve continuous coordinates.
- Focused checks compare the cached safety envelope against the previous per-piece calculation across grades, curves, loads and reversal. 5,000 pure follow calls performed zero discovery calls, allocated 40 bytes total and took 2.1097 ms. These are algorithm measurements, not whole-game CPU results. Native Eco checks cover activation, plan reuse, station arrival/departure, removal/repair and station-free torus traversal. Live performance and Linux server acceptance remain unverified.
- No recipes, serialized gameplay fields or client assets changed. The ZIP contains exactly the DLL and the unchanged 0.2.14 bundle, including all earlier placement and dismantling improvements. Client profiler 0.2.12 remains compatible.

# 0.2.15 staged test build - rail lookup and autopilot work reduction

- Bounded, thread-local physical connection results survive motion updates for up to one second. Block edits, rail registration/removal, switch changes and coupling changes invalidate them immediately. World reads, cargo/performance and nearby discovery remain scratch data for the current update; unscoped discovery remains fresh. Registry removals invalidate again after committing, and coaster rotation changes are now observed too.
- Autopilot caches positive and missing route decisions with topology, station/stop, tram-route, departed-station and minimum-radius checks. It skips station branch searches on a single continuation, keeps return-edge choices through ambiguous merges and preserves authoritative turnouts. Branch scoring remains bounded for actual alternatives.
- Stations maintain immutable arrays indexed by their bound rail cell. Registration, rebinding and destruction update that index instead of allocating a full station search and sort for every lookahead visit. Tram stop/settings changes invalidate routing; tram stop sequences are parsed only when changed.
- Repeated future slope/curve calculations share geometry for the same track profile and entry within one performance snapshot. The current rail position, load-dependent braking, stop distance, commands, station readiness and removal checks are still evaluated while moving.
- A warmed source comparison of 4,000 connection queries across 50 updates reduced dictionary-backed world reads from 105,000 to 2,100 and temporary allocation from 3,616,040 to 72,448 bytes. These are offline algorithm measurements, not a claimed whole-game CPU improvement. Native Eco checks passed for same-update removal/repair, forward/reverse drive, commanded braking, single-continuation routing, station index changes and turnout selection/removal. Live acceptance and a clean follow-up profile remain necessary.
- No recipes, serialized gameplay fields or client assets changed. The two-file ZIP includes the 0.2.14 placement/dismantle changes and uses its exact bundle. Client profiler 0.2.12 remains compatible.

# 0.2.14 staged test build - vehicle placement and generic dismantling prompts

- Every rail vehicle prefab now uses Eco's native preview-only placement volume. Snap previews exclude wheel flanges, pull handles and interaction colliders that overlap the supporting rail, while checking the vehicle body for obstructions. Eco removes the added collider after placement. Actual track placement and slopes still need live acceptance.
- Hammer interactions show Dismantle for uncoupled vehicles and Decouple To Dismantle for coupled vehicles. Coupling changes refresh the condition immediately; the existing server pickup guard still prevents dismantling coupled cars.
- The native server checked prompt registration and coupling-state changes. Inspection of the exported bundle confirmed 14 placement volumes, with all 1,645 mesh payloads and 36 material shader bindings unchanged. No recipes or serialized gameplay fields changed. The ZIP contains only the DLL and bundle; client profiler 0.2.12 remains compatible.

# 0.2.13 staged test build - shove from either end and reuse rail neighbors

- Minecart, coaster and train-car shoves now use the player's position relative to the touched car. A rear shove moves forward and a front shove moves backward without a camera alignment check. Reversed coupled cars convert their shove direction through actual coupler endpoints to the leader's rail direction, including on bends. The exact side midpoint consistently chooses forward. Existing access, speed, cooldown, damage and train-control checks still apply.
- Repeated physical neighbor queries share bounded scratch results within one synchronous motion update, including failed connections. Vehicle traversal and chain-only queries have separate entries. Block, switch and rail changes invalidate the cache immediately and each new update starts fresh. Inactive train routing reuses the physical query already made.
- Focused position and route-cache checks cover rotations, slope/player height, query modes, null results, removal/addition, nested updates and next-update freshness. Native validation checks actual coupling orientation and block event invalidation. Benchmark figures are offline results; the next live report must establish the improvement in the user's world.
- No recipes, serialized fields or Unity assets changed. The server ZIP contains only Eco.Minecarts.dll and EcoMinecarts.unity3d. Client profiler 0.2.12 remains compatible.

# 0.2.12 client profiler update - save performance reports

- Added Save report to the Shift+F11 panel. It exports a readable TXT and complete JSON snapshot to Desktop/Railcraft Profiler Logs, including all method rows, entity counts and aligned one-second samples regardless of collapsed rows, scrolling or search filters. Each capture represents the current 30-second rolling window; it is not a continuous session log.
- Unique filenames preserve earlier reports. Disk writes run on a background task and completion or errors appear in the panel. The existing authenticated server protocol is unchanged and the client remains compatible with server Railcraft 0.2.9 and newer, including 0.2.11.
- Packaged separately as Railcraft-Profiler-Client-0-2-12.zip. Native GUI and export checks passed in a disposable Eco client; connected-world button acceptance remains for live testing. Installation verified the exact tested plugin hash and preserved both server mod files and all 124 save files. Validation receipts are in validation/profiler-client-0.2.12.

# 0.2.11 staged test build - fixes identified by live Rails profiling

- Motion callbacks use a nonblocking vehicle gate. Overlapping timer/world callbacks are skipped instead of making worker threads wait; the next accepted update still integrates elapsed movement. The profiler exposes skipped callbacks separately and measures Tick after acquiring the gate.
- Connected-car membership is reused until coupling, removal or loaded-object registration changes it. Train loads, performance and leader selection are reused within a synchronous motion update and refreshed on the next update. Performance aggregates are computed once per load snapshot, and vehicle specification lookup no longer creates a predicate for each query.
- Rail reads and repeated neighborhood searches share bounded thread-local scratch caches within an update. Standard stock excludes complete coaster sections from its nearby search. Block changes, rail removal, complete-section registration and switch publication invalidate active caches; discovery outside a motion update remains fresh.
- No recipes, saved coupling fields or Unity assets changed. Railcraft-0-2-11.zip contains only Eco.Minecarts.dll and EcoMinecarts.unity3d. The existing client profiler 0.2.10 remains compatible. Native/offline comparison receipts are in validation/lag-fix-0.2.11; before/after live world profiling remains necessary to establish the actual gameplay improvement.

# 0.2.9 staged test build - opt-in Rails performance overlay

- Added authenticated Rails-only profiling and a separate Windows client overlay toggled by Shift+F11. Identical entity types and systems aggregate exclusive elapsed work; hierarchical drill-down, search, current/rolling-average/peak, counts, allocations and network-update call sorting are available. Physical rail types group rotations together. Native world-object events maintain object counts; an optional throttled census supplies voxel snapshot counts.
- Audited and instrumented synchronous runtime, geometry, physics, routing, switch, coupling, power, station, passenger, cargo interaction and persistence-snapshot paths. Async confirmation and iterator suspension are excluded; their synchronous callees are measured. Tender fuel transfer and confirmed fare payment have explicit scopes. Tracks' actual lookup costs are attributed to the returned rail type rather than inferred from placed counts. Static terrain blocks do not have invented entity ticks.
- Recording uses bounded 30-second buckets and thread-local nesting. Disabled scopes take one inexpensive branch and allocate nothing. Closing all viewers or losing refresh for eight seconds disables recording. Profiler recording and snapshot/census overhead are reported separately. Optional whole-server process CPU/RAM supplies context.
- Stopwatch data is elapsed work including waits, not hardware CPU cycles. Percent is measured Rails work, because Eco's independent tick schedulers lack a shared measured tick denominator here. Retained memory per entity and encoded wire bytes remain unavailable. Network counters show actual pose-update calls. The profiler does not claim to measure Unity rendering or native Eco code outside scoped mod calls. Automatic stockpile loading/unloading or eject-track logic is not implemented in this version and cannot yield samples.
- The server ZIP remains exactly the DLL and unchanged audited Unity bundle. The optional client package includes a reversible loader installer; do not put that package in server UserCode. Live connected-client hotkey and panel presentation still need user acceptance. No recipes or saved gameplay schemas change.

# 0.2.8 staged test build - large routes and a simpler coaster designer

- Raised the plan limit from 128 to 2,048 pieces and expanded import/export capacity. Discovery now alternates steps between front and rear, shares the remaining budget when one side ends, and reports each side's stop reason. Placed selection identities survive insertion of newly discovered rear rails.
- Replaced the long autogenerated command list with five native large buttons: Build Track, Add Next Rail, Edit Layout, View And Inspect, and Saved Layouts Menu. Existing action RPCs remain available internally. Native popup menus group editing, preview, inspection and sharing; rail selection uses 32-item pages. The page displays direction counts, built/planned totals, jump counts and seven nearby rails instead of sending the full layout text.
- Kept visible ghosts bounded to 128 per station and the existing 512 server-wide limit. A moving selection window includes unbuilt tips; changing the selection reconciles that window. Pending sockets are indexed during discovery, and the compatible-piece menu tests only the extension socket instead of repeatedly fitting the entire anchored plan.
- Focused checks cover 2,048-piece save/import, 300 front plus 75 rear rails, fair budget exhaustion on both sides, all 73 extension choices, bounded preview windows and native page/schema/save/reload behavior. The unchanged 0.2.6 bundle is reused. No whole-game CPU, GPU or memory improvement is claimed without another live profile.

# 0.2.7 staged test build - station blueprint sync and idle optimization

- Opening the coaster station blueprint page syncs placed rails in both directions. Added explicit Sync Placed Track, Choose Build End and Add Behind actions. Native default-origin straight rails, quarter-turn rotations, reverse-facing pieces and crafted sections use their real poses; extending either end protects constructed sections.
- Aligned gaps are retained as jumps with the actual landing position. Discovery is bounded to 32 metres horizontally and 16 vertically, stops at ambiguous branches/landings, and retains authored pieces that cannot be reached. Flat equal-height launches are flagged for review. This describes the layout; cart speed and ride safety still need a live test.
- Version 2 plan JSON saves a station anchor, traversal direction and gaps; version 1 forward-only layouts remain readable. Reusable imports reset the anchor and piece identities consistently. Picked-up rails become repair previews instead of remaining incorrectly marked as built.
- Idle editors skip unchanged validation, layout publication and ghost reconciliation. Block changes invalidate only watched footprints and socket/support cells. Preview recovery uses a station owner index, type lookups and JSON serializer metadata are cached, and preview creation remains bounded by the existing per-update/server limits.
- Focused geometry and private native save/reload validation cover both ends, jumps, repair previews and 10,000 unchanged updates. The connected-client page and ride behavior remain for user testing. The audited 0.2.6 asset bundle is unchanged; installation ZIP contains only the matching DLL and bundle.

# 0.2.6 staged test build - performance audit and manual drive connection

- Rail Chain Drives and Tram Cable Drives now have a single **Connect** action in the **Rail Connection** tab. It scans once, saves the member rails and switches the drive on. Existing drives need Connect after this update. Removing/replacing a connected rail or conductive station bridge stalls the complete run, turns it off and requires an explicit Connect after repair. Extensions are excluded until Connect; power outages and tram switch selection retain membership. Terrain-change and rail-object destruction events notify the affected drives immediately; no minute or recurring network discovery is needed.
- Stalled drives have a small native curved smoke effect (2 particles/sec, at most 12), hidden normally and bound to the native DriveStalled state. No custom client update script or transient arrow entity is added.
- Local complete-coaster lookups use spatial anchor buckets rather than scanning every complete section. Rail block types are parsed once while actual block presence remains live. Deterministic drive ordering is cached across hot queries, coupling performance avoids duplicate group/leader work, and oriented collision tests use stack buffers instead of heap arrays. Uncoupling releases the link lock before waking/braking motion, resolving a lock-order conflict.
- Focused validation: native Eco 0.14.1.1 connection/removal/repair/extension/save/reload checks for both drive types; 100,000 allocation-free idle link reads; negative-coordinate/replacement/concurrent spatial checks; 20,000 overlap and 1,000 swept-contact comparisons against preserved source. Exported bundle inspections confirmed both native smoke bindings, curved materials and 1,645 unchanged existing mesh payloads. Builds and the developer-tool exclusion guard passed. Updated connected-client gameplay, Linux-host execution and total CPU improvement remain unmeasured.
- Full ranked performance audit, scenario coverage and measurements are provided separately from the install ZIP. The distribution contains only **Eco.Minecarts.dll** and **EcoMinecarts.unity3d**. Recipes are unchanged; prior sleeping, smooth compact coaster geometry, curved lettering and train shoving remain included.

# 0.2.5 staged test build - idle vehicle sleeping

- Settled vehicles with no active driver sleep after one quiet second. Their expensive motion and rail checks run once per second, with lightweight authority maintenance every quarter second. Moving, driver-controlled, automatic, chain-lift and coaster-station vehicles keep the normal 20 Hz solver. Off-rail native hand carts also retain their existing physics updates.
- Shoving, brake/drive commands, boarding, coupling changes and recovery wake the solver immediately. External pose, cargo and track changes are detected by maintenance or the periodic probe. Sleeping time is never integrated as accumulated movement. No saved vehicle fields or recipes change.
- Includes 0.2.4 smooth compact coaster pieces and 0.2.3 native curved lettering/train shoving. Live gameplay and total CPU improvement require a connected-client test.

# 0.2.4 staged test build - smooth single-block coaster transitions

- All single-block flat/uphill/downhill transitions and crest/dip pieces, including chain variants, now ease curvature to zero at both joins. Existing block identities, one-block footprints, socket positions and endpoint pitches are preserved. Compact meshes use twice as many path rings and authored surface normals to remove faceted socket shading.
- Includes 0.2.3 native curved lettering and shoving for train cars. Matched DLL/bundle replacement and a restart are required; live ride and distant-text acceptance remains unverified.

# 0.2.3 staged test build - native curved vehicle lettering

- Vehicle lettering now uses Eco's native Curved/Standard shader, matching its nameplate backings. The static font atlas is baked into transparent glyph coverage so Standard renders readable letters rather than solid glyph rectangles. No runtime font generation or client plugin is added.
- Blank nameplates remain hidden and filled/cleared text retains the existing native vehicle bindings. Includes the 0.2.2 blueprint construction and hand-cart ramp changes. Connected-client distant text acceptance remains a separate live check.
- Added Shift+E shoving on train cars, locomotives, tenders, trams and handcars. Look along the rail to choose the direction. Coupled cars apply one mass-scaled nudge to the consist's leader; the train must be stopped with autopilot off and no operator at the controls. Existing coupling clicks and higher-priority cab controls remain available.

# 0.2.2 staged test build - blueprint construction interactions

- Moved ghost construct interactions onto their WorldObjectComponent so Eco actually registers them. Preview objects themselves do not implement Eco's interaction-registration interface. Added left-click construction alongside right-click and E selection. Preview clicks defer permissions to the real station; construction retains native plot, law, tool and inventory checks.
- Failed construction now reports missing tools/materials, distance, station access, a busy editor or placement rejection directly to the player instead of only changing the station's status field.
- Includes the 0.2.1 ramp and dropdown changes below, using the same inspected asset bundle.

# 0.2.1 staged test build - hand-cart ramps and blueprint menus

- Hand carts distribute the existing walking torque across four wheels, disable automobile traction/stability interventions, and use stronger longitudinal grip. Their suspension now supports the specified empty and rated cargo masses. The occupied collider covers the player's torso so its lower edge clears the rising deck; avatar position and hand anchors are retained.
- Blueprint track choice, planned-piece selection and saved-layout loading use Eco's native dropdown popup instead of typed numbers. Add Track combines choosing and appending a compatible piece. Blueprint controls have their own station tab; empty lists explain the next action and cancelled popups make no changes.
- These changes require a matched DLL/bundle pair and a client restart. Native UI presentation and walking input/networking still require a connected-client retest.

# 0.2.0 staged test build - roller coaster blueprints

- Added a station blueprint editor covering 60 modular coaster rail variants, 12 crafted complete sections and the station. Plans attach using the existing rail sockets and integer-grid transforms. Insert/replace/delete reflow the suffix; built sections are protected against displacement.
- Added 73 native transparent Curved/Standard previews with empty server occupancy, trigger-only selection spans and no vehicle physics or rail registration. Direct WorldObject registration avoids the force-placement helper's block-clearing behavior. Previews are removed after matching real rails are constructed.
- Added native station-panel commands for selection, compatible piece choice, undo/redo, finish/hide/cancel, validation, named saves, relative JSON import/export and personal waypoints. Construction consumes crafted items through Eco's native game actions and placement rules. Station authorization governs editing; construction also checks plot access.
- Placement checks cover terrain/structure/rail occupancy, planned footprint overlap, support warnings and world bounds. Nearby block changes invalidate the cached checks. Bounds checks currently reject layouts across the world wrap seam; cart-envelope clearance and closed-circuit certification are not implemented.
- Focused geometry/import checks and exported-bundle checks passed. Private Eco-server checks covered preview creation, manual fill recognition, actual world save/reload and station cleanup. No connected-client UI/interaction, multi-user or Linux-host acceptance is claimed. A custom orbit camera is not exposed through the supported server/asset setup used here. This build does not establish a fix for the previously reported coaster-cart server crash.

# Unreleased - distant vehicle material variants

- Vehicle paint materials now explicitly enable GPU instancing during authoring and refresh. This keeps the distant rendering path available in the exported bundle, alongside Eco's procedural instance setup and curved vertex transform. Added an exported-shader GPU comparison against Curved/Standard with real curvature, regular/indirect draws and spherical, cylindrical and flat axis masks. Live distant-view acceptance remains separate.
- Added a tram prefab check that automatic terrain depenetration stays disabled, alongside the existing kinematic, gravity and suspension checks. Bouncing on a powered rail in the older release still requires a live retest with this candidate.

# Unreleased - ground-placed tram authority

- Normalized all mod item sprites to native Eco icon dimensions: the 256px artwork now uses 300 pixels per unit, matching native 128px/150PPU sprites. Workstations, ordinary carts and rail items do not receive an oversized transport exception. The workbench retains the native Crafting category and default map scale. Actual map appearance still needs connected-client review.

- Passenger-only trams reject native client physics packets even during the initial guidance window. Ground-parked powered vehicles reassert server ownership each tick and refresh a zero-velocity pose four times per second, preventing a late ownership assignment or client suspension impulse from leaving them visually runaway.
- Disabled the tram's inherited WheelColliders and rigidbody gravity: passenger-only tram motion and airborne gravity are server-simulated. Floor and passenger interaction colliders remain available. This also removes native suspension competing against a stationary guided pose when cable power is absent. Wood/coal supply onboard propulsion on Standard Rail; Tram Rail requires a mechanically powered Tram Cable Drive.
- The tram starts kinematic and uses SyncPhysics's distant/kinematic path at every viewing distance. Nearby observers no longer run a competing dynamic tram body capable of producing suspension/impact excursions. This is a physical collision fix, not a claim that the reported impacts were merely visual; live tram-versus-cart contact must be retested.
- Added a private native regression for a ground-placed tram, late client ownership, an initial unguided packet and stationary pose refresh. The reported live ground-placement incident still requires connected-client reproduction/retest; no running server or save was changed.

# Unreleased - Railcraft Workbench presentation

- Added the native MinimapComponent and Crafting map category, matching Eco's Workbench and Wainwright Table. Map visibility follows normal crafting-object filters.

- Rebuilt the workbench with beveled timber and metal edges, plank seams, mortised stretchers, riveted leg straps, drawer fasteners and properly profiled spare rail stock.
- Added a vise with screw/handle, a measuring rule, mallet, bench-dog sockets, jig bolts and threaded press spindle. Small fittings are visual-only; the crafting press retains its operating animation and native Wainwright crafting sound.
- Combined stationary pieces by material so the richer model uses five static material groups plus the moving press pieces, rather than one renderer per fitting. Added exported-prefab checks for tooling, material groups and decorative collision.
- Uses the native wood table presentation for crafting. Crafting prices, recipes, upgrades and footprint remain as prepared previously. This is next-update work only; connected-client appearance and sound still require review.

# Unreleased - vehicle upholstery and workbench wall connection

- Added generic Fabric to vehicle upholstery: Heritage Tram 12, Passenger Car 16, Large Passenger Car 32, and 4 each for the Passenger Locomotive, Heavy-Haul Locomotive, Large Train Engine and Roller Coaster Cart. Any item accepted by Eco's Fabric tag can supply it; normal crafting reductions apply.
- Offset part of the upholstery cost: tram iron 40 -> 36, Passenger Car iron 24 -> 20, Large Passenger Car steel 56 -> 48, and coaster cart iron 20 -> 18. Starter carts, handcar, cargo cars and tenders retain their current recipes.
- Changed Railcraft Workbench occupancy to the ordinary object occupancy used by vanilla crafting tables, retaining its 2-by-2-by-1 reserved space and floor placement requirement. Adjacent wall meshes should no longer connect to it as a building piece. Check both existing and newly placed benches during the next live update test; re-place an existing bench if its old occupancy persists.
- Prepared in the source tree only. No installation, server restart or world reset was performed; live wall appearance remains to be checked in the next update pass.

# Unreleased - direct fare settings and coaster recovery

- The Passenger Service Fares page now edits its displayed fields and checkboxes directly, with owner/manager authorization and server-side validation on every change. The separate green configuration/toggle/list buttons are removed. Connected passenger cars are listed with the numbers used by the Eligible Cars field.
- Added a Shift+E shove interaction to visible bars on either side of a roller coaster cart. It acts only on an almost-stopped, rail-bound, undamaged consist, applies a small push in the direction the player faces along the track, and retains ownership, distance and cooldown checks. Station and other automatic brakes still govern subsequent motion.
- Fare payment behavior is unchanged: an eligible rider confirms the quote before payment, receives a time-limited service ticket after a successful transfer, and cannot board a charged car without a ticket or exemption.
- Corrected designated-car boarding so a car outside the paid selection cannot offer a fare for that car or mount the rider after payment; the server rechecks service scope and access before boarding.

# Unreleased - vehicle visual stability and handcart motion

- Corrected the vehicle paint shader's indirect-instance setup before Eco's curved-world vertex transform. This aligns distant vehicle rendering with the game's per-instance position and curvature path; live distant-view confirmation remains necessary.
- Recessed locomotive cab sills and step outriggers below their adjacent deck/running-board surfaces to remove near-coplanar z-fighting without changing collision or placement.
- Replaced the handcart's disabled legacy animation component with a looping Animator controller driven by the existing pumping state, so its rocker and linkage animate while operated and stop when parked.
- Rebuilt the client bundle and expanded exported-bundle checks for the shader ordering, trim clearance, and handcart animation bindings.

# Unreleased - rail economy and workbench polish

- Lowered the Railcraft Workbench's Basic Engineering entry cost from 6 iron bars and 12 boards to 4 iron bars and 8 boards, with 120 labor and a 3-minute base craft time. It is still made at the Wainwright Table; rail recipes remain on the workbench.
- Reduced the Wooden Minecart to 6 boards and 2 hewn logs so a personal wooden line is less front-loaded. Tram wide turns now yield two pieces per iron bar, matching the standard wide-turn iron ratio; their six-board cost remains unchanged.
- Added worktop edging, shelf bracing, and a screw/handle and bed guides to the Railcraft Workbench model. The press ram is aligned with its frame and no longer travels through the bed during its working animation. Decorative details do not add collision.
- Made the exported vehicle paint-clear visual check tolerate only a few isolated antialiased edge pixels; the observed Mine Train preview differed in two pixels out of 153,600 after clearing. The full exported-bundle audit then passed, including the workbench animation/audio binding and all vehicle paint regions.

# 0.1.0-beta.15 - public-server candidate

- Fixed explicit autopilot shutdown being undone by an empty cab or active tram service. Leaving an occupied control position still hands off through the actual departure event; ordinary coasting and safety stops remain separate.
- Standard wide rail turns now unregister and remove only their own remaining occupancy when picked up. Route-choice caching is bounded for extended journeys and cached connections are revalidated against the current sockets after track edits.
- Station cargo selections and restricted passenger tickets now use persistent vehicle GUIDs instead of recyclable network handles. The fare-service anchor for equal-priority engines also uses persistent identity. **Upgrade action:** reselect cars for existing car-specific station/fare settings and confirm settings on multi-engine services. Old restricted tickets cannot safely be rebound; the operator should honor or refund them manually. Whole-service legacy tickets remain valid.
- Expired/revoked passenger access prevents new boarding immediately but no longer ejects an existing passenger while rail velocity exceeds the stopped threshold.
- Expanded private-server regression coverage and refreshed full exported-model and block/paint audits. See RELEASE_READINESS.html for evidence, limitations and live-client/multiplayer gates. This candidate is not an unconditional public-release clearance.

# 0.1.0-beta.14

- Balanced route construction against the installed vanilla recipes: Standard Rail, Tram Rail and Powered Chain Rail now use 2 iron bars + 2 boards for 4 pieces, retaining two rails per ingot while cutting timber demand by two thirds. Wooden Rail uses one raw log per piece without an additional board charge. Metal supports remain four per bar and wooden supports one per log. Switches and wide turns have lower board and work-time costs.
- Railcraft Workbench costs 6 iron bars + 12 boards; Tram Stops cost 2 iron bars + 4 boards. Wooden carts and handcars are cheaper entry points. Heritage Tram, its cable drive and stops are available at Basic Engineering 3; the iron cart recipe is Basic Engineering 2 (its native iron wheels still require a level-3 supplier), Mine Train 4. Large engine requires Mechanics 5, large cars Mechanics 4.
- Replaced blanket vehicle ingredient/time costs with individual budgets. Large cargo cars and tenders no longer cost as much steel as a locomotive. Large vehicles retain Lumber, now 16 for cargo/tender and 24 for engine/passenger coach. Powered rolling stock requires one cast-iron stove, not four for the large engine. Full-size rail remains an industrial investment, with existing speed, capacity and traction unchanged.
- Tram cable demand is 10 W per active connected run + 0.25 W per rail cell + 20 W per active tram. A 200-cell, one-tram route needs 80 W rather than 420 W, within one vanilla 100 W waterwheel's output if no other machines use the grid. Disconnected tram runs on one mechanical grid share spare generation proportionally instead of each claiming the whole supply.
- Unified throttle-scaled fuel budgets with native fuel displays, tender refilling and station reserve estimates: Mine Train 110 W, tram fallback burner 60 W, Passenger Locomotive 240 W, Heavy-Haul 450 W, Large Train Engine 900 W. Cable-powered trams retain their no-onboard-fuel behavior. Repair costs and optional player-set fares are unchanged.
- Added deterministic economy checks and native registered-recipe, fuel-component and multi-network power regressions. This is a base-resource balance, not a guarantee of currency prices in player-run economies. Live survival/city playtesting remains necessary. Asset bundle unchanged from beta.12.

# 0.1.0-beta.13

- Large Train Engine, Large Cargo Car, Large Passenger Car and Large Coal Tender now require 32 Lumber instead of 32 Boards before skill/upgrade reductions. The Lumber category accepts vanilla lumber variants. All other ingredients, crafting stations, skills and smaller-vehicle recipes are unchanged.

# 0.1.0-beta.12

- Trolleys running under autopilot stop briefly at a rail buffer and reverse their driving direction without turning the model or losing their speed/throttle settings. Bare track ends, manual driving, paused commands, station holds and damaged vehicles do not trigger turnaround. Existing named stop sequences are retained; include return stops when configuring a specific round-trip itinerary.
- Refined locomotive smokebox hinges and locking dogs, water-tank straps, cylinder-cover bolts, bell mounts and cab grab-rail brackets. Cylinder covers now overlap their shells instead of floating in front. Vehicle dimensions, seat anchors and colliders are unchanged.
- Corrected reversed coupled cars checking buffers/collisions in the wrong travel direction. Collision orientation now retains coaster banking and facing across reversed track seams.
- Fixed weak pulls numerically reversing heavily loaded carts from rest. Expanded collision broad-phase bounds to account for long coaches rather than assuming every vehicle fits within four blocks.
- Vehicle-only asset refresh now refreshes all fourteen vehicles' paint materials and icons, including basic carts and the handcar, before export. Added loaded-cart, long-coach, reversed-track and native reversed-consist buffer regressions.
- Offline/native checks are separate from gameplay acceptance. Distant rendering and actual avatar/vehicle behaviour still require an in-game retest.

# 0.1.0-beta.11

- Seated locomotive smokebox front plates against the boiler face instead of leaving a length-dependent gap, including the compact Mine Train. This is a visual-only adjustment; collision and wheel positions are unchanged.
- Standardized vehicle paint zones across the full fleet: bodywork, underframe/running gear, and roof use separate native RGB paint channels. The exported-bundle audit now checks each locomotive's plate overlap, channel masks, and isolated paint-layer renders. In-game colour selection still needs player confirmation.

# 0.1.0-beta.10

- Replaced the Autopilot page's increase/decrease speed buttons with direct numeric Target Speed (km/h) and Throttle (%) inputs. Entering speed selects cruise control; entering throttle selects constant-throttle autopilot. Zero speed brakes; zero throttle coasts. Inputs retain authorization and distance checks, reject nonfinite values, and clamp to vehicle speed limits or 0-100 percent.
- Pause/resume preserves entered throttle and explicit zero-speed commands; saved throttle-mode settings survive reload. Physical driving controls and station/safety behavior remain intact. Legacy speed-step RPCs remain callable by older open views but no longer appear as buttons.

# 0.1.0-beta.9

- Aligned passenger mounts with the finished bench slats and coaster cushions. Rear tram passengers now face away from their backrests, and the rear backrest slats face those passengers. Raised both passenger-car roofs and extended their posts by 6 cm to retain seated camera clearance. Locomotive chair anchors, standing spots, exits and physical controls are retained. Exported-asset checks verify cushion heights, facing and cab clearance; actual avatar poses still need in-game testing.
- Removed duplicate chain power/speed and driving-mode readouts, and the redundant station condition-editor button. Kept requested/available chain speeds, autopilot controls, fares, routes, repairs and useful status. Autopilot braking buttons now say Pause and Brake / Stop and Disable Autopilot. Maximum speed uses km/h like current and target speeds; actual speed limits are unchanged.
- Added clear units and player-facing labels to weight, condition, cargo, ticket validity, stop waiting time and rail automation. Corrected the Mine Train and coaster station instructions. Native regression coverage checks that useful controls remain and duplicate UI fields stay hidden; older RPC names and saved settings remain supported.

# 0.1.0-beta.8

- Polished all four locomotives, the Heritage Tram, passenger cars, tenders and large cargo car. Added arched roofs, panel lining, lamp fittings, boiler plumbing, cab framing, cylinder details and textured seating. Heritage Tram now has a burgundy/cream finish, framed panels, roof vents and suspended ring handholds.
- Reworked the coaster cart's appearance with a shaped body, individual padded bucket seats, headrests, lap restraints, footwells and captive wheel carriers with lateral guide rollers. Existing rider anchors, rail contact geometry, couplers and gameplay are retained.
- Refreshed vehicle icons with the existing blue Eco backdrops. New solid surfaces use the existing three-channel paint system, and static details are combined into persistent meshes by material to limit extra rendering overhead. In-game rider fit and distant rendering still need connected-client acceptance.

# 0.1.0-beta.7

- Restored Shift+E autopilot switching on the engine boiler as well as the cab console, including switching modes from outside the cab. Manual driving no longer inherits station dwell. A guided powered train's curve overspeed is governed without automatically breaking its couplings; incompatible or unsupported track still triggers derailment handling.

# 0.1.0-beta.6

- Rail Chain Drive placement now uses Eco's downward-only floor snap rule instead of accepting side-face snaps that previewed the cabinet hanging in mid-air. Native placement regression checks reject air and side-wall-only support, accept dirt and lumber flooring, and retain pickup cleanup coverage.

# 0.1.0-beta.5

- Attached the Heritage Tram's standing handholds to the canopy instead of leaving two free-floating bars. Enabled Eco's procedural indirect-rendering path in the paintable vehicle shader to keep body parts aligned at distance; connected-client confirmation is still needed.
- Lowered the Rail Chain Drive cabinet and collision by half a block to match Eco's centered world-object placement origin, while retaining its one-cell occupancy and animated shaft.
- Set locomotive top speeds to 30 m/s and the powered Heritage Tram's to 20 m/s, matching the existing Maximum Speed UI values; the UI now labels those units. Passenger/cargo cars and tenders no longer cap their locomotive below 30 m/s. Starting autopilot from an unset target, and tram route service, command the applicable train limit after conversion to the km/h target-speed UI. Existing saved target speeds remain unchanged. Track curvature, loads, stops and available power may still reduce actual speed.

# 0.1.0-beta.3

- Fixed ground-placed powered locomotives visually flying away while the server held their original position. Unoccupied off-rail engines now remain server-authoritative and parked; actual airborne derailments still follow the existing flight path until landing.
- Hand-pulled minecarts now reproject on the 20 Hz motion timer between native packets and send bounded owner-visible corrections on corners at up to 10 Hz.
- Disabled Unity small-mesh culling on rail vehicle models so thin fittings and running gear remain visible at distance. The compact Mine Train's standing floor/cab is about one third narrower.
- Expanded native RGB paint regions to all solid rail-vehicle parts, including wheels, seats, floors and controls. Lettering and particle effects retain their specialized materials. Live client paint, distant rendering and ground-placement acceptance remain open.

- Added the first driverless Heritage Tram system: six passenger positions, front/rear Text destination boards, a repeating named-stop route, and a compact Tram Stop with line, direction, dwell and enabled controls.
- Added distinct hammer-built Tram Rail (including crossings, switches, four-part slopes and road-ramp overlays), a wide tram turn, and a mechanically powered Tram Cable Drive. Connected Tram Rail supplies traction without onboard fuel; compatible Standard Rail uses a low-speed burner fallback. Eco mechanical supply governs available tram power and speed.
- Tram rail uses a separate muted street-bed material and slimmer ties. Native server and exported-bundle checks cover registrations, recipes, object cleanup, rail geometry, passenger targets and cable-drive animation. Connected-client operation remains to be tested.

- Fixed missing continuous rails on complete coaster shapes and the coaster station: generated meshes are now saved as assets before prefab export. Added checks for actual exported vertices/triangles and rendered all affected shapes. This graphics correction applies to already placed sections after restarting Eco.
- Complete coaster sections now use the same continuous rail cross-section as hammer-built track at both ends. Each one-click section owns a small native snap shoe at each terminal cell so hammer track has a building-block target; pickup clears only its own shoes and lingering footprint. Added mirrored Loop Left alongside the existing Loop Right, moved complete-section placement pivots to their entry-end build cell, and replaced em dashes in displayed rail names with ordinary hyphens. Re-place existing complete coaster sections to use the new pivot and snap shoes; older saved pieces cannot retain their original world position automatically.
- Added 45° coaster ascents and descents (one block per cell), with two-part eased bottom and crest transitions to flat track. Matching chain-lift variants use the same geometry. All are hammer-built forms; the previous gentle/steep grades and complete hill/valley sections remain available.
- Added the Railcraft Workbench, crafted at the Wainwright Table with Basic Engineering 1. All mod recipes, including wooden rails and supports, now use the Railcraft Workbench. Includes a blue-backed icon, working press animation and Eco's native Wainwright crafting sound binding.
- Fixed Railcraft Workbench placement on ground and flooring; its supporting surface is now explicitly below the table, preventing the blank “must be placed on .” error.
- Coaster stations now place from the loading platform, with rails one cell alongside it so their track can hang over an edge. Support tier reaches increased to 6/12/18 for wood/iron/steel. Re-place existing coaster stations to align with the updated anchor.
- Fixed coaster hammer line-building: four-part slopes and chain slopes now select sections 1–2–3–4 in order, two-part steep slopes select 1–2, and the next cycle moves up or down one block. Corrected the rotation order to match Eco's native line placement.
- Matched the complete coaster bend and bank items' registered names to their displayed names (for example, “Bend Right”), so Eco's creative give search finds them by name.
- Coaster stations now pass chain-lift power between connected chain rails on both ends, while remaining passive loading rails. Added matching rail junction boxes at both ends and a native two-sided power/network regression test.

- Added hammer-built wood/iron/steel rail support materials, with base, middle and adaptive rail-saddle top forms, brown-backed construction icons and native terrain colliders. Grounded column reach is 6/12/18 blocks; rail-span placement enforcement remains pending.
- Standard, chain, coaster, wide-turn and switch rail recipes now provide two pieces per iron bar at base cost. Wooden rail provides one piece per raw log; support recipes provide four metal pieces per bar or one wooden piece per raw log.

First release candidate, targeting Eco 0.14.1.1 beta release-1079.

- Standardized coasters: 48 simple hammer forms in four groups, including 1–2–3–4 gentle slopes, 1–2 steep slopes, and 45° grades with eased entries/exits; eight complete specialty sections for large bends, banks, hills, valleys and left/right loops. Removed the old sliced-specialty catalog. Grid-face sockets are shared by modular rails, stations and complete sections. Brown construction icons throughout the rail catalog. Use a new test save; existing saves remain untouched.
- Rail recipe registration cannot omit its crafting table. Native regression now checks all remaining recipe families' client-view getters, preventing the skill-tree/login null-source failure.

- Server-owned locomotive throttle and guided movement; fixes the native driver reclaiming physics ownership after mounting.
- Matching server DLL and client asset bundle, with stable narrow-gauge release defaults.
- Sloped rails require a matching native quarter-ramp underneath. Invalid support resolves the rail to its normal straight version, preserving material and rotation.
- Switch direction signs now use distinct backing and raised green arrows on both sides, without overlapping the old flag.
- JDL-1 license and player-focused installation/control instructions.
- Native paint-sprayer support for all rail vehicles, using body/frame/roof regions where applicable, textured partial coats and original-finish restoration. Paint state remains owned by Eco's existing persistent paint component.

Validation evidence is retained in the development project's `validation` directory. Server probes exercise ownership, station settings, fares/removal, chain speed, passengers, switches, quick transfers, coupled-load restoration and slope support. Physics probes cover guidance, momentum, ramps, corners, brakes and connection geometry. Bundle and rendered-model audits check object registrations, meshes, materials, colliders and native bindings.

These are offline/private-server checks. They do not certify smooth rendering, seat/camera presentation or UI operation on a connected live client. The first release remains beta until the owner completes that playtest. No public upload is implied by creating this package.









# 0.2.43 — manual minecart test candidate

Rebuilt standard and wooden minecarts around one authoritative server rail state. Movement uses the shared coaster rail dynamics and pose sampling; native client driving and wheel forces no longer compete with it. Walking handles provide force input without mounting or correcting the player's position. Shoves act on the consist leader, and followers use its rail path. Existing object/component identifiers and serialized definitions remain compatible.

Verified: production build without warnings/errors, saved-schema compatibility, real Eco server single-cart/brake/client-packet rejection tests for both cart types, coupled follower tests, dumping rail and coaster recovery regressions, and the exported bundle's ownership/physics settings with unchanged geometry/material payloads. Pending: live client rendering, E-grab walking, bucket passengers, curves/slopes/powered chains and multiplayer. This is a test candidate, not a live-verified release.
