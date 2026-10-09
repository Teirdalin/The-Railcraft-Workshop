using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    // Only native Unity Animators and Eco state events are shipped in the bundle.
    public static class RailWheelAnimationBuilder
    {
        const string Folder="Assets/EcoMinecarts/Animations";
        public static void Apply()
        {
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/EcoMinecarts","Animations");
            int count=0;
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/EcoMinecarts/Prefabs"}))
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(asset.GetComponent<Vehicle>()==null)continue;
                var root=PrefabUtility.LoadPrefabContents(path);
                try{RailMechanicalAnimationBuilder.Prepare(root);Configure(root);RailMechanicalAnimationBuilder.CrankPins(root);RailDumpAnimationBuilder.Configure(root);RailMechanicalAnimationBuilder.Paint(root);PrefabUtility.SaveAsPrefabAsset(root,path);count++;}
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();
            Debug.Log("RAIL_WHEEL_ASSETS_OK: "+count+" vehicles; independent visual axles, signed motion, radius-scaled native animation.");
        }
        internal static void Configure(GameObject root)
        {
            var drive=root.GetComponent<RCCCarControllerV2>();
            var colliders=new[]{drive.FrontLeftWheelCollider,drive.FrontRightWheelCollider,drive.RearLeftWheelCollider,drive.RearRightWheelCollider};
            var poses=new[]{drive.FrontLeftWheelTransform,drive.FrontRightWheelTransform,drive.RearLeftWheelTransform,drive.RearRightWheelTransform};
            var world=root.GetComponent<WorldObject>();
            int rate=FloatIndex(world,"RailWheelSpeed"),direction=StringIndex(world,"RailWheelDirection"),moving=BoolIndex(world,"RailWheelsMoving");
            world.OnFloatStateChanged[rate]=new ChangedFloatStateEvent();
            world.OnStringStateChanged[direction]=new ChangedStringStateEvent();
            world.OnStateChangedEvents[moving]=new ChangedStateEvent();
            for(int i=0;i<4;i++)
            {
                string suffix=new[]{"FL","FR","RL","RR"}[i];
                var spin=root.transform.Find("WheelRoll_"+suffix);
                if(spin==null)
                {
                    spin=new GameObject("WheelRoll_"+suffix).transform;spin.SetParent(root.transform,false);
                    spin.localPosition=root.transform.InverseTransformPoint(colliders[i].transform.position);
                    if(root.name=="MinecartObject")
                    {
                        var mesh=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Wheel_"+suffix);
                        mesh.SetParent(spin,true);
                    }
                    else foreach(var child in poses[i].Cast<Transform>().ToArray())child.SetParent(spin,true);
                    // RCC may still update native wheel poses for the handcar. Its
                    // pose nodes no longer own meshes, avoiding two rotation writers.
                }
                EnsureVisualPivot(spin);
                var animator=spin.GetComponent<Animator>();if(animator==null)animator=spin.gameObject.AddComponent<Animator>();
                var mechanism=RailMechanicalAnimationBuilder.Motion(root,spin,i,colliders[i].radius);
                animator.runtimeAnimatorController=Controller(colliders[i].radius,false,mechanism==null?"":root.name+suffix,mechanism);
                animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;
                animator.speed=0;animator.enabled=false;
                animator.keepAnimatorStateOnDisable=true;
                UnityEventTools.AddPersistentListener(world.OnFloatStateChanged[rate],animatorRate(animator));
                UnityEventTools.AddPersistentListener(world.OnStringStateChanged[direction],animator.Play);
                UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[moving],animatorEnabled(animator));
                if(spin.GetComponentsInChildren<Renderer>(true).Length==0)throw new Exception("Wheel animation has no mesh: "+root.name+suffix);
            }
            // A second export also contains the copied legacy geometry. Never lift
            // those rollers out of their design branch into the shared wheel rig.
            var candidates=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Upstop roller"&&!LegacyBranch(t,root.transform)).ToArray();
            var unique=new System.Collections.Generic.List<Transform>();
            foreach(var roller in candidates)
            {
                if(unique.Any(other=>(other.position-roller.position).sqrMagnitude<.000001f))
                    UnityEngine.Object.DestroyImmediate(roller.gameObject);
                else unique.Add(roller);
            }
            var rollers=unique.ToArray();
            foreach(var roller in rollers)roller.SetParent(root.transform,true);
            foreach(var host in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("UpstopRoll_")).ToArray())
                if(host!=null)UnityEngine.Object.DestroyImmediate(host.gameObject);
            int rollerIndex=0;
            foreach(var roller in rollers)
            {
                var spin=new GameObject("UpstopRoll_"+rollerIndex).transform;spin.SetParent(root.transform,false);
                spin.localPosition=roller.localPosition;roller.SetParent(spin,true);
                rollerIndex++;
                EnsureVisualPivot(spin);
                var animator=spin.GetComponent<Animator>();if(animator==null)animator=spin.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController=Controller(.05f,true);animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.CullUpdateTransforms;animator.speed=0;animator.enabled=false;animator.keepAnimatorStateOnDisable=true;
                UnityEventTools.AddPersistentListener(world.OnFloatStateChanged[rate],animatorRate(animator));
                UnityEventTools.AddPersistentListener(world.OnStringStateChanged[direction],animator.Play);
                UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[moving],animatorEnabled(animator));
            }
        }
        static bool LegacyBranch(Transform node,Transform root)
        {
            for(var ancestor=node.parent;ancestor!=null&&ancestor!=root;ancestor=ancestor.parent)
                if(ancestor.name.StartsWith("LegacyDesignGeometry"))return true;
            return false;
        }
        static UnityEngine.Events.UnityAction<float> animatorRate(Animator animator)=>
            (UnityEngine.Events.UnityAction<float>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<float>),animator,typeof(Animator).GetProperty("speed").GetSetMethod());
        static UnityEngine.Events.UnityAction<bool> animatorEnabled(Animator animator)=>
            (UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),animator,typeof(Behaviour).GetProperty("enabled").GetSetMethod());
        static void EnsureVisualPivot(Transform host)
        {
            if(host.Find("Spin")!=null)return;
            var visuals=host.Cast<Transform>().ToArray();
            var pivot=new GameObject("Spin").transform;pivot.SetParent(host,false);
            foreach(var child in visuals)child.SetParent(pivot,true);
        }
        static RuntimeAnimatorController Controller(float radius,bool upstop=false,string identity="",AnimationClip mechanism=null)
        {
            var key=Mathf.RoundToInt(radius*1000).ToString()+(upstop?"Upstop":"")+identity;
            var clipPath=Folder+"/RailWheel"+key+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,clipPath);}
            foreach(var binding in AnimationUtility.GetCurveBindings(clip))AnimationUtility.SetEditorCurve(clip,binding,null);
            float circumference=2*Mathf.PI*radius;
            var curve=new AnimationCurve(Enumerable.Range(0,5).Select(i=>new Keyframe(circumference*i/4,(upstop?-1:1)*90*i)).ToArray());
            for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
            clip.SetCurve("Spin",typeof(Transform),"localEulerAnglesRaw.x",curve);
            if(mechanism!=null)foreach(var binding in AnimationUtility.GetCurveBindings(mechanism))
                AnimationUtility.SetEditorCurve(clip,binding,AnimationUtility.GetEditorCurve(mechanism,binding));
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
            var path=Folder+"/RailWheel"+key+".controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(path);
            if(controller.layers.Length==0)controller.AddLayer("Base Layer");
            var machine=controller.layers[0].stateMachine;
            foreach(var name in new[]{"RollForward","RollReverse"})
            {
                var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??machine.AddState(name);
                state.motion=clip;state.speed=name=="RollForward"?1:-1;state.writeDefaultValues=false;
                if(name=="RollForward")machine.defaultState=state;
            }
            EditorUtility.SetDirty(clip);EditorUtility.SetDirty(controller);return controller;
        }
        static int FloatIndex(WorldObject w,string name)
        {
            int i=Array.IndexOf(w.FloatStates,name);if(i>=0)return i;
            var names=w.FloatStates;var events=w.OnFloatStateChanged;i=names.Length;
            Array.Resize(ref names,i+1);Array.Resize(ref events,i+1);names[i]=name;
            w.FloatStates=names;w.OnFloatStateChanged=events;return i;
        }
        internal static int StringIndex(WorldObject w,string name)
        {
            int i=Array.IndexOf(w.StringStates,name);if(i>=0)return i;
            var names=w.StringStates;var events=w.OnStringStateChanged;i=names.Length;
            Array.Resize(ref names,i+1);Array.Resize(ref events,i+1);names[i]=name;
            w.StringStates=names;w.OnStringStateChanged=events;return i;
        }
        internal static int BoolIndex(WorldObject w,string name)
        {
            int i=Array.IndexOf(w.States,name);if(i>=0)return i;
            var names=w.States;var events=w.OnStateChangedEvents;var on=w.OnStateEnabledEvents;var off=w.OnStateDisabledEvents;i=names.Length;
            Array.Resize(ref names,i+1);Array.Resize(ref events,i+1);Array.Resize(ref on,i+1);Array.Resize(ref off,i+1);
            names[i]=name;on[i]=new SetStateEvent();off[i]=new SetStateEvent();
            w.States=names;w.OnStateChangedEvents=events;w.OnStateEnabledEvents=on;w.OnStateDisabledEvents=off;return i;
        }
    }
}
