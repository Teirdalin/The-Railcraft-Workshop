using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class RailSupportAssetBuilder
    {
        const string Root="Assets/EcoMinecarts";
        static void Box(Transform root,string name,Vector3 p,Vector3 size,Material material,Quaternion rotation)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(root,false);
            o.transform.localPosition=p;o.transform.localScale=size;o.transform.localRotation=rotation;
            o.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(o.GetComponent<Collider>());
        }
        static void Beam(Transform root,string name,Vector3 a,Vector3 b,float thickness,Material material)
        {Box(root,name,(a+b)/2,new Vector3(thickness,(b-a).magnitude,thickness),material,Quaternion.FromToRotation(Vector3.up,b-a));}
        static GameObject Geometry(string tier,string part,int phase,Material structure,Material fittings)
        {
            var root=new GameObject("Support");var upper=.43f+(part=="Top"&&phase>0?(phase-1)*.25f:0);
            foreach(var x in new[]{-.28f,.28f})foreach(var z in new[]{-.28f,.28f})
            {
                Beam(root.transform,"Column",new Vector3(x,-.5f,z),new Vector3(x,part=="Top"?upper:.5f,z),tier=="Wood"?.12f:.09f,structure);
                if(part=="Base"){
                    Box(root.transform,"Anchored foot plate",new Vector3(x,-.45f,z),new Vector3(.25f,.10f,.25f),fittings,Quaternion.identity);
                    foreach(var dx in new[]{-.075f,.075f})Box(root.transform,"Anchor bolt",new Vector3(x+dx,-.385f,z),new Vector3(.025f,.03f,.025f),fittings,Quaternion.identity);
                }
            }
            foreach(var z in new[]{-.28f,.28f}){
                Beam(root.transform,"Diagonal brace",new Vector3(-.28f,-.35f,z),new Vector3(.28f,upper-.12f,z),.055f,structure);
                Beam(root.transform,"Diagonal brace",new Vector3(.28f,-.35f,z),new Vector3(-.28f,upper-.12f,z),.055f,structure);
            }
            foreach(var y in new[]{-.46f,upper-.07f})Box(root.transform,"Joint collar",new Vector3(0,y,0),new Vector3(.72f,.065f,.72f),fittings,Quaternion.identity);
            if(part=="Top"){
                var pitch=phase>0?Quaternion.Euler(-Mathf.Atan(.25f)*Mathf.Rad2Deg,0,0):Quaternion.identity;
                Box(root.transform,"Rail saddle crossbeam",new Vector3(0,upper,0),new Vector3(.94f,.11f,.22f),structure,pitch);
                foreach(var x in new[]{-.32f,.32f}){
                    Box(root.transform,"Rail clamp shoe",new Vector3(x,upper+.067f,0),new Vector3(.18f,.035f,.30f),fittings,pitch);
                    Box(root.transform,"Clamp bolt",new Vector3(x,upper+.10f,.09f),new Vector3(.035f,.05f,.035f),fittings,pitch);
                }
            }
            return root;
        }
        public static BlockSet Build()
        {
            var path=Root+"/CoasterBlocks/RailSupports.asset";
            var set=AssetDatabase.LoadAssetAtPath<BlockSet>(path);
            if(set==null){set=ScriptableObject.CreateInstance<BlockSet>();AssetDatabase.CreateAsset(set,path);}set.Blocks.Clear();
            foreach(var tier in new[]{"Wood","Iron","Steel"}){
                var structure=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+(tier=="Wood"?"MAT_WoodRail":tier=="Iron"?"MAT_IronBare":"MAT_IronPainted")+".mat");
                var fittings=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+(tier=="Iron"?"MAT_IronPainted":"MAT_IronBare")+".mat");
                if(structure==null||fittings==null)throw new Exception("Support finish materials missing");
                var key="RailSupport"+tier;
                Action<string,string,int,int> add=(name,part,phase,turn)=>{
                    var root=Geometry(tier,part,phase,structure,fittings);root.transform.rotation=Quaternion.Euler(0,turn,0);
                    // Bake the rotation into a parent, matching the native rotated variant contract.
                    var parent=new GameObject("BakedSupport");root.transform.SetParent(parent.transform,false);
                    CoasterTerrainAssetBuilder.Register(set,parent,name,structure,fittings);
                    set.Blocks.Last().Category="Rail Supports";set.Blocks.Last().AudioCategory=tier=="Wood"?"Wood":"Metal";
                    Object.DestroyImmediate(parent);
                };
                add(key,"Middle",0,0);
                foreach(var part in new[]{"Base","Middle","Top"}){
                    foreach(var turn in new[]{0,90,180,270})add(key+part+(turn==0?"":"R"+turn),part,0,turn);
                    MinecartIconBuilder.Render(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/CoasterBlocks/"+key+part+"Block.prefab"),key+part);
                }
                foreach(var phase in new[]{1,2,3,4})foreach(var turn in new[]{0,90,180,270})add(key+"TopSlope"+phase+(turn==0?"":"R"+turn),"Top",phase,turn);
                for(var n=1;n<=4;n++){
                    var root=new GameObject("Carried support stack");
                    for(var i=0;i<n;i++)Box(root.transform,"Bundled beams",new Vector3(0,-.4f+i*.17f,0),new Vector3(.68f,.14f,.68f),structure,Quaternion.identity);
                    Box(root.transform,"Stack binding",new Vector3(0,-.4f,0),new Vector3(.74f,.04f,.1f),fittings,Quaternion.identity);
                    CoasterTerrainAssetBuilder.Register(set,root,key+"Stacked"+n,structure,fittings);set.Blocks.Last().Category="Rail Supports";Object.DestroyImmediate(root);
                }
            }
            EditorUtility.SetDirty(set);AssetDatabase.SaveAssets();Debug.Log("RAIL_SUPPORT_ASSETS_OK: "+set.Blocks.Count+" registered brown-icon support blocks");return set;
        }
    }
}
