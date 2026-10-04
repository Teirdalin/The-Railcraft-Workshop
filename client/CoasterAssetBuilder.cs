using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class CoasterAssetBuilder
    {
        [Serializable] public class Point { public float X,Y,Z,UpX,UpY,UpZ; public Vector3 Position=>new Vector3(X,Y,Z); public Vector3 Up=>new Vector3(UpX,UpY,UpZ); }
        [Serializable] public class Path {public string Key;public bool Chain;public Point[] Points;}
        // Uniform physical spacing, independent of the simulation sample count.
        public static void AtSpacing(Point[] points,float spacing,Action<Vector3,Vector3,Quaternion,float> emit)
        {
            var lengths=new float[points.Length];
            for(var i=1;i<points.Length;i++)lengths[i]=lengths[i-1]+Vector3.Distance(points[i-1].Position,points[i].Position);
            var total=lengths[lengths.Length-1];if(total<.00001f)return;
            var count=Math.Max(1,Mathf.RoundToInt(total/spacing));var interval=total/count;var segment=1;
            for(var n=0;n<count;n++){
                var distance=(n+.5f)*interval;
                while(segment<lengths.Length-1&&lengths[segment]<distance)segment++;
                var a=points[segment-1];var b=points[segment];
                var t=(distance-lengths[segment-1])/Mathf.Max(.00001f,lengths[segment]-lengths[segment-1]);
                var up=Vector3.Slerp(a.Up,b.Up,t).normalized;
                emit(Vector3.Lerp(a.Position,b.Position,t),up,Quaternion.LookRotation(b.Position-a.Position,up),interval);
            }
        }
        private static Transform Box(Transform root,string name,Vector3 point,Vector3 size,Material material,Quaternion rotation,bool collider=false)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(root,false);
            obj.transform.localPosition=point;obj.transform.localRotation=rotation;obj.transform.localScale=size;
            obj.GetComponent<Renderer>().sharedMaterial=material;
            if(!collider)Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj.transform;
        }
        static void SaveSweptMeshes(GameObject geometry,string key)
        {
            const string folder="Assets/EcoMinecarts/CoasterMeshes";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/EcoMinecarts","CoasterMeshes");
            var filters=geometry.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Continuous rail").ToArray();
            for(var i=0;i<filters.Length;i++)
            {
                // A procedural mesh assigned only in memory is lost when its
                // prefab is saved. Give each running rail and spine a stable asset.
                var mesh=filters[i].sharedMesh;mesh.name=key+"Rail"+i;
                var path=folder+"/"+mesh.name+".asset";
                var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}
                else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
                filters[i].sharedMesh=saved;
            }
        }
        public static IEnumerable<GameObject> Build(Path[] paths,IReadOnlyDictionary<string,Material> materials)
        {
            // Simple rails are hammer blocks; complex geometry is placed whole.
            foreach(var path in paths.Where(p=>p.Key!="CoasterStraight").Concat(new[]{new Path{Key="CoasterStation",Points=Enumerable.Range(0,65).Select(i=>new Point{X=-1,Y=-.35f,Z=-.5f+i/64f,UpY=1}).ToArray()}}))
            {
                var root=new GameObject(path.Key+"Object");root.tag="ModObject";root.AddComponent<WorldObject>();root.AddComponent<HighlightableObject>();
                var steel=materials["MAT_IronBare"];var paint=materials["MAT_IronPainted"];
                // One continuous mesh profile on both block and whole-section
                // rails; the prefab pivot sits at the entrance build cell.
                var offset=path.Key=="CoasterStation"?Vector3.zero:new Vector3(0,0,Mathf.Round(-path.Points[0].Z-.5f));
                var geometry=CoasterTerrainAssetBuilder.Geometry(path,steel,paint);
                SaveSweptMeshes(geometry,path.Key);
                geometry.transform.SetParent(root.transform,false);geometry.transform.localPosition=offset;
                for(var i=0;i<path.Points.Length-1;i+=4)
                {
                    // Short collision spans follow the open centre of a loop;
                    // a single large box would falsely fill the entire loop.
                    var a=path.Points[i];var end=path.Points[Math.Min(i+4,path.Points.Length-1)];
                    var up=Vector3.Slerp(a.Up,end.Up,.5f).normalized;
                    var hit=new GameObject("RailCollision");hit.transform.SetParent(root.transform,false);
                    hit.transform.localPosition=(a.Position+end.Position)*.5f+offset-up*.035f;
                    hit.transform.localRotation=Quaternion.LookRotation(end.Position-a.Position,up);
                    hit.AddComponent<BoxCollider>().size=new Vector3(.72f,.10f,Vector3.Distance(a.Position,end.Position)+.01f);
                }
                if(path.Key=="CoasterStation")
                {
                    Box(root.transform,"Loading platform",new Vector3(0,-.30f,0),new Vector3(.9f,.10f,1),materials["MAT_WoodRail"],Quaternion.identity,true);
                    foreach(var end in new[]{-1,1})
                        Box(root.transform,"Station rail junction box",new Vector3(-1,-.52f,end*.3f),new Vector3(.5f,.18f,.25f),paint,Quaternion.identity);
                    Box(root.transform,"Station control cabinet",new Vector3(.2f,.15f,0),new Vector3(.4f,.8f,.3f),paint,Quaternion.identity,true);
                    Box(root.transform,"Station sign post",new Vector3(.2f,.72f,0),new Vector3(.06f,.40f,.06f),steel,Quaternion.identity,true);
                    Box(root.transform,"Station sign",new Vector3(.2f,1,0),new Vector3(.8f,.3f,.08f),paint,Quaternion.identity,true);
                    foreach(var collider in root.GetComponentsInChildren<Collider>())
                    {
                        var loading=collider.gameObject.AddComponent<SpecificInteractable>();
                        loading.interactionTargetName="CoasterLoadingStation";loading.interactionTargetValue="";
                    }
                }
                var saved=PrefabUtility.SaveAsPrefabAsset(root,"Assets/EcoMinecarts/Prefabs/"+root.name+".prefab");Object.DestroyImmediate(root);yield return saved;
            }
        }
        public static void FitCart(GameObject root,Transform model,RailExpansionAssetBuilder.Spec spec,Material paint,Material steel)
        {
            foreach(Transform node in model.Cast<Transform>().ToArray())
                if(node.name=="Coach roof"||node.name=="Window pillar")Object.DestroyImmediate(node.gameObject);
            foreach(var side in new[]{-1,1})
            {
                Box(model,"Shove bar",new Vector3(side*(spec.BodyWidth/2+.025f),.53f,0),new Vector3(.045f,.07f,.56f),steel,Quaternion.identity);
                Box(model,"Seat restraint grip",new Vector3(side*.29f,1.0f,.18f),new Vector3(.42f,.045f,.05f),steel,Quaternion.identity);
                Box(model,"Restraint pivot upright",new Vector3(side*.51f,.885f,.18f),new Vector3(.045f,.27f,.06f),steel,Quaternion.identity);
                foreach(var end in new[]{-1,1})
                {
                    // Under-rail rollers make captive coaster guidance visible.
                    Box(model,"Guide wheel bracket",new Vector3(side*.30f,-.08f,end*.4f),new Vector3(.05f,.20f,.16f),steel,Quaternion.identity);
                    var roller=GameObject.CreatePrimitive(PrimitiveType.Cylinder);roller.name="Upstop roller";roller.transform.SetParent(model,false);
                    roller.transform.localPosition=new Vector3(side*.30f,-.11f,end*.4f);roller.transform.localRotation=Quaternion.Euler(0,0,90);roller.transform.localScale=new Vector3(.10f,.035f,.10f);
                    roller.GetComponent<Renderer>().sharedMaterial=steel;Object.DestroyImmediate(roller.GetComponent<Collider>());
                }
            }
            foreach(var node in new[]{"BoilerInteraction","CabInteraction","SteamExhaust","AutomaticSteamExhaust"})
            {var target=root.transform.Find(node);if(target!=null)Object.DestroyImmediate(target.gameObject);}
            root.GetComponent<RCCCarControllerV2>().engineTorque=0;
        }
    }
}
