using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace EcoMinecarts.Editor {
public static class VehicleIntegrationProbe {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 public static void VerifyExported(GameObject[] prefabs){
  int count=0;
  foreach(var prefab in prefabs.Where(p=>p.GetComponent<Vehicle>()!=null)){
   var o=UnityEngine.Object.Instantiate(prefab);o.name=prefab.name;
   try{
    o.SetActive(true);RailVehicleDesignProbe.Select(o,true);RailPreparedVehicleFit.Verify(o);count++;
    Check(o.transform.Find("PreparedVehicleArt")!=null,"Unconverted shipping vehicle "+o.name);
    var volumes=o.GetComponentsInChildren<ColliderPlacementOptions>(true).Where(p=>p.ColliderType==PlacementColliderType.UseAsPlacementVolume).ToArray();
    Check(volumes.Length==1&&volumes[0].RemoveColliderAfterPlacement,"Invalid snap placement volume "+o.name);
    var placement=volumes[0].GetComponent<BoxCollider>();
    Check(Mathf.Abs(placement.center.y-placement.size.y/2)<.0001f,"Snap volume forces vehicle into ground "+o.name);
    Check(placement.transform.localPosition==Vector3.zero&&placement.transform.localScale==Vector3.one,"Transformed snap placement datum "+o.name);
    var drive=o.GetComponent<RCCCarControllerV2>();
    foreach(var wheel in new[]{drive.FrontLeftWheelCollider,drive.FrontRightWheelCollider,drive.RearLeftWheelCollider,drive.RearRightWheelCollider})
      Check(Mathf.Abs(o.transform.InverseTransformPoint(wheel.transform.position).y-wheel.radius)<.025f,"Wheel contact does not match snap datum "+o.name);
    if(o.transform.Find("CabFittings")!=null)StandingCabAssetBuilder.Verify(o);
    var world=o.GetComponent<WorldObject>();
    if(o.name=="MineTrainObject")Check(!o.GetComponent<RCCCarControllerV2>().enabled,"Competing minetrain client driving controller");
    if(o.name=="HeritageTramObject"){
     var slot=Array.IndexOf(world.States,"TramNightLights");Check(slot>=0,"Missing night lamps");var evt=world.OnStateChangedEvents[slot];
     for(int i=0;i<evt.GetPersistentEventCount();i++)evt.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
     var lamps=o.transform.Find("Tram night lamps").GetComponentsInChildren<Light>();Check(lamps.Length==2,"Missing end lamps");
     evt.Invoke(true);Check(lamps.All(l=>l.enabled),"Night lamps failed on");evt.Invoke(false);Check(lamps.All(l=>!l.enabled),"Day lamps failed off");
     var vehicle=o.GetComponent<Vehicle>();foreach(var label in new[]{vehicle.LicensePlate}.Concat(vehicle.ExtraLicensePlates))label.text="Central Station";
     var plates=world.OnStateChangedEvents[Array.IndexOf(world.States,"VehicleTextVisible")];
     for(int i=0;i<plates.GetPersistentEventCount();i++)plates.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
     plates.Invoke(true);Check(o.transform.Find("Optional vehicle nameplates").GetComponentsInChildren<MeshRenderer>().All(r=>r.enabled),"Destination placard incomplete");
     Capture(o,"destination-day",false);evt.Invoke(true);Capture(o,"destination-night",false,true);evt.Invoke(false);
     plates.Invoke(false);Check(o.transform.Find("Optional vehicle nameplates").GetComponentsInChildren<MeshRenderer>().Where(r=>r.GetComponent<TMPro.TextMeshPro>()==null).All(r=>!r.enabled),"Blank destination leaves visible frame");
     Check(!vehicle.LicensePlate.enabled&&vehicle.ExtraLicensePlates.All(t=>!t.enabled),"Blank destination leaves text enabled");
    }
    foreach(var key in new[]{"RailRestraintPose","RailThrottlePose","RailBrakePose","RailReverserPose"}){
     int slot=Array.IndexOf(world.StringStates,key);if(slot<0)continue;var evt=world.OnStringStateChanged[slot];
     for(int i=0;i<evt.GetPersistentEventCount();i++)evt.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
     var targets=Enumerable.Range(0,evt.GetPersistentEventCount()).Where(i=>evt.GetPersistentMethodName(i)=="SetTrigger").Select(i=>(Animator)evt.GetPersistentTarget(i)).ToArray();
     Check(targets.Length==1,"Wrong replicated visual targets "+o.name+key);
     foreach(var animator in targets){
      animator.Rebind();animator.Update(0);
      var pivot=animator.transform.Find(key=="RailRestraintPose"?"LeftPivot":"Pivot");
      var names=key=="RailRestraintPose"?new[]{"Secured","Boarding","Secured"}:key=="RailThrottlePose"?new[]{"Throttle10","Throttle0"}:key=="RailBrakePose"?new[]{"BrakeOn","BrakeOff"}:new[]{"Reverse","Forward"};
      foreach(var name in names){
       var before=pivot.localRotation;evt.Invoke(name);animator.Update(0);animator.Update(.02f);animator.Update(key=="RailRestraintPose"?.4f:.09f);
       float halfway=Quaternion.Angle(before,pivot.localRotation);animator.Update(1.1f);
       float expected=name=="Secured"?-30:name=="Boarding"?-105:name=="Throttle10"||name=="BrakeOn"||name=="Reverse"?25:name=="BrakeOff"?-20:-25;
       Check(Quaternion.Angle(pivot.localRotation,Quaternion.Euler(expected,0,0))<.3f,"Replicated pose mismatch "+o.name+key+name+" actual "+pivot.localEulerAngles);
       if(key=="RailRestraintPose")Check(Quaternion.Angle(animator.transform.Find("RightPivot").localRotation,pivot.localRotation)<.3f,"Restraints disagree");
       Check(halfway>0&&halfway<Quaternion.Angle(before,pivot.localRotation),"Animation snapped instead of blending "+key);
      }
     }
    }
    if(o.name=="RollerCoasterCartObject"){
     var evt=world.OnStringStateChanged[Array.IndexOf(world.StringStates,"RailRestraintPose")];
     var targets=Enumerable.Range(0,evt.GetPersistentEventCount()).Select(i=>(Animator)evt.GetPersistentTarget(i)).Distinct().ToArray();
     foreach(var a in targets){a.Rebind();a.Update(0);}
     // Repeated defaults and several changes before a rendered frame must not
     // leave stale triggers or opposing left/right restraint states.
     foreach(var pose in new[]{"Boarding","Boarding","Secured","Boarding","Secured"})evt.Invoke(pose);
     foreach(var pose in new[]{"Secured","Boarding"}){
      evt.Invoke(pose);foreach(var a in targets){a.Update(0);a.Update(.02f);a.Update(1.1f);a.Update(1.1f);foreach(var side in new[]{"LeftPivot","RightPivot"})Check(Quaternion.Angle(a.transform.Find(side).localRotation,Quaternion.Euler(pose=="Secured"?-30:-105,0,0))<.3f,"Stale or asymmetric restraint pose");}
      Capture(o,pose,false);
     }
    }
    if(o.transform.Find("CabFittings")!=null)Capture(o,"cab",true);
    if(new[]{"MinecartObject","WoodenMinecartObject","MineTrainObject","HeritageTramObject","RailroadHandcarObject","PassengerCarObject","LargePassengerCarObject"}.Contains(o.name))Capture(o,"assembly",false);
   }finally{UnityEngine.Object.DestroyImmediate(o);}
  }
  Check(count==14,"Missing integrated vehicles");Debug.Log("VEHICLE_INTEGRATION_EXPORTED_OK: fourteen replacement assemblies, mounts, interaction exemptions and actual native Animator restraint/control transitions");
 }
 internal static void Capture(GameObject root,string suffix,bool cab,bool night=false){
  string dir=Environment.GetEnvironmentVariable("ECO_ANIMATION_PREVIEW_DIR");if(string.IsNullOrEmpty(dir))return;
  Directory.CreateDirectory(dir);root.transform.position=Vector3.zero;
  foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
  foreach(var l in root.GetComponentsInChildren<LODGroup>())l.ForceLOD(0);
  var rig=new GameObject("Integration preview");var camera=rig.AddComponent<Camera>();camera.cullingMask=1<<31;camera.nearClipPlane=.01f;camera.farClipPlane=50;
  camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.18f,.20f);
  var lamp=new GameObject("Light");lamp.transform.SetParent(rig.transform);var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.cullingMask=1<<31;light.intensity=night?.05f:1.3f;lamp.transform.rotation=Quaternion.Euler(35,150,0);
  if(cab){var target=root.transform.Find("CabFittings/Cab console").position;camera.transform.position=target+new Vector3(.42f,.5f,-1.45f);camera.transform.LookAt(target);camera.fieldOfView=65;}
  else{var bounds=new Bounds();bool first=true;foreach(var r in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&(r.name=="Render_LOD0"||!r.name.StartsWith("Render_LOD")))){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}camera.orthographic=true;camera.orthographicSize=Mathf.Max(1.15f,bounds.size.y*.65f,bounds.size.z*.55f);camera.transform.position=bounds.center+new Vector3(4,2,5);camera.transform.LookAt(bounds.center);}
  var rt=RenderTexture.GetTemporary(1200,900,24);var previous=RenderTexture.active;var texture=new Texture2D(1200,900,TextureFormat.RGB24,false);
  var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;bool keyword=Shader.IsKeywordEnabled("NO_CURVE");
  var radius=Shader.GetGlobalFloat("_WorldRadius");var center=Shader.GetGlobalVector("_WorldCenter");var axes=Shader.GetGlobalVector("_CurveAxisMask");
  try{Shader.SetGlobalFloat("_WorldRadius",10000);Shader.SetGlobalVector("_WorldCenter",new Vector4(0,-10000,0,0));Shader.SetGlobalVector("_CurveAxisMask",Vector4.zero);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=Color.white*(night?.02f:.6f);Shader.EnableKeyword("NO_CURVE");camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1200,900),0,0);texture.Apply();Check(texture.GetPixels32().Select(p=>(p.r,p.g,p.b)).Distinct().Count()>100,"Empty presentation preview "+root.name+suffix);File.WriteAllBytes(Path.Combine(dir,root.name+"-"+suffix+".png"),texture.EncodeToPNG());}
  finally{Shader.SetGlobalFloat("_WorldRadius",radius);Shader.SetGlobalVector("_WorldCenter",center);Shader.SetGlobalVector("_CurveAxisMask",axes);RenderTexture.active=previous;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(rig);RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;if(!keyword)Shader.DisableKeyword("NO_CURVE");}
 }
 public static void Snapshot() {
  var output=new StringBuilder();
  foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/EcoMinecarts/Prefabs"})) {
   var o=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));if(o.GetComponent<Vehicle>()==null)continue;
   output.AppendLine("VEHICLE "+o.name);
   foreach(var t in o.GetComponentsInChildren<Transform>(true).Where(t=>t.GetComponent<MountSpot>()!=null||t.GetComponent<SpecificInteractable>()!=null||t.name.Contains("bench")||t.name.Contains("floor")||t.name.Contains("roof")||t.name.Contains("Passenger")||t.name.Contains("Cab")))
    output.AppendLine(t.name+" pos="+o.transform.InverseTransformPoint(t.position).ToString("F3")+" scale="+t.lossyScale.ToString("F3")+" yaw="+t.eulerAngles.y+" target="+t.GetComponent<SpecificInteractable>()?.interactionTargetName);
  }
  File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../../validation/vehicle-mount-snapshot.txt")),output.ToString());
 }
}}
