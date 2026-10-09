using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEngine;
namespace EcoMinecarts.Editor
{
    public static class RailDumpAnimationBuilder
    {
        internal static readonly string[] Buckets={"MinecartObject","WoodenMinecartObject","CoalTenderObject","LargeCoalTenderObject","LargeCargoCarObject"};
        const string Folder="Assets/EcoMinecarts/Animations";
        internal static void Configure(GameObject root)
        {
            if(!Buckets.Contains(root.name))return;
            var hinge=root.transform.Find("DumpAnimation/DumpHinge")??root.transform.Find("DumpHinge");
            if(hinge==null)
            {
                hinge=new GameObject("DumpHinge").transform;hinge.SetParent(root.transform,false);
                var bucket=new GameObject("Bucket").transform;bucket.SetParent(hinge,false);
                var names=new[]{"Minecart_Body","Side plank","End plank","Floor plank","Timber brace","Cargo side","End wall","Hopper reinforcing rib","Body top cap","Body seam rivet","CargoContents"};
                foreach(var n in root.GetComponentsInChildren<Transform>(true).Where(t=>names.Contains(t.name)).ToArray())n.SetParent(bucket,true);
                var trim=root.transform.Find("VehicleDetail/Body");if(trim!=null)trim.SetParent(bucket,true);
                // Generated hoppers formerly had side walls but no visible floor.
                if(root.name!="MinecartObject"&&root.name!="WoodenMinecartObject")
                {
                    var drive=root.GetComponent<RCCCarControllerV2>();
                    var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Hopper floor";floor.transform.SetParent(bucket,false);
                    floor.transform.localPosition=new Vector3(0,.42f,0);
                    floor.transform.localScale=new Vector3(Mathf.Abs(drive.FrontLeftWheelCollider.transform.localPosition.x)*2.5f,.045f,root.GetComponent<WorldObject>().size.z-.45f);
                    floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronBare.mat");UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
                }
            }
            var host=root.transform.Find("DumpAnimation");if(host==null){host=new GameObject("DumpAnimation").transform;host.SetParent(root.transform,false);}
            var old=hinge.GetComponent<Animator>();if(old!=null)UnityEngine.Object.DestroyImmediate(old);
            hinge.SetParent(host,true);
            var existingFloor=hinge.Find("Bucket/Hopper floor");if(existingFloor!=null){var scale=existingFloor.localScale;scale.x=2*HalfWidth(root);existingFloor.localScale=scale;}
            RailPresentationPolish.RemoveBearings(root);
            var animated=host.GetComponent<Animator>();if(animated==null)animated=host.gameObject.AddComponent<Animator>();
            animated.runtimeAnimatorController=Controller(root);animated.applyRootMotion=false;animated.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var world=root.GetComponent<WorldObject>();int state=RailWheelAnimationBuilder.StringIndex(world,"RailDumpPose");
            world.OnStringStateChanged[state]=new ChangedStringStateEvent();UnityEventTools.AddPersistentListener(world.OnStringStateChanged[state],animated.SetTrigger);
        }
        static RuntimeAnimatorController Controller(GameObject root)
        {
            var path=Folder+"/"+root.name+"Dump.controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(path);
            if(controller.layers.Length==0)controller.AddLayer("Base Layer");
            var machine=controller.layers[0].stateMachine;
            foreach(var prior in machine.anyStateTransitions)machine.RemoveAnyStateTransition(prior);
            var coll=root.GetComponent<RCCCarControllerV2>().FrontLeftWheelCollider;
            float halfWidth=HalfWidth(root);
            float height=HingeHeight(root);
            foreach(int side in new[]{-1,1})for(int step=0;step<=28;step++)
            {
                var name="Dump"+(side<0?"Left":"Right")+step.ToString("D2");
                var clipPath=Folder+"/"+root.name+name+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,clipPath);}
                void Constant(string binding,string property,float value)=>clip.SetCurve(binding,typeof(Transform),property,AnimationCurve.Constant(0,1,value));
                foreach(var binding in AnimationUtility.GetCurveBindings(clip))AnimationUtility.SetEditorCurve(clip,binding,null);
                Constant("DumpHinge","localPosition.x",side*halfWidth);Constant("DumpHinge","localPosition.y",height);
                Constant("DumpHinge","localEulerAnglesRaw.z",-side*step*2.5f);
                Constant("DumpHinge/Bucket","localPosition.x",-side*halfWidth);Constant("DumpHinge/Bucket","localPosition.y",-height);
                var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??machine.AddState(name);
                state.motion=clip;state.speed=0;state.writeDefaultValues=false;
                if(side==1&&step==0)machine.defaultState=state;
                if(!controller.parameters.Any(p=>p.name==name))controller.AddParameter(name,AnimatorControllerParameterType.Trigger);
                var transition=machine.AddAnyStateTransition(state);transition.hasExitTime=false;transition.hasFixedDuration=true;transition.duration=.055f;
                transition.canTransitionToSelf=true;transition.interruptionSource=TransitionInterruptionSource.Destination;
                transition.AddCondition(AnimatorConditionMode.If,0,name);
                EditorUtility.SetDirty(clip);
            }
            EditorUtility.SetDirty(controller);return controller;
        }
        internal static float HingeHeight(GameObject root)=>root.transform.Find("PreparedVehicleArt")!=null?.42f:root.name=="MinecartObject"||root.name=="WoodenMinecartObject"?.29f:.42f;
        internal static float HalfWidth(GameObject root)
        {
            if(root.name=="MinecartObject"||root.name=="WoodenMinecartObject")return .34f;
            return root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Cargo side")
                .Select(t=>Mathf.Abs(root.transform.InverseTransformPoint(t.position).x)).DefaultIfEmpty(.4f).Max();
        }
    }
}
