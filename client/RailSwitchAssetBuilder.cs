using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Events;
using Object=UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    public static class RailSwitchAssetBuilder
    {
        [Serializable] public class Point {public float X,Y,Z;public Vector3 Value=>new Vector3(X,Y,Z);}
        [Serializable] public class Path {public int Route;public Point[] Points;}
        [Serializable] public class Definition {public string Key,Name; public int Footprint;public bool Industrial;public float HalfGauge;public Path[] Paths;}
        static string Label(int route)=>route<0?"Left":route>0?"Right":"Forward";
        static Transform Box(Transform parent,string name,Vector3 p,Vector3 size,Material mat)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(parent,false);obj.transform.localPosition=p;obj.transform.localScale=size;
            obj.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(obj.GetComponent<Collider>());return obj.transform;
        }
        static void Beam(Transform parent,string name,Vector3 a,Vector3 b,float width,float height,Material mat)
        {var node=Box(parent,name,(a+b)/2,new Vector3(width,height,Vector3.Distance(a,b)+.001f),mat);node.localRotation=Quaternion.LookRotation(b-a);}
        static void Indicator(Transform parent,string route,Vector3 center,Material material,Material backing)
        {
            var indicator=new GameObject("SwitchIndicator"+route);indicator.transform.SetParent(parent,false);
            // Keep the green route marker in front of the grey metal flag so
            // the two surfaces never occupy the same depth plane.
            Box(indicator.transform,"Indicator plate",center,new Vector3(.25f,.20f,.025f),backing);
            // Raised symbols on both faces, with a real two-stroke arrowhead.
            foreach(var face in new[]{-1,1})
            {
                var symbol=new GameObject("Arrow face "+face).transform;symbol.SetParent(indicator.transform,false);
                symbol.localPosition=center+Vector3.forward*(face*.024f);
                symbol.localRotation=Quaternion.Euler(0,0,route=="Forward"?0:route=="Left"?90:-90);
                Box(symbol,"Green shaft",Vector3.down*.015f,new Vector3(.025f,.11f,.015f),material);
                var a=Box(symbol,"Green head left",new Vector3(-.021f,.037f,0),new Vector3(.023f,.070f,.015f),material);
                var b=Box(symbol,"Green head right",new Vector3(.021f,.037f,0),new Vector3(.023f,.070f,.015f),material);
                a.localRotation=Quaternion.Euler(0,0,-45);b.localRotation=Quaternion.Euler(0,0,45);
            }
            indicator.SetActive(false);
        }
        static void Rail(Transform parent,string key,Point[] points,int first,int last,float offset,float width,float height,Material material)
        {
            var indices=Enumerable.Range(first,last-first+1).ToArray();
            var right=indices.Select(i=>Vector3.Cross(Vector3.up,(points[Math.Min(points.Length-1,i+1)].Value-points[Math.Max(0,i-1)].Value).normalized).normalized).ToArray();
            RailSectionMesh.Create(parent,key,indices.Select((i,n)=>points[i].Value+right[n]*offset).ToArray(),right,width,height,material);
        }
        public static IEnumerable<GameObject> Build(Definition[] definitions,IReadOnlyDictionary<string,Material> materials)
        {
            foreach(var d in definitions)
            {
                var root=new GameObject(d.Key+"Object");root.tag="ModObject";
                var world=root.AddComponent<WorldObject>();root.AddComponent<HighlightableObject>();
                var iron=materials["MAT_IronBare"];var wood=materials["MAT_WoodRail"];var paint=materials["MAT_IronPainted"];var indicator=materials["MAT_SwitchIndicator"];
                var street=d.Key.StartsWith("Tram",StringComparison.Ordinal);
                var bed=street?TrackBlockAssetBuilder.TramBed:wood;
                world.States=new[]{"RouteLeft","RouteForward","RouteRight"};
                world.OnStateChangedEvents=new[]{new ChangedStateEvent(),new ChangedStateEvent(),new ChangedStateEvent()};
                world.OnStateEnabledEvents=new[]{new SetStateEvent(),new SetStateEvent(),new SetStateEvent()};
                world.OnStateDisabledEvents=new[]{new SetStateEvent(),new SetStateEvent(),new SetStateEvent()};
                // Rail top is -.35, sleeper top -.405 and bottom -.495.
                // The stand sits outside ordinary sleepers: give it an extended
                // mounting timber, and rest the base directly on that timber.
                var stand=new Vector3(-d.HalfGauge-.22f,-.355f,-d.Footprint*.5f+.16f);
                var supportLeft=stand.x-.10f;var supportRight=d.HalfGauge+.11f;
                Box(root.transform,"Switch mounting sleeper",new Vector3((supportLeft+supportRight)/2,-.45f,stand.z),
                    new Vector3(supportRight-supportLeft,.09f,.29f),bed);
                Box(root.transform,"Switch stand base",stand,new Vector3(.16f,.10f,.25f),iron);
                Box(root.transform,"Switch stand mast",stand+Vector3.up*.26f,new Vector3(.045f,.55f,.045f),iron);
                Beam(root.transform,"Point operating rod",stand,new Vector3(0,stand.y,stand.z),.035f,.03f,iron);
                var hit=new GameObject("Switch lever interaction");hit.transform.SetParent(root.transform,false);hit.transform.localPosition=stand+Vector3.up*.30f;
                hit.AddComponent<BoxCollider>().size=new Vector3(.24f,.7f,.3f);hit.AddComponent<SpecificInteractable>().interactionTargetName="RailSwitch";
                var emitted=new HashSet<string>();
                foreach(var path in d.Paths)
                {
                    var active=new GameObject("Selected"+Label(path.Route));active.transform.SetParent(root.transform,false);
                    UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[path.Route+1],active.SetActive);
                    active.SetActive(path.Route==0);
                    Indicator(active.transform,Label(path.Route),stand+Vector3.up*.59f,indicator,paint);
                    active.transform.Find("SwitchIndicator"+Label(path.Route)).gameObject.SetActive(true);
                    var angle=path.Route*45f;
                    var lever=Box(active.transform,"Route lever",stand+Vector3.up*.36f+Vector3.forward*.075f,new Vector3(.045f,.36f,.045f),wood);lever.localRotation=Quaternion.Euler(0,0,-angle);
                    var travelled=0f;
                    var cut=1;
                    for(int i=1;i<path.Points.Length;i++){travelled+=Vector3.Distance(path.Points[i-1].Value,path.Points[i].Value);cut=i;if(travelled>=Mathf.Min(.4f,d.Footprint*.3f))break;}
                    foreach(var side in new[]{-1,1})
                    {
                        Rail(active.transform,d.Key+Label(path.Route)+side+"Points",path.Points,0,cut,side*d.HalfGauge,d.Industrial?.08f:.055f,.07f,iron);
                        Rail(root.transform,d.Key+Label(path.Route)+side+"Stock",path.Points,cut,path.Points.Length-1,side*d.HalfGauge,d.Industrial?.08f:.055f,.07f,iron);
                    }
                    if(path.Route!=0)Rail(root.transform,d.Key+Label(path.Route)+"Check",path.Points,28,40,path.Route*(d.HalfGauge-.075f),.025f,.045f,iron);
                    for(int i=0;i<path.Points.Length-1;i++)
                    {
                        var a=path.Points[i].Value;var b=path.Points[i+1].Value;var tangent=(b-a).normalized;var right=Vector3.Cross(Vector3.up,tangent).normalized;
                        var segment=Vector3.Distance(a,b);
                        var deck=new GameObject("Continuous switch deck");deck.transform.SetParent(root.transform,false);deck.transform.localPosition=(a+b)/2-Vector3.up*.03f;deck.transform.localRotation=Quaternion.LookRotation(tangent);
                        deck.AddComponent<BoxCollider>().size=new Vector3(d.HalfGauge*2+.22f,.06f,segment+.005f);deck.AddComponent<SpecificInteractable>().interactionTargetName="RailSwitch";
                    }
                }
                // A turnout has one common timber bed, not overlapping beds
                // laid independently underneath each possible route.
                var points=d.Paths.SelectMany(p=>p.Points).ToArray();
                for(var z=points.Min(p=>p.Z)+.08f;z<points.Max(p=>p.Z)-.04f;z+=.22f){
                    if(Mathf.Abs(z-stand.z)<.205f)continue;
                    var crossings=new List<float>();
                    foreach(var path in d.Paths)for(var i=1;i<path.Points.Length;i++){
                        var a=path.Points[i-1].Value;var b=path.Points[i].Value;
                        if(z<Mathf.Min(a.z,b.z)||z>Mathf.Max(a.z,b.z)||Mathf.Abs(b.z-a.z)<.000001f)continue;
                        var x=Mathf.Lerp(a.x,b.x,(z-a.z)/(b.z-a.z));
                        var tangent=(b-a).normalized;
                        // Project the rail bed's perpendicular width onto
                        // this row, including the almost sideways exit ends.
                        var reach=(d.HalfGauge+.11f)/Mathf.Max(.35f,Mathf.Abs(tangent.z));
                        crossings.Add(x-reach);crossings.Add(x+reach);
                    }
                    if(crossings.Count>0){
                        var left=Mathf.Max(crossings.Min(),points.Min(p=>p.X)-d.HalfGauge-.11f);
                        var right=Mathf.Min(crossings.Max(),points.Max(p=>p.X)+d.HalfGauge+.11f);
                        Box(root.transform,street?"Street tie":"Turnout sleeper",new Vector3((left+right)/2,-.45f,z),
                            new Vector3(right-left,street?.055f:.09f,street?.07f:.10f),bed);
                    }
                }
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,"Assets/EcoMinecarts/Prefabs/"+root.name+".prefab");Object.DestroyImmediate(root);yield return prefab;
            }
        }
    }
}
