using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class OriginalVehicleReleaseProbe
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        public static void Verify(GameObject[] prefabs)
        {
            int count=0;
            foreach(var prefab in prefabs.Where(p=>p.GetComponent<Vehicle>()!=null))
            {
                var root=Object.Instantiate(prefab);root.name=prefab.name;
                var original=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/LegacyVehicleSources/"+root.name+".prefab"));
                original.name=root.name;
                try
                {
                    RailPresentationPolish.Original(original);root.SetActive(true);
                    string Shape(MeshRenderer renderer)=>renderer.name+"/"+renderer.GetComponent<MeshFilter>()?.sharedMesh?.name;
                    var hiddenShapes=original.GetComponentsInChildren<MeshRenderer>(true).GroupBy(Shape)
                        .Where(g=>g.All(r=>!r.enabled)).Select(g=>g.Key).ToHashSet();
                    Check(root.transform.Find(OriginalVehicleReleaseBuilder.Marker)!=null,"Missing original release marker "+root.name);
                    var world=root.GetComponent<WorldObject>();int flag=Array.IndexOf(world.States,"RailLegacyDesign");
                    Check(world.OnStateEnabledEvents[flag].GetPersistentEventCount()==0&&world.OnStateDisabledEvents[flag].GetPersistentEventCount()==0,"Deferred design visibility callbacks remain "+root.name);
                    var seats=root.GetComponent<Mountable>().seats;var expected=original.GetComponent<Mountable>().seats;
                    var vehicleCount=root.GetComponentsInChildren<Vehicle>(true).Length;
                    var bodyCount=root.GetComponentsInChildren<Rigidbody>(true).Length;
                    foreach(var modern in new[]{false,true,false})
                    {
                        RailVehicleDesignProbe.Select(root,modern);
                        for(int i=0;i<seats.Length;i++)
                            Check(Vector3.Distance(seats[i].transform.position-root.transform.position,expected[i].transform.position-original.transform.position)<.003f,"Original seat fit changed "+root.name+i);
                        var visible=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
                        Check(visible.Any(r=>r.transform.Ancestors(root.transform).Any(t=>t.name.StartsWith("LegacyDesignGeometry"))),"Original artwork disappeared "+root.name);
                        Check(!visible.Any(r=>r.transform.Ancestors(root.transform).Any(t=>t.name.StartsWith("LegacyDesignGeometry"))&&hiddenShapes.Contains(Shape(r))),"Hidden original collision shapes rendered "+root.name);
                        Check(root.GetComponentsInChildren<Vehicle>(true).Length==vehicleCount&&root.GetComponentsInChildren<Rigidbody>(true).Length==bodyCount,"Design changed native rig "+root.name);
                        RailVehicleDesignProbe.Mechanisms(root,true);
                    }
                    OriginalCoasterRestraintProbe.Verify(root);
                    VehicleIntegrationProbe.Capture(root,"original-release",false);count++;
                }
                finally{Object.DestroyImmediate(root);Object.DestroyImmediate(original);}
            }
            Check(count==14,"Original release audit missing vehicles");
            Debug.Log("ORIGINAL_VEHICLES_EXPORTED_OK: fourteen original-only designs; native seats, restraints, controls, dumping and handcar pumping verified, including stale modern state compatibility");
        }
        static System.Collections.Generic.IEnumerable<Transform> Ancestors(this Transform node,Transform root)
        {for(var t=node;t!=null&&t!=root;t=t.parent)yield return t;}
    }
}
