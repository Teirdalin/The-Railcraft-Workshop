using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EcoMinecarts.Editor {
 [InitializeOnLoad]
 public static class HandcarWheelPlayProbe {
  const string Pending="Railworks.HandcarWheelPlayProbe";
  static readonly List<GameObject> rigs=new List<GameObject>();
  static double began;
  static float peak, min=10000, max=-10000;
  static HandcarWheelPlayProbe(){EditorApplication.update+=Update;}
  public static void Run(){SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();}
  static void Update(){
   if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying)return;
   try{
    if(rigs.Count==0){
     Physics.simulationMode=SimulationMode.FixedUpdate;Physics.IgnoreLayerCollision(0,0,false);
     var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/Prefabs/RailroadHandcarObject.prefab");
     foreach(var mass in new[]{180f,440f}){
      var p=new Vector3(200+rigs.Count*15,20.1f,200);
      var floor=new GameObject("Handcar test floor");floor.transform.position=p-new Vector3(0,.6f,0);floor.AddComponent<BoxCollider>().size=new Vector3(10,1,10);
      var rig=RailWheelSuspension.CreateSettlingRig(prefab.transform,mass,p);rig.name="Handcar "+mass+"kg";
      var body=rig.GetComponent<Rigidbody>();body.constraints=RigidbodyConstraints.FreezePositionX|RigidbodyConstraints.FreezePositionZ|RigidbodyConstraints.FreezeRotation;body.centerOfMass=prefab.GetComponent<Rigidbody>().centerOfMass;
      body.rotation=Quaternion.identity;rigs.Add(rig);
     }
     Physics.SyncTransforms();began=EditorApplication.timeSinceStartup;return;
    }
    var elapsed=EditorApplication.timeSinceStartup-began;
    if(elapsed>5)foreach(var rig in rigs){var body=rig.GetComponent<Rigidbody>();peak=Mathf.Max(peak,Mathf.Abs(body.linearVelocity.y));min=Mathf.Min(min,body.position.y);max=Mathf.Max(max,body.position.y);}
    if(elapsed<8)return;
    foreach(var rig in rigs){var body=rig.GetComponent<Rigidbody>();
     if(Mathf.Abs(body.position.y-20)>.03f||Mathf.Abs(body.linearVelocity.y)>.02f||rig.GetComponentsInChildren<WheelCollider>().Any(w=>!w.isGrounded))throw new Exception("Handcar wheel settling failed: "+rig.name+" height="+body.position.y+" velocity="+body.linearVelocity);
     Debug.Log("HANDCAR_WHEEL_PLAY_OK: "+rig.name+" offset="+(body.position.y-20)+" velocity="+body.linearVelocity.y);
    }
    if(peak>.02f)throw new Exception("Handcar suspension oscillates: peak="+peak);
    Debug.Log("HANDCAR_SUSPENSION_PLAY_OK: native FixedUpdate, empty/rated load, four grounded wheels, isolated vertical settling and final three seconds peak vertical speed="+peak);
    SessionState.SetBool(Pending,false);EditorApplication.Exit(0);
   }catch(Exception e){Debug.LogException(e);SessionState.SetBool(Pending,false);EditorApplication.Exit(1);}
  }
 }
}
