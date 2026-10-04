using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    // Original compact locomotive geometry. Reuses the proven cart's native
    // networking, wheel contact geometry and audio; no game model is redistributed.
    public static class MineTrainAssetBuilder
    {
        private const string Root = "Assets/EcoMinecarts";

        public static GameObject Build(GameObject cartPrefab, IReadOnlyDictionary<string, Material> materials)
        {
            var root = Object.Instantiate(cartPrefab);
            root.name = "MineTrainObject";
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                if (child.name == "Minecart_Visual" || child.name == "CargoContents" || child.name == "COL_Minecart_Body" || child.name.StartsWith("COL_GrabHandle")
                    || child.name.StartsWith("BucketPassenger") || child.name == "NativePullMount" || child.name == "PullPlayerCollider"
                    || child.name.StartsWith("Grip") || child.name.StartsWith("Passenger") || child.name.StartsWith("PullExit")
                    || child.name.StartsWith("RailCoupler") || child.name.StartsWith("CoupledCoupler"))
                    Object.DestroyImmediate(child.gameObject);
            var iron = materials["MAT_IronBare"];
            var paint = materials["MAT_IronPainted"];
            var wood = materials["MAT_WoodRail"];
            var model = new GameObject("MineTrain_Visual").transform;
            model.SetParent(root.transform, false);
            Box(model, "Chassis", new Vector3(0, .24f, 0), new Vector3(.72f, .18f, 1.58f), paint);
            Box(model, "RunningBoard", new Vector3(0, .355f, 0), new Vector3(.85f, .05f, 1.58f), iron);
            Cylinder(model, "Boiler", new Vector3(0, .65f, .34f), .25f, .84f, Quaternion.Euler(90, 0, 0), paint);
            foreach (var z in new[] { -.03f, .34f, .70f })
                Cylinder(model, "BoilerBand", new Vector3(0, .65f, z), .258f, .035f, Quaternion.Euler(90, 0, 0), iron);
            // Let the door overlap the boiler end instead of leaving a seam.
            Cylinder(model, "SmokeboxDoor", new Vector3(0, .65f, .765f), .22f, .025f, Quaternion.Euler(90, 0, 0), iron);
            Box(model, "DoorLatch", new Vector3(0, .65f, .801f), new Vector3(.18f, .025f, .025f), paint);
            Cylinder(model, "SteamDome", new Vector3(0, .965f, .02f), .10f, .18f, Quaternion.identity, iron);
            Cylinder(model, "ExhaustPipe", new Vector3(0, 1.13f, .49f), .075f, .55f, Quaternion.identity, paint);
            Cylinder(model, "StackRim", new Vector3(0, 1.41f, .49f), .105f, .045f, Quaternion.identity, iron);
            // Dark inset creates a visible open stack mouth.
            Cylinder(model, "StackMouth", new Vector3(0, 1.434f, .49f), .081f, .005f, Quaternion.identity, paint);
            Box(model, "CabFloor", new Vector3(0, .39f, -.49f), new Vector3(.78f, .06f, .63f), wood);
            Box(model, "CabRear", new Vector3(0, .76f, -.76f), new Vector3(.80f, .68f, .045f), paint);
            foreach (var x in new[] { -.38f, .38f })
            {
                Box(model, "CabSide", new Vector3(x, .58f, -.47f), new Vector3(.035f, .30f, .54f), paint);
                foreach (var z in new[] { -.76f, -.20f })
                    Box(model, "RoofPost", new Vector3(x, 1.29375f, z), new Vector3(.045f, 1.3475f, .045f), iron);
            }
            // Native first-person camera retains its standing eye offset while
            // sitting. Keep the roof above that view rather than inventing a
            // cameraTarget override (it does not control first-person position).
            Box(model, "CabRoof", new Vector3(0, 2.00f, -.47f), new Vector3(.92f, .065f, .72f), paint);
            Box(model, "DriverBench", new Vector3(0, .60f, -.61f), new Vector3(.48f, .075f, .25f), wood);
            Box(model, "Backrest", new Vector3(0, .77f, -.735f), new Vector3(.48f, .3f, .055f), wood);
            Box(model, "ControlConsole", new Vector3(0, .91f, -.20f), new Vector3(.50f, .16f, .08f), paint);
            Box(model, "ThrottleLever", new Vector3(.16f, 1.05f, -.27f), new Vector3(.025f, .19f, .025f), iron);
            Box(model, "ThrottleGrip", new Vector3(.16f, 1.15f, -.27f), new Vector3(.10f, .035f, .035f), wood);
            Cylinder(model, "PressureGauge", new Vector3(-.12f, .95f, -.254f), .055f, .02f, Quaternion.Euler(90, 0, 0), iron);
            foreach (var end in new[] { -1f, 1f })
            {
                Box(model, "BufferBeam", new Vector3(0, .27f, end * .81f), new Vector3(.78f, .12f, .08f), wood);
                Box(model, "CouplingStem", new Vector3(0, .27f, end * .88f), new Vector3(.10f, .075f, .10f), iron);
                Box(model, "CouplingHead", new Vector3(0, .27f, end * .93f), new Vector3(.16f, .10f, .04f), paint);
            }
            var controller = root.GetComponent<RCCCarControllerV2>();
            var wheelNodes = new[] { controller.FrontLeftWheelTransform, controller.FrontRightWheelTransform,
                controller.RearLeftWheelTransform, controller.RearRightWheelTransform };
            foreach (var wheel in wheelNodes)
            {
                Cylinder(wheel, "DrivingWheel", Vector3.zero, .15f, .065f, Quaternion.Euler(0, 0, 90), paint);
                Cylinder(wheel, "WheelFlange", new Vector3(wheel.localPosition.x > 0 ? -.035f : .035f, 0, 0), .165f, .02f, Quaternion.Euler(0, 0, 90), iron);
                Cylinder(wheel, "Hub", new Vector3(wheel.localPosition.x > 0 ? .039f : -.039f, 0, 0), .055f, .025f, Quaternion.Euler(0, 0, 90), iron);
            }
            var body = root.GetComponent<Rigidbody>();
            body.mass = 600;
            RailWheelSuspension.Configure(root,600,1350);
            body.centerOfMass = new Vector3(0, .32f, 0);
            controller.COM.localPosition = body.centerOfMass;
            controller.footPoweredCart = false;
            controller._wheelTypeChoise = 2; // Powered Cart's all-wheel drive.
            controller.engineTorque = 5000;
            controller.brake = 6000;
            controller.autoReverse = true;
            controller.autoBrakeWhenNotUsingGas = true;
            controller.steerAngle = controller.highspeedsteerAngle = 0; // Rails choose direction.
            controller.maxspeed = 28.8f;
            controller.gearSpeed = new[] { 14.4f };
            controller.engineTorqueCurve = new[] { AnimationCurve.Linear(0, 1, 28.8f, 0) };
            var cab = Collider(root, "CabInteraction", new Vector3(0, .74f, -.48f), new Vector3(.76f, .70f, .58f), "MineTrainCab");
            Collider(root, "BoilerInteraction", new Vector3(0, .67f, .34f), new Vector3(.57f, .57f, .87f), "MineTrainBoiler");
            var seatNode = new GameObject("TrainDriverSeat").transform;
            seatNode.SetParent(root.transform, false);
            // Sitting supplies its own pelvis height above the mount origin.
            // Lower the avatar/camera 6 cm and the bench 14 cm together: this
            // leaves 8 cm more pelvis clearance without lifting the eye point.
            seatNode.localPosition = new Vector3(0, .04f, -.55f);
            var driver = seatNode.gameObject.AddComponent<MountSpot>();
            driver.overrideAvatarState = (Eco.Animation.AnimationStateManager.AvatarState)3;
            driver.setAsParent = true;
            driver.lockRotation = false;
            driver.exitPosition = Anchor(root, "TrainExitLeft", new Vector3(-1.1f, .05f, -.48f));
            driver.alternativeExitPosition = Anchor(root, "TrainExitRight", new Vector3(1.1f, .05f, -.48f));
            // Installed Powered Cart leaves this null: native driver camera
            // follows the avatar. An additional elevated target put it in the roof.
            driver.cameraTarget = null;
            root.GetComponent<Mountable>().seats = new[] { driver };
            root.GetComponent<MoveThroughSounds>().OverlapCheck = cab;
            var vehicle = root.GetComponent<Vehicle>();
            vehicle.AllVehicleColliders = root.GetComponentsInChildren<UnityEngine.Collider>();
            vehicle.size = new Vector3(1, 2.1f, 2);
            AddExhaust(root, vehicle);
            StandingCabAssetBuilder.Build(root,model,.42f,-.49f,1.2f,1.08f,wood,iron,paint);
            MinecartRuntimeAssets.AttachCouplers(root, .93f);
            vehicle.AllVehicleColliders = root.GetComponentsInChildren<UnityEngine.Collider>();
            RailRiderInteractionAssetBuilder.Configure(root, explicitExit:true);
            var result = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/MineTrainObject.prefab");
            Object.DestroyImmediate(root);
            return result;
        }

        private static void AddExhaust(GameObject root, Vehicle vehicle)
        {
            var node = new GameObject("SteamExhaust");
            node.transform.SetParent(root.transform, false);
            node.transform.localPosition = new Vector3(0, 1.45f, .49f);
            var particles = node.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.35f, .60f);
            main.startSize = new ParticleSystem.MinMaxCurve(.08f, .16f);
            main.startColor = new Color(.55f, .55f, .55f, .35f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12;
            shape.radius = .06f;
            node.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var emission = particles.emission;
            emission.rateOverTime = 12;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 3));
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(.8f, 0), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var path = Root + "/Materials/MAT_TrainExhaust.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find(RailWorldMaterialBuilder.ParticleShader)); AssetDatabase.CreateAsset(material, path); }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Materials/BrakeSpark.asset");
            RailWorldMaterialBuilder.Particle(material, false);
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            // Same native operating callbacks found in the installed PoweredCart.
            UnityEventTools.AddPersistentListener(vehicle.OnEnableOperating, particles.Play);
            UnityEventTools.AddPersistentListener(vehicle.OnDisableOperating, particles.Stop);
            var auto = Object.Instantiate(particles.gameObject, root.transform);
            auto.name = "AutomaticSteamExhaust";
            var world = root.GetComponent<WorldObject>(); var index = world.States.Length;
            var states = world.States; var changed = world.OnStateChangedEvents;
            var on = world.OnStateEnabledEvents; var off = world.OnStateDisabledEvents;
            Array.Resize(ref states,index+1); Array.Resize(ref changed,index+1); Array.Resize(ref on,index+1); Array.Resize(ref off,index+1);
            states[index]="AutoRunning"; changed[index]=new ChangedStateEvent(); on[index]=new SetStateEvent(); off[index]=new SetStateEvent();
            UnityEventTools.AddPersistentListener(on[index],auto.GetComponent<ParticleSystem>().Play);
            UnityEventTools.AddPersistentListener(off[index],auto.GetComponent<ParticleSystem>().Stop);
            world.States=states; world.OnStateChangedEvents=changed; world.OnStateEnabledEvents=on; world.OnStateDisabledEvents=off;
        }

        private static Transform Anchor(GameObject root, string name, Vector3 position)
        {
            var node = new GameObject(name).transform;
            node.SetParent(root.transform, false);
            node.localPosition = position;
            return node;
        }
        private static BoxCollider Collider(GameObject root, string name, Vector3 position, Vector3 size, string target)
        {
            var node = Anchor(root, name, position).gameObject;
            var collider = node.AddComponent<BoxCollider>();
            collider.size = size;
            node.AddComponent<SpecificInteractable>().interactionTargetName = target;
            return collider;
        }
        private static void Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
            => Primitive(parent, name, PrimitiveType.Cube, position, scale, Quaternion.identity, material);
        private static void Cylinder(Transform parent, string name, Vector3 position, float radius, float length, Quaternion rotation, Material material)
            => Primitive(parent, name, PrimitiveType.Cylinder, position, new Vector3(radius * 2, length * .5f, radius * 2), rotation, material);
        private static void Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            var node = GameObject.CreatePrimitive(type);
            node.name = name;
            node.transform.SetParent(parent, false);
            node.transform.localPosition = position;
            node.transform.localScale = scale;
            node.transform.localRotation = rotation;
            node.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(node.GetComponent<UnityEngine.Collider>());
        }
    }
}
