using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    // Authoring-only fixes. Archived original artwork remains untouched.
    public static class RailPresentationPolish
    {
        public const float HandcarOperatorLift=.12f;
        static void HandcarOperatorFit(GameObject root,bool modern=false)
        {
            if(root.name!="RailroadHandcarObject")return;
            var seat=root.GetComponent<Mountable>().seats[0];var p=seat.transform.localPosition;p.y=.49f+HandcarOperatorLift+(modern?.08f:0);p.z=modern?-.91f:-.69f;seat.transform.localPosition=p;
            foreach(var target in seat.IKTargets){p=target.transform.localPosition;p.y=modern?.067f:HandcarOperatorLift;p.z=modern?-.47f:-.48f;target.transform.localPosition=p;}
        }
        static Transform Box(Transform parent,string name,Vector3 position,Vector3 size,Material material)
        {
            var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.transform.SetParent(parent,false);
            part.transform.localPosition=position;part.transform.localScale=size;
            Object.DestroyImmediate(part.GetComponent<Collider>());part.GetComponent<Renderer>().sharedMaterial=material;return part.transform;
        }
        public static void RemoveBearings(GameObject root)
        {
            foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Dump hinge bearing ")).ToArray())Object.DestroyImmediate(t.gameObject);
        }
        public static void Original(GameObject root)
        {
            TrainLampPresentation.Configure(root);
            HandcarOperatorFit(root);
            RemoveBearings(root);
            if(RailMechanicalAnimationBuilder.Engines.Contains(root.name))
                foreach(var bar in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Smokebox locking bar"))
                    bar.position+=root.transform.forward*.006f;
            var mounts=root.GetComponent<Mountable>();
            if(root.name=="HeritageTramObject")TramLampPresentation.Configure(root);
            if(root.name=="LargeTrainEngineObject"){
                var boiler=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Steam boiler");
                var floor=root.transform.Find("CabFittings/Cab floor").GetComponent<BoxCollider>();
                Physics.SyncTransforms();float rear=boiler.bounds.min.z-root.transform.position.z;
                float cabFront=floor.bounds.max.z-root.transform.position.z;
                float length=rear-cabFront+.04f;
                if(length>.04f){
                    var neck=new GameObject("Firebox neck",typeof(MeshFilter),typeof(MeshRenderer));neck.transform.SetParent(root.transform,false);
                    neck.GetComponent<MeshFilter>().sharedMesh=boiler.GetComponent<MeshFilter>().sharedMesh;neck.GetComponent<MeshRenderer>().sharedMaterials=boiler.sharedMaterials;
                    neck.transform.localPosition=new Vector3(0,boiler.transform.position.y-root.transform.position.y,(rear+cabFront)/2);
                    neck.transform.localRotation=boiler.transform.rotation;neck.transform.localScale=new Vector3(boiler.transform.lossyScale.x,length/2,boiler.transform.lossyScale.z);
                }
            }
            if(root.name=="HeritageTramObject"){
                var bench=root.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name=="Passenger bench");
                foreach(var seat in mounts.seats.Skip(5)){
                    var p=seat.transform.localPosition;p.y=mounts.seats[1].transform.localPosition.y;seat.transform.localPosition=p;
                    seat.overrideAvatarState=(Eco.Animation.AnimationStateManager.AvatarState)3;
                    seat.transform.localRotation=Quaternion.Euler(0,0,0);
                    var support=new GameObject("Centre tram chair "+seat.name).transform;support.SetParent(root.transform,false);
                    float top=p.y+RailRiderFit.SeatedHipHeight-RailRiderFit.TramRiderLift;
                    Box(support,"Centre chair cushion",new Vector3(p.x,top-.04f,p.z),new Vector3(.34f,.08f,.35f),bench.sharedMaterial);
                    Box(support,"Centre chair back",new Vector3(p.x,top+.17f,p.z-.15f),new Vector3(.34f,.35f,.045f),bench.sharedMaterial);
                    foreach(var x in new[]{-.12f,.12f})Box(support,"Centre chair leg",new Vector3(p.x+x,(top-.08f+.54f)/2,p.z),new Vector3(.045f,top-.08f-.54f,.22f),bench.sharedMaterial);
                }
            }
            bool coach=root.name=="PassengerCarObject"||root.name=="LargePassengerCarObject";
            if(coach||root.name=="RollerCoasterCartObject"){
                foreach(var seat in mounts.seats.Skip(1))seat.transform.localPosition+=Vector3.up*.20f;
                if(coach){
                    // The visible curved roof is a batched detail mesh; moving
                    // only the retired box left the pillars through its surface.
                    var visibleRoof=root.transform.Find("VehicleDetail/Roof");
                    if(visibleRoof!=null)visibleRoof.localPosition+=Vector3.up*.20f;
                    foreach(var roofCollider in root.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.name.StartsWith("Passenger roof")))
                        roofCollider.transform.localPosition+=Vector3.up*.20f;
                    foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Coach roof"||t.name=="Window pillar")){
                        t.localPosition+=Vector3.up*(t.name=="Coach roof"?.20f:.10f);
                        if(t.name=="Window pillar")t.localScale+=Vector3.up*.20f;
                    }
                    var vehicle=root.GetComponent<Vehicle>();vehicle.size+=Vector3.up*.20f;
                }
            }
            // The linkage meshes must follow their existing shared animated transforms.
            if(root.name=="RailroadHandcarObject")foreach(var name in new[]{"PumpRod","PumpSlider"}){
                var t=root.transform.Find(name);var r=t?.GetComponent<MeshRenderer>();if(r==null)continue;
                var child=new GameObject("Linkage render",typeof(MeshFilter),typeof(MeshRenderer));child.transform.SetParent(t,false);
                child.GetComponent<MeshFilter>().sharedMesh=t.GetComponent<MeshFilter>().sharedMesh;child.GetComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;
                Object.DestroyImmediate(r);Object.DestroyImmediate(t.GetComponent<MeshFilter>());
            }
        }
        public static void Modern(GameObject root)
        {
            HandcarOperatorFit(root,true);
            RemoveBearings(root);
            if(root.name=="RailroadHandcarObject"){
                // The imported body pass hides procedural static fittings, but
                // the shared rod still terminates at this animated beam pin.
                foreach(var pin in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Linkage upper pin"))
                {
                    var renderers=pin.GetComponentsInChildren<MeshRenderer>(true);
                    if(renderers.Length==0){
                        var bearing=GameObject.CreatePrimitive(PrimitiveType.Cylinder);bearing.name="Moving beam bearing";
                        bearing.transform.SetParent(pin,false);bearing.transform.localScale=Vector3.one;
                        Object.DestroyImmediate(bearing.GetComponent<Collider>());
                        bearing.GetComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronBare.mat");
                    }
                    foreach(var r in pin.GetComponentsInChildren<MeshRenderer>(true))r.enabled=true;
                }
            }
            if(root.name=="HeritageTramObject")TramLampPresentation.Configure(root);
            if(root.name!="RollerCoasterCartObject"&&root.name!="PassengerCarObject"&&root.name!="LargePassengerCarObject")return;
            foreach(var seat in root.GetComponent<Mountable>().seats.Skip(1)){
                var marker=root.transform.Find("PreparedVehicleArt/SeatSurface_"+seat.name);
                if(marker!=null)seat.transform.position=marker.position+root.transform.up*(RailPreparedVehicleFit.PassengerLift(root)-RailRiderFit.SeatedHipHeight);
            }
        }
        public static void PreparePumpRig(GameObject root)
        {
            if(root.name!="RailroadHandcarObject")return;
            var host=root.transform.Find("Handcar pump animation");
            if(host==null){host=new GameObject("Handcar pump animation").transform;host.SetParent(root.transform,false);}
            foreach(var name in new[]{"PumpPivot","PumpRod","PumpSlider"}){var t=root.transform.Find(name);if(t!=null)t.SetParent(host,true);}
        }
        public static void Pump(GameObject root,AnimatorController designController,Animator designAnimator)
        {
            if(root.name!="RailroadHandcarObject")return;
            // Eco owns the vehicle-root Animator. Keep the mechanism on its own
            // native child rig, like the existing working wheel animators.
            designController.layers=designController.layers.Where(l=>l.name!="Handcar pump").ToArray();
            designController.parameters=designController.parameters.Where(p=>p.name!="Pumping"&&p.name!="PumpOn"&&p.name!="PumpOff").ToArray();
            var host=root.transform.Find("Handcar pump animation");
            var animator=host.GetComponent<Animator>();if(animator==null)animator=host.gameObject.AddComponent<Animator>();
            const string path="Assets/EcoMinecarts/Animations/Designs/HandcarMechanism.controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path)??AnimatorController.CreateAnimatorControllerAtPath(path);
            if(controller.layers.Length==0)controller.AddLayer("Base Layer");
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/EcoMinecarts/Prefabs/HandcarPump.anim");
            var idlePath="Assets/EcoMinecarts/Animations/Designs/HandcarParked.anim";
            var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(idlePath);if(idle==null){idle=new AnimationClip();AssetDatabase.CreateAsset(idle,idlePath);}
            foreach(var binding in AnimationUtility.GetCurveBindings(clip))AnimationUtility.SetEditorCurve(idle,binding,AnimationCurve.Constant(0,1,AnimationUtility.GetEditorCurve(clip,binding).Evaluate(0)));
            var sm=controller.layers[0].stateMachine;
            foreach(var t in sm.anyStateTransitions)sm.RemoveAnyStateTransition(t);
            foreach(var name in new[]{"Parked","Pumping"}){
                var state=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??sm.AddState(name);state.motion=name=="Parked"?idle:clip;state.writeDefaultValues=false;
                if(name=="Parked")sm.defaultState=state;
            }
            var world=root.GetComponent<WorldObject>();int slot=Array.IndexOf(world.States,"HandcarPumping");
            world.OnStateChangedEvents[slot]=new ChangedStateEvent();world.OnStateEnabledEvents[slot]=new SetStateEvent();world.OnStateDisabledEvents[slot]=new SetStateEvent();
            foreach(var name in new[]{"PumpOn","PumpOff"}){
                if(!controller.parameters.Any(p=>p.name==name))controller.AddParameter(name,AnimatorControllerParameterType.Trigger);
                var state=sm.states.Single(s=>s.state.name==(name=="PumpOn"?"Pumping":"Parked")).state;
                var t=sm.AddAnyStateTransition(state);t.hasExitTime=false;t.duration=.15f;t.canTransitionToSelf=false;t.AddCondition(AnimatorConditionMode.If,0,name);
            }
            int pose=RailWheelAnimationBuilder.StringIndex(world,"RailPumpPose");world.OnStringStateChanged[pose]=new ChangedStringStateEvent();
            foreach(var name in new[]{"PumpOn","PumpOff"})UnityEventTools.AddStringPersistentListener(world.OnStringStateChanged[pose],animator.ResetTrigger,name);
            UnityEventTools.AddPersistentListener(world.OnStringStateChanged[pose],animator.SetTrigger);
            animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.enabled=true;
            idle.SampleAnimation(host.gameObject,0);EditorUtility.SetDirty(idle);EditorUtility.SetDirty(controller);
        }
    }
}
