using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class IndustrialTrackAssetBuilder
    {
        [Serializable] public class Definition { public string Key,Name,Shape; public bool Chain,Bend; public int Footprint; }
        public static Vector3 Point(Definition d,float t)
        {
            if(d.Bend) { var a=Mathf.PI-t*Mathf.PI/2; var p=new Vector3(3.5f+5.5f*Mathf.Cos(a),-.35f,-3.5f+5.5f*Mathf.Sin(a)); if(d.Shape.EndsWith("Left")) p.x=-p.x; return p; }
            var slope=d.Shape.StartsWith("Slope")||d.Shape.StartsWith("RampTop");
            var phase=slope?int.Parse(d.Shape.Substring(d.Shape.StartsWith("Slope")?5:7)):1;
            return new Vector3(0,-.35f+(phase-1)*.25f-(d.Shape.StartsWith("RampTop")?1:0)+t*(slope?.25f:0),t-.5f);
        }
        static Transform Box(Transform root,string name,Vector3 p,Vector3 size,Material m)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name=name; obj.transform.SetParent(root,false);
            obj.transform.localPosition=p; obj.transform.localScale=size; obj.GetComponent<Renderer>().sharedMaterial=m;
            Object.DestroyImmediate(obj.GetComponent<Collider>()); return obj.transform;
        }
        static void Beam(Transform root,string name,Vector3 a,Vector3 b,float width,float height,Material m)
        { var n=Box(root,name,(a+b)/2,new Vector3(width,height,Vector3.Distance(a,b)),m); n.rotation=Quaternion.LookRotation(b-a); }
        public static IEnumerable<GameObject> Build(Definition[] definitions,IReadOnlyDictionary<string,Material> materials)
        {
            foreach(var d in definitions) {
                var root=new GameObject(d.Key+"Object"); root.tag="ModObject"; root.AddComponent<WorldObject>(); root.AddComponent<HighlightableObject>();
                var iron=materials["MAT_IronBare"]; var wood=materials["MAT_WoodRail"]; var steps=d.Bend?128:8;
                for(int i=0;i<steps;i++) {
                    var a=Point(d,(float)i/steps); var b=Point(d,(float)(i+1)/steps);
                    var right=Vector3.Cross(Vector3.up,(b-a).normalized).normalized;
                    foreach(var side in new[]{-1,1}) Beam(root.transform,"Industrial rail",a+right*side-Vector3.up*.04f,b+right*side-Vector3.up*.04f,.085f,.08f,iron);
                    var node=new GameObject("Continuous industrial deck"); node.transform.SetParent(root.transform,false);
                    node.transform.localPosition=(a+b)/2-Vector3.up*.025f; node.transform.rotation=Quaternion.LookRotation(b-a);
                    node.AddComponent<BoxCollider>().size=new Vector3(2.9f,.05f,Vector3.Distance(a,b)+.005f);
                    if(d.Chain) {
                        foreach(var side in new[]{-1,1}) Beam(root.transform,"Chain guide",a+right*(side*.09f)-Vector3.up*.045f,b+right*(side*.09f)-Vector3.up*.045f,.035f,.05f,iron);
                        if(i%2==0) Beam(root.transform,"Chain cross link",(a+b)/2-right*.09f-Vector3.up*.02f,(a+b)/2+right*.09f-Vector3.up*.02f,.045f,.035f,iron);
                    }
                }
                var ties=d.Bend?29:4;
                for(int i=0;i<ties;i++) {
                    var t=(i+.5f)/ties; var p=Point(d,t); var tangent=(Point(d,Mathf.Min(1,t+.001f))-Point(d,Mathf.Max(0,t-.001f))).normalized;
                    var sleeper=Box(root.transform,"Industrial sleeper",p-Vector3.up*.11f,new Vector3(2.9f,.10f,.15f),wood); sleeper.rotation=Quaternion.LookRotation(tangent);
                }
                if(d.Shape=="Stopper") {
                    Box(root.transform,"Industrial buffer beam",new Vector3(0,-.02f,.27f),new Vector3(2.9f,.24f,.19f),wood);
                    foreach(var side in new[]{-1,1}) Beam(root.transform,"Buffer brace",new Vector3(side,-.38f,-.35f),new Vector3(side,-.02f,.27f),.09f,.12f,iron);
                    var buffer=new GameObject("Buffer collision"); buffer.transform.SetParent(root.transform,false); buffer.transform.localPosition=new Vector3(0,-.02f,.27f); buffer.AddComponent<BoxCollider>().size=new Vector3(2.9f,.24f,.19f);
                }
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,"Assets/EcoMinecarts/Prefabs/"+root.name+".prefab"); Object.DestroyImmediate(root); yield return prefab;
            }
        }
    }
}
