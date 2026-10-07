using System;using System.Linq;using UnityEditor;using UnityEngine;
namespace EcoMinecarts.Editor {
 public static class BrakingChainAssetPatch {
  public static void Build(){
   var section=RailExpansionAssetBuilder.ReadCatalog().CoasterTerrain.Single(s=>s.Key=="CoasterTrackChainBrakeSection01");
   var set=AssetDatabase.LoadAssetAtPath<BlockSet>("Assets/EcoMinecarts/CoasterBlocks/CoasterTrack.asset");
   var steel=AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronBare.mat");
   const string finish="Assets/EcoMinecarts/Materials/MAT_CoasterBrake.mat";
   var paint=AssetDatabase.LoadAssetAtPath<Material>(finish);
   if(paint==null){paint=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronPainted.mat"));AssetDatabase.CreateAsset(paint,finish);}
   paint.name="MAT_CoasterBrake";paint.color=new Color(.52f,.23f,.08f);EditorUtility.SetDirty(paint);
   set.Blocks.RemoveAll(b=>b.Name.StartsWith(section.Key));
   var geometry=CoasterTerrainAssetBuilder.Geometry(section,steel,paint);
   for(var turn=0;turn<4;turn++){
    var rotated=new GameObject("Brake rotation");geometry.transform.SetParent(rotated.transform,false);geometry.transform.localRotation=Quaternion.Euler(0,turn*90,0);
    CoasterTerrainAssetBuilder.Register(set,rotated,section.Key+(turn==0?"":"R"+(turn*90)),steel,paint);
    geometry.transform.SetParent(null,false);UnityEngine.Object.DestroyImmediate(rotated);
   }
   UnityEngine.Object.DestroyImmediate(geometry);EditorUtility.SetDirty(set);
   MinecartIconBuilder.Render(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/CoasterBlocks/"+section.Key+"Block.prefab"),section.Key);
   foreach(var block in set.Blocks.Where(b=>b.Name.StartsWith(section.Key)))CoasterTerrainAssetBuilder.Verify(block);
   ExportRepairAssetProbe.BuildAndCheck();
   Debug.Log("BRAKING_CHAIN_ASSETS_OK: four native terrain rotations, readable UV meshes/colliders, curved textured brake finish, icon and blueprint preview; existing library preserved.");
  }
  public static void Verify(Block[] blocks){
   var variants=blocks.Where(b=>b.Name.StartsWith("CoasterTrackChainBrakeSection01")).ToArray();
   if(variants.Length!=4)throw new Exception("Braking-chain rotation missing from exported bundle");
   foreach(var block in variants){CoasterTerrainAssetBuilder.Verify(block);
    if(block.Material.shader.name!="Curved/Standard"||block.Materials.Length!=1||block.Materials[0].name!="MAT_CoasterBrake"||block.Materials[0].shader.name!="Curved/Standard")throw new Exception("Braking chain finish/shader missing");
   }
   Debug.Log("BRAKING_CHAIN_EXPORTED_OK: four registered textured curved terrain meshes, UV0 and running-rail colliders verified.");
  }
 }
}
