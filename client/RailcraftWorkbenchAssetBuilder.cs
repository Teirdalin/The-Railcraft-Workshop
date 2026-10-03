using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class RailcraftWorkbenchAssetBuilder
    {
        const string Root="Assets/EcoMinecarts";
        static void Box(Transform p,string name,Vector3 pos,Vector3 scale,Material mat,bool solid=true)
        {var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(p,false);o.transform.localPosition=pos;o.transform.localScale=scale;o.GetComponent<Renderer>().sharedMaterial=mat;if(!solid)Object.DestroyImmediate(o.GetComponent<Collider>());}
        public static GameObject Build()
        {
            Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+name+".mat");
            var wood=Mat("MAT_WoodRail");var metal=Mat("MAT_IronBare");var paint=Mat("MAT_IronPainted");
            var root=new GameObject("RailcraftWorkbenchObject");root.tag="ModObject";var world=root.AddComponent<WorldObject>();
            root.AddComponent<HighlightableObject>();
            var model=new GameObject("Workbench");model.transform.SetParent(root.transform,false);model.transform.localPosition=new Vector3(.5f,0,0);
            Box(model.transform,"Thick timber worktop",new Vector3(0,.91f,0),new Vector3(1.86f,.14f,.86f),wood);
            foreach(var z in new[]{-.425f,.425f})Box(model.transform,"Worktop edge iron",new Vector3(0,.925f,z),new Vector3(1.82f,.035f,.035f),metal,false);
            foreach(var x in new[]{-.77f,.77f})foreach(var z in new[]{-.31f,.31f}){
                Box(model.transform,"Timber leg",new Vector3(x,.42f,z),new Vector3(.15f,.84f,.15f),wood);
                Box(model.transform,"Iron foot",new Vector3(x,.065f,z),new Vector3(.19f,.13f,.19f),metal);
                Box(model.transform,"Corner strap",new Vector3(x,.79f,z),new Vector3(.18f,.11f,.18f),metal);
            }
            Box(model.transform,"Lower shelf",new Vector3(0,.24f,0),new Vector3(1.64f,.065f,.66f),wood);
            foreach(var z in new[]{-.31f,.31f})Box(model.transform,"Shelf end brace",new Vector3(0,.205f,z),new Vector3(1.64f,.055f,.045f),metal,false);
            foreach(var z in new[]{-.25f,0f,.25f})Box(model.transform,"Rail stock",new Vector3(0,.30f,z),new Vector3(1.15f,.055f,.075f),metal);
            Box(model.transform,"Front apron",new Vector3(0,.77f,-.34f),new Vector3(1.7f,.15f,.10f),wood);
            Box(model.transform,"Tool drawer",new Vector3(-.4f,.73f,-.41f),new Vector3(.56f,.18f,.04f),paint);
            Box(model.transform,"Drawer handle",new Vector3(-.4f,.74f,-.46f),new Vector3(.18f,.035f,.04f),metal);
            foreach(var x in new[]{.20f,.59f})Box(model.transform,"Rail jig",new Vector3(x,1.02f,0),new Vector3(.065f,.075f,.60f),metal);
            foreach(var z in new[]{-.20f,.20f})Box(model.transform,"Rail jig clamp",new Vector3(.40f,1.035f,z),new Vector3(.61f,.04f,.075f),paint);
            Box(model.transform,"Press base",new Vector3(-.47f,1.01f,.08f),new Vector3(.5f,.065f,.42f),metal);
            foreach(var x in new[]{-.68f,-.26f})Box(model.transform,"Press upright",new Vector3(x,1.23f,.23f),new Vector3(.055f,.44f,.065f),paint);
            Box(model.transform,"Press crosshead",new Vector3(-.47f,1.44f,.23f),new Vector3(.50f,.075f,.09f),paint);
            Box(model.transform,"Press screw",new Vector3(-.47f,1.55f,.23f),new Vector3(.055f,.18f,.055f),metal,false);
            Box(model.transform,"Press screw handle",new Vector3(-.47f,1.64f,.23f),new Vector3(.33f,.035f,.045f),wood,false);
            Box(model.transform,"Press bed guide left",new Vector3(-.66f,1.065f,.08f),new Vector3(.035f,.045f,.38f),paint,false);
            Box(model.transform,"Press bed guide right",new Vector3(-.28f,1.065f,.08f),new Vector3(.035f,.045f,.38f),paint,false);
            var ram=new GameObject("PressRam");ram.transform.SetParent(model.transform,false);ram.transform.localPosition=new Vector3(-.47f,1.32f,.20f);
            Box(ram.transform,"Stamping ram",Vector3.zero,new Vector3(.06f,.27f,.06f),metal);
            Box(ram.transform,"Press shoe",new Vector3(0,-.13f,0),new Vector3(.19f,.07f,.15f),metal);
            Box(model.transform,"Workbench badge",new Vector3(.55f,.78f,-.41f),new Vector3(.38f,.13f,.035f),paint);
            foreach(var x in new[]{.43f,.67f})Box(model.transform,"Badge rail",new Vector3(x,.78f,-.435f),new Vector3(.025f,.1f,.025f),metal);
            var clip=new AnimationClip{legacy=true,wrapMode=WrapMode.Loop};
            clip.SetCurve("Workbench/PressRam",typeof(Transform),"localPosition.y",new AnimationCurve(new Keyframe(0,1.32f),new Keyframe(.35f,1.215f),new Keyframe(.5f,1.215f),new Keyframe(1,1.32f)));
            var path=Root+"/Prefabs/RailcraftWorkbenchWorking.anim";var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(saved==null){AssetDatabase.CreateAsset(clip,path);saved=clip;}else{EditorUtility.CopySerialized(clip,saved);Object.DestroyImmediate(clip);}
            var animation=root.AddComponent<Animation>();animation.AddClip(saved,"Working");animation.clip=saved;animation.playAutomatically=true;animation.enabled=false;
            world.OnOperatingChanged=new ChangedStateEvent();
            UnityEventTools.AddPersistentListener(world.OnOperatingChanged,(UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),animation,typeof(Behaviour).GetProperty("enabled").GetSetMethod()));
            var sound=new GameObject("CraftingSound");sound.transform.SetParent(root.transform,false);sound.transform.localPosition=new Vector3(.5f,1,0);
            sound.AddComponent<Eco.Audio.SoundControllers.CraftingSoundController>().TableName="WainwrightTable";
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/RailcraftWorkbenchObject.prefab");Object.DestroyImmediate(root);
            MinecartIconBuilder.Render(prefab,"RailcraftWorkbench");Debug.Log("RAILCRAFT_WORKBENCH_ASSET_OK: working ram, native Wainwright sound controller, blue icon");return prefab;
        }
        public static void Verify(GameObject prefab)
        {
            var animation=prefab.GetComponent<Animation>();var world=prefab.GetComponent<WorldObject>();
            var sound=prefab.GetComponentInChildren<Eco.Audio.SoundControllers.CraftingSoundController>();
            if(animation==null||animation.clip==null||animation.enabled||world.OnOperatingChanged.GetPersistentEventCount()!=1||sound==null||sound.TableName!="WainwrightTable"||sound.GetType().Assembly.GetName().Name!="Eco.Client")throw new Exception("Railcraft operating animation/audio binding incomplete");
            var bench=prefab.transform.Find("Workbench");
            var press=bench?.Find("PressRam");
            var top=bench?.Find("Thick timber worktop");
            var guides=bench?.Find("Press bed guide left");
            if(bench==null||press==null||top==null||guides==null
                ||Mathf.Abs(press.localPosition.z-.20f)>.005f
                ||prefab.GetComponentsInChildren<Renderer>().Any(r=>r.sharedMaterial==null))
                throw new Exception("Railcraft workbench model has missing or misaligned parts/materials");
            var instance=Object.Instantiate(prefab);try{
                var liveAnimation=instance.GetComponent<Animation>();var liveWorld=instance.GetComponent<WorldObject>();
                liveWorld.OnOperatingChanged.Invoke(true);if(!liveAnimation.enabled)throw new Exception("Crafting does not enable the press animation");
                var ram=instance.transform.Find("Workbench/PressRam");var y=ram.localPosition.y;animation.clip.SampleAnimation(instance,.35f);
                if(Mathf.Abs(ram.localPosition.y-y)<.08f)throw new Exception("Railcraft press animation does not move");
                liveWorld.OnOperatingChanged.Invoke(false);if(liveAnimation.enabled)throw new Exception("Idle workbench keeps animating");
            }finally{Object.DestroyImmediate(instance);}
            Debug.Log("RAILCRAFT_WORKBENCH_ASSET_VERIFIED: native operating event, moving press and Wainwright audio identity");
        }
    }
}
