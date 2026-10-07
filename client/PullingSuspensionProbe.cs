using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace EcoMinecarts.Editor {
public static class PullingSuspensionProbe {
public static void Run() {
 Physics.simulationMode=SimulationMode.Script;
 var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(100,99.5f,100);floor.transform.localScale=new Vector3(20,1,20);
 var report="";
 foreach(var key in new[]{"Minecart","WoodenMinecart"}) {
 var p=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/Prefabs/"+key+"Object.prefab");
 foreach(var mass in key=="Minecart"?new[]{280f,2940f}:new[]{80f,640f}) {
 var rig=UnityEngine.Object.Instantiate(p);
 rig.SetActive(true);rig.transform.position=new Vector3(100,100.025f,100);
 foreach(var placement in rig.GetComponentsInChildren<ColliderPlacementOptions>(true))
  if(placement.RemoveColliderAfterPlacement)foreach(var c in placement.GetComponents<Collider>())UnityEngine.Object.DestroyImmediate(c);
 var body=rig.GetComponent<Rigidbody>();body.isKinematic=false;body.useGravity=true;body.mass=mass;body.ResetInertiaTensor();
 foreach(var w in rig.GetComponentsInChildren<WheelCollider>(true)){w.enabled=true;w.sprungMass=mass/4;}
 var b=rig.GetComponent<Rigidbody>();float peak=0,min=10000,max=-10000;
 b.constraints=RigidbodyConstraints.FreezePositionX|RigidbodyConstraints.FreezePositionZ;
 b.rotation=Quaternion.Euler(3,0,2);
 for(int i=0;i<400;i++){Physics.Simulate(.02f);if(i>100){peak=Mathf.Max(peak,Mathf.Abs(b.linearVelocity.y));min=Mathf.Min(min,b.position.y);max=Mathf.Max(max,b.position.y);}}
 if(peak>.02f||max-min>.002f)throw new Exception("Unstable actual pulling suspension: "+key+" mass="+mass+" peak="+peak+" span="+(max-min));
 report+=key+" mass="+mass+" peak="+peak+" span="+(max-min)+" finalY="+b.position.y+"\n";
 UnityEngine.Object.DestroyImmediate(rig);
 }
 }
 File.WriteAllText(Environment.GetEnvironmentVariable("ECO_PULLING_SUSPENSION_REPORT"),report);Debug.Log("PULLING_SUSPENSION_MEASURED: "+report);UnityEngine.Object.DestroyImmediate(floor);
}
}}
