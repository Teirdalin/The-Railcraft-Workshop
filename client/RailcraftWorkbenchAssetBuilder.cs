using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class RailcraftWorkbenchAssetBuilder
    {
        const string Root="Assets/EcoMinecarts";
        static Mesh SaveMesh(string name,Mesh mesh)
        {
            const string folder=Root+"/WorkbenchMeshes";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder(Root,"WorkbenchMeshes");
            var path=folder+"/"+name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
            EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);return saved;
        }
        // Flat shaded bevel faces soften the silhouette without increasing collision complexity.
        static Mesh Bevel(Vector3 size)
        {
            var h=size/2;var b=Mathf.Min(.012f,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*.24f);
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            void Face(params Vector3[] points){
                var center=points.Aggregate(Vector3.zero,(a,v)=>a+v)/points.Length;
                if(Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]),center)<0)Array.Reverse(points);
                var start=vertices.Count;var normal=Vector3.Cross(points[1]-points[0],points[2]-points[0]).normalized;
                foreach(var p in points){vertices.Add(p);uv.Add(Mathf.Abs(normal.y)>.5f?new Vector2(p.x,p.z)*2:Mathf.Abs(normal.x)>.5f?new Vector2(p.z,p.y)*2:new Vector2(p.x,p.y)*2);}
                for(var i=1;i<points.Length-1;i++)triangles.AddRange(new[]{start,start+i,start+i+1});
            }
            Vector3 Point(int axis,float a,int second,float c,int third,float d){var v=Vector3.zero;v[axis]=a;v[second]=c;v[third]=d;return v;}
            for(var a=0;a<3;a++)foreach(var s in new[]{-1,1}){
                var c=(a+1)%3;var d=(a+2)%3;
                Face(Point(a,s*h[a],c,-h[c]+b,d,-h[d]+b),Point(a,s*h[a],c,h[c]-b,d,-h[d]+b),Point(a,s*h[a],c,h[c]-b,d,h[d]-b),Point(a,s*h[a],c,-h[c]+b,d,h[d]-b));
            }
            for(var a=0;a<3;a++)for(var c=a+1;c<3;c++)foreach(var sa in new[]{-1,1})foreach(var sc in new[]{-1,1}){
                var d=3-a-c;
                Face(Point(a,sa*h[a],c,sc*(h[c]-b),d,-h[d]+b),Point(a,sa*(h[a]-b),c,sc*h[c],d,-h[d]+b),Point(a,sa*(h[a]-b),c,sc*h[c],d,h[d]-b),Point(a,sa*h[a],c,sc*(h[c]-b),d,h[d]-b));
            }
            foreach(var x in new[]{-1,1})foreach(var y in new[]{-1,1})foreach(var z in new[]{-1,1})
                Face(new Vector3(x*h.x,y*(h.y-b),z*(h.z-b)),new Vector3(x*(h.x-b),y*h.y,z*(h.z-b)),new Vector3(x*(h.x-b),y*(h.y-b),z*h.z));
            var mesh=new Mesh();mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        static void Box(Transform p,string name,Vector3 pos,Vector3 scale,Material mat,bool solid=true)
        {var o=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));o.transform.SetParent(p,false);o.transform.localPosition=pos;o.GetComponent<MeshFilter>().sharedMesh=SaveMesh("Part-"+name.Replace(" ","-")+"-"+scale.x.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"-"+scale.y.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+"-"+scale.z.ToString("F3",System.Globalization.CultureInfo.InvariantCulture),Bevel(scale));o.GetComponent<Renderer>().sharedMaterial=mat;if(solid)o.AddComponent<BoxCollider>().size=scale;}
        static void Pin(Transform p,string name,Vector3 pos,float radius,float length,Material mat,Quaternion rotation)
        {var o=GameObject.CreatePrimitive(PrimitiveType.Cylinder);o.name=name;o.transform.SetParent(p,false);o.transform.localPosition=pos;o.transform.localRotation=rotation;o.transform.localScale=new Vector3(radius*2,length/2,radius*2);o.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(o.GetComponent<Collider>());}
        static void CombineStatic(Transform model,Transform moving)
        {
            var filters=model.GetComponentsInChildren<MeshFilter>().Where(f=>!f.transform.IsChildOf(moving)).ToArray();
            foreach(var group in filters.GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial)){
                var mesh=new Mesh();mesh.CombineMeshes(group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=model.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray());
                var part=new GameObject("Static "+group.Key.name,typeof(MeshFilter),typeof(MeshRenderer));part.transform.SetParent(model,false);
                part.GetComponent<MeshFilter>().sharedMesh=SaveMesh("Combined-"+group.Key.name,mesh);part.GetComponent<Renderer>().sharedMaterial=group.Key;
            }
            foreach(var f in filters){Object.DestroyImmediate(f.GetComponent<MeshRenderer>());Object.DestroyImmediate(f);}
        }
        public static GameObject Build()
        {
            Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+name+".mat");
            var wood=Mat("MAT_WoodRail");var metal=Mat("MAT_IronBare");var paint=Mat("MAT_IronPainted");
            var dark=RailVisualFinish.Accent("MAT_WorkbenchRecess",metal,new Color(.075f,.085f,.085f));
            var brass=RailVisualFinish.Accent("MAT_WorkbenchBrass",metal,new Color(.64f,.44f,.19f));
            var root=new GameObject("RailcraftWorkbenchObject");root.tag="ModObject";var world=root.AddComponent<WorldObject>();
            root.AddComponent<HighlightableObject>();
            var model=new GameObject("Workbench");model.transform.SetParent(root.transform,false);model.transform.localPosition=new Vector3(.5f,0,0);
            Box(model.transform,"Thick timber worktop",new Vector3(0,.91f,0),new Vector3(1.86f,.14f,.86f),wood);
            foreach(var z in new[]{-.215f,0,.215f})Box(model.transform,"Worktop plank seam",new Vector3(0,.981f,z),new Vector3(1.82f,.002f,.004f),dark,false);
            foreach(var z in new[]{-.425f,.425f})Box(model.transform,"Worktop edge iron",new Vector3(0,.925f,z),new Vector3(1.82f,.035f,.035f),metal,false);
            foreach(var x in new[]{-.77f,.77f})foreach(var z in new[]{-.31f,.31f}){
                Box(model.transform,"Timber leg",new Vector3(x,.42f,z),new Vector3(.15f,.84f,.15f),wood);
                Box(model.transform,"Iron foot",new Vector3(x,.065f,z),new Vector3(.19f,.13f,.19f),metal);
                Box(model.transform,"Corner strap",new Vector3(x,.79f,z),new Vector3(.18f,.11f,.18f),metal);
                foreach(var y in new[]{.765f,.815f})Pin(model.transform,"Strap rivet",new Vector3(x,y,z+Mathf.Sign(z)*.095f),.012f,.012f,brass,Quaternion.Euler(90,0,0));
            }
            foreach(var x in new[]{-.77f,.77f})Box(model.transform,"Mortised end stretcher",new Vector3(x,.40f,0),new Vector3(.11f,.13f,.66f),wood,false);
            Box(model.transform,"Rear stretcher",new Vector3(0,.56f,.31f),new Vector3(1.54f,.11f,.09f),wood,false);
            Box(model.transform,"Lower shelf",new Vector3(0,.24f,0),new Vector3(1.64f,.065f,.66f),wood);
            foreach(var z in new[]{-.31f,.31f})Box(model.transform,"Shelf end brace",new Vector3(0,.205f,z),new Vector3(1.64f,.055f,.045f),metal,false);
            foreach(var z in new[]{-.23f,0f,.23f}){
                Box(model.transform,"Rail stock foot",new Vector3(0,.29f,z),new Vector3(1.15f,.024f,.09f),metal,false);
                Box(model.transform,"Rail stock web",new Vector3(0,.33f,z),new Vector3(1.15f,.06f,.025f),metal,false);
                Box(model.transform,"Rail stock head",new Vector3(0,.367f,z),new Vector3(1.15f,.028f,.055f),metal,false);
            }
            Box(model.transform,"Front apron",new Vector3(0,.77f,-.34f),new Vector3(1.7f,.15f,.10f),wood);
            Box(model.transform,"Tool drawer",new Vector3(-.4f,.73f,-.41f),new Vector3(.56f,.18f,.04f),paint);
            Box(model.transform,"Drawer handle",new Vector3(-.4f,.74f,-.46f),new Vector3(.18f,.035f,.04f),metal);
            foreach(var x in new[]{-.66f,-.14f})Pin(model.transform,"Drawer face screw",new Vector3(x,.73f,-.435f),.012f,.009f,brass,Quaternion.Euler(90,0,0));
            foreach(var x in new[]{.20f,.59f})Box(model.transform,"Rail jig",new Vector3(x,1.02f,0),new Vector3(.065f,.075f,.60f),metal);
            foreach(var z in new[]{-.20f,.20f})Box(model.transform,"Rail jig clamp",new Vector3(.40f,1.035f,z),new Vector3(.61f,.04f,.075f),paint);
            Box(model.transform,"Press base",new Vector3(-.47f,1.01f,.08f),new Vector3(.5f,.065f,.42f),metal);
            foreach(var x in new[]{-.68f,-.26f})Box(model.transform,"Press upright",new Vector3(x,1.23f,.23f),new Vector3(.055f,.44f,.065f),paint);
            Box(model.transform,"Press crosshead",new Vector3(-.47f,1.44f,.23f),new Vector3(.50f,.075f,.09f),paint);
            Box(model.transform,"Press screw",new Vector3(-.47f,1.55f,.23f),new Vector3(.055f,.18f,.055f),metal,false);
            for(var i=0;i<10;i++)Pin(model.transform,"Press screw thread",new Vector3(-.47f,1.48f+i*.016f,.23f),.037f,.005f,dark,Quaternion.identity);
            Box(model.transform,"Press screw handle",new Vector3(-.47f,1.64f,.23f),new Vector3(.33f,.035f,.045f),wood,false);
            Box(model.transform,"Press bed guide left",new Vector3(-.66f,1.065f,.08f),new Vector3(.035f,.045f,.38f),paint,false);
            Box(model.transform,"Press bed guide right",new Vector3(-.28f,1.065f,.08f),new Vector3(.035f,.045f,.38f),paint,false);
            var ram=new GameObject("PressRam");ram.transform.SetParent(model.transform,false);ram.transform.localPosition=new Vector3(-.47f,1.32f,.20f);
            Box(ram.transform,"Stamping ram",Vector3.zero,new Vector3(.06f,.27f,.06f),metal);
            Box(ram.transform,"Press shoe",new Vector3(0,-.13f,0),new Vector3(.19f,.07f,.15f),metal);
            Box(model.transform,"Workbench badge",new Vector3(.55f,.78f,-.41f),new Vector3(.38f,.13f,.035f),paint);
            foreach(var x in new[]{.43f,.67f})Box(model.transform,"Badge rail",new Vector3(x,.78f,-.435f),new Vector3(.025f,.1f,.025f),metal);
            // Tooling has no collider: the usable bench volume stays simple.
            Box(model.transform,"Vise base",new Vector3(.70f,1.015f,-.24f),new Vector3(.28f,.065f,.24f),paint,false);
            foreach(var z in new[]{-.32f,-.17f})Box(model.transform,"Vise jaw",new Vector3(.70f,1.075f,z),new Vector3(.25f,.09f,.045f),metal,false);
            Pin(model.transform,"Vise screw",new Vector3(.70f,1.035f,-.34f),.021f,.23f,metal,Quaternion.Euler(90,0,0));
            Pin(model.transform,"Vise sliding handle",new Vector3(.70f,1.035f,-.445f),.012f,.18f,brass,Quaternion.identity);
            Box(model.transform,"Mallet handle",new Vector3(.06f,.998f,-.19f),new Vector3(.028f,.025f,.28f),wood,false);
            Box(model.transform,"Mallet head",new Vector3(.06f,1.028f,-.31f),new Vector3(.16f,.07f,.07f),metal,false);
            Box(model.transform,"Measuring rule",new Vector3(.15f,.993f,.33f),new Vector3(.64f,.012f,.055f),brass,false);
            for(var i=0;i<13;i++)Box(model.transform,"Rule tick",new Vector3(-.14f+i*.048f,1.001f,.32f),new Vector3(.003f,.002f,i%2==0?.03f:.018f),dark,false);
            foreach(var x in new[]{-.82f,-.15f,.15f,.82f})Pin(model.transform,"Bench dog socket",new Vector3(x,.982f,.10f),.018f,.002f,dark,Quaternion.identity);
            foreach(var z in new[]{-.20f,.20f})foreach(var x in new[]{.21f,.58f})Pin(model.transform,"Jig clamp bolt",new Vector3(x,1.061f,z),.018f,.022f,brass,Quaternion.identity);
            CombineStatic(model.transform,ram.transform);
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
            foreach(var part in new[]{"Vise base","Vise jaw","Measuring rule","Mallet head","Mortised end stretcher","Rail stock head","Press screw thread"})
                if(!bench.GetComponentsInChildren<Transform>().Any(t=>t.name==part))throw new Exception("Workbench tooling missing: "+part);
            var renderers=prefab.GetComponentsInChildren<MeshRenderer>();
            var imported=prefab.transform.Find("Meshy workbench-fixed")!=null;
            if(imported){
                if(renderers.Count(r=>r.enabled)!=2||press.Find("Meshy workbench-ram")==null)
                    throw new Exception("Imported workbench must retain separate fixed art and moving ram");
                if(prefab.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Meshy ")).Any(t=>t.GetComponent<Collider>()!=null))
                    throw new Exception("Imported workbench art changed collision");
            }else if(renderers.Length>8||renderers.Length<5)throw new Exception("Workbench static batching did not preserve the expected material groups");
            if(prefab.GetComponentsInChildren<Collider>().Any(c=>c.transform.name.StartsWith("Rule ")||c.transform.name.Contains("rivet")||c.transform.name.Contains("thread")))
                throw new Exception("Decorative workbench tooling should not obstruct interactions");
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
