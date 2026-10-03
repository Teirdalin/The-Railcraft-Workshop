using System;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

namespace EcoMinecarts.Editor
{
    public static class MinecartRuntimeAssets
    {
        private const string Root = "Assets/EcoMinecarts";

        public static void AttachCargo(GameObject root)
        {
            var contents = new GameObject("CargoContents");
            contents.transform.SetParent(root.transform, false);
            // Same 2x1x2 grid as MinecartObject's ModularStockpileComponent.
            // Native meshes are centered about the display transform. Fit the
            // four cells inside the hopper, above its .349m interior floor.
            contents.transform.localPosition = new Vector3(0, .505f, 0);
            contents.transform.localScale = new Vector3(.19f, .30f, .42f);
            contents.AddComponent<StockpileMeshBuilder>().contentDimensions = new Vector3Int(2, 1, 2);
            // Matches the native wooden cart: runtime supplies the mesh.
            contents.AddComponent<MeshCollider>().convex = true;
            // Clicking a generated cargo surface must still target storage,
            // not accidentally turn into Pull or obscure the bucket actions.
            contents.AddComponent<SpecificInteractable>().interactionTargetName = "MinecartStorage";
        }

        public static void AttachCouplers(GameObject root, float offset)
        {
            var world = root.GetComponent<WorldObject>();
            var massIndex = Array.IndexOf(world.FloatStates, "TrainMass");
            if (massIndex < 0)
            {
                massIndex = world.FloatStates.Length;
                var floatStates = world.FloatStates; var floatEvents = world.OnFloatStateChanged;
                Array.Resize(ref floatStates, massIndex + 1); Array.Resize(ref floatEvents, massIndex + 1);
                floatStates[massIndex] = "TrainMass";
                world.FloatStates = floatStates; world.OnFloatStateChanged = floatEvents;
            }
            world.OnFloatStateChanged[massIndex] = new ChangedFloatStateEvent();
            UnityEventTools.AddPersistentListener(world.OnFloatStateChanged[massIndex], Setter<float>(root.GetComponent<Rigidbody>(), "mass"));
            var collisionIndex = Array.IndexOf(world.States, "VehicleCollisionsEnabled");
            if (collisionIndex < 0)
            {
                collisionIndex = world.States.Length;
                var states = world.States; var events = world.OnStateChangedEvents;
                var enabled = world.OnStateEnabledEvents; var disabled = world.OnStateDisabledEvents;
                Array.Resize(ref states, collisionIndex + 1); Array.Resize(ref events, collisionIndex + 1);
                Array.Resize(ref enabled, collisionIndex + 1); Array.Resize(ref disabled, collisionIndex + 1);
                states[collisionIndex] = "VehicleCollisionsEnabled";
                enabled[collisionIndex] = new SetStateEvent(); disabled[collisionIndex] = new SetStateEvent();
                world.States = states; world.OnStateChangedEvents = events;
                world.OnStateEnabledEvents = enabled; world.OnStateDisabledEvents = disabled;
            }
            world.OnStateChangedEvents[collisionIndex] = new ChangedStateEvent();
            UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[collisionIndex], Setter<bool>(root.GetComponent<Rigidbody>(), "detectCollisions"));
            foreach (var end in new[] { -1, 1 })
            {
                var label = end > 0 ? "Front" : "Rear";
                var hit = new GameObject("RailCoupler" + label);
                hit.transform.SetParent(root.transform, false);
                hit.transform.localPosition = new Vector3(0, .27f, end * offset);
                hit.AddComponent<BoxCollider>().size = new Vector3(.23f, .20f, .05f);
                var target = hit.AddComponent<SpecificInteractable>();
                target.interactionTargetName = "RailCoupler";
                target.interactionTargetValue = end.ToString();
                var bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bridge.name = "CoupledCoupler" + label;
                bridge.transform.SetParent(root.transform, false);
                bridge.transform.localPosition = new Vector3(0, .27f, end * (offset + .025f));
                bridge.transform.localScale = new Vector3(.08f, .065f, .10f);
                UnityEngine.Object.DestroyImmediate(bridge.GetComponent<Collider>());
                bridge.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MAT_IronBare.mat");
                var state = "Coupled" + label;
                var index = Array.IndexOf(world.States, state);
                if (index < 0)
                {
                    index = world.States.Length;
                    var states = world.States; var changed = world.OnStateChangedEvents;
                    var enabled = world.OnStateEnabledEvents; var disabled = world.OnStateDisabledEvents;
                    Array.Resize(ref states, index + 1); Array.Resize(ref changed, index + 1);
                    Array.Resize(ref enabled, index + 1); Array.Resize(ref disabled, index + 1);
                    states[index] = state; enabled[index] = new SetStateEvent(); disabled[index] = new SetStateEvent();
                    world.States = states; world.OnStateChangedEvents = changed;
                    world.OnStateEnabledEvents = enabled; world.OnStateDisabledEvents = disabled;
                }
                world.OnStateChangedEvents[index] = new ChangedStateEvent();
                UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[index], bridge.SetActive);
                bridge.SetActive(false);
            }
        }

        public static void AttachSound(GameObject cart)
        {
            var path = Root + "/Audio/Mine_Cart_Rail_Loop_Perfect.ogg";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("Missing user-supplied rail audio: " + path);
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            var source = cart.AddComponent<AudioSource>();
            source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            source.loop = true;
            source.playOnAwake = true;
            source.volume = 0;
            source.pitch = .65f;
            source.spatialBlend = 1;
            source.dopplerLevel = 0; // Speed drives pitch; avoid doubling it with Doppler.
            source.minDistance = 2;
            source.maxDistance = 25;
            source.rolloffMode = AudioRolloffMode.Linear;
            var world = cart.GetComponent<WorldObject>();
            world.FloatStates = new[] { "RailVolume", "RailPitch" };
            world.OnFloatStateChanged = new[] { new ChangedFloatStateEvent(), new ChangedFloatStateEvent() };
            UnityEventTools.AddPersistentListener(world.OnFloatStateChanged[0], Setter<float>(source, "volume"));
            UnityEventTools.AddPersistentListener(world.OnFloatStateChanged[1], Setter<float>(source, "pitch"));
            AttachBrakes(cart, world);
            AttachCornerSound(cart, world);
            AttachChainLiftSound(cart, world);
            if (source.clip == null || source.clip.length <= 0) throw new InvalidOperationException("Invalid minecart loop.");
            Debug.Log("ECO_RAIL_AUDIO_OK: " + source.clip.length + " seconds; speed-linked volume and pitch.");
        }

        private static void AttachCornerSound(GameObject cart, WorldObject world)
        {
            var path = Root + "/Audio/minecart_corner_screech_loop.ogg";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("Missing supplied corner loop.");
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            var node = new GameObject("CornerAudio");
            node.transform.SetParent(cart.transform, false);
            var source = node.AddComponent<AudioSource>();
            source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (source.clip == null || source.clip.length <= 0) throw new InvalidOperationException("Invalid corner loop.");
            source.loop = source.playOnAwake = true;
            source.volume = 0;
            source.pitch = 1;
            source.spatialBlend = 1;
            source.dopplerLevel = 0;
            source.minDistance = 2;
            source.maxDistance = 18;
            source.rolloffMode = AudioRolloffMode.Linear;
            world.FloatStates = new[] { "RailVolume", "RailPitch", "BrakeVolume", "CornerVolume" };
            var events = world.OnFloatStateChanged;
            world.OnFloatStateChanged = new[] { events[0], events[1], events[2], new ChangedFloatStateEvent() };
            UnityEventTools.AddPersistentListener(world.OnFloatStateChanged[3], Setter<float>(source, "volume"));
            Debug.Log("ECO_CORNER_AUDIO_OK: supplied positional loop, native speed/curvature volume state.");
        }

        private static void AttachBrakes(GameObject cart, WorldObject world)
        {
            var path = Root + "/Audio/minecart_brake_loop.ogg";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("Missing user-supplied brake audio: " + path);
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            var audioNode = new GameObject("BrakeAudio");
            audioNode.transform.SetParent(cart.transform, false);
            var source = audioNode.AddComponent<AudioSource>();
            source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (source.clip == null || source.clip.length <= 0) throw new InvalidOperationException("Invalid brake loop.");
            source.loop = source.playOnAwake = true;
            source.volume = 0;
            source.pitch = 1;
            source.spatialBlend = 1;
            source.dopplerLevel = 0;
            source.minDistance = 2;
            source.maxDistance = 25;
            source.rolloffMode = AudioRolloffMode.Linear;
            world.FloatStates = new[] { "RailVolume", "RailPitch", "BrakeVolume" };
            var railEvents = world.OnFloatStateChanged;
            world.OnFloatStateChanged = new[] { railEvents[0], railEvents[1], new ChangedFloatStateEvent() };
            UnityEventTools.AddPersistentListener(world.OnFloatStateChanged[2], Setter<float>(source, "volume"));

            var shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader == null) throw new InvalidOperationException("Missing built-in additive particle shader.");
            var materialPath = Root + "/Materials/MAT_BrakeSparks.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
            material.shader = shader;
            material.SetColor("_TintColor", Color.white);
            // Explicit soft texture: a null MainTex fails our material audit and
            // gives a hard rectangular billboard rather than a spark streak.
            var texturePath = Root + "/Materials/BrakeSpark.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                texture.name = "BrakeSpark";
                for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                {
                    var radius = new Vector2((x + .5f) / 8 - 1, (y + .5f) / 8 - 1).magnitude;
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - radius), 2)));
                }
                texture.Apply();
                texture.wrapMode = TextureWrapMode.Clamp;
                AssetDatabase.CreateAsset(texture, texturePath);
            }
            material.mainTexture = texture;
            world.States = new[] { "BrakeSparks1", "BrakeSparks2", "BrakeSparks3" };
            world.OnStateChangedEvents = new[] { new ChangedStateEvent(), new ChangedStateEvent(), new ChangedStateEvent() };
            world.OnStateEnabledEvents = new[] { new SetStateEvent(), new SetStateEvent(), new SetStateEvent() };
            world.OnStateDisabledEvents = new[] { new SetStateEvent(), new SetStateEvent(), new SetStateEvent() };
            for (var tier = 1; tier <= 3; tier++)
            {
                var group = new GameObject("BrakeSparks" + tier);
                group.transform.SetParent(cart.transform, false);
                foreach (var x in new[] { -.30f, .30f })
                foreach (var z in new[] { -.41f, .41f })
                {
                    var node = new GameObject("WheelSparks_" + x + "_" + z);
                    node.transform.SetParent(group.transform, false);
                    node.transform.localPosition = new Vector3(x, .025f, z);
                    // Cone points away from the wheel contact, not into the deck.
                    node.transform.localRotation = Quaternion.LookRotation(new Vector3(x > 0 ? 1 : -1, .6f, -.4f));
                    var particles = node.AddComponent<ParticleSystem>();
                    var main = particles.main;
                    main.loop = main.playOnAwake = true;
                    main.duration = 1;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(.09f, .22f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(.5f * tier, 1.1f * tier);
                    main.startSize = new ParticleSystem.MinMaxCurve(.012f, .024f);
                    main.startColor = new Color(1, .48f, .08f, 1);
                    main.gravityModifier = .6f;
                    main.maxParticles = 40;
                    var emission = particles.emission;
                    emission.rateOverTime = tier == 1 ? 8 : tier == 2 ? 20 : 40;
                    var shape = particles.shape;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 20;
                    shape.radius = .018f;
                    var color = particles.colorOverLifetime;
                    color.enabled = true;
                    var gradient = new Gradient();
                    gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1, .25f, .02f), 1) },
                        new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
                    color.color = gradient;
                    var renderer = node.GetComponent<ParticleSystemRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.lengthScale = 1.5f;
                    renderer.velocityScale = .08f;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[tier - 1], group.SetActive);
                group.SetActive(false);
            }
            Debug.Log("ECO_BRAKE_EFFECTS_OK: supplied loop, native state-driven volume and three wheel-spark density tiers.");
        }

        public static GameObject CreateChainDrive()
        {
            var iron = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MAT_IronBare.mat");
            var wood = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MAT_WoodRail.mat");
            var painted = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MAT_IronPainted.mat");
            var root = new GameObject("MinecartChainDriveObject");
            root.tag = "ModObject";
            var world = root.AddComponent<WorldObject>();
            // Eco places world-object origins at the centre of the occupied
            // block. Keep the cabinet authored from floor=0, then lower the
            // complete visual by half a cell so it sits on that block's floor.
            var visual = new GameObject("ChainDriveVisual").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0, -.5f, 0);
            // The server declares the occupied cell. A second client-authored
            // one-block footprint uses a different origin and offsets the
            // cabinet preview from the actual placement cell.
            Cube(visual, "Base", new Vector3(0, .05f, 0), new Vector3(.96f, .1f, .96f), wood);
            Cube(visual, "EnclosedHousing", new Vector3(0, .49f, 0), new Vector3(.88f, .78f, .84f), painted);
            Cube(visual, "Lid", new Vector3(0, .90f, 0), new Vector3(.94f, .06f, .90f), iron);
            foreach (var side in new[] { -1f, 1f })
            {
                Cube(visual, "SidePanel" + side, new Vector3(side * .446f, .49f, 0), new Vector3(.025f, .58f, .64f), iron);
                Cube(visual, "EndPanel" + side, new Vector3(0, .49f, side * .428f), new Vector3(.67f, .58f, .025f), iron);
                foreach (var y in new[] { .23f, .75f })
                foreach (var edge in new[] { -.285f, .285f })
                {
                    Cube(visual, "SideBolt", new Vector3(side * .465f, y, edge), new Vector3(.025f, .042f, .042f), painted);
                    Cube(visual, "EndBolt", new Vector3(edge, y, side * .448f), new Vector3(.042f, .042f, .025f), painted);
                }
            }
            // The mechanism is enclosed. A small shaft cap is the only moving
            // visible part; decorative geometry has no independent colliders.
            var shaft = new GameObject("ShaftIndicator");
            shaft.transform.SetParent(visual, false);
            shaft.transform.localPosition = new Vector3(0, .49f, -.46f);
            Cube(shaft.transform, "Horizontal", Vector3.zero, new Vector3(.26f, .055f, .05f), painted);
            Cube(shaft.transform, "Vertical", Vector3.zero, new Vector3(.055f, .26f, .05f), painted);
            foreach (var collider in root.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
            var bodyCollider = root.AddComponent<BoxCollider>();
            bodyCollider.center = new Vector3(0, -.035f, 0);
            bodyCollider.size = new Vector3(.96f, .93f, .98f);
            var clip = new AnimationClip { legacy = true, wrapMode = WrapMode.Loop };
            clip.SetCurve("ChainDriveVisual/ShaftIndicator", typeof(Transform), "localEulerAngles.z", AnimationCurve.Linear(0, 0, 2, 360));
            var clipPath = Root + "/Prefabs/ChainDriveLoop.anim";
            var savedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (savedClip == null) { AssetDatabase.CreateAsset(clip, clipPath); savedClip = clip; }
            else { EditorUtility.CopySerialized(clip, savedClip); UnityEngine.Object.DestroyImmediate(clip); }
            var animation = root.AddComponent<Animation>();
            animation.AddClip(savedClip, "ChainLoop");
            animation.clip = savedClip;
            animation.playAutomatically = true;
            animation.enabled = false;
            world.States = new[] { "ChainRunning" };
            world.OnStateChangedEvents = new[] { new ChangedStateEvent() };
            world.OnStateEnabledEvents = new[] { new SetStateEvent() };
            world.OnStateDisabledEvents = new[] { new SetStateEvent() };
            UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[0], Setter<bool>(animation, "enabled"));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/MinecartChainDriveObject.prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static void AttachChainLiftSound(GameObject cart, WorldObject world)
        {
            var audioPath = Root + "/Audio/Chain_Racket.ogg";
            var audioImporter = AssetImporter.GetAtPath(audioPath) as AudioImporter;
            if (audioImporter == null) throw new InvalidOperationException("Missing supplied chain racket loop.");
            audioImporter.forceToMono = true;
            var audioSettings = audioImporter.defaultSampleSettings;
            audioSettings.loadType = AudioClipLoadType.CompressedInMemory;
            audioImporter.defaultSampleSettings = audioSettings;
            audioImporter.SaveAndReimport();
            var audioNode = new GameObject("ChainLiftAudio");
            audioNode.transform.SetParent(cart.transform, false);
            audioNode.transform.localPosition = new Vector3(0, .14f, 0);
            var audio = audioNode.AddComponent<AudioSource>();
            audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(audioPath);
            if (audio.clip == null || audio.clip.length <= 0) throw new InvalidOperationException("Invalid chain racket loop.");
            audio.loop = audio.playOnAwake = true;
            audio.volume = .35f;
            audio.spatialBlend = 1;
            audio.dopplerLevel = 0;
            audio.minDistance = 1;
            audio.maxDistance = 8;
            audio.rolloffMode = AudioRolloffMode.Linear;
            // Append without replacing the cart's existing brake spark events.
            var index = world.States.Length;
            var states = world.States;
            var changed = world.OnStateChangedEvents;
            var enabled = world.OnStateEnabledEvents;
            var disabled = world.OnStateDisabledEvents;
            Array.Resize(ref states, index + 1);
            Array.Resize(ref changed, index + 1);
            Array.Resize(ref enabled, index + 1);
            Array.Resize(ref disabled, index + 1);
            states[index] = "ChainLiftRunning";
            changed[index] = new ChangedStateEvent();
            enabled[index] = new SetStateEvent();
            disabled[index] = new SetStateEvent();
            world.States = states;
            world.OnStateChangedEvents = changed;
            world.OnStateEnabledEvents = enabled;
            world.OnStateDisabledEvents = disabled;
            UnityEventTools.AddPersistentListener(changed[index], audioNode.SetActive);
            audioNode.SetActive(false);
            var floatIndex=world.FloatStates.Length;
            var floatStates=world.FloatStates;
            var floatEvents=world.OnFloatStateChanged;
            Array.Resize(ref floatStates,floatIndex+1);
            Array.Resize(ref floatEvents,floatIndex+1);
            floatStates[floatIndex]="ChainLiftPitch";
            floatEvents[floatIndex]=new ChangedFloatStateEvent();
            world.FloatStates=floatStates;
            world.OnFloatStateChanged=floatEvents;
            audio.pitch=1;
            UnityEventTools.AddPersistentListener(floatEvents[floatIndex],Setter<float>(audio,"pitch"));
        }

        private static UnityAction<T> Setter<T>(UnityEngine.Object target, string property) =>
            (UnityAction<T>)Delegate.CreateDelegate(typeof(UnityAction<T>), target, target.GetType().GetProperty(property).GetSetMethod());

        private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }
    }
}
