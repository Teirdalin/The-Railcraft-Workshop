using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EcoMinecarts.Editor
{
    [InitializeOnLoad]
    public static class MinecartAssetBuilder
    {
        private const string Root = "Assets/EcoMinecarts";
        private const string Materials = Root + "/Materials";
        private const string Prefabs = Root + "/Prefabs";
        private const string ScenePath = Root + "/Scenes/EcoMinecarts.unity";
        private const string BundleName = "ecominecarts";

        private static readonly string[] ModelFiles =
        {
            "Minecart.fbx",
            "Rail_Straight.fbx",
            "Rail_Curve90_R3.fbx",
            "Rail_Slope_1x4.fbx",
            "Buffer_Stop.fbx"
        };

        static MinecartAssetBuilder()
        {
            EditorApplication.delayCall += AutoBuildFirstImport;
        }

        private static void AutoBuildFirstImport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                return;

            try
            {
                BuildClientContent();
            }
            catch (Exception ex)
            {
                Debug.LogError("ECO_MINECARTS_BUILD_FAILED\n" + ex);
            }
        }

        [MenuItem("Eco Tools/Minecarts/Rebuild Client Content")]
        public static void BuildClientContent()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            TmpEssentialsSetup.Ensure();
            ConfigureImports();
            var materials = CreateMaterials();

            var cartPrefab = CreateMinecartPrefab(materials);
            var trainPrefab = MineTrainAssetBuilder.Build(cartPrefab, materials);
            var worldPrefabs = new[]
            {
                cartPrefab,
                MinecartRuntimeAssets.CreateChainDrive(),
                trainPrefab,
            }.Concat(RailExpansionAssetBuilder.Build(cartPrefab, trainPrefab, materials)).Concat(new[]{RailcraftWorkbenchAssetBuilder.Build()}).ToArray();

            var blockSets = new[] { TrackBlockAssetBuilder.Build(), TrackBlockAssetBuilder.Build(true), TrackBlockAssetBuilder.Build(wooden: true), TrackBlockAssetBuilder.Build(tram:true),CoasterTerrainAssetBuilder.Build(materials),RailSupportAssetBuilder.Build() };
            // Finish the hand cart after cloning its chassis for locomotives.
            RailPullingAssetBuilder.RefreshBase();
            RailVisualFinish.Apply(worldPrefabs,materials);
            RailVehiclePaintBuilder.Apply(worldPrefabs);
            MinecartIconBuilder.Build(worldPrefabs);
            CreateBundleScene(worldPrefabs, blockSets);
            ValidateContent(worldPrefabs);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ECO_MINECARTS_BUILD_OK: cart, chain drive and mine locomotive prefabs, hammer blocks and bundle scene ready.");
        }

        [MenuItem("Eco Tools/Minecarts/Build Client Bundle")]
        public static void BuildClientBundle()
        {
            BuildClientContent();
            BuildSavedClientBundle();
        }
        public static void BuildSavedClientBundle()
        {
            RemoveRetiredCoasterEntries();
            ElectricalDriveAssetBuilder.Apply();
            ExportCurrentLibrary();
        }
        // Material-only refreshes preserve the already-reviewed library scene.
        public static void BuildAuthoredClientBundle() => ExportCurrentLibrary();
        public static void BuildOriginalVehiclesBundle()
        {OriginalVehicleReleaseBuilder.Apply();BuildLibraryBundle();}
        public static void BuildHandcarFitBundle()
        {
            RailVehicleDesignBuilder.RestoreModernAuthoringFits();MeshyVehicleLibraryBuilder.ApplyHandcarFit();
            RailVehicleDesignBuilder.Apply();BuildLibraryBundle();
        }
        public static void BuildCoasterSeatFitBundle()
        {
            RailVehicleDesignBuilder.RestoreModernAuthoringFits();
            var path=Root+"/Prefabs/MinecartObject.prefab";var cart=PrefabUtility.LoadPrefabContents(path);
            try{
                RailPullingAssetBuilder.Configure(cart,280,2500);RailVehiclePhysicsAssetBuilder.Configure(cart);
                PrefabUtility.SaveAsPrefabAsset(cart,path);
            }finally{PrefabUtility.UnloadPrefabContents(cart);}
            MeshyVehicleLibraryBuilder.ApplyCoasterSeatFit();
            MeshyInfrastructureAssetBuilder.RepairPresentation();RailVehicleDesignBuilder.Apply();BuildLibraryBundle();
        }
        public static void AddDumpRailAndBuild()
        {
            var materials=CreateMaterials();
            var prefab=RailExpansionAssetBuilder.DumpRail(materials);
            MinecartIconBuilder.Render(prefab,"MinecartDumpRail");
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            var container=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ModkitPrefabContainer>(true)).Single();
            container.Prefabs=container.Prefabs.Where(p=>p!=null&&p.name!=prefab.name).Concat(new[]{prefab}).ToArray();
            var items=scene.GetRootGameObjects().Single(r=>r.name=="Items").transform;
            var old=items.Find("MinecartDumpRailItem");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            CreateItem(items,items.GetChild(0).gameObject,"MinecartDumpRailItem",AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Icons/MinecartDumpRail.png"));
            EditorUtility.SetDirty(container);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            BuildSavedClientBundle();
            Debug.Log("DUMP_RAIL_ASSETS_OK: dumping rail prefab/icon added; fourteen snap volumes anchored at rail-contact pivots.");
        }
        public static void NormalizeIconScaleAndBuildBundle()
        {
            foreach(var guid in AssetDatabase.FindAssets("t:Texture2D",new[]{Root+"/Icons"})){
                var path=AssetDatabase.GUIDToAssetPath(guid);
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null||texture==null||importer.textureType!=TextureImporterType.Sprite)continue;
                var ppu=texture.width*150f/128f;
                if(Mathf.Abs(importer.spritePixelsPerUnit-ppu)<.001f)continue;
                importer.spritePixelsPerUnit=ppu;importer.SaveAndReimport();
            }
            BuildSavedClientBundle();
            Debug.Log("ECO_ICON_SCALE_OK: native 128px/150PPU sprite dimensions retained at source resolution");
        }
        private static void ExportCurrentLibrary()
        {
            RailVehicleDesignBuilder.RestoreModernAuthoringFits();
            RailWheelAnimationBuilder.Apply();
            MeshyVehicleLibraryBuilder.Apply();
            CoasterBlueprintAssetBuilder.BuildAssetsAndRegister();
            RailVehicleTextBuilder.Apply();
            RailVehiclePlacementBuilder.Apply();
            RailSupportAssetBuilder.RefreshClimbing();
            MeshyInfrastructureAssetBuilder.Apply();
            RailWorldMaterialBuilder.NormalizeMaterials();
            RailBlockDistanceAppearance.Apply();
            RailVehicleDesignBuilder.Apply();
            BuildLibraryBundle();
        }
        public static void BuildDesignBundle()
        {
            MeshyInfrastructureAssetBuilder.RepairPresentation();
            RailVehicleDesignBuilder.Apply();
            RailVehicleDesignProbe.Verify(AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"}).Select(g=>AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).ToArray());
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"})){
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if(prefab.GetComponent<Vehicle>()!=null)MinecartIconBuilder.Render(prefab,prefab.name.Substring(0,prefab.name.Length-6));
            }
            BuildLibraryBundle();
        }
        public static void BuildPresentationBundle()
        {
            MeshyInfrastructureAssetBuilder.RepairPresentation();
            BuildLibraryBundle();
        }
        private static void BuildLibraryBundle()
        {
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            RegisterComponentIcons(scene.GetRootGameObjects().Single(n=>n.name=="Items").transform);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            ConfigureBundleDependencies();
            PlayerSettings.stripUnusedMeshComponents = false;

            // The root manifest is named after this folder. Do not name the folder
            // EcoMinecarts or Unity will collide it with the ecominecarts bundle.
            var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../AssetBundles/Windows"));
            Directory.CreateDirectory(outputDirectory);
            var manifest = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                BuildAssetBundleOptions.ChunkBasedCompression,
                EditorUserBuildSettings.activeBuildTarget);

            if (manifest == null || !manifest.GetAllAssetBundles().Contains(BundleName))
                throw new InvalidOperationException("Unity did not produce the " + BundleName + " asset bundle.");

            var buildDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Build"));
            Directory.CreateDirectory(buildDirectory);
            var destination = Path.Combine(buildDirectory, "EcoMinecarts.unity3d");
            File.Copy(Path.Combine(outputDirectory, BundleName), destination, true);
            Debug.Log("ECO_MINECARTS_BUNDLE_OK: " + destination);
        }

        public static void RefreshVehicleFinish()
        {
            // Read the existing library's prefab references without opening the
            // large scene or regenerating unrelated track/placement assets.
            var text=File.ReadAllText(ScenePath);
            var list=System.Text.RegularExpressions.Regex.Match(text,@"(?m)^  Prefabs:\r?\n((?:  - \{[^\r\n]+\}\r?\n)+)");
            if(!list.Success)throw new InvalidOperationException("Missing saved prefab library");
            var vehicles=System.Text.RegularExpressions.Regex.Matches(list.Groups[1].Value,@"guid: ([a-f0-9]+)")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m=>AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(m.Groups[1].Value)))
                .Where(p=>p!=null&&p.GetComponent<RCCCarControllerV2>()!=null).ToArray();
            if(vehicles.Length!=14)throw new InvalidOperationException("Expected fourteen paintable vehicles; got "+vehicles.Length);
            var prefabs=vehicles.Where(RailVehicleDetail.Handles).ToArray();
            if(prefabs.Length!=11)throw new InvalidOperationException("Expected eleven train, tram and coaster models; got "+prefabs.Length);
            RailVisualFinish.Apply(prefabs,CreateMaterials());
            // Shared base materials are regenerated above; refresh all vehicles,
            // including the handcar and basic carts, before exporting them.
            RailVehiclePaintBuilder.Apply(vehicles);
            foreach(var prefab in vehicles)MinecartIconBuilder.Render(prefab,prefab.name.Substring(0,prefab.name.Length-6));
            AssetDatabase.SaveAssets();ExportCurrentLibrary();
        }

        public static void RefreshSurfaceFinish()
        {
            var materials=CreateMaterials();
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            var prefabs=scene.GetRootGameObjects().Single(n=>n.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs;
            RailVisualFinish.Apply(prefabs,materials);
            RailVehiclePaintBuilder.Apply(prefabs);
            MinecartIconBuilder.Build(prefabs);
            foreach(var section in RailExpansionAssetBuilder.ReadCatalog().CoasterTerrain)
                if(section.SourceKey!="CoasterStraight"&&section.SourceKey!="CoasterChainStraight"||section.Section==1)
                    MinecartIconBuilder.Render(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/CoasterBlocks/"+section.Key+"Block.prefab"),section.Key);
            AssetDatabase.SaveAssets();BuildSavedClientBundle();
        }

        private static void ConfigureImports()
        {
            foreach (var file in ModelFiles)
            {
                var path = Root + "/Models/" + file;
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                    throw new FileNotFoundException("Model was not imported by Unity.", path);

                importer.globalScale = 1f;
                importer.useFileScale = true;
                importer.importAnimation = false;
                importer.importBlendShapes = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.meshCompression = ModelImporterMeshCompression.Low;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = path.EndsWith("_BaseColor.png", StringComparison.OrdinalIgnoreCase);
                importer.maxTextureSize = 1024;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.SaveAndReimport();
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Icons" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 300;
                importer.sRGBTexture = true;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, Material> CreateMaterials()
        {
            var shader = Shader.Find("Curved/Standard");
            if (shader == null)
                throw new InvalidOperationException("Eco ModKit Curved/Standard shader is missing. This project must not use URP/HDRP.");

            return new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase)
            {
                // Eco's installed carts leave scalar metallic at zero and opt in
                // to metallic only through a packed map.  These assets currently
                // have albedo + ORM source maps, not Unity Standard-packed maps;
                // forcing .75-.90 metallic discarded nearly all diffuse light and
                // made the textures look black in game. The icon renderer has a
                // bright key and .65 ambient light; Eco's shaded forest does not.
                // These stronger, deliberately colored diffuse tints make the same
                // authored albedos readable in the world without adding emission.
                // The source painted texture is blue-green (average RGB 33/50/55).
                // A warm-neutral multiplier preserves its wear/detail while bringing
                // the final cart back to the brief's dark charcoal option.
                ["MAT_IronPainted"] = CreateMaterial("MAT_IronPainted", shader, 0.05f, 0.28f, new Color(2.80f, 2.10f, 1.90f, 1)),
                ["MAT_IronBare"] = CreateMaterial("MAT_IronBare", shader, 0.08f, 0.30f, new Color(2.25f, 2.20f, 2.15f, 1)),
                ["MAT_RustWear"] = CreateMaterial("MAT_RustWear", shader, 0.0f, 0.15f, new Color(1.65f, 1.35f, 1.10f, 1)),
                ["MAT_WoodRail"] = CreateMaterial("MAT_WoodRail", shader, 0.0f, 0.12f, new Color(1.22f, 1.28f, 1.24f, 1)),
                ["MAT_SwitchIndicator"] = CreateMaterial("MAT_SwitchIndicator", shader, 0.0f, 0.18f, new Color(0.10f, 4.00f, 0.10f, 1)),
                ["MAT_Ore"] = CreateMaterial("MAT_Ore", shader, 0.0f, 0.18f, new Color(1.55f, 1.55f, 1.55f, 1))
            };
        }

        private static Material CreateMaterial(string name, Shader shader, float metallic, float smoothness, Color tint)
        {
            var path = Materials + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            var texturePath=Root + "/Textures/" + name + "_BaseColor.png";
            if (!File.Exists(texturePath)) texturePath=Root + "/Textures/MAT_IronBare_BaseColor.png";
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            if(name=="MAT_IronBare"||name=="MAT_IronPainted"||name=="MAT_RustWear")material.SetTexture("_MainTex",SubtleMetalSurface(name));
            // Broader grain and subtle metal wear read at game scale without
            // the former high-frequency checker/striping on every primitive.
            material.SetTextureScale("_MainTex",name=="MAT_WoodRail"?new Vector2(.28f,.42f):new Vector2(.30f,.30f));
            material.SetColor("_Color", tint);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            // ORM is occlusion/roughness/metallic. It cannot be connected directly
            // to Standard's metallic(R)/smoothness(A) slot without repacking.
            material.SetTexture("_MetallicGlossMap", null);
            material.DisableKeyword("_METALLICGLOSSMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D SubtleMetalSurface(string name)
        {
            // Replace intersecting sine-wave bands (a visible checker pattern)
            // with quiet, irregular rolled-metal variation. Preserve the source
            // albedo average and native diffuse response in forest lighting.
            var baseColor=name=="MAT_IronBare"?new Color(56.31f,62.43f,66.10f):name=="MAT_IronPainted"?new Color(33.05f,50.19f,55.08f):new Color(61.19f,28.16f,13.46f);
            baseColor/=255f;baseColor.a=1;
            const int size=128;var pixels=new Color[size*size];
            for(var y=0;y<size;y++)for(var x=0;x<size;x++){
                var noise=(Mathf.PerlinNoise(13+x*.039f,29+y*.039f)-.5f)*.022f+(Mathf.PerlinNoise(5+x*.19f,19+y*.19f)-.5f)*.006f;
                var color=baseColor*(1+noise);color.a=1;pixels[y*size+x]=color;
            }
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,true,false){name=name+"Surface",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
            texture.SetPixels(pixels);texture.Apply(true,false);
            var path=Materials+"/"+name+"Surface.asset";var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(existing==null){AssetDatabase.CreateAsset(texture,path);return texture;}
            EditorUtility.CopySerialized(texture,existing);UnityEngine.Object.DestroyImmediate(texture);EditorUtility.SetDirty(existing);return existing;
        }

        internal static GameObject CreateMinecartPrefab(IReadOnlyDictionary<string, Material> materials,string destination=null)
        {
            var root = CreateWorldRoot("MinecartObject", "Minecart.fbx", materials);
            var world = root.GetComponent<WorldObject>();
            world.hasOccupancy = true;
            world.overrideOccupancy = true;
            world.size = new Vector3(1f, 1f, 2f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.mass = 280f;
            body.linearDamping = 0.02f;
            body.angularDamping = 0.05f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.maxAngularVelocity = 6f;
            body.automaticCenterOfMass = false;
            body.centerOfMass = new Vector3(0f, 0.24f, 0f);

            var sync = root.AddComponent<SyncPhysics>();
            sync.SyncPos = true;
            // Rail guidance already publishes the tangent velocity in every
            // keyframe. Distant kinematic clients need it to extrapolate between
            // poses; position-only smoothing visibly trails a fast car above or
            // beside its rail on slopes and bends.
            sync.SyncVelocity = true;
            sync.UseBaseVelocity = false;
            sync.LimitVelocity = false;
            sync.VelocityLimit = 12f;
            sync.SyncRot = true;
            sync.RotSyncMode = AxisSyncMode.AxisXYZ;
            sync.ManuallyUpdated = false;
            sync.distanceToIgnorePhysics = 25f;
            sync.snapDistance = 5f;
            sync.maxDepenetationVelocity = 0.2f;
            // Poses are produced by the server rail/ground solver. The generic
            // rubble depenetrator treated thin rails as a full terrain collision
            // and visibly teleported the cart during the live test.
            sync.AutomaticChunkDepenetrationEnabled = false;
            MinecartRuntimeAssets.AttachSound(root);

            var bucket = AddBox(root.transform, "COL_Minecart_Body", new Vector3(0f, 0.43f, 0f), new Vector3(0.76f, 0.56f, 1.20f));
            bucket.gameObject.AddComponent<SpecificInteractable>().interactionTargetName = "MinecartStorage";
            // Both modeled end handles sit outside the bucket collider. Give each
            // a forgiving, separate hit target, not a cart-wide grab interaction.
            foreach (var end in new[] { -1, 1 })
            {
                var handle = AddBox(root.transform, "COL_GrabHandle_" + end,
                    new Vector3(0, .55f, end * .72f), new Vector3(.62f, .38f, .25f));
                var target = handle.gameObject.AddComponent<SpecificInteractable>();
                target.interactionTargetName = "MinecartHandle";
                // The server uses this to preserve the exact end the player took.
                target.interactionTargetValue = end.ToString();
            }
            AddBox(root.transform, "COL_Minecart_Underframe", new Vector3(0f, 0.14f, 0f), new Vector3(0.64f, 0.18f, 0.92f));
            // Rail capture is a server constraint. The former low guide box
            // penetrated the continuous deck by 2 cm, resisting native pulling.

            AddWheel(root.transform, "FL", new Vector3(-0.30f, 0.15f, 0.41f));
            AddWheel(root.transform, "FR", new Vector3(0.30f, 0.15f, 0.41f));
            AddWheel(root.transform, "RL", new Vector3(-0.30f, 0.15f, -0.41f));
            AddWheel(root.transform, "RR", new Vector3(0.30f, 0.15f, -0.41f));

            var controller = root.AddComponent<RCCCarControllerV2>();
            var wheels = root.GetComponentsInChildren<WheelCollider>();
            controller.FrontLeftWheelCollider = wheels.Single(w => w.name == "WheelCollider_FL");
            controller.FrontRightWheelCollider = wheels.Single(w => w.name == "WheelCollider_FR");
            controller.RearLeftWheelCollider = wheels.Single(w => w.name == "WheelCollider_RL");
            controller.RearRightWheelCollider = wheels.Single(w => w.name == "WheelCollider_RR");
            controller.allWheelColliders = wheels;
            // Separate pose nodes keep the imported wheel mesh transforms intact.
            Transform WheelPose(string suffix, WheelCollider wheel)
            {
                var node = new GameObject("WheelPose_" + suffix).transform;
                node.SetParent(root.transform, false);
                node.localPosition = wheel.transform.localPosition;
                return node;
            }
            controller.FrontLeftWheelTransform = WheelPose("FL", controller.FrontLeftWheelCollider);
            controller.FrontRightWheelTransform = WheelPose("FR", controller.FrontRightWheelCollider);
            controller.RearLeftWheelTransform = WheelPose("RL", controller.RearLeftWheelCollider);
            controller.RearRightWheelTransform = WheelPose("RR", controller.RearRightWheelCollider);
            controller.footPoweredCart = true;
            // The native walking controller supplies the input, but the stock
            // cart torque is calibrated for level ground. Keep the same walking
            // speed ceiling while giving a loaded minecart enough wheel torque
            // to start and continue on a rail ramp in either direction.
            controller.engineTorque = 14000f;
            // Engine indexes both arrays by currentGear. Native carts serialize
            // a populated one-gear setup even with auto-generation enabled.
            controller.totalGears = 1;
            controller.currentGear = 0;
            controller.gearSpeed = new[] { 20f };
            controller.engineTorqueCurve = new[] { new AnimationCurve(
                new Keyframe(0, 1, -.0125f, -.0125f),
                new Keyframe(20, .75f, -.04375f, -.04375f),
                new Keyframe(30, 0, -.075f, -.075f)) };
            controller.autoBrakeWhenNotUsingGas = false;
            controller.UseTerrainSplatMapForGroundPhysics = false;
            controller.maxspeed = 12;
            root.AddComponent<LimitVelocity>();

            var seatNode = new GameObject("BucketPassengerSeat");
            seatNode.transform.SetParent(root.transform, false);
            seatNode.transform.localPosition = new Vector3(0, .08f, 0);
            var seat = seatNode.AddComponent<MountSpot>();
            // The server selects one of two native passenger orientations from
            // the entering player's look direction; never rotate the cart.
            Transform SeatAnchor(string name, Vector3 position)
            {
                var anchor = new GameObject(name).transform;
                anchor.SetParent(root.transform, false);
                anchor.localPosition = position;
                return anchor;
            }
            seat.exitPosition = SeatAnchor("PassengerExitRight", new Vector3(1.2f, .05f, 0));
            seat.alternativeExitPosition = SeatAnchor("PassengerExitLeft", new Vector3(-1.2f, .05f, 0));
            seat.cameraTarget = SeatAnchor("PassengerCamera", new Vector3(0, 1.2f, 0));
            var reverseSeatNode = new GameObject("BucketPassengerSeatReverse");
            reverseSeatNode.transform.SetParent(root.transform, false);
            reverseSeatNode.transform.localPosition = seatNode.transform.localPosition;
            reverseSeatNode.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var reverseSeat = reverseSeatNode.AddComponent<MountSpot>();
            reverseSeat.exitPosition = seat.alternativeExitPosition;
            reverseSeat.alternativeExitPosition = seat.exitPosition;
            reverseSeat.cameraTarget = seat.cameraTarget;
            var driverNode = new GameObject("NativePullMount");
            driverNode.transform.SetParent(root.transform, false);
            // Minecart model origin is its wheel contact plane, not the vanilla
            // wooden cart's pivot. The occupant's feet belong on that plane.
            driverNode.transform.localPosition = new Vector3(0, 0, 1.15f);
            var driver = driverNode.AddComponent<MountSpotPulled>();
            driver.setAsParent = true;
            driver.lockRotation = false;
            // Vanilla MountSpotPulled leaves the avatar state unforced (255).
            // Its hand solver owns the grip; a full-body Pulling override can
            // conflict with the locomotion/IK presentation.
            driver.overrideAvatarState = (Eco.Animation.AnimationStateManager.AvatarState)255;
            driver.ApplyHandsGripOnMount = true;
            driver.exitPosition = SeatAnchor("PullExit", new Vector3(0, .05f, 1.5f));
            driver.alternativeExitPosition = SeatAnchor("PullExitSide", new Vector3(1.2f, .05f, 1.15f));
            driver.CenterOfMassWithoutOccupancy = SeatAnchor("UnoccupiedCOM", body.centerOfMass);
            driver.CenterOfMassOnOccupancy = SeatAnchor("OccupiedCOM", body.centerOfMass);
            controller.COM = driver.CenterOfMassOnOccupancy;
            driver.WheelCollider = new GameObject("PullWheelGroup");
            driver.WheelCollider.transform.SetParent(root.transform, false);
            controller.FrontLeftWheelCollider.transform.SetParent(driver.WheelCollider.transform, true);
            controller.FrontRightWheelCollider.transform.SetParent(driver.WheelCollider.transform, true);
            RootMotion.FinalIK.InteractionTarget HandTarget(string name, float x, int effector, Quaternion rotation)
            {
                // build_models.py: grab crossbar x=-.13..+.13, y=.60, z=.674.
                var node = SeatAnchor(name, new Vector3(x, .60f, .674f));
                node.localRotation = rotation;
                var hand = node.gameObject.AddComponent<RootMotion.FinalIK.InteractionTarget>();
                hand.effectorType = effector;
                return hand;
            }
            driver.lHand = HandTarget("GripLeft", -.09f, 5, new Quaternion(-.49313f, -.27861f, .34491f, .74849f));
            driver.rHand = HandTarget("GripRight", .09f, 6, new Quaternion(.31578f, .51886f, .73621f, .29844f));
            // The occupied collider belongs to the cart rigidbody. Its bottom
            // must not extend below the running surface and lift the whole rig.
            var occupied = AddBox(root.transform, "PullPlayerCollider", new Vector3(0, .90f, 1.15f), new Vector3(.5f, 1.7f, .5f));
            occupied.enabled = false;
            driver.occupiedCollider = occupied;
            var vehicle = root.GetComponent<Vehicle>();
            // Native VehicleBase dereferences these during local boarding and
            // exit. A mount-only prefab attaches the avatar, then throws before
            // entering driving mode or completing the dismount RPC.
            root.AddComponent<Eco.Client.InteractionBlocker>();
            root.AddComponent<WaitForGround>();
            var moveSounds = root.AddComponent<MoveThroughSounds>();
            moveSounds.OverlapCheck = bucket;
            vehicle.frontWheels = driver.WheelCollider;
            vehicle.AllVehicleColliders = root.GetComponentsInChildren<Collider>();
            var mount = root.AddComponent<Mountable>();
            mount.seats = new MountSpot[] { driver, seat, reverseSeat };
            mount.handleDismount = true;
            mount.blockDismountWhenNoExit = true;
            mount.freezeWhenUnmounted = false;

            // Seat zero is the driver. Seats one/two are mutually exclusive
            // orientations of ONE bucket passenger, enforced by the server.

            MinecartRuntimeAssets.AttachCargo(root);
            MinecartRuntimeAssets.AttachCouplers(root, .806f);
            vehicle.AllVehicleColliders = root.GetComponentsInChildren<Collider>();
            RailRiderInteractionAssetBuilder.Configure(root);
            return SavePrefab(root, "MinecartObject",destination);
        }

        private enum TrackKind { Straight, Curve, Slope, Buffer }

        private static GameObject CreateTrackPrefab(string objectName, string modelFile, TrackKind kind, IReadOnlyDictionary<string, Material> materials)
        {
            var root = CreateWorldRoot(objectName, modelFile, materials);
            var world = root.GetComponent<WorldObject>();
            world.hasOccupancy = true;
            world.overrideOccupancy = true;
            world.size = kind switch
            {
                TrackKind.Curve => new Vector3(4f, 1f, 4f),
                TrackKind.Slope => new Vector3(1f, 2f, 4f),
                _ => new Vector3(1f, 1f, 1f)
            };

            switch (kind)
            {
                case TrackKind.Straight:
                    AddStraightRailColliders(root.transform, 0f, 1f);
                    break;
                case TrackKind.Curve:
                    AddCurveRailColliders(root.transform, 24);
                    break;
                case TrackKind.Slope:
                    AddSlopeRailColliders(root.transform);
                    break;
                case TrackKind.Buffer:
                    AddStraightRailColliders(root.transform, 0f, 1f);
                    AddBox(root.transform, "COL_BufferImpact", new Vector3(0f, 0.255f, 0.545f), new Vector3(0.82f, 0.30f, 0.12f));
                    break;
            }

            return SavePrefab(root, objectName);
        }

        private static GameObject CreateWorldRoot(string objectName, string modelFile, IReadOnlyDictionary<string, Material> materials)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/" + modelFile);
            if (model == null)
                throw new FileNotFoundException("Missing imported model " + modelFile);

            var root = new GameObject(objectName);
            root.tag = "ModObject";
            if (objectName == "MinecartObject") root.AddComponent<Vehicle>();
            else root.AddComponent<WorldObject>();
            root.AddComponent<HighlightableObject>();

            var visual = UnityEngine.Object.Instantiate(model, root.transform);
            visual.name = Path.GetFileNameWithoutExtension(modelFile) + "_Visual";
            // FBX import supplies the unit/axis conversion on its root transform.
            // Resetting rotation/scale makes this cart 100x too small and sideways.
            visual.transform.localPosition = Vector3.zero;
            ApplyMaterials(visual, materials);
            return root;
        }

        private static void ApplyMaterials(GameObject visual, IReadOnlyDictionary<string, Material> materials)
        {
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                var originals = renderer.sharedMaterials;
                var replacements = new Material[originals.Length];
                for (var index = 0; index < replacements.Length; index++)
                    replacements[index] = ChooseMaterial(renderer.name, originals[index] != null ? originals[index].name : string.Empty, materials);
                renderer.sharedMaterials = replacements;
            }
        }

        private static Material ChooseMaterial(string objectName, string materialName, IReadOnlyDictionary<string, Material> materials)
        {
            foreach (var pair in materials)
                if (materialName.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;

            if (objectName.IndexOf("Tie", StringComparison.OrdinalIgnoreCase) >= 0) return materials["MAT_WoodRail"];
            if (objectName.IndexOf("Cargo", StringComparison.OrdinalIgnoreCase) >= 0) return materials["MAT_Ore"];
            if (objectName.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0) return materials["MAT_IronPainted"];
            return materials["MAT_IronBare"];
        }

        private static void AddWheel(Transform parent, string suffix, Vector3 position)
        {
            var wheelObject = new GameObject("WheelCollider_" + suffix);
            wheelObject.transform.SetParent(parent, false);
            wheelObject.transform.localPosition = position;
            var wheel = wheelObject.AddComponent<WheelCollider>();
            wheel.radius = 0.15f;
            wheel.mass = 12f;
            wheel.suspensionDistance = 0.025f;
            wheel.forceAppPointDistance = 0.05f;
            wheel.suspensionSpring = new JointSpring { spring = 35000f, damper = 4500f, targetPosition = 0.5f };
            wheel.forwardFriction = new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.75f, stiffness = 1.5f };
            wheel.sidewaysFriction = new WheelFrictionCurve { extremumSlip = 0.2f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f, stiffness = 2.25f };
        }

        private static void AddStraightRailColliders(Transform parent, float startZ, float length)
        {
            AddBox(parent, "COL_Rail_Left", new Vector3(-0.30f, -0.0375f, startZ + length * 0.5f), new Vector3(0.055f, 0.075f, length));
            AddBox(parent, "COL_Rail_Right", new Vector3(0.30f, -0.0375f, startZ + length * 0.5f), new Vector3(0.055f, 0.075f, length));
        }

        private static void AddSlopeRailColliders(Transform parent)
        {
            var direction = new Vector3(0f, 1f, 4f);
            var length = direction.magnitude;
            var rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            AddBox(parent, "COL_Rail_Left", new Vector3(-0.30f, 0.5f, 2f), new Vector3(0.055f, 0.075f, length), rotation);
            AddBox(parent, "COL_Rail_Right", new Vector3(0.30f, 0.5f, 2f), new Vector3(0.055f, 0.075f, length), rotation);
        }

        private static void AddCurveRailColliders(Transform parent, int segments)
        {
            const float centerRadius = 3f;
            const float halfGauge = 0.30f;
            for (var side = -1; side <= 1; side += 2)
            {
                var radius = centerRadius + side * halfGauge;
                for (var index = 0; index < segments; index++)
                {
                    var t0 = Mathf.PI - index * (Mathf.PI * 0.5f / segments);
                    var t1 = Mathf.PI - (index + 1) * (Mathf.PI * 0.5f / segments);
                    var p0 = new Vector3(3f + radius * Mathf.Cos(t0), -0.0375f, radius * Mathf.Sin(t0));
                    var p1 = new Vector3(3f + radius * Mathf.Cos(t1), -0.0375f, radius * Mathf.Sin(t1));
                    var delta = p1 - p0;
                    var name = "COL_Curve_" + (side < 0 ? "Inner_" : "Outer_") + index.ToString("00");
                    AddBox(parent, name, (p0 + p1) * 0.5f, new Vector3(0.055f, 0.075f, delta.magnitude + 0.006f), Quaternion.LookRotation(delta.normalized, Vector3.up));
                }
            }
        }

        private static BoxCollider AddBox(Transform parent, string name, Vector3 center, Vector3 size, Quaternion? rotation = null)
        {
            var colliderObject = new GameObject(name);
            colliderObject.transform.SetParent(parent, false);
            colliderObject.transform.localPosition = center;
            colliderObject.transform.localRotation = rotation ?? Quaternion.identity;
            var collider = colliderObject.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private static void AddOptionalComponent(GameObject target, string typeName)
        {
            Type found = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    found = assembly.GetTypes().FirstOrDefault(type => type.Name == typeName && typeof(Component).IsAssignableFrom(type));
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                    // Some optional Unity packages cannot enumerate all types in the editor.
                }
                if (found != null) break;
            }

            if (found != null && target.GetComponent(found) == null)
                target.AddComponent(found);
            else if (found == null)
                Debug.Log("Eco Minecarts: optional client component is not exposed by this ModKit: " + typeName);
        }

        private static GameObject SavePrefab(GameObject root, string name,string destination=null)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, destination??Prefabs + "/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new InvalidOperationException("Failed to save prefab " + name);
            return prefab;
        }

        private static void RemoveRetiredCoasterEntries()
        {
            // Recreate only the library scene from its saved asset references. Do
            // not load the retired entity library into the editor first: it is
            // unnecessary for retirement and can stall large legacy scenes.
            var text=File.ReadAllText(ScenePath);
            string[] Paths(string field)
            {
                var match=System.Text.RegularExpressions.Regex.Match(text,@"(?m)^  "+field+@":\r?\n((?:  - \{[^\r\n]+\}\r?\n)+)");
                if(!match.Success)throw new InvalidOperationException("Missing saved library references: "+field);
                return System.Text.RegularExpressions.Regex.Matches(match.Groups[1].Value,@"guid: ([a-f0-9]+)")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m=>AssetDatabase.GUIDToAssetPath(m.Groups[1].Value)).ToArray();
            }
            var retired=RailExpansionAssetBuilder.ReadCatalog().Coasters.Select(p=>p.Key+"Object").ToArray();
            Debug.Log("RAIL_RETIRE_REFERENCES_OK");
            GameObject LoadPrefab(string path)
            {
                Debug.Log("RAIL_RETIRE_LOAD: "+path);
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            var materials=CreateMaterials();
            var coasterPrefabs=CoasterAssetBuilder.Build(RailExpansionAssetBuilder.ReadCatalog().Coasters,materials).ToArray();
            foreach(var prefab in coasterPrefabs)MinecartIconBuilder.Render(prefab,prefab.name.Replace("Object",""));
            var prefabs=Paths("Prefabs").Where(p=>!Path.GetFileNameWithoutExtension(p).StartsWith("Coaster")&&Path.GetFileNameWithoutExtension(p)!="RailcraftWorkbenchObject")
                .Select(LoadPrefab).Concat(coasterPrefabs).Concat(new[]{RailcraftWorkbenchAssetBuilder.Build()}).ToArray();
            Debug.Log("RAIL_RETIRE_PREFABS_OK");
            var sets=Paths("blockSets").Select(p=>AssetDatabase.LoadAssetAtPath<BlockSet>(p)).Where(s=>s!=null&&s.name!="RailSupports"&&s.name!="CoasterTrack"&&s.name!="MinecartChain").Concat(new[]{RailSupportAssetBuilder.Build(),CoasterTerrainAssetBuilder.Build(materials),TrackBlockAssetBuilder.Build(chain:true)}).ToArray();
            Debug.Log("RAIL_RETIRE_BLOCKSETS_OK");
            if(prefabs.Any(p=>p==null)||sets.Any(s=>s==null))throw new InvalidOperationException("Missing source library asset");
            CreateBundleScene(prefabs,sets);
            Debug.Log("RAIL_RETIRE_SAVED_OK: "+prefabs.Length+" current object prefabs; existing geometry and icons reused.");
        }
        private static void CreateBundleScene(IReadOnlyList<GameObject> worldPrefabs, BlockSet[] blockSets)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var objects = new GameObject("Objects");
            objects.AddComponent<ModkitPrefabContainer>().Prefabs = worldPrefabs
                .Where(p => !p.name.StartsWith("RailChain") || !p.name.EndsWith("Indicator"))
                .ToArray(); // Recovery release: omit unverified transient arrow entities.

            var items = new GameObject("Items", typeof(RectTransform), typeof(Canvas));
            var itemTemplate = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoModKit/Prefabs/DefaultItem.prefab");
            if (itemTemplate == null) throw new FileNotFoundException("Eco ModKit DefaultItem prefab is missing.");
            var cartIcon = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/Minecart.png");
            var railIcon = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/MinecartTrackStraight.png");
            CreateItem(items.transform, itemTemplate, "MinecartItem", cartIcon);
            CreateItem(items.transform,itemTemplate,"RailcraftWorkbenchItem",AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Icons/RailcraftWorkbench.png"));
            CreateItem(items.transform, itemTemplate, "MineTrainItem", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/MineTrain.png"));
            CreateItem(items.transform, itemTemplate, "MinecartTrackItem", railIcon);
            CreateItem(items.transform, itemTemplate, "TramTrackItem", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/TramTrackStraight.png"));
            CreateItem(items.transform, itemTemplate, "MinecartChainItem", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/MinecartChainStraight.png"));
            CreateItem(items.transform, itemTemplate, "MinecartChainDriveItem", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/ChainDrive.png"));
            RegisterComponentIcons(items.transform);
            foreach (var prefab in worldPrefabs.Where(p => p.name != "MinecartObject" && p.name != "MineTrainObject" && p.name != "MinecartChainDriveObject"))
            {
                var key = prefab.name.Substring(0, prefab.name.Length - 6);
                CreateItem(items.transform, itemTemplate, key + "Item", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/" + key + ".png"));
            }
            CreateItem(items.transform, itemTemplate, "WoodenTrackItem", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/WoodenTrackStraight.png"));
            foreach(var section in RailExpansionAssetBuilder.ReadCatalog().CoasterTerrain)
                if(section.SourceKey!="CoasterStraight"&&section.SourceKey!="CoasterChainStraight"||section.Section==1)
                    CreateItem(items.transform,itemTemplate,section.Key+"Form",AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Icons/"+section.Key+".png"));
            CreateItem(items.transform,itemTemplate,"CoasterTrackItem",AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Icons/CoasterTrackStraightSection01.png"));
            foreach(var tier in new[]{"Wood","Iron","Steel"}){
                var key="RailSupport"+tier;
                CreateItem(items.transform,itemTemplate,key+"Item",AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Icons/"+key+"Middle.png"));
                CreateItem(items.transform,itemTemplate,key+"GroupFormGroup",AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Icons/"+key+"Top.png"));
                foreach(var part in new[]{"Base","Middle","Top"})CreateItem(items.transform,itemTemplate,key+part+"Form",AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Icons/"+key+part+".png"));
            }
            foreach(var group in RailExpansionAssetBuilder.ReadCatalog().CoasterTerrain.GroupBy(s=>s.MenuGroup))
                CreateItem(items.transform,itemTemplate,"CoasterTrack"+group.Key.Substring(7)+"GroupFormGroup",AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Icons/"+group.First().Key+".png"));
            foreach (var shape in new[] { "Straight", "Bend", "Stopper", "Slope1", "Slope2", "Slope3", "Slope4", "RampTop", "RampTop1", "RampTop2", "RampTop3", "RampTop4" })
                CreateItem(items.transform, itemTemplate, "WoodenTrack" + shape + "Form", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/WoodenTrack" + shape + ".png"));
            foreach (var shape in new[] { "Straight", "Bend", "Stopper", "Slope1", "Slope2", "Slope3", "Slope4", "RampTop", "RampTop1", "RampTop2", "RampTop3", "RampTop4" })
                CreateItem(items.transform, itemTemplate, "MinecartTrack" + shape + "Form", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/MinecartTrack" + shape + ".png"));
            foreach (var shape in new[] { "Straight", "Bend", "Stopper", "Slope1", "Slope2", "Slope3", "Slope4", "RampTop", "RampTop1", "RampTop2", "RampTop3", "RampTop4" })
                CreateItem(items.transform, itemTemplate, "TramTrack" + shape + "Form", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/TramTrack" + shape + ".png"));
            CreateItem(items.transform, itemTemplate, "TramTrackCrossingForm", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/TramTrackCrossing.png"));
            foreach (var shape in new[] { "Straight", "Bend", "Stopper", "Slope1", "Slope2", "Slope3", "Slope4", "RampTop", "RampTop1", "RampTop2", "RampTop3", "RampTop4" })
                CreateItem(items.transform, itemTemplate, "MinecartChain" + shape + "Form", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/MinecartChain" + shape + ".png"));

            CreateItem(items.transform, itemTemplate, "MinecartChainBendLeftForm", AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Icons/MinecartChainBendLeft.png"));
            new GameObject("Emoji").AddComponent<ChatEmoteSetOld>();
            new GameObject("BlockSets").AddComponent<BlockSetContainer>().blockSets = blockSets;

            // Eco loads the scene as a prefab library, then activates instances.
            // Match the official ModExporter before serializing the bundle scene.
            foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void RegisterComponentIcons(Transform items)
        {
            var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoModKit/Prefabs/DefaultItem.prefab");
            foreach(var key in new[]{"MineTrainDrivingComponent","TrainControllerComponent","RailConditionComponent","TrainFareComponent","RailVehicleAccessComponent","ChainDriveSpeedComponent","RailPowerConnectionComponent","TrainStationComponent","RailAutomationComponent","StationDepartureContext"}){
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/Icons/"+(key.Contains("Station")||key.Contains("Condition")||key.Contains("Automation")?"TrainStation":key.Contains("Drive")||key.Contains("Power")?"ChainDrive":"MineTrain")+".png");
                if(sprite==null)throw new InvalidOperationException("Missing component icon sprite: "+key);
                var existing=items.Find(key);
                if(existing==null)CreateItem(items,template,key,sprite);
                else existing.GetComponentsInChildren<Image>(true).Single(i=>i.name=="Foreground").sprite=sprite;
            }
        }
        private static void CreateItem(Transform parent, GameObject template, string itemName, Sprite icon)
        {
            var item = UnityEngine.Object.Instantiate(template, parent);
            item.name = itemName;
            var images = item.GetComponentsInChildren<Image>(true);
            var foreground = images.FirstOrDefault(image => image.name == "Foreground");
            if (foreground != null) foreground.sprite = icon;
            var background = images.FirstOrDefault(image => image.name == "Background");
            if (background != null) background.sprite = null;
        }

        private static void ConfigureBundleDependencies()
        {
            // A scene bundle automatically pulls in its dependencies. Unity 6 rejects
            // explicitly mixing a scene and loose assets under the same bundle name.
            foreach (var path in AssetDatabase.GetAssetPathsFromAssetBundle(BundleName))
            {
                if (path == ScenePath) continue;
                var oldImporter = AssetImporter.GetAtPath(path);
                if (oldImporter != null) oldImporter.SetAssetBundleNameAndVariant(string.Empty, string.Empty);
            }

            var sceneImporter = AssetImporter.GetAtPath(ScenePath);
            sceneImporter.SetAssetBundleNameAndVariant(BundleName, string.Empty);
            AssetDatabase.SaveAssets();
        }

        private static void ValidateContent(IEnumerable<GameObject> worldPrefabs)
        {
            if (SceneManager.GetActiveScene().GetRootGameObjects().Any(root => root.activeSelf))
                throw new InvalidOperationException("Bundle scene roots must start disabled.");
            foreach (var prefab in worldPrefabs)
            {
                if (prefab.GetComponent<WorldObject>() == null)
                    throw new InvalidOperationException(prefab.name + " lacks WorldObject.");
                if (!prefab.CompareTag("ModObject"))
                    throw new InvalidOperationException(prefab.name + " lacks ModObject tag.");
                if (!prefab.GetComponentsInChildren<Renderer>(true).Any())
                    throw new InvalidOperationException(prefab.name + " has no renderers.");
                if (!prefab.GetComponentsInChildren<Collider>(true).Any())
                    throw new InvalidOperationException(prefab.name + " has no colliders.");
            }

            var minecart = worldPrefabs.First(prefab => prefab.name == "MinecartObject");
            ValidateCartGeometry(minecart);
        }

        public static void ValidateCartGeometry(GameObject minecart)
        {
            var visibleBounds = MinecartIconBuilder.BoundsOf(minecart);
            if (visibleBounds.size.x < .6f || visibleBounds.size.x > 1.3f || visibleBounds.size.y < .5f || visibleBounds.size.y > 1.5f || visibleBounds.size.z < 1f || visibleBounds.size.z > 2.5f)
                throw new InvalidOperationException("Minecart visible bounds do not match meter-scale upright geometry: " + visibleBounds);
            Debug.Log("ECO_CART_SCALE_OK: " + visibleBounds);
            foreach (var required in new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR", "Seat_Anchor" })
                if (FindDeepChild(minecart.transform, required) == null)
                    throw new InvalidOperationException("Minecart model is missing required node " + required);
            var physicsWheels = minecart.GetComponentsInChildren<WheelCollider>(true)
                .Select(w => minecart.transform.InverseTransformPoint(w.transform.TransformPoint(w.center))).ToList();
            if (physicsWheels.Count != 4) throw new InvalidOperationException("Minecart requires four physics wheels.");
            foreach (var suffix in new[] { "FL", "FR", "RL", "RR" })
            {
                var wheel = FindDeepChild(minecart.transform, "Wheel_" + suffix);
                var position = minecart.transform.InverseTransformPoint(wheel.position);
                // FBX handedness conversion can exchange source left/right names.
                // Validate the actual four locations, with one-to-one matching.
                var index = physicsWheels.FindIndex(p => Vector3.Distance(position, p) < .01f);
                if (index < 0) throw new InvalidOperationException("Wheel/physics misalignment " + suffix + ": " + position);
                physicsWheels.RemoveAt(index);
            }
            Debug.Log("ECO_CART_WHEEL_ALIGNMENT_OK: all four wheel pivots match the meter-scale physics rig.");
            var targets = minecart.GetComponentsInChildren<SpecificInteractable>(true);
            if (targets.Count(x => x.interactionTargetName == "MinecartHandle") != 2 ||
                targets.Count(x => x.interactionTargetName == "MinecartStorage") != 2)
                throw new InvalidOperationException("Expected two grab handles plus bucket and cargo storage targets.");
            if (targets.Any(x => x.GetComponent<Collider>() == null))
                throw new InvalidOperationException("Every interaction target must have a collider.");
            Debug.Log("ECO_CART_INTERACTION_TARGETS_OK: two handles and one bucket.");
        }

        private static Transform FindDeepChild(Transform root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
