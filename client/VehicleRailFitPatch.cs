using System;using System.Linq;using UnityEditor;using UnityEngine;
namespace EcoMinecarts.Editor {
 public static class VehicleRailFitPatch {
  public static void ConfigureHandcar(GameObject root,RailExpansionAssetBuilder.Spec spec){
   // Handcar has no inherited locomotive axle load. Support cargo plus two operators.
   RailWheelSuspension.Configure(root,spec.EmptyKg,spec.EmptyKg+spec.CargoKg+160);
   foreach(var wheel in root.GetComponentsInChildren<WheelCollider>(true)){
    // Use 40% of travel at rated load; bound damper impulse for the empty 50 Hz body.
    var suspension=wheel.suspensionSpring;suspension.spring*=.625f;suspension.damper=.4f*spec.EmptyKg/(4*.02f);wheel.suspensionSpring=suspension;wheel.center=Vector3.up*(.03f-spec.EmptyKg*9.81f/(4*suspension.spring));
   }
   var drive=root.GetComponent<RCCCarControllerV2>();drive.footPoweredCart=true;drive._wheelTypeChoise=2;drive.TCS=false;drive.ESP=false;
   drive.autoGenerateGearCurves=false;drive.autoGenerateTargetSpeedsForChangingGear=false;drive.totalGears=1;drive.currentGear=0;
   drive.maxspeed=spec.MaximumSpeed*3.6f;drive.gearSpeed=new[]{drive.maxspeed};drive.engineTorqueCurve=new[]{AnimationCurve.Linear(0,1,drive.maxspeed,0)};
   var limiter=root.GetComponent<LimitVelocity>()??root.AddComponent<LimitVelocity>();limiter.MaxVelocity=spec.MaximumSpeed;limiter.MaxAccel=1.5f;limiter.ignoreY=true;
  }
  static void FitLarge(GameObject root,RailExpansionAssetBuilder.Spec spec){
   var drive=root.GetComponent<RCCCarControllerV2>();
   var poses=new[]{drive.FrontLeftWheelTransform,drive.FrontRightWheelTransform,drive.RearLeftWheelTransform,drive.RearRightWheelTransform};
   var wheels=new[]{drive.FrontLeftWheelCollider,drive.FrontRightWheelCollider,drive.RearLeftWheelCollider,drive.RearRightWheelCollider};
   for(int i=0;i<4;i++){
    var p=new Vector3((i%2==0?-1:1)*spec.HalfGauge,.20f,(i<2?1:-1)*spec.Wheelbase/2);
    poses[i].localPosition=p;wheels[i].transform.position=root.transform.TransformPoint(p);
   }
   foreach(var node in root.GetComponentsInChildren<Transform>(true)){
    var p=node.localPosition;
    if(node.name=="Riveted chassis"){var s=node.localScale;s.x=spec.Industrial?1.7f:.44f;node.localScale=s;}
    if(node.name=="Wheel axle"){var s=node.localScale;s.y=(2*spec.HalfGauge+.05f)/2;node.localScale=s;}
    if(node.name=="Axle bearing"||node.name=="Leaf spring") {p.x=Mathf.Sign(p.x)*(spec.HalfGauge-.08f);node.localPosition=p;}
    if(node.name=="Piston cylinder"){p.x=Mathf.Sign(p.x)*(spec.HalfGauge+.09f);node.localPosition=p;}
    if(node.name=="Connecting rod"){p.x=Mathf.Sign(p.x)*(spec.HalfGauge+.05f);node.localPosition=p;}
    if(node.name=="Frame beam"){p.x=Mathf.Sign(p.x)*(spec.HalfGauge-.065f);node.localPosition=p;}
    if(node.name=="Frame rivet"){p.x=Mathf.Sign(p.x)*(spec.HalfGauge-.037f);node.localPosition=p;}
   }
  }
  public static void Build(){
   var catalog=RailExpansionAssetBuilder.ReadCatalog();
   foreach(var spec in catalog.Vehicles.Where(s=>s.HumanPowered||s.Key.StartsWith("Large"))){
    var path="Assets/EcoMinecarts/Prefabs/"+spec.Key+"Object.prefab";var root=PrefabUtility.LoadPrefabContents(path);
    try{if(spec.HumanPowered)ConfigureHandcar(root,spec);else {FitLarge(root,spec);if(spec.Key=="LargeTrainEngine")RailVehicleDetail.Apply(root,new System.Collections.Generic.Dictionary<string,Material>{{"MAT_IronBare",AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronBare.mat")},{"MAT_WoodRail",AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_WoodRail.mat")}});}PrefabUtility.SaveAsPrefabAsset(root,path);}
    finally{PrefabUtility.UnloadPrefabContents(root);}
   }
   var prefabs=catalog.Vehicles.Where(s=>s.HumanPowered||s.Key.StartsWith("Large")).Select(s=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/Prefabs/"+s.Key+"Object.prefab")).ToArray();
   Verify(prefabs);
   ExportRepairAssetProbe.BuildAndCheck();Debug.Log("VEHICLE_RAIL_FIT_ASSETS_OK: handcar suspension/drive retuned; four large vehicles match release gauge; library and contact-plane pivots preserved.");
  }
  public static void Verify(GameObject[] prefabs){
   foreach(var spec in RailExpansionAssetBuilder.ReadCatalog().Vehicles.Where(s=>s.HumanPowered||s.Key.StartsWith("Large"))){
    var root=prefabs.Single(p=>p.name==spec.Key+"Object");var drive=root.GetComponent<RCCCarControllerV2>();
    var poses=new[]{drive.FrontLeftWheelTransform,drive.FrontRightWheelTransform,drive.RearLeftWheelTransform,drive.RearRightWheelTransform};
    var wheels=new[]{drive.FrontLeftWheelCollider,drive.FrontRightWheelCollider,drive.RearLeftWheelCollider,drive.RearRightWheelCollider};
    for(int i=0;i<4;i++){
     if(Mathf.Abs(Mathf.Abs(root.transform.InverseTransformPoint(poses[i].position).x)-spec.HalfGauge)>.001f||Mathf.Abs(Mathf.Abs(root.transform.InverseTransformPoint(wheels[i].transform.position).x)-spec.HalfGauge)>.001f)throw new Exception("Wheel gauge mismatch: "+root.name);
     if(poses[i].GetComponentsInChildren<MeshRenderer>().Count(r=>r.enabled)<3)throw new Exception("Wheel geometry missing: "+root.name);
     if(Mathf.Abs(poses[i].localPosition.y-wheels[i].radius)>.001f)throw new Exception("Wheel contact-plane mismatch: "+root.name);
    }
    if(spec.HumanPowered){var wheel=wheels[0];var extension=wheel.suspensionDistance*(1-wheel.suspensionSpring.targetPosition)-spec.EmptyKg*9.81f/(4*wheel.suspensionSpring.spring);
     if(Mathf.Abs(extension-wheel.center.y)>.001f||wheel.suspensionSpring.damper>1200||drive._wheelTypeChoise!=2||drive.TCS||drive.ESP)throw new Exception("Handcar native drive setup mismatch");
    }
   }
   Debug.Log("VEHICLE_RAIL_FIT_EXPORTED_OK: five vehicles retain four visible wheels at correct gauge/contact plane; handcar equilibrium and bounded native damping.");
  }
 }
}
