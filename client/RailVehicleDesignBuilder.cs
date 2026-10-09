using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    // One saved identity, physics rig and wheel/dump animation rig. The design
    // snapshot selects artwork and the corresponding native seat/target fit.
    // Only native Unity animation and Eco state events ship to clients.
    public static class RailVehicleDesignBuilder
    {
        const string Root="Assets/EcoMinecarts";
        const string Sources=Root+"/LegacyVehicleSources";
        static string Repo=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
        static string PathOf(Transform t,Transform root)=>AnimationUtility.CalculateTransformPath(t,root);
        internal static bool AnimatedControl(Transform t,Transform root)
        {for(var p=t;p!=null&&p!=root;p=p.parent)if(p.name=="ThrottleAnimation"||p.name=="BrakeAnimation"||p.name=="ReverserAnimation")return true;return false;}
        static Dictionary<string,Material> Materials()=>AssetDatabase.FindAssets("t:Material",new[]{Root+"/Materials"}).Select(g=>AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).GroupBy(m=>m.name).ToDictionary(g=>g.Key,g=>g.First());
        static void RestoreModern(GameObject root)
        {
            var previous=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animations/Designs/"+root.name+"ModernDesign.anim");
            if(previous!=null)previous.SampleAnimation(root,0);
            var world=root.GetComponent<WorldObject>();int flag=Array.IndexOf(world.States,"RailLegacyDesign");if(flag<0)return;
            var callback=world.OnStateDisabledEvents[flag];
            var states=Enumerable.Range(0,callback.GetPersistentEventCount()).Select(i=>callback.GetPersistentListenerState(i)).ToArray();
            try{for(int i=0;i<states.Length;i++)callback.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);callback.Invoke();}
            finally{for(int i=0;i<states.Length;i++)callback.SetPersistentListenerState(i,states[i]);}
        }
        public static void RestoreModernAuthoringFits()
        {
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"})){
                var path=AssetDatabase.GUIDToAssetPath(guid);if(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Vehicle>()==null)continue;
                var root=PrefabUtility.LoadPrefabContents(path);try{RestoreModern(root);PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
            }
        }
        public static void Prepare()
        {
            Directory.CreateDirectory(Sources);
            foreach(var file in Directory.GetFiles(Path.Combine(Repo,"validation/backups/vehicle-integration-20261007-125015/Prefabs"),"*.prefab"))
            {
                var name=Path.GetFileNameWithoutExtension(file);
                if(!RailExpansionAssetBuilder.ReadCatalog().Vehicles.Any(s=>s.Key+"Object"==name)&&name!="MinecartObject"&&name!="MineTrainObject")continue;
                string destination=Sources+"/"+name+".prefab";
                if(!File.Exists(destination))File.Copy(file,destination);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // Rebuild wood from the raw standard-cart constructor, not from a
            // previously fitted/retired Meshy prefab or any stale LOD branch.
            {
                // A constructor source must never temporarily overwrite the
                // shipping cart and its tuned native suspension.
                var mats=Materials();var raw=MinecartAssetBuilder.CreateMinecartPrefab(mats,Sources+"/RawMinecartConstructor.prefab");
                var spec=RailExpansionAssetBuilder.ReadCatalog().Vehicles.Single(s=>s.Key=="WoodenMinecart");
                var wood=RailExpansionAssetBuilder.Vehicle(spec,raw,null,mats);
                RailVisualFinish.Apply(new[]{wood},mats);
                var w=PrefabUtility.LoadPrefabContents(Root+"/Prefabs/WoodenMinecartObject.prefab");
                try
                {
                    RailWheelAnimationBuilder.Configure(w);RailDumpAnimationBuilder.Configure(w);
                    RailVehiclePhysicsAssetBuilder.Configure(w);SingleLod(w);
                    PrefabUtility.SaveAsPrefabAsset(w,Root+"/Prefabs/WoodenMinecartObject.prefab");
                    PrefabUtility.SaveAsPrefabAsset(w,Sources+"/WoodenMinecartObject.prefab");
                }
                finally{PrefabUtility.UnloadPrefabContents(w);}
            }
            AssetDatabase.SaveAssets();
            Debug.Log("WOODEN_CART_REBUILT: raw standard cart, shared wheel/dump/physics builders, no inherited LOD branches");
        }
        public static void SingleLod(GameObject root)
        {
            foreach(var group in root.GetComponentsInChildren<LODGroup>(true))
            {
                var lods=group.GetLODs();var keep=lods.Length==0?new HashSet<Renderer>():new HashSet<Renderer>(lods[0].renderers);
                foreach(var r in lods.Skip(1).SelectMany(l=>l.renderers).Where(r=>r!=null&&!keep.Contains(r)).Distinct().ToArray())Object.DestroyImmediate(r.gameObject);
                foreach(var r in keep.Where(r=>r!=null)){r.enabled=true;r.gameObject.SetActive(true);}
                Object.DestroyImmediate(group);
            }
        }
        static bool Shared(Transform t,Transform root)
        {
            string p=PathOf(t,root);
            // Preserve common moving controls; body/cab artwork belongs to a
            // design. Sharing the whole cab leaked prepared details into legacy.
            bool control=p.StartsWith("CabFittings/")&&(p.Contains(" shaft")||p.Contains(" grip"));
            return control||(p.StartsWith("Tram night lamps/")||p.StartsWith("Train automatic lamps/"))||p.Contains("travel gate")||p.Contains("MechanicalLinkage/")||p.Contains("Animated crank pin")||p.Contains("Coupler")||p.Contains("Optional vehicle nameplates/")||p.Contains("Vehicle lettering")||t.GetComponent<TMPro.TMP_Text>()!=null;
        }
        static void Curve(AnimationClip clip,string path,Type type,string field,float value)=>clip.SetCurve(path,type,field,AnimationCurve.Constant(0,1,value));
        static void Vector(AnimationClip clip,string path,Type type,string field,Vector3 value)
        {for(int i=0;i<3;i++)Curve(clip,path,type,field+"."+"xyz"[i],value[i]);}
        static void Fit(AnimationClip modern,AnimationClip old,Transform target,Transform source,Transform root)
        {
            string path=PathOf(target,root);Vector(modern,path,typeof(Transform),"localPosition",target.localPosition);
            Vector(old,path,typeof(Transform),"localPosition",target.parent.InverseTransformPoint(source.position));
            Vector(modern,path,typeof(Transform),"localScale",target.localScale);
            var scale=target.parent.lossyScale;
            Vector(old,path,typeof(Transform),"localScale",new Vector3(source.lossyScale.x/scale.x,source.lossyScale.y/scale.y,source.lossyScale.z/scale.z));
            var rotation=Quaternion.Inverse(target.parent.rotation)*source.rotation;
            for(int i=0;i<4;i++){
                Curve(modern,path,typeof(Transform),"localRotation."+"xyzw"[i],target.localRotation[i]);
                Curve(old,path,typeof(Transform),"localRotation."+"xyzw"[i],rotation[i]);
            }
        }
        internal static BoxCollider OriginalCollider(GameObject original,BoxCollider target,Transform root)
        {
            var exact=original.transform.Find(PathOf(target.transform,root))?.GetComponent<BoxCollider>();
            if(exact!=null)return exact;
            var split=target.name.LastIndexOf("_Design",StringComparison.Ordinal);
            if(split>=0&&int.TryParse(target.name.Substring(split+7),out var index))return original.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.name==target.name.Substring(0,split)).Skip(index).FirstOrDefault();
            var unique=original.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.name==target.name).ToArray();
            if(unique.Length==1)return unique[0]; // Shared animated targets can have a different parent.
            return null;
        }
        static GameObject Geometry(Transform source)
        {
            // Copy only authored transforms and rendering assets. Removing
            // components from a native vehicle violates RequireComponent chains
            // and can retain colliders/network wrappers on the visual branch.
            var result=new GameObject(source.name);var t=result.transform;
            t.localPosition=source.localPosition;t.localRotation=source.localRotation;t.localScale=source.localScale;
            var filter=source.GetComponent<MeshFilter>();var renderer=source.GetComponent<MeshRenderer>();
            if(filter!=null)result.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
            if(renderer!=null){var r=result.AddComponent<MeshRenderer>();r.sharedMaterials=renderer.sharedMaterials;r.enabled=renderer.enabled;r.shadowCastingMode=renderer.shadowCastingMode;r.receiveShadows=renderer.receiveShadows;r.lightProbeUsage=renderer.lightProbeUsage;r.reflectionProbeUsage=renderer.reflectionProbeUsage;r.allowOcclusionWhenDynamic=false;}
            var names=new HashSet<string>();
            foreach(Transform child in source){var copy=Geometry(child);var name=child.name;int index=1;while(!names.Add(name))name=child.name+"_"+index++;copy.name=name;copy.transform.SetParent(t,false);}
            result.SetActive(source.gameObject.activeSelf);return result;
        }
        static void IsolateNativeMeshes(GameObject root)
        {
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.GetComponent<Collider>()!=null||r.GetComponent<SpecificInteractable>()!=null||r.GetComponent<Rigidbody>()!=null||r.name=="PumpRod").ToArray()){
                if(Shared(r.transform,root.transform))continue;
                var child=new GameObject("DesignRender");child.transform.SetParent(r.transform,false);
                var f=r.GetComponent<MeshFilter>();if(f!=null){child.AddComponent<MeshFilter>().sharedMesh=f.sharedMesh;Object.DestroyImmediate(f);}
                var copy=child.AddComponent<MeshRenderer>();EditorUtility.CopySerialized(r,copy);Object.DestroyImmediate(r);
            }
        }
        public static void Apply()
        {
            int count=0;
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(asset.GetComponent<Vehicle>()==null)continue;
                var root=PrefabUtility.LoadPrefabContents(path);GameObject old=null;
                try
                {
                    // The shipping prefab starts in the production design.
                    // Restore its independent modern fit before regenerating
                    // snapshots, so repeated exports cannot overwrite it.
                    RestoreModern(root);
                    RailVehicleModernFit.Configure(root);
                    RailPresentationPolish.PreparePumpRig(root);
                    RailPresentationPolish.Modern(root);
                    TrainLampPresentation.Configure(root);
                    // Remove the previous generated branch before recapturing
                    // current visuals. Repeat exports cannot accumulate designs.
                    foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("LegacyDesignGeometry")).ToArray())if(t!=null)Object.DestroyImmediate(t.gameObject);
                    // Unity binds animation by transform path. Repeated primitive
                    // names otherwise leave all but the first band/beam visible.
                    foreach(var parent in root.GetComponentsInChildren<Transform>(true))
                    foreach(var group in parent.Cast<Transform>().GroupBy(t=>t.name).Where(g=>g.Count()>1).ToArray())
                    {
                        int index=1;foreach(var node in group.Skip(1))node.name=group.Key+"_Design"+index++;
                    }
                    IsolateNativeMeshes(root);
                    var modernRenderers=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>!Shared(r.transform,root.transform)).ToArray();
                    foreach(var renderer in modernRenderers)renderer.gameObject.SetActive(true);
                    old=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Sources+"/"+root.name+".prefab"));old.name=root.name;old.transform.SetPositionAndRotation(root.transform.position,root.transform.rotation);
                    RailPresentationPolish.Original(old);
                    var oldMounts=old.GetComponent<Mountable>();var mounts=root.GetComponent<Mountable>();
                    string folder=Root+"/Animations/Designs";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
                    var clips=new[]{"ModernDesign","LegacyDesign"}.Select(n=>{
                        string cp=folder+"/"+root.name+n+".anim";var c=AssetDatabase.LoadAssetAtPath<AnimationClip>(cp);
                        if(c==null){c=new AnimationClip();AssetDatabase.CreateAsset(c,cp);}
                        foreach(var b in AnimationUtility.GetCurveBindings(c))AnimationUtility.SetEditorCurve(c,b,null);return c;
                    }).ToArray();
                    for(int i=0;i<mounts.seats.Length;i++){
                        var target=mounts.seats[i];var source=oldMounts.seats[i];
                        // Avatar state is a native enum, not a float animation
                        // property. Both geometries use the same rider role;
                        // only its attachment position/rotation/scale varies.
                        target.setAsParent=source.setAsParent;target.overrideAvatarState=source.overrideAvatarState;
                        Fit(clips[0],clips[1],target.transform,source.transform,root.transform);
                        foreach(var grip in target.IKTargets??new RootMotion.FinalIK.InteractionTarget[0]){
                            var originalGrip=(source.IKTargets??new RootMotion.FinalIK.InteractionTarget[0]).FirstOrDefault(g=>g.effectorType==grip.effectorType);
                            if(originalGrip!=null)Fit(clips[0],clips[1],grip.transform,originalGrip.transform,root.transform);
                        }
                    }
                    foreach(var spot in mounts.seats)
                    {
                        int i=Array.IndexOf(mounts.seats,spot);
                        if(spot.exitPosition!=null&&oldMounts.seats[i].exitPosition!=null)Fit(clips[0],clips[1],spot.exitPosition,oldMounts.seats[i].exitPosition,root.transform);
                        if(spot.alternativeExitPosition!=null&&oldMounts.seats[i].alternativeExitPosition!=null)Fit(clips[0],clips[1],spot.alternativeExitPosition,oldMounts.seats[i].alternativeExitPosition,root.transform);
                    }
                    foreach(var target in root.GetComponentsInChildren<SpecificInteractable>(true))
                    {
                        var match=old.transform.Find(PathOf(target.transform,root.transform))?.GetComponent<SpecificInteractable>()??old.GetComponentsInChildren<SpecificInteractable>(true).FirstOrDefault(t=>t.name==target.name&&t.interactionTargetName==target.interactionTargetName);
                        if(match!=null&&!AnimatedControl(target.transform,root.transform))Fit(clips[0],clips[1],target.transform,match.transform,root.transform);
                    }
                    foreach(var name in new[]{"Throttle","Brake","Reverser"}){
                        var host=root.transform.Find("CabFittings/"+name+"Animation");if(host==null)continue;
                        var controlController=host.GetComponent<Animator>().runtimeAnimatorController;
                        var neutral=name=="Throttle"?"Throttle0":name=="Brake"?"BrakeOff":"Forward";
                        controlController.animationClips.Single(c=>c.name.EndsWith(neutral,StringComparison.Ordinal)).SampleAnimation(host.gameObject,0);
                        var grip=host.GetComponentsInChildren<SpecificInteractable>(true).Single(t=>t.name==name+" grip");
                        var original=old.GetComponentsInChildren<SpecificInteractable>(true).Single(t=>t.name==name+" grip");
                        var anchor=new GameObject("Original control anchor").transform;
                        try{anchor.position=host.position+original.transform.position-grip.transform.position;anchor.rotation=host.rotation;anchor.localScale=host.lossyScale;Fit(clips[0],clips[1],host,anchor,root.transform);}
                        finally{Object.DestroyImmediate(anchor.gameObject);}
                    }
                    foreach(var name in new[]{"SteamExhaust","AutomaticSteamExhaust","Optional vehicle nameplates","Tram night lamps","Tram night lamps/Front warm headlamp","Tram night lamps/Rear warm headlamp","Train automatic lamps","Train automatic lamps/Front headlamp","Train automatic lamps/Rear headlamp"}){
                        var target=root.transform.Find(name);var source=old.transform.Find(name);
                        if(target!=null&&source!=null)Fit(clips[0],clips[1],target,source,root.transform);
                    }
                    Vector(clips[0],"",typeof(Vehicle),"size",root.GetComponent<WorldObject>().size);
                    Vector(clips[1],"",typeof(Vehicle),"size",old.GetComponent<WorldObject>().size);
                    foreach(var collider in root.GetComponentsInChildren<BoxCollider>(true))
                    {
                        var match=OriginalCollider(old,collider,root.transform);
                        var p=PathOf(collider.transform,root.transform);
                        if(match==null){Curve(clips[0],p,typeof(BoxCollider),"m_Enabled",collider.enabled?1:0);Curve(clips[1],p,typeof(BoxCollider),"m_Enabled",0);continue;}
                        if(!AnimatedControl(collider.transform,root.transform))Fit(clips[0],clips[1],collider.transform,match.transform,root.transform);
                        Vector(clips[0],p,typeof(BoxCollider),"m_Center",collider.center);Vector(clips[1],p,typeof(BoxCollider),"m_Center",match.center);
                        Vector(clips[0],p,typeof(BoxCollider),"m_Size",collider.size);Vector(clips[1],p,typeof(BoxCollider),"m_Size",match.size);
                        Curve(clips[0],p,typeof(BoxCollider),"m_Enabled",collider.enabled?1:0);Curve(clips[1],p,typeof(BoxCollider),"m_Enabled",match.enabled?1:0);
                        Curve(clips[0],p,typeof(BoxCollider),"m_IsTrigger",collider.isTrigger?1:0);Curve(clips[1],p,typeof(BoxCollider),"m_IsTrigger",match.isTrigger?1:0);
                    }
                    // Legacy mesh nodes retain their authored hierarchy until
                    // mounted under the shared rolling/tipping/pumping pivots.
                    var geometry=Geometry(old.transform);Object.DestroyImmediate(old);old=geometry;
                    foreach(var r in old.GetComponentsInChildren<MeshRenderer>(true).Where(r=>Shared(r.transform,old.transform)).ToArray()){
                        var f=r.GetComponent<MeshFilter>();Object.DestroyImmediate(r);if(f!=null)Object.DestroyImmediate(f);
                    }
                    foreach(var node in old.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="MechanicalLinkage").ToArray())Object.DestroyImmediate(node.gameObject);
                    old.name="LegacyDesignGeometry";old.transform.SetParent(root.transform,false);old.SetActive(true);
                    foreach(var prefix in new[]{"WheelRoll_","UpstopRoll_"})
                    foreach(var suffix in new[]{"FL","FR","RL","RR"})
                    {
                        var source=old.transform.Find(prefix+suffix+"/Spin");var target=root.transform.Find(prefix+suffix+"/Spin");
                        if(prefix=="UpstopRoll_"&&source==null)continue;
                        if(source==null||target==null)throw new Exception("Missing animated legacy wheel "+root.name+suffix);
                        var branch=new GameObject("LegacyDesignGeometry_"+prefix+suffix).transform;branch.SetParent(target,false);
                        foreach(var child in source.Cast<Transform>().ToArray())child.SetParent(branch,true);
                    }
                    foreach(var pivotPath in new[]{"DumpAnimation/DumpHinge/Bucket","PumpPivot","PumpRod","PumpSlider"})
                    {
                        var source=old.transform.Find(pivotPath);var target=root.transform.Find(pivotPath)??root.transform.Find("Handcar pump animation/"+pivotPath);
                        if(source==null||target==null)continue;
                        var branch=new GameObject("LegacyDesignGeometry_"+source.name).transform;branch.SetParent(target,false);
                        foreach(var child in source.Cast<Transform>().ToArray())child.SetParent(branch,true);
                    }
                    // The original coaster bars use the same replicated hinge
                    // controller as the experimental restraints, not static art.
                    if(root.name=="RollerCoasterCartObject"){
                        foreach(var grip in old.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Seat restraint grip")).ToArray()){
                            var pivot=root.transform.Find("PreparedVehicleArt/RestraintAnimation/"+(root.transform.InverseTransformPoint(grip.position).x<0?"LeftPivot":"RightPivot"));
                            var rotation=pivot.localRotation;pivot.localRotation=Quaternion.Euler(-30,0,0);
                            var branch=new GameObject("LegacyDesignGeometry_Restraint"+grip.GetInstanceID()).transform;branch.SetParent(pivot,false);grip.SetParent(branch,true);pivot.localRotation=rotation;
                        }
                    }
                    var world=root.GetComponent<WorldObject>();int flag=Array.IndexOf(world.States,"RailLegacyDesign");
                    if(flag<0){
                        var names=world.States;var changed=world.OnStateChangedEvents;var enabled=world.OnStateEnabledEvents;var disabled=world.OnStateDisabledEvents;flag=names.Length;
                        Array.Resize(ref names,flag+1);Array.Resize(ref changed,flag+1);Array.Resize(ref enabled,flag+1);Array.Resize(ref disabled,flag+1);
                        names[flag]="RailLegacyDesign";changed[flag]=new ChangedStateEvent();world.States=names;world.OnStateChangedEvents=changed;world.OnStateEnabledEvents=enabled;world.OnStateDisabledEvents=disabled;
                    }
                    world.OnStateEnabledEvents[flag]=new SetStateEvent();world.OnStateDisabledEvents[flag]=new SetStateEvent();
                    foreach(var r in modernRenderers){UnityEventTools.AddBoolPersistentListener(world.OnStateEnabledEvents[flag],r.gameObject.SetActive,false);UnityEventTools.AddBoolPersistentListener(world.OnStateDisabledEvents[flag],r.gameObject.SetActive,true);r.gameObject.SetActive(false);}
                    foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true).Except(modernRenderers).Where(r=>PathOf(r.transform,root.transform).Contains("LegacyDesignGeometry"))){
                        for(var parent=r.transform.parent;parent!=null&&parent!=root.transform;parent=parent.parent)parent.gameObject.SetActive(true);
                        UnityEventTools.AddBoolPersistentListener(world.OnStateEnabledEvents[flag],r.gameObject.SetActive,true);UnityEventTools.AddBoolPersistentListener(world.OnStateDisabledEvents[flag],r.gameObject.SetActive,false);r.gameObject.SetActive(true);
                    }
                    string controllerPath=folder+"/"+root.name+".controller";
                    var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)??AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                    if(controller.layers.Length==0)controller.AddLayer("Base Layer");
                    var machine=controller.layers[0].stateMachine;
                    foreach(var transition in machine.anyStateTransitions)machine.RemoveAnyStateTransition(transition);
                    for(int i=0;i<2;i++){
                        string name=i==0?"ModernDesign":"LegacyDesign";
                        var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??machine.AddState(name);state.motion=clips[i];state.speed=0;state.writeDefaultValues=false;
                        if(i==1)machine.defaultState=state;
                        if(!controller.parameters.Any(p=>p.name==name))controller.AddParameter(name,AnimatorControllerParameterType.Trigger);
                        var t=machine.AddAnyStateTransition(state);t.hasExitTime=false;t.duration=0;t.canTransitionToSelf=false;t.AddCondition(AnimatorConditionMode.If,0,name);
                    }
                    var animator=root.GetComponent<Animator>();if(animator==null)animator=root.AddComponent<Animator>();animator.runtimeAnimatorController=controller;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.applyRootMotion=false;
                    RailPresentationPolish.Pump(root,controller,animator);
                    int slot=RailWheelAnimationBuilder.StringIndex(world,"RailDesignPose");world.OnStringStateChanged[slot]=new ChangedStringStateEvent();
                    foreach(var name in new[]{"ModernDesign","LegacyDesign"})UnityEventTools.AddStringPersistentListener(world.OnStringStateChanged[slot],animator.ResetTrigger,name);
                    UnityEventTools.AddPersistentListener(world.OnStringStateChanged[slot],animator.SetTrigger);
                    foreach(var clip in clips)EditorUtility.SetDirty(clip);EditorUtility.SetDirty(controller);
                    if(root.name=="WoodenMinecartObject")SingleLod(root);
                    clips[1].SampleAnimation(root,0);
                    // These gates have their own replicated moving/parked state.
                    // A design selector must neither show nor close them.
                    foreach(var gate in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Left travel gate"||t.name=="Right travel gate"))gate.gameObject.SetActive(false);
                    PrefabUtility.SaveAsPrefabAsset(root,path);count++;
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();Debug.Log("VEHICLE_DESIGNS_AUTHORED: "+count+" modern/legacy configurations on shared saved identities and motion rigs");
        }
    }
}
