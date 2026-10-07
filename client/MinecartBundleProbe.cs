using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    // Reload the EXPORTED scene, not the source assets, and compare against keys
    // captured from a running native Eco server. Invoke without -quit.
    [InitializeOnLoad]
    public static class MinecartBundleProbe
    {
        private const string Pending = "EcoMinecarts.BundleProbe";
        private static AssetBundle bundle;
        private static AsyncOperation loading;
        static MinecartBundleProbe() { EditorApplication.update += Update; }

        public static void Run()
        {
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        private static void Update()
        {
            if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying) return;
            try
            {
                if (bundle == null)
                {
                    bundle = AssetBundle.LoadFromFile(BundlePath);
                    if (bundle == null) throw new Exception("Bundle could not be reopened.");
                    loading = SceneManager.LoadSceneAsync(bundle.GetAllScenePaths().Single(), LoadSceneMode.Single);
                    return;
                }
                if (!loading.isDone) return;
                Verify();
                RailVehiclePaintProbe.Verify(SceneManager.GetActiveScene().GetRootGameObjects().Single(n=>n.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
                SessionState.SetBool(Pending, false);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("ECO_EXPORTED_BUNDLE_FAILED: " + e);
                SessionState.SetBool(Pending, false);
                EditorApplication.Exit(1);
            }
        }

        private static void VerifyBrakeEffects(GameObject cart)
        {
            var world = cart.GetComponent<WorldObject>();
            var audio = cart.transform.Find("BrakeAudio").GetComponent<AudioSource>();
            if (audio.clip == null || audio.clip.name != "minecart_brake_loop" || !audio.loop || audio.volume != 0 || audio.pitch != 1 || audio.spatialBlend != 1)
                throw new Exception("Missing or incorrectly configured supplied brake loop.");
            var audioEvent = Array.IndexOf(world.FloatStates, "BrakeVolume");
            if (audioEvent < 0) throw new Exception("Missing native brake-volume state.");
            world.OnFloatStateChanged[audioEvent].Invoke(.4f);
            if (Math.Abs(audio.volume - .4f) > .001f) throw new Exception("Exported brake-volume callback does not reach AudioSource.");
            world.OnFloatStateChanged[audioEvent].Invoke(0);
            var corner = cart.transform.Find("CornerAudio").GetComponent<AudioSource>();
            var cornerIndex = Array.IndexOf(world.FloatStates, "CornerVolume");
            if (cornerIndex < 0 || corner.clip == null || corner.clip.name != "minecart_corner_screech_loop" || !corner.loop || corner.volume != 0)
                throw new Exception("Missing or incorrectly configured corner loop.");
            world.OnFloatStateChanged[cornerIndex].Invoke(.1f);
            if (Math.Abs(corner.volume - .1f) > .001f) throw new Exception("Exported corner-volume callback is broken.");
            world.OnFloatStateChanged[cornerIndex].Invoke(0);
            var chain=cart.transform.Find("ChainLiftAudio").GetComponent<AudioSource>();
            var chainIndex=Array.IndexOf(world.FloatStates,"ChainLiftPitch");
            if(chainIndex<0 || chain.clip==null || chain.clip.name!="Chain_Racket" || !chain.loop || chain.minDistance!=1 || chain.maxDistance!=8)
                throw new Exception("Missing chain pitch state or supplied loop.");
            foreach(var multiplier in new[]{.25f,.5f,1f,2f,3f})
            {
                world.OnFloatStateChanged[chainIndex].Invoke(multiplier);
                if(Math.Abs(chain.pitch-multiplier)>.001f) throw new Exception("Exported chain-pitch callback is broken.");
            }
            world.OnFloatStateChanged[chainIndex].Invoke(1);
            Debug.Log("ECO_CHAIN_SPEED_AUDIO_OK: exported loop pitch callbacks exercised at 0.25x, 0.5x, 1x, 2x and 3x.");
            for (var tier = 1; tier <= 3; tier++)
            {
                var group = cart.transform.Find("BrakeSparks" + tier).gameObject;
                var index = Array.IndexOf(world.States, group.name);
                if (index < 0 || group.activeSelf) throw new Exception("Spark state missing or active while parked.");
                var particles = group.GetComponentsInChildren<ParticleSystem>(true);
                if (particles.Length != 4 || particles.Any(p => p.GetComponent<ParticleSystemRenderer>().sharedMaterial == null))
                    throw new Exception("Missing wheel spark systems/material.");
                world.OnStateChangedEvents[index].Invoke(true);
                if (!group.activeSelf) throw new Exception("Exported spark-enable callback is broken.");
                world.OnStateChangedEvents[index].Invoke(false);
                if (group.activeSelf) throw new Exception("Exported spark-disable callback is broken.");
            }
            Debug.Log("ECO_EXPORTED_BRAKE_EFFECTS_OK: loop and all native callbacks reopened and exercised; four wheel emitters per speed tier.");
        }

        private static void VerifyChainDrive(GameObject prefab)
        {
            var drive = Object.Instantiate(prefab);
            try
            {
                drive.SetActive(true);
                var housing = drive.transform.Find("ChainDriveVisual/EnclosedHousing");
                if (housing == null || drive.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("MovingLink")))
                    throw new Exception("Chain drive must have an enclosed housing without exposed links.");
                var colliders = drive.GetComponentsInChildren<Collider>(true);
                if (colliders.Length != 1 || !(colliders[0] is BoxCollider box) || box.size.x < .88f || box.size.y < .9f)
                    throw new Exception("Chain drive needs one solid enclosure collider.");
                foreach (var renderer in drive.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var b = renderer.bounds;
                    if (b.min.y < -.501f || b.max.y > .501f || b.min.x < -.5f || b.max.x > .5f || b.min.z < -.5f || b.max.z > .5f)
                        throw new Exception("Chain drive mesh exceeds its single-block footprint: " + renderer.name);
                    if (renderer.sharedMaterial == null || renderer.sharedMaterial.mainTexture == null || !renderer.sharedMaterial.shader.isSupported)
                        throw new Exception("Missing enclosed-drive texture/shader: " + renderer.name);
                }
                if (drive.GetComponentsInChildren<AudioSource>(true).Length != 0)
                    throw new Exception("Chain racket belongs on the moving cart, not the drive housing.");
                var animation = drive.GetComponent<Animation>();
                var world = drive.GetComponent<WorldObject>();
                if (world.overrideOccupancy || Mathf.Abs(box.center.x) > .001f || Mathf.Abs(box.center.z) > .001f || Mathf.Abs(box.center.y+.035f)>.001f)
                    throw new Exception("Chain drive must use only its native one-cell occupancy and a centered collider.");
                var index = Array.IndexOf(world.States, "ChainRunning");
                if (index < 0) throw new Exception("Missing chain running state.");
                world.OnStateChangedEvents[index].Invoke(true);
                if (!animation.enabled) throw new Exception("Power-on does not activate the shaft.");
                var shaft = drive.transform.Find("ChainDriveVisual/ShaftIndicator");
                animation.clip.SampleAnimation(drive, .5f);
                if (Quaternion.Angle(shaft.localRotation, Quaternion.identity) < 30) throw new Exception("Exported chain shaft animation does not turn.");
                world.OnStateChangedEvents[index].Invoke(false);
                if (animation.enabled) throw new Exception("Power-off does not stop the shaft.");
                Debug.Log("ECO_ENCLOSED_CHAIN_DRIVE_OK: textured single-block enclosure, solid collider, shaft animation; housing has no racket audio.");
            }
            finally { Object.DestroyImmediate(drive); }
        }

        private static void Verify()
        {
            BlockDataDebugger.Export(SceneManager.GetActiveScene(), BundlePath);
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var itemNames=roots.Single(r=>r.name=="Items").transform.Cast<Transform>().Select(t=>t.name).ToArray();
            foreach(var path in RailExpansionAssetBuilder.ReadCatalog().Coasters.Where(p=>p.Key!="CoasterStraight"))
                if(!itemNames.Contains(path.Key+"Item"))throw new Exception("Missing complete coaster section item view: "+path.Key);
            if(itemNames.Contains("CoasterStraightItem"))throw new Exception("Duplicate straight coaster item view");
            ItemIconAudit.Verify(roots);
            foreach(var prefab in roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs)
                if(prefab.GetComponent<global::Vehicle>() != null)
                {
                    RailRiderInteractionAssetBuilder.Verify(prefab, explicitExit:prefab.transform.Find("CabFittings") != null);
                    RailExteriorPlatformAssetBuilder.Verify(prefab);
                }
            VerifyExpansion(roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
            CoasterAssetProbe.Verify(roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
            RailSwitchAssetProbe.Verify(roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
            TramAssetProbe.Verify(roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
            CabHeadroomProbe.Verify(roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
            StationModelProbe.Verify(roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
            RailWheelSuspension.Verify(roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
            VerifyMineTrain(roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs.Single(p => p.name == "MineTrainObject"));
            VerifyChainDrive(roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs.Single(p => p.name == "MinecartChainDriveObject"));
            var container = roots.Single(r => r.name == "BlockSets").GetComponent<BlockSetContainer>();
            RailcraftWorkbenchAssetBuilder.Verify(roots.Single(r=>r.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs.Single(p=>p.name=="RailcraftWorkbenchObject"));
            var blocks = container.blockSets.SelectMany(s => s.Blocks).ToArray();
            var expected = File.ReadAllLines(Path.GetFullPath("../../validation/server-client-block-keys.txt"));
            var names = blocks.Select(b => b.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (names.Length != expected.Length || names.Distinct().Count() != expected.Length ||
                !names.SequenceEqual(expected.OrderBy(x => x, StringComparer.Ordinal)))
                throw new Exception("Exported block keys do not match the native server. Missing: " + string.Join(",", expected.Except(names)));
            foreach (var block in blocks)
            {
                var builder = block.Builder as CustomBuilder;
                if (builder == null || builder.usageCases.Count != 1 || builder.usageCases[0].conditions.Count != 0 || builder.usageCases[0].mesh == null)
                    throw new Exception("Missing unconditional native terrain builder: " + block.Name);
                var lods = builder.usageCases[0].blockMeshLodGroup;
                if(block.Name.StartsWith("RailSupport"))
                {
                    if(lods==null||lods.LOD0.Length!=1||lods.Collider==null||!lods.Collider.isReadable||lods.Collider.uv.Length!=lods.Collider.vertexCount||!block.BuildCollider||!block.GenerateMeshCollider)
                        throw new Exception("Support block lacks native readable mesh collider: "+block.Name);
                    if(lods.LOD0[0].mesh.vertexCount<24||lods.LOD0[0].mesh.subMeshCount!=2)throw new Exception("Support geometry missing: "+block.Name);
                    continue;
                }
                if(block.Name.StartsWith("CoasterTrack"))
                {
                    CoasterTerrainAssetBuilder.Verify(block);
                    if(!block.Solid||!block.BuildCollider||!block.GenerateMeshCollider)throw new Exception("Missing coaster terrain collider: "+block.Name);
                    using(var copies=Mesh.AcquireReadOnlyMeshData(lods.Collider))
                    using(var uv=new Unity.Collections.NativeArray<Vector2>(lods.Collider.vertexCount,Unity.Collections.Allocator.Temp))copies[0].GetUVs(0,uv);
                    continue;
                }
                if (lods == null || lods.LOD0.Length != 1 || lods.Collider == null)
                    throw new Exception("Missing exported terrain mesh/collider: " + block.Name);
                // Reproduce the MeshData attribute access seen in Eco's live
                // ThreadSafeMeshCopy stack, not just Unity MeshCollider ray tests.
                using (var copies = Mesh.AcquireReadOnlyMeshData(lods.Collider))
                using (var uv = new Unity.Collections.NativeArray<Vector2>(lods.Collider.vertexCount, Unity.Collections.Allocator.Temp))
                {
                    copies[0].GetUVs(0, uv);
                    if (uv.Length != lods.Collider.vertexCount)
                        throw new Exception("Eco collider copy missing TexCoord0: " + block.Name);
                }
                VerifyDeckSupport(block.Name, lods.Collider);
                for (var level = 0; level < 3; level++)
                {
                    var mesh = lods.GetLodMesh(level, 0);
                    if (!mesh.isReadable || mesh.vertexCount < 24 || mesh.triangles.Length < 36)
                        throw new Exception("Unreadable/empty terrain mesh: " + block.Name);
                    TrackBlockAssetBuilder.VerifySupportClearance(block.Name, mesh);
                }
                if (!block.Solid || !block.BuildCollider || !block.GenerateMeshCollider) throw new Exception("Missing shape collider: " + block.Name);
            }
            Debug.Log("ECO_EXPORTED_TERRAIN_MESHES_OK: "+blocks.Length+" CustomBuilders, readable LOD0/1/2, two material slots and shape colliders. This is not a live Eco render test.");
            Debug.Log("ECO_SUPPORT_CLEARANCE_OK: every vertex in every rotation and LOD stays above its intended flat/ramp support surface.");
            Debug.Log("ECO_COLLISION_DECK_COPY_AND_SUPPORT_OK: collider MeshData UV copy and continuous center/wheel-line ray tests passed for all placed track shapes.");
            foreach (var prefix in new[] { "MinecartTrack", "MinecartChain", "TramTrack" })
                VerifyCurveWheelFootprint(blocks, prefix);

            var cartPrefab = roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs.Single(p => p.name == "MinecartObject");
            foreach(var vehicle in roots.Single(r => r.name == "Objects").GetComponent<ModkitPrefabContainer>().Prefabs.Where(p=>p.GetComponent<global::Vehicle>()!=null))
            {
                var railSync=vehicle.GetComponent<SyncPhysics>();
                var serverOnly=RailVehiclePhysicsAssetBuilder.ServerOnly(vehicle);
                if(railSync==null||!railSync.SyncPos||railSync.SyncVelocity==serverOnly||!railSync.SyncRot||railSync.ManuallyUpdated)
                    throw new Exception("Vehicle synchronization does not match its server/native ownership: "+vehicle.name);
                if(serverOnly&&(!vehicle.GetComponent<Rigidbody>().isKinematic||vehicle.GetComponent<Rigidbody>().useGravity
                    ||vehicle.GetComponentsInChildren<WheelCollider>(true).Any(w=>w.enabled)))
                    throw new Exception("Server-only vehicle retains dynamic suspension: "+vehicle.name);
                if(vehicle.name=="HeritageTramObject" || vehicle.name=="PassengerLocomotiveObject" || vehicle.name=="LargeTrainEngineObject")
                {
                    var expectedSpeedCap=vehicle.name=="HeritageTramObject" ? 72f : 108f;
                    if(Math.Abs(vehicle.GetComponent<RCCCarControllerV2>().maxspeed-expectedSpeedCap)>.01f)
                        throw new Exception("Vehicle speed catalog lost its faster cap: "+vehicle.name);
                }
            }
            var mount = cartPrefab.GetComponent<Mountable>();
            VerifyCargo(cartPrefab);
            VerifyCouplers(cartPrefab, .806f);
            VerifyBrakeEffects(cartPrefab);
            var chainAudio = cartPrefab.transform.Find("ChainLiftAudio").GetComponent<AudioSource>();
            var cartWorld = cartPrefab.GetComponent<WorldObject>();
            var liftIndex = Array.IndexOf(cartWorld.States, "ChainLiftRunning");
            if (liftIndex < 0 || chainAudio.clip == null || chainAudio.clip.name != "Chain_Racket" || !chainAudio.loop
                || chainAudio.spatialBlend != 1 || chainAudio.gameObject.activeSelf)
                throw new Exception("Missing or incorrectly configured cart chain-lift racket.");
            cartWorld.OnStateChangedEvents[liftIndex].Invoke(true);
            if (!chainAudio.gameObject.activeSelf) throw new Exception("Cart chain-lift audio callback failed to start.");
            cartWorld.OnStateChangedEvents[liftIndex].Invoke(false);
            if (chainAudio.gameObject.activeSelf) throw new Exception("Cart chain-lift audio callback failed to stop.");
            Debug.Log("ECO_CART_CHAIN_LIFT_AUDIO_OK: supplied racket attached to the cart; lift-state start/stop callbacks passed.");
            if (cartPrefab.GetComponent<Eco.Client.InteractionBlocker>() == null ||
                cartPrefab.GetComponent<WaitForGround>() == null || cartPrefab.GetComponent<MoveThroughSounds>() == null)
                throw new Exception("Missing native VehicleBase boarding/exit dependencies.");
            var pullCollider = cartPrefab.transform.Find("PullPlayerCollider").GetComponent<BoxCollider>();
            if (pullCollider.transform.localPosition.y + pullCollider.center.y - pullCollider.size.y * .5f < .04f)
                throw new Exception("Occupied pull collider penetrates the running surface.");
            if (mount == null || mount.seats.Length != 3 || mount.seats.Any(s => s == null) || !mount.handleDismount || mount.freezeWhenUnmounted)
                throw new Exception("Missing native passenger mount or incorrect dismount/physics flags.");
            var seat = mount.seats[1];
            if (seat.AlignWithPlayerSide || mount.seats[2].AlignWithPlayerSide
                || Vector3.Dot(seat.transform.localRotation * Vector3.forward, Vector3.forward) < .999f
                || Vector3.Dot(mount.seats[2].transform.localRotation * Vector3.forward, Vector3.back) < .999f
                || Vector3.Distance(seat.transform.localPosition, mount.seats[2].transform.localPosition) > .001f)
                throw new Exception("Passenger must have opposite native orientations at the same bucket position, selected by the server.");
            if (cartPrefab.transform.Find("Rail_Guide") != null)
                throw new Exception("Removed penetrating rail guide must not return.");
            if (!seat.setAsParent || seat.exitPosition == null || seat.alternativeExitPosition == null || seat.cameraTarget == null)
                throw new Exception("Passenger seat lacks follow-parent/camera/exit anchors.");
            var driver = mount.seats[0] as MountSpotPulled;
            var controller = cartPrefab.GetComponent<RCCCarControllerV2>();
            if (driver == null || driver.lHand == null || driver.rHand == null || !driver.ApplyHandsGripOnMount
                || driver.lHand.effectorType != 5 || driver.rHand.effectorType != 6
                || controller == null || !controller.footPoweredCart || Mathf.Abs(controller.engineTorque - 3000f) > .01f || controller.maxspeed > 5.401f || controller.allWheelColliders.Length != 4
                || cartPrefab.GetComponent<Vehicle>() == null || cartPrefab.GetComponent<Rigidbody>().isKinematic)
                throw new Exception("Native pulling requires Vehicle, dynamic body, foot-powered RCC, four wheels and both hand targets.");
            Debug.Log("ECO_NATIVE_PULL_BINDING_OK: Vehicle + RCCCarControllerV2 + MountSpotPulled, left/right hand IK and separate passenger seat. Actual input/mounting remains a live test.");
            if (controller.totalGears != 1 || controller.currentGear != 0 || controller.gearSpeed.Length != 1
                || controller.gearSpeed[0] <= 0 || controller.engineTorqueCurve.Length != 1
                || controller.engineTorqueCurve[0] == null || controller.engineTorqueCurve[0].length < 2)
                throw new Exception("Native engine gear arrays cannot be empty or inconsistent.");
            if (driver.transform.localPosition.y < 0 || driver.exitPosition.localPosition.y < 0
                || driver.alternativeExitPosition.localPosition.y < 0 || (int)driver.overrideAvatarState != 255)
                throw new Exception("Pull attachment/exit sinks below the running plane or forces the wrong avatar state.");
            foreach (var target in new[] { driver.lHand, driver.rHand })
            {
                var grip = target.transform.localPosition;
                if (Mathf.Abs(grip.x) > .116f || Mathf.Abs(grip.y - .60f) > .001f || Mathf.Abs(grip.z - .674f) > .001f)
                    throw new Exception("Hand target lies outside the actual modeled grab crossbar.");
            }
            Debug.Log("ECO_PULL_ENGINE_AND_GRIP_OK: populated native gear arrays; nonnegative feet/exit plane; modeled crossbar targets; unforced avatar state. Live input and pose remain unverified.");
            MinecartAssetBuilder.ValidateCartGeometry(cartPrefab);
            var cart = Object.Instantiate(cartPrefab);
            cart.SetActive(true);
            Physics.SyncTransforms();
            foreach (var end in new[] { -1, 1 })
            {
                var handle = CheckHit(new Vector3(0, .6f, end * 1.5f), Vector3.forward * -end, "MinecartHandle");
                if (handle.interactionTargetValue != end.ToString())
                    throw new Exception("Wrong handle-side value: expected " + end + "; got " + handle.interactionTargetValue);
            }
            CheckHit(new Vector3(2, .5f, 0), Vector3.left, "MinecartStorage");
            CheckHit(new Vector3(0, 2, 0), Vector3.down, "MinecartStorage");
            var sync = cart.GetComponent<SyncPhysics>();
            if (sync == null || sync.AutomaticChunkDepenetrationEnabled)
                throw new InvalidOperationException("Server-controlled cart must disable automatic chunk depenetration.");
            foreach (var renderer in cart.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials.Where(x => x != null).Distinct())
            {
                if (material.GetTexture("_MainTex") == null) throw new InvalidOperationException(material.name + " lacks albedo texture.");
                // Curved particles preserve vertex alpha and additive blending.
                if (renderer is ParticleSystemRenderer)
                {
                    if (material.shader == null || !material.shader.isSupported || material.shader.name != RailWorldMaterialBuilder.ParticleShader
                        || material.GetColor("_Color").maxColorComponent < .5f || material.GetFloat("_ZWrite") != 0
                        || material.GetFloat("_DstBlend") != (float)UnityEngine.Rendering.BlendMode.One)
                        throw new InvalidOperationException("Invalid exported spark shader/tint: " + material.name);
                    continue;
                }
                if (material.GetFloat("_Metallic") > .10f) throw new InvalidOperationException(material.name + " scalar metallic is too dark for an unmapped material.");
                var color = material.GetColor("_Color");
                // The visual polish deliberately replaces the old strong red
                // boost with a reviewed neutral timber tint. Keep an explicit
                // contract for it; do not weaken the dark-metal visibility guard.
                if(material.name=="MAT_WoodRail" || material.name.StartsWith("MAT_WoodRail_Paint",StringComparison.Ordinal)){
                    if(Vector3.Distance(new Vector3(color.r,color.g,color.b),new Vector3(1.22f,1.28f,1.24f))>.01f)
                        throw new InvalidOperationException("Timber lost the reviewed neutral visual-polish tint.");
                }else if (Mathf.Max(color.r, color.g, color.b) < 1.5f) throw new InvalidOperationException(material.name + " lacks world-light visibility tint.");
            }
            Debug.Log("ECO_EXPORTED_HIT_TARGETS_OK: both end handles and bucket side/top raycasts resolve to the correct E target.");
            cart.transform.position = Vector3.up * .15f;
            var straight = blocks.Single(b => b.Name == "MinecartTrackStraight");
            if (straight.Material == null || straight.Material.name != "MAT_IronBare"
                || straight.Materials == null || straight.Materials.Length != 1 || straight.Materials[0] == null
                || straight.Materials[0].name != "MAT_WoodRail")
                throw new InvalidOperationException("Track must map submesh 0 to main IronBare and submesh 1 to the sole additional WoodRail material.");
            var rail = new GameObject("ExportedNativeRailMesh", typeof(MeshFilter), typeof(MeshRenderer));
            rail.GetComponent<MeshFilter>().sharedMesh = ((CustomBuilder)straight.Builder).usageCases[0].blockMeshLodGroup.LOD0[0].mesh;
            if (rail.GetComponent<MeshFilter>().sharedMesh.subMeshCount != 2)
                throw new InvalidOperationException("Track terrain mesh must contain iron and wood submeshes.");
            rail.GetComponent<MeshRenderer>().sharedMaterials = new[] { straight.Material }.Concat(straight.Materials).ToArray();
            rail.transform.position = Vector3.up * .5f; // block center
            Render(cart);
        }

        private static void VerifyExpansion(GameObject[] prefabs)
        {
            foreach (var spec in RailExpansionAssetBuilder.ReadCatalog().Vehicles)
            {
                var prefab = prefabs.Single(p => p.name == spec.Key + "Object");
                ModelPolishProbe.Verify(prefab,spec);
                var body = prefab.GetComponent<Rigidbody>();
                var mount = prefab.GetComponent<Mountable>();
                var controller = prefab.GetComponent<RCCCarControllerV2>();
                if (body == null || Mathf.Abs(body.mass-spec.EmptyKg)>.01f || controller == null)
                    throw new Exception("Expansion module native mass/controller missing: " + spec.Key);
                var seats = spec.Powered ? Math.Max(2,spec.PassengerSeats+1) : spec.Pullable ? 3 : spec.PassengerSeats + 1;
                if (mount == null || mount.seats.Length != seats || mount.seats.Any(s=>s==null))
                    throw new Exception("Expansion seat registration mismatch: " + spec.Key);
                if (Mathf.Abs(controller.FrontLeftWheelTransform.localPosition.z-controller.RearLeftWheelTransform.localPosition.z-spec.Wheelbase)>.001f)
                    throw new Exception("Expansion wheelbase mismatch: " + spec.Key);
                if(spec.Pullable && (controller.engineTorque<14000f || Mathf.Abs(mount.seats[0].transform.localPosition.x)>.001f))
                    throw new Exception("Pullable cart lacks grade torque or centered rail grip: " + spec.Key);
                VerifyCouplers(prefab,spec.Length/2);
                if(spec.HumanPowered)
                {
                    if(!controller.footPoweredCart || controller.engineTorque<=0 || mount.seats[0].IKTargets.Length!=2
                        || !mount.seats[0].ApplyHandsGripOnMount || (int)mount.seats[0].overrideAvatarState!=255
                        || prefab.transform.Find("SteamExhaust")!=null)
                        throw new Exception("Handcar requires native human drive, standing mount, pump grips and no exhaust.");
                    var world=prefab.GetComponent<WorldObject>();
                    var animation=prefab.GetComponent<Animator>();
                    var index=Array.IndexOf(world.States,"HandcarPumping");
                    if(index<0 || animation==null || animation.runtimeAnimatorController==null
                        || animation.runtimeAnimatorController.animationClips.Length!=1 || animation.enabled
                        || animation.applyRootMotion || animation.cullingMode!=AnimatorCullingMode.AlwaysAnimate)
                        throw new Exception("Handcar pump animation missing or running while parked.");
                    world.OnStateChangedEvents[index].Invoke(true);
                    if(!animation.enabled) throw new Exception("Handcar pump callback cannot start.");
                    world.OnStateChangedEvents[index].Invoke(false);
                    if(animation.enabled) throw new Exception("Handcar pump callback cannot stop.");
                    if(!prefab.GetComponentsInChildren<SpecificInteractable>(true).Any(x=>x.interactionTargetName=="HandcarPump"))
                        throw new Exception("Handcar operating target missing.");
                    Debug.Log("ECO_HANDCAR_BUNDLE_OK: human-powered drive, standing operator, looping Animator pump, moving grip targets, native pump callbacks and couplers.");
                }
                if (spec.PassengerSeats>0 && prefab.GetComponentsInChildren<SpecificInteractable>(true).Count(x=>x.interactionTargetName=="RailPassengerSeat")!=spec.PassengerSeats)
                    throw new Exception("Passenger seats lack individual interactions: " + spec.Key);
                if (spec.Model.Contains("Cargo") || spec.Tender || spec.Pullable)
                {
                    var cargo = prefab.GetComponentInChildren<StockpileMeshBuilder>(true);
                    if (cargo == null || cargo.contentDimensions != new Vector3Int(2,1,spec.Length>3?4:2))
                        throw new Exception("Native cargo display missing: " + spec.Key);
                }
                if (spec.Powered && spec.Model!="Tram" && (mount.seats[0].cameraTarget!=null || prefab.transform.Find("SteamExhaust")==null))
                    throw new Exception("Engine camera or exhaust contract failed: " + spec.Key);
                if(spec.Powered && spec.Model!="Tram") StandingCabAssetBuilder.Verify(prefab);
            }
            foreach(var key in new[]{"WideRailTurnObject","TrainStationObject","BrokenWoodenTrackObject"})
                if(prefabs.Single(p=>p.name==key).GetComponentsInChildren<Collider>(true).Length==0)
                    throw new Exception("Missing infrastructure collider: " + key);
            Debug.Log("ECO_EXPANSION_BUNDLE_OK: nine vehicle native identities, masses, wheelbases, mounts, couplers, cargo displays and three infrastructure prefabs.");
        }

        private static void VerifyMineTrain(GameObject prefab)
        {
            if (prefab.GetComponentInChildren<StockpileMeshBuilder>(true) != null)
                throw new Exception("Locomotive must not inherit the minecart hopper cargo display.");
            VerifyCouplers(prefab, .93f);
            var controller = prefab.GetComponent<RCCCarControllerV2>();
            var mount = prefab.GetComponent<Mountable>();
            var vehicle = prefab.GetComponent<Vehicle>();
            if (controller == null || controller.footPoweredCart || controller.engineTorque <= 0
                || controller.allWheelColliders.Length != 4 || controller.steerAngle != 0
                || prefab.GetComponent<Rigidbody>().mass != 600 || vehicle == null)
                throw new Exception("Locomotive lacks native powered four-wheel vehicle configuration.");
            StandingCabAssetBuilder.Verify(prefab);
            var deck=prefab.transform.Find("CabFittings/Cab floor");
            var board=prefab.transform.Find("MineTrain_Visual/RunningBoard");
            var finish=prefab.transform.Find("VisualFinish");
            var deckTop=deck.localPosition.y+deck.localScale.y/2;
            var boardTop=board.localPosition.y+board.localScale.y/2;
            var sills=finish.Cast<Transform>().Where(t=>t.name=="Cab sill").ToArray();
            var outriggers=finish.Cast<Transform>().Where(t=>t.name=="Cab outrigger").ToArray();
            if(sills.Length!=2 || sills.Any(t=>deckTop-(t.localPosition.y+t.localScale.y/2)<.01f)
                || outriggers.Length!=2 || outriggers.Any(t=>boardTop-(t.localPosition.y+t.localScale.y/2)<.015f))
                throw new Exception("Mine Train cab sill/outrigger surface overlaps its deck or running board");
            foreach (var target in new[] { "MineTrainCab", "MineTrainBoiler" })
                if (!prefab.GetComponentsInChildren<SpecificInteractable>(true).Any(x => x.interactionTargetName == target))
                    throw new Exception("Missing locomotive interaction: " + target);
            var exhaust = prefab.transform.Find("SteamExhaust").GetComponent<ParticleSystem>();
            if (exhaust.main.playOnAwake || !exhaust.main.loop || exhaust.GetComponent<ParticleSystemRenderer>().sharedMaterial == null)
                throw new Exception("Exhaust must be looped, material-bound and operating-controlled.");
            if (vehicle.OnEnableOperating.GetPersistentEventCount() != 1 || vehicle.OnEnableOperating.GetPersistentTarget(0) != exhaust
                || vehicle.OnEnableOperating.GetPersistentMethodName(0) != "Play"
                || vehicle.OnDisableOperating.GetPersistentTarget(0) != exhaust || vehicle.OnDisableOperating.GetPersistentMethodName(0) != "Stop")
                throw new Exception("Exported locomotive exhaust events do not follow native operating state.");
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.sharedMaterials.Any(m => m == null || m.mainTexture == null))
                    throw new Exception("Locomotive renderer lost an authored material/texture: " + renderer.name);
            Debug.Log("ECO_MINE_TRAIN_BUNDLE_OK: four wheels, one operator chair, cab/boiler interactions, textured original model and native exhaust callbacks. Connected-client acceptance remains unverified.");
        }

        private static void VerifyCargo(GameObject prefab)
        {
            var contents = prefab.transform.Find("CargoContents");
            var builders = prefab.GetComponentsInChildren<StockpileMeshBuilder>(true);
            if (contents == null || builders.Length != 1 || builders[0].transform != contents
                || builders[0].contentDimensions != new Vector3Int(2, 1, 2))
                throw new Exception("Cargo must use one native 2x1x2 stockpile display matching server storage.");
            if (builders[0].GetType().Assembly.GetName().Name != "Eco.Client"
                || !contents.GetComponent<MeshCollider>().convex
                || contents.GetComponent<SpecificInteractable>().interactionTargetName != "MinecartStorage")
                throw new Exception("Wrong native cargo script identity, collider or storage interaction.");
            if (Vector3.Distance(contents.localPosition, new Vector3(0, .505f, 0)) > .001f
                || Vector3.Distance(contents.localScale, new Vector3(.19f, .30f, .42f)) > .001f)
                throw new Exception("Cargo display is outside the hopper interior.");
            Debug.Log("ECO_CART_CARGO_ASSETS_OK: native storage mesh, grid, placement and storage target verified.");
        }

        private static void VerifyCouplers(GameObject prefab, float offset)
        {
            var world = prefab.GetComponent<WorldObject>();
            foreach (var end in new[] { -1, 1 })
            {
                var label = end > 0 ? "Front" : "Rear";
                var hit = prefab.transform.Find("RailCoupler" + label);
                var target = hit.GetComponent<SpecificInteractable>();
                if (target.interactionTargetName != "RailCoupler" || target.interactionTargetValue != end.ToString()
                    || Mathf.Abs(hit.localPosition.z - end * offset) > .001f)
                    throw new Exception("Wrong connector hit target or endpoint.");
                var bridge = prefab.transform.Find("CoupledCoupler" + label).gameObject;
                var index = Array.IndexOf(world.States, "Coupled" + label);
                if (index < 0 || bridge.activeSelf) throw new Exception("Coupled connector visual must start hidden.");
                world.OnStateChangedEvents[index].Invoke(true);
                if (!bridge.activeSelf) throw new Exception("Coupled connector callback failed.");
                world.OnStateChangedEvents[index].Invoke(false);
            }
            var massIndex = Array.IndexOf(world.FloatStates, "TrainMass");
            var body = prefab.GetComponent<Rigidbody>();
            var originalMass = body.mass;
            world.OnFloatStateChanged[massIndex].Invoke(originalMass + 280);
            if (body.mass != originalMass + 280) throw new Exception("Native train mass callback failed.");
            world.OnFloatStateChanged[massIndex].Invoke(originalMass);
            var collisionsIndex = Array.IndexOf(world.States, "VehicleCollisionsEnabled");
            world.OnStateChangedEvents[collisionsIndex].Invoke(false);
            if (body.detectCollisions) throw new Exception("Explicit collision-state callback failed.");
            world.OnStateChangedEvents[collisionsIndex].Invoke(true);
            if (!body.detectCollisions) throw new Exception("Solid guided-vehicle collision callback failed.");
            Debug.Log("ECO_COUPLING_ASSETS_OK: both connector targets, visible link callbacks and native Rigidbody train-mass update.");
        }

        private static SpecificInteractable CheckHit(Vector3 from, Vector3 direction, string expected)
        {
            if (!Physics.Raycast(from, direction, out var hit, 3))
                throw new Exception("Wrong hit target: " + expected + " from " + from + "; got none");
            var target = hit.collider.GetComponent<SpecificInteractable>();
            if (target?.interactionTargetName != expected)
                throw new Exception("Wrong hit target: " + expected + " from " + from + "; got " + (hit.collider == null ? "none" : hit.collider.name));
            return target;
        }

        private static string BundlePath => Environment.GetEnvironmentVariable("ECO_MINECART_AUDIT_BUNDLE") ?? Path.GetFullPath("Build/EcoMinecarts.unity3d");

        private static void Render(GameObject cart)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.65f, .65f, .65f);
            var light = new GameObject("ProbeLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            var camera = new GameObject("ProbeCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .14f, .16f);
            camera.transform.position = new Vector3(2, 1.8f, 2.5f);
            camera.transform.LookAt(new Vector3(0, .4f, 0));
            camera.orthographic = true;
            camera.orthographicSize = 1.1f;
            var target = new RenderTexture(768, 768, 24);
            camera.targetTexture = target;
            var noCurve = Shader.IsKeywordEnabled("NO_CURVE");
            Shader.EnableKeyword("NO_CURVE");
            try { camera.Render(); }
            finally { if (!noCurve) Shader.DisableKeyword("NO_CURVE"); }
            RenderTexture.active = target;
            var texture = new Texture2D(768, 768, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.GetFullPath("../../validation/exported-cart-track.png"), texture.EncodeToPNG());
            Debug.Log("ECO_EXPORTED_BUNDLE_OK: serialized native mesh data and handle/bucket raycasts verified. Live Eco terrain rendering remains unverified.");
        }

        public static void InspectReference()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ValidationReference/OfficialGlassCube.fbx");
            if (model == null) throw new Exception("Missing official coordinate reference.");
            var instance = Object.Instantiate(model);
            Debug.Log("OFFICIAL_BLOCK_COORDINATES: " + MinecartIconBuilder.BoundsOf(instance));
            foreach (var f in instance.GetComponentsInChildren<MeshFilter>()) Debug.Log("OFFICIAL_BLOCK_MESH_LOCAL: " + f.sharedMesh.bounds + " matrix=" + f.transform.localToWorldMatrix);
            Object.DestroyImmediate(instance);
        }

        public static void BuildAndCheckReference()
        {
            InspectReference();
            MinecartAssetBuilder.BuildClientBundle();
        }

        private static void VerifyCurveWheelFootprint(Block[] blocks, string prefix)
        {
            Mesh Collider(string shape) => ((CustomBuilder)blocks.Single(b => b.Name == prefix + shape).Builder).usageCases[0].blockMeshLodGroup.Collider;
            var probes = new System.Collections.Generic.List<GameObject>();
            try
            {
                foreach (var entry in new[] {
                    ("Bend", Vector3.zero), ("Straight", new Vector3(0, 0, -1)), ("Straight90", new Vector3(1, 0, 0)) })
                {
                    var node = new GameObject("WheelFootprintProbe", typeof(MeshCollider)); probes.Add(node);
                    node.transform.position = entry.Item2;
                    node.GetComponent<MeshCollider>().sharedMesh = Collider(entry.Item1);
                }
                Physics.SyncTransforms();
                var length = Mathf.PI / 4;
                Vector3 Sample(float distance)
                {
                    if (distance < 0) return new Vector3(0, -.35f, -.5f + distance);
                    if (distance > length) return new Vector3(.5f + distance - length, -.35f, 0);
                    var angle = Mathf.PI - distance * 2;
                    return new Vector3(.5f + .5f * Mathf.Cos(angle), -.35f, -.5f + .5f * Mathf.Sin(angle));
                }
                for (var i = 0; i <= 40; i++)
                {
                    var distance = length * i / 40;
                    var rear = Sample(distance - .41f); var front = Sample(distance + .41f);
                    var body = (rear + front) * .5f; var forward = (front - rear).normalized;
                    var right = Vector3.Cross(Vector3.up, forward);
                    foreach (var axle in new[] { -1, 1 })
                    foreach (var side in new[] { -1, 1 })
                    {
                        var wheel = body + forward * (axle * .41f) + right * (side * .30f);
                        if (!probes.Any(p => TrackBlockAssetBuilder.HasSupport(p.GetComponent<MeshCollider>(), wheel)))
                            throw new Exception(prefix + " inner/outer wheel loses support across curve join: " + wheel);
                    }
                }
            }
            finally { foreach (var node in probes) Object.DestroyImmediate(node); }
            Debug.Log("ECO_CURVE_WHEEL_FOOTPRINT_OK: " + prefix + " four rigid wheel contact points supported across straight/bend/straight at 41 axle-chord poses.");
        }

        private static void VerifyDeckSupport(string name, Mesh mesh)
        {
            if (name.Contains("Stacked")) return;
            var shape = name.Substring(name.StartsWith("WoodenTrack") ? 11 : name.StartsWith("TramTrack") ? 9 : 13);
            var turn = 0;
            foreach (var degrees in new[] { 270, 180, 90 })
                if (shape.EndsWith(degrees.ToString(), StringComparison.Ordinal))
                { turn = degrees; shape = shape.Substring(0, shape.Length - degrees.ToString().Length); break; }
            if (shape == "BendLeft") { shape = "Bend"; turn += 90; }
            var overlay = shape.StartsWith("RampTop");
            var full = shape == "RampTop";
            var slope = overlay || shape.StartsWith("Slope");
            var phase = !slope || full ? 1 : int.Parse(shape.Substring(overlay ? 7 : 5));
            var low = (phase - 1) * .25f - (overlay ? 1 : 0);
            var rise = full ? 1f : slope ? .25f : 0;
            var rotation = Quaternion.Euler(0, turn, 0);
            var probe = new GameObject("ExportedDeckProbe", typeof(MeshCollider));
            try
            {
                var collider = probe.GetComponent<MeshCollider>(); collider.sharedMesh = mesh;
                for (var i = 1; i < 40; i++)
                foreach (var side in new[] { -1, 0, 1 })
                {
                    var t = i / 40f;
                    var angle = Mathf.PI - t * Mathf.PI / 2;
                    var point = shape == "Bend" ? new Vector3(.5f + (.5f + side * .30f) * Mathf.Cos(angle), .15f,
                        -.5f + (.5f + side * .30f) * Mathf.Sin(angle)) : new Vector3(side * .30f, low + rise * t + .15f, t - .5f);
                    point = rotation * (point - Vector3.up * .5f);
                    if (!TrackBlockAssetBuilder.HasSupport(collider, point))
                        throw new Exception("Exported collision deck has a center/wheel gap: " + name + " at " + point);
                }
            }
            finally { Object.DestroyImmediate(probe); }
        }

        private static void InspectInstalledBlocks()
        {
            var directory = @"G:\SteamLibrary\steamapps\common\Eco\Eco_Data\StreamingAssets\aa\StandaloneWindows64";
            var path = Directory.GetFiles(directory, "blocksetsandrubble_assets_all_*.bundle").Single();
            var vanilla = AssetBundle.LoadFromFile(path);
            if (vanilla == null) throw new Exception("Cannot inspect installed block bundle.");
            foreach (var mesh in vanilla.LoadAllAssets<Mesh>().Where(m => m.name.IndexOf("cube", StringComparison.OrdinalIgnoreCase) >= 0).Take(12))
                Debug.Log("INSTALLED_BLOCK_MESH: " + mesh.name + " bounds=" + mesh.bounds);
            foreach (var set in vanilla.LoadAllAssets<BlockSet>())
            foreach (var block in set.Blocks.Where(b => b != null && b.Name.IndexOf("GlassCube", StringComparison.OrdinalIgnoreCase) >= 0).Take(2))
            {
                Debug.Log("INSTALLED_BLOCK_BUILDER: " + block.Name + " type=" + block.Builder?.GetType().Name + " materials=" + block.Materials.Length);
                if (block.Builder is CustomBuilder builder)
                foreach (var usage in builder.usageCases)
                {
                    var group = usage.blockMeshLodGroup;
                    Debug.Log("INSTALLED_BLOCK_CASE: mesh=" + usage.mesh?.name + " lods=" + group?.name + " rotate=" + usage.applyConditionsToAllRotations + " noRotate=" + usage.dontRotateBaseMesh);
                    if (group != null) Debug.Log("INSTALLED_BLOCK_LOD: " + group.LOD0[0].mesh?.bounds);
                }
            }
            vanilla.Unload(true);
        }
    }
}
