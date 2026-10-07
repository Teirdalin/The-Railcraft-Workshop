using System;using System.Linq;using UnityEditor;using UnityEngine;
namespace EcoMinecarts.Editor {
 public static class TramSwitchSurfacePatch {
  public static void Apply(){
   var count=0;var bed=TrackBlockAssetBuilder.TramBed;
   foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/EcoMinecarts/Prefabs"})){
    var path=AssetDatabase.GUIDToAssetPath(guid);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if(!prefab.name.StartsWith("Tram")||!prefab.name.Contains("RailSwitch"))continue;
    var root=PrefabUtility.LoadPrefabContents(path);
    try{foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
     if(renderer.name=="Street tie"||renderer.name=="Switch mounting sleeper")renderer.sharedMaterial=bed;
     PrefabUtility.SaveAsPrefabAsset(root,path);count++;
    }finally{PrefabUtility.UnloadPrefabContents(root);}
   }
   if(count!=6)throw new Exception("Expected six tram switches; got "+count);
   Debug.Log("TRAM_SWITCH_FINISH_OK: all six switch beds match the regular tram street-bed material.");
  }
  public static void Verify(GameObject[] prefabs){
   var count=0;
   foreach(var prefab in prefabs.Where(p=>p.name.StartsWith("Tram")&&p.name.Contains("RailSwitch"))){
    foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true)){
     var material=renderer.sharedMaterial;
     if(material==null || material.mainTexture==null || material.shader.name!="Curved/Standard")throw new Exception("Untextured tram switch surface: "+prefab.name+"/"+renderer.name);
     if((renderer.name=="Street tie"||renderer.name=="Switch mounting sleeper")&&material.name!="MAT_TramStreetBed")throw new Exception("Tram switch bed finish mismatch");
     var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
     if(mesh==null || !mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))throw new Exception("Tram switch texture coordinates missing");
    }count++;
   }
   if(count!=6)throw new Exception("Expected six exported tram switch variants");
   Debug.Log("TRAM_SWITCH_FINISH_EXPORTED_OK: six bundled variants retain textured curved materials, UV0 and matching street beds.");
  }
 }
}
