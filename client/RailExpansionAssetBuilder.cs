using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.Animations;
using UnityEngine;
using TMPro;
using Object = UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    // Specifications are exported from the server's RailVehicleSpec catalog.
    public static class RailExpansionAssetBuilder
    {
        private const string Root = "Assets/EcoMinecarts";
        [Serializable] public class Catalog { public Spec[] Vehicles; public IndustrialTrackAssetBuilder.Definition[] IndustrialTracks; public RailSwitchAssetBuilder.Definition[] Switches; public CoasterAssetBuilder.Path[] Coasters; public CoasterTerrainAssetBuilder.TileDefinition[] CoasterTerrain; }
        [Serializable] public class Spec
        {
            public string Key, Name, Model, Tier;
            public float EmptyKg, CargoKg, Length, Wheelbase, MinimumRadius, MaximumSpeed, PowerWatts, TractionN, BrakeN;
            public int PassengerSeats, Slots;
            public bool Pullable, Tender, Powered, HumanPowered;
            public bool Industrial;
            public float HalfGauge, BodyWidth;
        }
        public static Catalog ReadCatalog() => JsonUtility.FromJson<Catalog>(File.ReadAllText(Root + "/Config/RailVehicles.json"));
        public static IEnumerable<GameObject> Build(GameObject cart, GameObject engine, IReadOnlyDictionary<string, Material> materials)
        {
            var result = new List<GameObject>();
            foreach (var spec in ReadCatalog().Vehicles) result.Add(Vehicle(spec, cart, engine, materials));
            result.Add(Infrastructure("TrainStation", materials));
            result.Add(Infrastructure("WideRailTurn", materials));
            result.Add(Infrastructure("TramWideRailTurn", materials));
            result.Add(Infrastructure("BrokenWoodenTrack", materials));
            result.Add(Infrastructure("TramStop",materials));
            result.Add(Infrastructure("TramCableDrive",materials));
            result.AddRange(IndustrialTrackAssetBuilder.Build(ReadCatalog().IndustrialTracks, materials));
            result.AddRange(RailSwitchAssetBuilder.Build(ReadCatalog().Switches, materials));
            result.AddRange(CoasterAssetBuilder.Build(ReadCatalog().Coasters,materials));
            return result;
        }
        private static GameObject Vehicle(Spec spec, GameObject cart, GameObject engine, IReadOnlyDictionary<string, Material> materials)
        {
            var root = Object.Instantiate(spec.Pullable ? cart : engine);
            root.name = spec.Key + "Object";
            var wood = materials["MAT_WoodRail"];
            var iron = materials["MAT_IronBare"];
            var paint = materials["MAT_IronPainted"];
            var large = spec.Length > 3;
            var width = spec.BodyWidth;
            foreach (Transform node in root.transform.Cast<Transform>().ToArray())
                if (node.name.StartsWith("RailCoupler") || node.name.StartsWith("CoupledCoupler")) Object.DestroyImmediate(node.gameObject);
            if (spec.Pullable)
            {
                RailModelPolish.WoodenBody(root, wood);
            }
            else
            {
                var inheritedCab=root.transform.Find("CabFittings");
                if(inheritedCab!=null)Object.DestroyImmediate(inheritedCab.gameObject);
                root.GetComponent<Mountable>().seats=root.GetComponent<Mountable>().seats.Take(1).ToArray();
                foreach (var name in new[] { "MineTrain_Visual", "CabInteraction", "BoilerInteraction", "CargoContents" })
                { var node = root.transform.Find(name); if (node != null) Object.DestroyImmediate(node.gameObject); }
                var model = new GameObject(spec.Key + "_Visual").transform; model.SetParent(root.transform, false);
                Box(model, "Riveted chassis", new Vector3(0, .28f, 0), new Vector3(spec.Industrial ? 1.7f : .44f, .12f, spec.Length - .22f), iron);
                Box(model, "Deck", new Vector3(0, .44f, 0), new Vector3(width, .04f, spec.Length - .3f), wood);
                foreach (var side in new[] { -1, 1 })
                {
                    Box(model, "Frame beam", new Vector3(side * (spec.HalfGauge-.065f), .35f, 0), new Vector3(.05f, .14f, spec.Length - .3f), iron);
                    for (var z = -spec.Length / 2 + .3f; z < spec.Length / 2; z += .35f)
                        Cylinder(model, "Frame rivet", new Vector3(side * (spec.HalfGauge-.037f), .35f, z), .018f, .012f, Quaternion.Euler(0, 0, 90), iron);
                }
                if (spec.Model=="Tram") Tram(root,model,spec,width,wood,iron,paint);
                else if (spec.HumanPowered) Handcar(root,model,spec,width,wood,iron);
                else if (spec.Powered) Engine(root, model, spec, width, wood, iron, paint);
                else if (spec.PassengerSeats > 0)
                {
                    Passengers(root, model, spec, width, wood, iron, paint);
                    if(spec.Model=="Coaster")
                    {
                        CoasterAssetBuilder.FitCart(root,model,spec,paint,iron);
                        foreach(var side in new[]{-1,1})
                            Hit(root.transform,"CoasterShoveInteraction",new Vector3(side*(width/2+.025f),.53f,0),new Vector3(.10f,.28f,.62f),"CoasterShove");
                    }
                }
                else Cargo(root, model, spec, width, wood, iron);
                foreach (var end in new[] { -1, 1 })
                {
                    Box(model, "End buffer", new Vector3(0, .27f, end * (spec.Length / 2 - .12f)), new Vector3(width, .12f, .10f), paint);
                    Box(model, "Drawbar", new Vector3(0, .27f, end * spec.Length / 2), new Vector3(.12f, .08f, .22f), iron);
                    Cylinder(model, "Coupling pin", new Vector3(0, .30f, end * spec.Length / 2), .025f, .12f, Quaternion.identity, iron);
                }
            }
            var controller = root.GetComponent<RCCCarControllerV2>();
            controller.engineTorque = spec.Powered || spec.HumanPowered ? spec.TractionN : spec.Pullable ? 400 : 0;
            if(spec.HumanPowered) controller.footPoweredCart=true;
            controller.brake = spec.BrakeN;
            controller.maxspeed = spec.MaximumSpeed * 3.6f;
            controller.gearSpeed = new[] { spec.MaximumSpeed * 3.6f };
            controller.engineTorqueCurve = new[] { AnimationCurve.Linear(0, 1, spec.MaximumSpeed * 3.6f, 0) };
            var wheels = new[] { controller.FrontLeftWheelTransform, controller.FrontRightWheelTransform, controller.RearLeftWheelTransform, controller.RearRightWheelTransform };
            var colliders = new[] { controller.FrontLeftWheelCollider, controller.FrontRightWheelCollider, controller.RearLeftWheelCollider, controller.RearRightWheelCollider };
            for (var i = 0; i < wheels.Length; i++)
            {
                var radius = spec.HumanPowered || spec.Pullable ? .18f : .20f;
                var position = wheels[i].localPosition; position.z = (i < 2 ? 1 : -1) * spec.Wheelbase / 2;
                position.y = radius; // The contact plane remains y=0 for every tier.
                position.x = (i % 2 == 0 ? -1 : 1) * spec.HalfGauge;
                wheels[i].localPosition = position;
                colliders[i].transform.position = root.transform.TransformPoint(position);
                colliders[i].radius = radius;
                RailModelPolish.Wheel(wheels[i], radius, spec.Pullable, wood, iron, paint);
            }
            if(!spec.Pullable) RailModelPolish.Axles(root, spec.Wheelbase, spec.HumanPowered ? .18f : .20f, iron, spec.HalfGauge);
            var body = root.GetComponent<Rigidbody>(); body.mass = spec.EmptyKg;
            if(spec.Powered) RailWheelSuspension.Configure(root,spec.EmptyKg,spec.EmptyKg+spec.CargoKg+500);
            body.centerOfMass = new Vector3(0, .34f, 0); controller.COM.localPosition = body.centerOfMass;
            var world = root.GetComponent<global::Vehicle>();
            var height = spec.Powered ? (large ? 2.9f : 2.7f) : spec.PassengerSeats > 0 ? 2.2f : large ? 2.4f : 1.8f;
            world.size = new Vector3(spec.Powered?Mathf.Max(1.5f,width)+.4f:width, height, spec.Length);
            MinecartRuntimeAssets.AttachCouplers(root, spec.Length / 2);
            foreach(var group in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("BrakeSparks")))
                foreach(Transform emitter in group) emitter.localPosition=new Vector3(Mathf.Sign(emitter.localPosition.x)*spec.HalfGauge,.025f,Mathf.Sign(emitter.localPosition.z)*spec.Wheelbase/2);
            if(spec.Pullable)
                foreach(var label in new[]{"Front","Rear"})
                    root.transform.Find("CoupledCoupler"+label).GetComponent<Renderer>().sharedMaterial=wood;
            world.AllVehicleColliders = root.GetComponentsInChildren<Collider>();
            RailRiderInteractionAssetBuilder.Configure(root, explicitExit:spec.Powered && spec.Model!="Tram");
            return Save(root);
        }
        private static void Handcar(GameObject root,Transform model,Spec spec,float width,Material wood,Material iron)
        {
            // Separate platform, open pump tower and rocking cross-handles.
            for(var i=0;i<7;i++)
                Box(model,"Deck plank",new Vector3(0,.475f,(i-3)*.21f),new Vector3(width,.03f,.20f),wood);
            foreach(var side in new[]{-1,1})
            {
                Box(model,"Pump upright",new Vector3(side*.15f,.81f,0),new Vector3(.07f,.80f,.12f),iron);
                Cylinder(model,"Axle",new Vector3(0,.18f,side*spec.Wheelbase/2),.035f,.65f,Quaternion.Euler(0,0,90),iron);
            }
            Cylinder(model,"Pivot pin",new Vector3(0,1.22f,0),.055f,.40f,Quaternion.Euler(0,0,90),iron);
            var pivot=Anchor(root,"PumpPivot",new Vector3(0,1.22f,0));
            Box(pivot,"Pump beam",Vector3.zero,new Vector3(.09f,.065f,1.02f),wood);
            foreach(var end in new[]{-1,1}) Cylinder(pivot,"Cross handle",new Vector3(0,0,end*.48f),.032f,.66f,Quaternion.Euler(0,0,90),wood);
            var seat=root.GetComponent<Mountable>().seats[0];
            seat.transform.localPosition=new Vector3(0,.49f,-.69f);
            seat.overrideAvatarState=(Eco.Animation.AnimationStateManager.AvatarState)255;
            seat.lockRotation=false; seat.cameraTarget=null; seat.ApplyHandsGripOnMount=true;
            var grips=new List<RootMotion.FinalIK.InteractionTarget>();
            foreach(var side in new[]{-1,1})
            {
                var grip=new GameObject(side<0?"PumpGripLeft":"PumpGripRight").transform;
                grip.SetParent(pivot,false); grip.localPosition=new Vector3(side*.23f,0,-.48f);
                grip.localRotation=side<0?new Quaternion(-.49313f,-.27861f,.34491f,.74849f):new Quaternion(.31578f,.51886f,.73621f,.29844f);
                var target=grip.gameObject.AddComponent<RootMotion.FinalIK.InteractionTarget>(); target.effectorType=side<0?5:6; grips.Add(target);
            }
            seat.IKTargets=grips.ToArray();
            seat.exitPosition.localPosition=new Vector3(-1.05f,.05f,-.55f);
            seat.alternativeExitPosition.localPosition=new Vector3(1.05f,.05f,-.55f);
            root.GetComponent<MoveThroughSounds>().OverlapCheck=Hit(root.transform,"PumpInteraction",new Vector3(0,1.0f,0),new Vector3(.72f,.62f,1.12f),"HandcarPump");
            Hit(root.transform,"PlatformCollider",new Vector3(0,.44f,0),new Vector3(width,.10f,spec.Length-.25f),"HandcarPump");
            Box(model,"Toolbox",new Vector3(.22f,.605f,.60f),new Vector3(.32f,.23f,.30f),wood);
            Hit(root.transform,"ToolboxInteraction",new Vector3(.22f,.605f,.60f),new Vector3(.34f,.25f,.32f),"MinecartStorage");
            foreach(var name in new[]{"SteamExhaust","AutomaticSteamExhaust"})
            { var node=root.transform.Find(name); if(node!=null) Object.DestroyImmediate(node.gameObject); }
            // A looping native Animator controller actually begins playback when
            // its component is enabled. The old disabled legacy Animation only
            // had its enabled flag toggled and could remain stopped after Awake.
            var clip=new AnimationClip { legacy=false,wrapMode=WrapMode.Loop };
            RailModelPolish.PumpLinkage(root, pivot, clip, iron);
            var path=Root+"/Prefabs/HandcarPump.anim";
            var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(saved==null) { AssetDatabase.CreateAsset(clip,path); saved=clip; } else { EditorUtility.CopySerialized(clip,saved); Object.DestroyImmediate(clip); }
            var clipSettings=AnimationUtility.GetAnimationClipSettings(saved);
            clipSettings.loopTime=true;
            AnimationUtility.SetAnimationClipSettings(saved,clipSettings);
            var controllerPath=Root+"/Prefabs/HandcarPump.controller";
            var pumpController=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)
                ??AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine=pumpController.layers[0].stateMachine;
            var pumpState=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Pump")??machine.AddState("Pump");
            pumpState.motion=saved;machine.defaultState=pumpState;
            var animator=root.AddComponent<Animator>();animator.runtimeAnimatorController=pumpController;
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.keepAnimatorStateOnDisable=false;animator.enabled=false;
            var world=root.GetComponent<WorldObject>(); var index=world.States.Length;
            var states=world.States; var changed=world.OnStateChangedEvents; var on=world.OnStateEnabledEvents; var off=world.OnStateDisabledEvents;
            Array.Resize(ref states,index+1); Array.Resize(ref changed,index+1); Array.Resize(ref on,index+1); Array.Resize(ref off,index+1);
            states[index]="HandcarPumping"; changed[index]=new ChangedStateEvent(); on[index]=new SetStateEvent(); off[index]=new SetStateEvent();
            var setter=(UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),animator,typeof(Behaviour).GetProperty("enabled").GetSetMethod());
            UnityEventTools.AddPersistentListener(changed[index],setter);
            world.States=states; world.OnStateChangedEvents=changed; world.OnStateEnabledEvents=on; world.OnStateDisabledEvents=off;
        }
        private static void Engine(GameObject root, Transform model, Spec spec, float width, Material wood, Material iron, Material paint)
        {
            var large = spec.Length > 3; var floor = large ? .70f : .50f; var cabZ = -spec.Length * .31f;
            var radius = large ? .40f : spec.Model == "Express" ? .27f : .34f;
            var boilerY = floor + radius; var boilerZ = spec.Length * .15f;
            Cylinder(model, "Steam boiler", new Vector3(0, boilerY, boilerZ), radius, spec.Length * .51f, Quaternion.Euler(90, 0, 0), paint);
            RailModelPolish.EngineSupports(model, floor, cabZ, boilerZ, radius, spec.Length, width, iron, paint);
            for (var i = 0; i < 4; i++) Cylinder(model, "Boiler band", new Vector3(0, boilerY, boilerZ + (i - 1.5f) * spec.Length * .12f), radius + .012f, .035f, Quaternion.Euler(90, 0, 0), iron);
            // Seat the rear of the door inside the actual boiler end. The old
            // length-scaled position left a visible gap on long locomotives.
            var boilerFront = boilerZ + spec.Length * .51f / 2;
            Cylinder(model, "Smokebox door", new Vector3(0, boilerY, boilerFront + .005f), radius * .9f, .04f, Quaternion.Euler(90, 0, 0), iron);
            var stackZ = spec.Length * .32f;
            Cylinder(model, "Exhaust chimney", new Vector3(0, boilerY + radius + .22f, stackZ), .08f, .46f, Quaternion.identity, paint);
            Cylinder(model, "Chimney lip", new Vector3(0, boilerY + radius + .46f, stackZ), .12f, .05f, Quaternion.identity, iron);
            Cylinder(model, "Steam dome", new Vector3(0, boilerY + radius, .06f), .13f, .28f, Quaternion.identity, iron);
            foreach (var side in new[] { -1, 1 })
            {
                Cylinder(model, "Piston cylinder", new Vector3(side * (spec.HalfGauge+.09f), .29f, spec.Wheelbase * .36f), .10f, .32f, Quaternion.Euler(90, 0, 0), iron);
                Box(model, "Connecting rod", new Vector3(side * (spec.HalfGauge+.05f), .20f, 0), new Vector3(.035f, .035f, spec.Wheelbase), iron);
                Box(model, "Cab side", new Vector3(side * width * .44f, floor + .25f, cabZ), new Vector3(.045f, .50f, spec.Length * .29f), paint);
                foreach (var z in new[] { cabZ - spec.Length * .14f, cabZ + spec.Length * .14f })
                    Box(model, "Cab window post", new Vector3(side * width * .44f, floor + 1.0175f, z), new Vector3(.045f, 1.485f, .045f), iron);
            }
            Box(model, "Cab roof", new Vector3(0, floor + 1.8f, cabZ), new Vector3(width * 1.13f, .08f, spec.Length * .34f), paint);
            Box(model, "Bench", new Vector3(0, floor + .34f, cabZ - .1f), new Vector3(.52f, .08f, .30f), wood);
            Box(model, "Cab floor", new Vector3(0, floor - .04f, cabZ), new Vector3(width, .07f, spec.Length * .31f), wood);
            Box(model, "Control desk", new Vector3(0, floor + .49f, cabZ + .30f), new Vector3(.60f, .16f, .13f), paint);
            Cylinder(model, "Pressure gauge", new Vector3(-.15f, floor + .58f, cabZ + .22f), .07f, .025f, Quaternion.Euler(90, 0, 0), iron);
            Box(model, "Regulator lever", new Vector3(.17f, floor + .65f, cabZ + .20f), new Vector3(.035f, .32f, .035f), wood);
            if (spec.Model == "Express")
                Box(model, "Streamlined running skirt", new Vector3(0, .36f, .15f), new Vector3(width * .94f, .17f, spec.Length * .63f), paint);
            var cab = Hit(root.transform, "CabInteraction", new Vector3(0, floor + .48f, cabZ), new Vector3(width, .85f, spec.Length * .3f), "MineTrainCab");
            Hit(root.transform, "BoilerInteraction", new Vector3(0, boilerY, boilerZ), new Vector3(radius * 2, radius * 2, spec.Length * .53f), "MineTrainBoiler");
            var seat = root.GetComponent<Mountable>().seats[0]; seat.transform.localPosition = new Vector3(0, floor - .30f, cabZ);
            seat.exitPosition.localPosition = new Vector3(-width / 2 - .65f, .05f, cabZ);
            seat.alternativeExitPosition.localPosition = new Vector3(width / 2 + .65f, .05f, cabZ);
            root.GetComponent<MoveThroughSounds>().OverlapCheck = cab;
            StandingCabAssetBuilder.Build(root,model,floor-.005f,cabZ,large ? 1.35f : 1.2f,Mathf.Max(1.08f,spec.Length*.31f),wood,iron,paint);
            var exhaust = root.transform.Find("SteamExhaust");
            if (exhaust != null) exhaust.localPosition = new Vector3(0, boilerY + radius + .50f, stackZ);
            // Autopilot has no native driver, so provide a separate native state event.
            var existingAuto = root.transform.Find("AutomaticSteamExhaust");
            if (existingAuto != null && exhaust != null) existingAuto.localPosition = exhaust.localPosition;
            if (exhaust != null && existingAuto == null)
            {
                var auto = Object.Instantiate(exhaust.gameObject, root.transform); auto.name = "AutomaticSteamExhaust";
                var world = root.GetComponent<WorldObject>(); var index = world.States.Length;
                var states = world.States; var changed = world.OnStateChangedEvents; var on = world.OnStateEnabledEvents; var off = world.OnStateDisabledEvents;
                Array.Resize(ref states, index + 1); Array.Resize(ref changed, index + 1); Array.Resize(ref on, index + 1); Array.Resize(ref off, index + 1);
                states[index] = "AutoRunning"; changed[index] = new ChangedStateEvent(); on[index] = new SetStateEvent(); off[index] = new SetStateEvent();
                UnityEventTools.AddPersistentListener(on[index], auto.GetComponent<ParticleSystem>().Play);
                UnityEventTools.AddPersistentListener(off[index], auto.GetComponent<ParticleSystem>().Stop);
                world.States = states; world.OnStateChangedEvents = changed; world.OnStateEnabledEvents = on; world.OnStateDisabledEvents = off;
            }
        }
        private static void Passengers(GameObject root, Transform model, Spec spec, float width, Material wood, Material iron, Material paint)
        {
            var seats = new List<MountSpot> { root.GetComponent<Mountable>().seats[0] };
            var rows = spec.PassengerSeats / 2;
            for (var i = 0; i < spec.PassengerSeats; i++)
            {
                var x = (i % 2 == 0 ? -1 : 1) * width * .24f;
                var z = (i / 2 - (rows - 1) * .5f) * (spec.Length - .6f) / rows;
                Box(model, "Passenger bench", new Vector3(x, .74f, z), new Vector3(width * .40f, .08f, .30f), wood);
                Box(model, "Bench back", new Vector3(x, .95f, z - .15f), new Vector3(width * .40f, .38f, .04f), wood);
                var spot = new GameObject("PassengerSeat" + (i + 1)); spot.transform.SetParent(root.transform, false);
                // Match the finished bucket cushion or wooden bench slats.
                var surface = spec.Model == "Coaster"
                    ? RailRiderFit.CoasterCushionCentre + RailRiderFit.CoasterCushionThickness / 2
                    : .78f + RailRiderFit.SlatSurfaceOffset;
                spot.transform.localPosition = new Vector3(x, surface - RailRiderFit.SeatedHipHeight, z);
                var mount = spot.AddComponent<MountSpot>(); mount.setAsParent = true; mount.overrideAvatarState = (Eco.Animation.AnimationStateManager.AvatarState)3;
                mount.exitPosition = Anchor(root, "PassengerExit" + i, new Vector3(Mathf.Sign(x) * (width / 2 + .65f), .05f, z));
                mount.alternativeExitPosition = Anchor(root, "AlternateExit" + i, new Vector3(-Mathf.Sign(x) * (width / 2 + .65f), .05f, z));
                seats.Add(mount);
                Hit(root.transform, "PassengerSeatTarget" + i, new Vector3(x, .85f, z), new Vector3(width * .44f, .75f, .45f), "RailPassengerSeat", (i + 1).ToString());
            }
            foreach (var side in new[] { -1, 1 })
            {
                Box(model, "Coach side", new Vector3(side * width / 2, .65f, 0), new Vector3(.05f, .50f, spec.Length - .35f), paint);
                for (var z = -spec.Length / 2 + .25f; z < spec.Length / 2; z += .65f)
                    Box(model, "Window pillar", new Vector3(side * width / 2, 1.48375f, z), new Vector3(.045f, 1.2375f, .045f), wood);
            }
            Box(model, "Coach roof", new Vector3(0, 2.14f, 0), new Vector3(width + .16f, .075f, spec.Length - .2f), paint);
            root.GetComponent<Mountable>().seats = seats.ToArray();
            root.GetComponent<MoveThroughSounds>().OverlapCheck = Hit(root.transform, "Luggage", new Vector3(0, .45f, spec.Length / 2 - .3f), new Vector3(.6f, .25f, .2f), "MinecartStorage");
            var exhaust = root.transform.Find("SteamExhaust"); if (exhaust != null) exhaust.gameObject.SetActive(false);
        }
        private static void Tram(GameObject root,Transform model,Spec spec,float width,Material wood,Material iron,Material paint)
        {
            var length=spec.Length;
            Box(model,"Passenger deck",new Vector3(0,.49f,0),new Vector3(width,.075f,length-.22f),wood);
            foreach(var side in new[]{-1,1})
            {
                Box(model,"Tram sill",new Vector3(side*(width/2-.03f),.69f,0),new Vector3(.07f,.35f,length-.32f),paint);
                Box(model,"Handrail",new Vector3(side*(width/2-.04f),1.10f,0),new Vector3(.045f,.045f,length-.40f),iron);
                foreach(var z in new[]{-length*.36f,-length*.13f,length*.13f,length*.36f})
                    Box(model,"Canopy upright",new Vector3(side*(width/2-.06f),1.48f,z),new Vector3(.06f,1.66f,.06f),wood);
                Box(model,"Running board",new Vector3(side*(width/2+.08f),.48f,0),new Vector3(.22f,.045f,length*.52f),wood);
            }
            foreach(var end in new[]{-1,1})
            {
                Box(model,"End bulkhead",new Vector3(0,1.16f,end*(length/2-.15f)),new Vector3(width*.72f,1.1f,.055f),paint);
                Box(model,"Automatic cab housing",new Vector3(0,.75f,end*(length/2-.37f)),new Vector3(width*.36f,.37f,.28f),iron);
                Box(model,"Cab status lens",new Vector3(0,.99f,end*(length/2-.34f)),new Vector3(.14f,.055f,.045f),wood);
                Box(model,"Destination board",new Vector3(0,2.22f,end*(length/2-.10f)),new Vector3(width*.86f,.29f,.06f),iron);
                Box(model,"Step",new Vector3(0,.41f,end*(length/2+.06f)),new Vector3(width*.45f,.06f,.22f),wood);
            }
            Box(model,"Canopy roof",new Vector3(0,2.36f,0),new Vector3(width+.19f,.09f,length-.11f),paint);
            Box(model,"Clerestory",new Vector3(0,2.43f,0),new Vector3(width*.58f,.12f,length*.56f),wood);
            Hit(root.transform,"TramFloor",new Vector3(0,.49f,0),new Vector3(width-.12f,.09f,length-.28f),null);
            var seats=new List<MountSpot>{root.GetComponent<Mountable>().seats[0]};
            for(var i=0;i<spec.PassengerSeats;i++)
            {
                var x=(i%2==0?-1:1)*width*.26f;
                var standing=i>=4;
                var z=standing?0:(i/2==0?-1:1)*length*.19f;
                if(!standing)
                {
                    Box(model,"Passenger bench",new Vector3(x,.79f,z),new Vector3(width*.40f,.08f,.35f),wood);
                    Box(model,"Bench back",new Vector3(x,.99f,z-Mathf.Sign(z)*.15f),new Vector3(width*.40f,.38f,.045f),wood);
                }
                else
                {
                    // Hang the grip from the canopy rather than leaving a loose bar in the cabin.
                    Box(model,"Handhold hanger",new Vector3(x,2.105f,z),new Vector3(.035f,.43f,.035f),iron);
                    Box(model,"Standing handhold",new Vector3(x,1.90f,z),new Vector3(.11f,.045f,.045f),iron);
                    Box(model,"Standing position",new Vector3(x,.54f,z),new Vector3(.31f,.012f,.36f),wood);
                }
                var spot=new GameObject("PassengerSeat"+(i+1));spot.transform.SetParent(root.transform,false);
                spot.transform.localPosition=new Vector3(x,standing?.54f:.83f+RailRiderFit.SlatSurfaceOffset-RailRiderFit.SeatedHipHeight,z);
                if(!standing && z<0)spot.transform.localRotation=Quaternion.Euler(0,180,0);
                var mount=spot.AddComponent<MountSpot>();mount.setAsParent=true;
                mount.overrideAvatarState=(Eco.Animation.AnimationStateManager.AvatarState)(standing?255:3);
                mount.exitPosition=Anchor(root,"PassengerExit"+i,new Vector3(Mathf.Sign(x)*(width/2+.66f),.05f,z));
                mount.alternativeExitPosition=Anchor(root,"AlternateExit"+i,new Vector3(-Mathf.Sign(x)*(width/2+.66f),.05f,z));
                seats.Add(mount);
                Hit(root.transform,"PassengerSeatTarget"+i,new Vector3(x,.89f,z),new Vector3(width*.42f,.77f,.46f),"RailPassengerSeat",(i+1).ToString());
            }
            root.GetComponent<Mountable>().seats=seats.ToArray();
            root.GetComponent<MoveThroughSounds>().OverlapCheck=Hit(root.transform,"TramBoarding",new Vector3(0,.63f,0),new Vector3(width,.25f,length*.58f),"MinecartStorage");
            var vehicle=root.GetComponent<global::Vehicle>();
            TextMeshPro Board(string name,float end)
            {
                var node=new GameObject(name);node.transform.SetParent(root.transform,false);
                node.transform.localPosition=new Vector3(0,2.21f,end*(length/2-.05f));
                node.transform.localRotation=Quaternion.Euler(0,end>0?180:0,0);
                var text=node.AddComponent<TextMeshPro>();text.text="CITY LINE";text.fontSize=1.7f;text.alignment=TextAlignmentOptions.Center;
                text.color=new Color(.95f,.89f,.68f);text.enableAutoSizing=true;text.fontSizeMin=1f;text.fontSizeMax=1.7f;
                node.transform.localScale=Vector3.one*.55f;
                return text;
            }
            vehicle.LicensePlate=Board("Front destination text",1);
            vehicle.ExtraLicensePlates=new[]{Board("Rear destination text",-1)};
            foreach(var name in new[]{"SteamExhaust","AutomaticSteamExhaust"})
            {var exhaust=root.transform.Find(name);if(exhaust!=null)Object.DestroyImmediate(exhaust.gameObject);}
        }
        private static void Cargo(GameObject root, Transform model, Spec spec, float width, Material wood, Material iron)
        {
            var length = spec.Length - .45f;
            foreach (var side in new[] { -1, 1 })
            {
                Box(model, "Cargo side", new Vector3(side * width * .45f, .77f, 0), new Vector3(.045f, .70f, length), iron);
                Box(model, "End wall", new Vector3(0, .77f, side * length / 2), new Vector3(width * .92f, .70f, .045f), iron);
                for (var z = -length / 2; z <= length / 2; z += .55f)
                    Box(model, "Hopper reinforcing rib", new Vector3(side * width * .48f, .77f, z), new Vector3(.045f, .75f, .06f), wood);
            }
            MinecartRuntimeAssets.AttachCargo(root);
            var contents = root.transform.Find("CargoContents"); var rows = spec.Length > 3 ? 4 : 2;
            contents.GetComponent<StockpileMeshBuilder>().contentDimensions = new Vector3Int(2, 1, rows);
            contents.localPosition = new Vector3(0, .70f, 0); contents.localScale = new Vector3(width * .40f, .50f, (length - .1f) / rows);
            root.GetComponent<MoveThroughSounds>().OverlapCheck = Hit(root.transform, "CargoBucket", new Vector3(0, .77f, 0), new Vector3(width * .94f, .72f, length), "MinecartStorage");
            var exhaust = root.transform.Find("SteamExhaust"); if (exhaust != null) exhaust.gameObject.SetActive(false);
        }
        private static GameObject Infrastructure(string key, IReadOnlyDictionary<string, Material> materials)
        {
            var root = new GameObject(key + "Object"); root.tag = "ModObject"; root.AddComponent<WorldObject>(); root.AddComponent<HighlightableObject>();
            var iron = materials["MAT_IronBare"]; var wood = materials["MAT_WoodRail"]; var paint = materials["MAT_IronPainted"];
            if (key == "WideRailTurn" || key == "TramWideRailTurn")
            {
                foreach(var radius in new[]{2.2f,2.8f}){
                    var angles=Enumerable.Range(0,97).Select(i=>Mathf.PI-i*Mathf.PI/192).ToArray();
                    RailSectionMesh.Create(root.transform,key+radius.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        angles.Select(a=>new Vector3(1.5f+radius*Mathf.Cos(a),-.35f,-1.5f+radius*Mathf.Sin(a))).ToArray(),
                        angles.Select(a=>new Vector3(-Mathf.Cos(a),0,-Mathf.Sin(a))).ToArray(),.055f,.06f,iron);
                }
                for (var i = 0; i < 48; i++)
                {
                    var a = Mathf.PI - i * Mathf.PI / 96; var b = Mathf.PI - (i + 1) * Mathf.PI / 96;
                    Vector3 Point(float radius, float angle) => new Vector3(1.5f + radius * Mathf.Cos(angle), -.35f, -1.5f + radius * Mathf.Sin(angle));
                    if (i % 3 == 0)
                    {
                        var tie = Box(root.transform, key=="TramWideRailTurn"?"Street tie":"Sleeper", Point(2.5f, (a + b) / 2) - Vector3.up * .08f,
                            new Vector3(.85f, key=="TramWideRailTurn"?.045f:.07f, key=="TramWideRailTurn"?.08f:.12f),
                            key=="TramWideRailTurn"?iron:wood);
                        tie.rotation = Quaternion.LookRotation(Point(2.5f, b) - Point(2.5f, a));
                    }
                    var from = Point(2.5f, a); var to = Point(2.5f, b);
                    var collider = Hit(root.transform, "ContinuousDeck", (from + to) / 2 - Vector3.up * .025f,
                        new Vector3(.85f, .05f, Vector3.Distance(from, to) + .015f), null);
                    collider.transform.rotation = Quaternion.LookRotation(to - from);
                }
            }
            else if (key == "BrokenWoodenTrack")
            {
                for (var i = 0; i < 7; i++) { var splinter = Box(root.transform, "Broken timber", new Vector3((i % 3 - 1) * .22f, -.38f, (i / 3 - 1) * .28f), new Vector3(.12f, .06f, .48f), wood); splinter.localRotation = Quaternion.Euler(0, i * 47, i % 2 * 9); }
                Hit(root.transform, "BrokenTrack", new Vector3(0, -.37f, 0), new Vector3(.9f, .18f, .9f), null);
            }
            else if(key=="TramStop")
            {
                Box(root.transform,"Street-side base",new Vector3(0,-.43f,0),new Vector3(.78f,.12f,.78f),wood);
                Box(root.transform,"Stop post",new Vector3(0,.54f,0),new Vector3(.085f,1.85f,.085f),iron);
                Box(root.transform,"Stop sign",new Vector3(0,1.48f,0),new Vector3(.69f,.39f,.075f),paint);
                Box(root.transform,"Timetable case",new Vector3(0,.79f,.06f),new Vector3(.42f,.53f,.10f),wood);
                Hit(root.transform,"TramStopBase",new Vector3(0,-.43f,0),new Vector3(.78f,.12f,.78f),null);
                Hit(root.transform,"TramStopPost",new Vector3(0,.54f,0),new Vector3(.12f,1.85f,.12f),null);
                Hit(root.transform,"TramStopSign",new Vector3(0,1.48f,0),new Vector3(.69f,.39f,.10f),null);
            }
            else if(key=="TramCableDrive")
            {
                Box(root.transform,"Drive plinth",new Vector3(0,-.41f,0),new Vector3(.82f,.17f,.82f),iron);
                Box(root.transform,"Drive cabinet",new Vector3(0,.13f,0),new Vector3(.67f,.90f,.65f),paint);
                var pivot=Anchor(root,"CableDrumPivot",new Vector3(0,.70f,0));
                Cylinder(pivot,"Cable drum",Vector3.zero,.27f,.50f,Quaternion.Euler(0,0,90),iron);
                Cylinder(pivot,"Drum shaft",Vector3.zero,.07f,.77f,Quaternion.Euler(0,0,90),wood);
                Box(root.transform,"Power gauge",new Vector3(0,.39f,.34f),new Vector3(.33f,.16f,.04f),wood);
                Hit(root.transform,"TramDriveCabinet",new Vector3(0,.13f,0),new Vector3(.72f,.98f,.70f),null);
                var clip=new AnimationClip{legacy=true,wrapMode=WrapMode.Loop};
                clip.SetCurve("CableDrumPivot",typeof(Transform),"localEulerAngles.x",AnimationCurve.Linear(0,0,1,360));
                var clipPath=Root+"/Prefabs/TramCableDrive.anim";
                var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if(saved==null){AssetDatabase.CreateAsset(clip,clipPath);saved=clip;}
                else{EditorUtility.CopySerialized(clip,saved);Object.DestroyImmediate(clip);}
                var animation=root.AddComponent<Animation>();animation.AddClip(saved,"CableRun");animation.clip=saved;animation.playAutomatically=true;animation.enabled=false;
                var world=root.GetComponent<WorldObject>();var index=world.States.Length;
                var states=world.States;var changed=world.OnStateChangedEvents;var on=world.OnStateEnabledEvents;var off=world.OnStateDisabledEvents;
                Array.Resize(ref states,index+1);Array.Resize(ref changed,index+1);Array.Resize(ref on,index+1);Array.Resize(ref off,index+1);
                states[index]="CableRunning";changed[index]=new ChangedStateEvent();on[index]=new SetStateEvent();off[index]=new SetStateEvent();
                var setter=(UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),animation,typeof(Animation).GetProperty("enabled").GetSetMethod());
                UnityEventTools.AddPersistentListener(changed[index],setter);
                world.States=states;world.OnStateChangedEvents=changed;world.OnStateEnabledEvents=on;world.OnStateDisabledEvents=off;
            }
            else
            {
                Box(root.transform, "Platform", new Vector3(0, .06f, 0), new Vector3(.85f, .12f, .85f), wood);
                Box(root.transform, "Station post", new Vector3(0, .85f, 0), new Vector3(.10f, 1.60f, .10f), wood);
                Box(root.transform, "Station board", new Vector3(0, 1.35f, 0), new Vector3(.8f, .42f, .08f), iron);
                Box(root.transform, "Control cabinet", new Vector3(0, .5f, .1f), new Vector3(.48f, .52f, .25f), iron);
                // Match the visible pieces: the former broad box missed the
                // upper sign and blocked otherwise empty space beside the post.
                Hit(root.transform, "StationPlatformCollision", new Vector3(0, .06f, 0), new Vector3(.85f, .12f, .85f), null);
                Hit(root.transform, "StationPostCollision", new Vector3(0, .85f, 0), new Vector3(.10f, 1.60f, .10f), null);
                Hit(root.transform, "StationSignCollision", new Vector3(0, 1.35f, 0), new Vector3(.8f, .42f, .08f), null);
                Hit(root.transform, "StationInteraction", new Vector3(0, .5f, .1f), new Vector3(.48f, .52f, .25f), null);
            }
            return Save(root);
        }
        private static Transform Anchor(GameObject root, string name, Vector3 position)
        { var node = new GameObject(name).transform; node.SetParent(root.transform, false); node.localPosition = position; return node; }
        private static BoxCollider Hit(Transform root, string name, Vector3 position, Vector3 size, string target, string value = "")
        {
            var node = new GameObject(name); node.transform.SetParent(root, false); node.transform.localPosition = position;
            var collider = node.AddComponent<BoxCollider>(); collider.size = size;
            if (target != null) { var interaction = node.AddComponent<SpecificInteractable>(); interaction.interactionTargetName = target; interaction.interactionTargetValue = value; }
            return collider;
        }
        private static Transform Box(Transform root, string name, Vector3 position, Vector3 size, Material material)
        { return Primitive(root, name, PrimitiveType.Cube, position, size, Quaternion.identity, material); }
        private static Transform Cylinder(Transform root, string name, Vector3 position, float radius, float length, Quaternion rotation, Material material)
        { return Primitive(root, name, PrimitiveType.Cylinder, position, new Vector3(radius * 2, length / 2, radius * 2), rotation, material); }
        private static Transform Primitive(Transform root, string name, PrimitiveType type, Vector3 position, Vector3 size, Quaternion rotation, Material material)
        {
            var node = GameObject.CreatePrimitive(type); node.name = name; node.transform.SetParent(root, false);
            node.transform.localPosition = position; node.transform.localScale = size; node.transform.localRotation = rotation;
            node.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(node.GetComponent<Collider>()); return node.transform;
        }
        private static GameObject Save(GameObject root)
        { var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + root.name + ".prefab"); Object.DestroyImmediate(root); return prefab; }
    }
}
