using System;using System.Linq;using UnityEditor;using UnityEngine;using Unity.Collections;using TMPro;using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor {
 public static class ExportRepairAssetProbe {
  public static void BuildAndCheck(){
   MinecartAssetBuilder.BuildAuthoredClientBundle();
   Check();
  }
  public static void Check(){
   int meshes=0,plates=0,placements=0;
   foreach(var guid in AssetDatabase.FindAssets("t:Mesh",new[]{"Assets/EcoMinecarts"})){
    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));if(mesh.vertexCount==0||!mesh.name.EndsWith("BlockClimbCollision"))continue;
    if(!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))throw new Exception("Missing UV0: "+mesh.name);
    using(var copy=Mesh.AcquireReadOnlyMeshData(mesh))using(var uv=new NativeArray<Vector2>(mesh.vertexCount,Allocator.Temp)){copy[0].GetUVs(0,uv);}meshes++;
   }
   var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.position=new Vector3(0,0,0);
   try{foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/EcoMinecarts/Prefabs"})){
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));if(prefab.GetComponent<Vehicle>()==null)continue;
    var root=Object.Instantiate(prefab);root.SetActive(true);try{
     var volume=root.GetComponentsInChildren<ColliderPlacementOptions>(true).Single(c=>c.ColliderType==PlacementColliderType.UseAsPlacementVolume).GetComponent<BoxCollider>();
     for(var angle=0;angle<360;angle+=90){root.transform.rotation=Quaternion.Euler(0,angle,0);Physics.SyncTransforms();Vector3 direction;float distance;
      if(Physics.ComputePenetration(volume,volume.transform.position,volume.transform.rotation,ground.GetComponent<BoxCollider>(),ground.transform.position,Quaternion.identity,out direction,out distance))throw new Exception("Snap volume penetrates rail voxel: "+root.name);
     } placements++;
     var vehicle=root.GetComponent<Vehicle>();var group=root.transform.Find("Optional vehicle nameplates");if(group==null)continue;
     var labels=group.GetComponentsInChildren<TextMeshPro>(true);var backs=group.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.GetComponent<TextMeshPro>()==null).ToArray();
     if(!group.gameObject.activeInHierarchy||labels.Any(t=>t.enabled||!t.gameObject.activeInHierarchy)||backs.Any(r=>r.enabled))throw new Exception("Blank label initialization invalid");
     var evt=vehicle.OnStateChangedEvents[Array.IndexOf(vehicle.States,"VehicleTextVisible")];
     for(int i=0;i<evt.GetPersistentEventCount();i++)evt.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
     foreach(var t in labels)t.text="NORTH LINE 24";evt.Invoke(true);
     foreach(var t in labels){t.ForceMeshUpdate();if(!t.enabled||t.textInfo.characterCount<10)throw new Exception("Filled label not rendered");}
     if(backs.Any(r=>!r.enabled))throw new Exception("Filled backing hidden");
     foreach(var t in labels)t.text="";evt.Invoke(false);
     if(labels.Any(t=>t.enabled||!t.gameObject.activeInHierarchy)||backs.Any(r=>r.enabled))throw new Exception("Cleared label visible or inactive");plates++;
    }finally{Object.DestroyImmediate(root);}
   }}finally{Object.DestroyImmediate(ground);}
   Debug.Log("EXPORT_ASSET_REPAIR_OK: "+meshes+" meshes permit native UV reads; "+plates+" active TMP targets hide blank plates; "+placements+" snap volumes clear solid rail voxels in all yaw rotations. Live placement/climbing still requires gameplay.");
  }
 }
}
