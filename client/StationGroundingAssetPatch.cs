using System;using System.IO;using System.Linq;using UnityEditor;using UnityEngine;
namespace EcoMinecarts.Editor {
 public static class StationGroundingAssetPatch {
  public static void Build(){
   const string path="Assets/EcoMinecarts/Prefabs/CoasterStationObject.prefab";
   var root=PrefabUtility.LoadPrefabContents(path);
   try{var deck=root.transform.Find("Loading platform");if(deck==null)throw new Exception("Station deck missing");
    deck.localPosition=new Vector3(0,-.375f,0);deck.localScale=new Vector3(.9f,.25f,1);
    PrefabUtility.SaveAsPrefabAsset(root,path);
   }finally{PrefabUtility.UnloadPrefabContents(root);}
   var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/EcoMinecarts/Scenes/EcoMinecarts.unity");
   var prefabs=scene.GetRootGameObjects().Single(g=>g.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs;
   CoasterAssetProbe.Verify(prefabs);
   TramSwitchSurfacePatch.Apply();
   ExportRepairAssetProbe.BuildAndCheck();
   Debug.Log("STATION_GROUNDING_OK: deck/collider bottom -0.5m, boarding surface -0.25m, rails and fittings preserved.");
  }
  public static void Verify(GameObject station){
   var deck=station.transform.Find("Loading platform");var box=deck.GetComponent<BoxCollider>();
   if(box==null)throw new Exception("Exported station deck collision missing");
   for(var yaw=0;yaw<360;yaw+=90){var rotation=Quaternion.Euler(0,yaw,0);
    var bottom=(rotation*(deck.localPosition+Vector3.down*deck.localScale.y*box.size.y*.5f)).y;
    if(Mathf.Abs(bottom+.5f)>.0001f)throw new Exception("Exported station floor gap at yaw "+yaw);
   }
   if(Mathf.Abs(deck.localPosition.y+deck.localScale.y*.5f+.25f)>.0001f)throw new Exception("Boarding surface changed");
   Debug.Log("STATION_GROUNDING_EXPORTED_OK: bundled station floor contact and unchanged boarding height verified in all yaw rotations.");
  }
 }
}
