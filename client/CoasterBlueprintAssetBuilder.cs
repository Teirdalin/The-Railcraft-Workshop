using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    public static class CoasterBlueprintAssetBuilder
    {
        const string Root = "Assets/EcoMinecarts";
        const string Folder = Root + "/Blueprints";
        static readonly string[] States = { "PreviewClear", "PreviewWarning", "PreviewBlocked", "PreviewSelected" };
        static readonly Color[] Colors = { new Color(.16f,.85f,.36f,.34f), new Color(.95f,.68f,.12f,.34f), new Color(.95f,.16f,.13f,.40f), new Color(.92f,.97f,1,.54f) };
        static Material Material(int state)
        {
            var path = Folder + "/" + States[state] + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Curved/Standard")); AssetDatabase.CreateAsset(mat,path); }
            mat.color=Colors[state];mat.SetFloat("_Mode",3); mat.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);mat.SetFloat("_ZWrite",0);
            mat.SetFloat("_Metallic",0);mat.SetFloat("_Glossiness",.1f);
            mat.SetOverrideTag("RenderType","Transparent"); mat.renderQueue=3000;
            mat.shaderKeywords=new[]{"_ALPHABLEND_ON"};mat.enableInstancing=true;
            EditorUtility.SetDirty(mat);return mat;
        }
        public static void BuildAndExport() => MinecartAssetBuilder.BuildAuthoredClientBundle();
        public static void BuildAssetsAndRegister()
        {
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Root,"Blueprints");
            var catalog=RailExpansionAssetBuilder.ReadCatalog();var materials=Enumerable.Range(0,4).Select(Material).ToArray();
            var sources=new List<(string Key,GameObject Prefab,CoasterAssetBuilder.Point[] Points,Vector3 Offset)>();
            foreach(var tile in catalog.CoasterTerrain)
                sources.Add((tile.Key,AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/CoasterBlocks/"+tile.Key+"Block.prefab"),tile.Points,Vector3.zero));
            foreach(var shape in catalog.Coasters.Where(p=>p.Key!="CoasterStraight"))
                sources.Add((shape.Key,AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+shape.Key+"Object.prefab"),shape.Points,
                    new Vector3(0,0,Mathf.Round(-shape.Points[0].Z-.5f))));
            sources.Add(("CoasterStation",AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/CoasterStationObject.prefab"),
                new[]{new CoasterAssetBuilder.Point{X=-1,Y=-.35f,Z=-.5f,UpY=1},new CoasterAssetBuilder.Point{X=-1,Y=-.35f,Z=.5f,UpY=1}},Vector3.zero));
            var prefabs=new List<GameObject>();
            foreach(var source in sources)
            {
                if(source.Prefab==null)throw new Exception("Missing blueprint source: "+source.Key);
                var name="CoasterGhost"+source.Key+"Object";
                var root=new GameObject(name);root.tag="ModObject";
                var world=root.AddComponent<WorldObject>();root.AddComponent<HighlightableObject>();
                world.hasOccupancy=false;world.overrideOccupancy=false;world.size=Vector3.one;world.interactable=true;
                world.States=States.ToArray();world.OnStateChangedEvents=new ChangedStateEvent[4];
                world.OnStateEnabledEvents=new SetStateEvent[4];world.OnStateDisabledEvents=new SetStateEvent[4];
                for(int state=0;state<4;state++)
                {
                    var draw=new GameObject(States[state]);draw.transform.SetParent(root.transform,false);
                    foreach(var filter in source.Prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var renderer=filter.GetComponent<MeshRenderer>();
                        if(renderer==null||!renderer.enabled||filter.sharedMesh==null)continue;
                        var mesh=new GameObject(filter.name,typeof(MeshFilter),typeof(MeshRenderer));mesh.transform.SetParent(draw.transform,false);
                        mesh.transform.localPosition=source.Prefab.transform.InverseTransformPoint(filter.transform.position);
                        mesh.transform.localRotation=Quaternion.Inverse(source.Prefab.transform.rotation)*filter.transform.rotation;
                        mesh.transform.localScale=filter.transform.lossyScale;
                        mesh.GetComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
                        var visual=mesh.GetComponent<MeshRenderer>();visual.sharedMaterials=Enumerable.Repeat(materials[state],renderer.sharedMaterials.Length).ToArray();
                        visual.shadowCastingMode=ShadowCastingMode.Off;visual.receiveShadows=false;
                    }
                    world.OnStateChangedEvents[state]=new ChangedStateEvent();world.OnStateEnabledEvents[state]=new SetStateEvent();world.OnStateDisabledEvents[state]=new SetStateEvent();
                    UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[state],draw.SetActive);draw.SetActive(false);
                }
                // Thin trigger spans follow the actual rail. There is no solid
                // bounding box across the empty interior of a loop or coil.
                var stride=Math.Max(1,(source.Points.Length-1)/32);
                for(int i=0;i<source.Points.Length-1;i+=stride)
                {
                    var a=source.Points[i].Position+source.Offset;var b=source.Points[Math.Min(i+stride,source.Points.Length-1)].Position+source.Offset;
                    if((b-a).sqrMagnitude<.000001f)continue;
                    var hit=new GameObject("BlueprintRailClick");hit.transform.SetParent(root.transform,false);
                    hit.transform.localPosition=(a+b)/2;hit.transform.localRotation=Quaternion.LookRotation((b-a).normalized,Vector3.Slerp(source.Points[i].Up,source.Points[Math.Min(i+stride,source.Points.Length-1)].Up,.5f));
                    var box=hit.AddComponent<BoxCollider>();box.size=new Vector3(.76f,.22f,(b-a).magnitude+.01f);box.isTrigger=true;
                    var target=hit.AddComponent<SpecificInteractable>();target.interactionTargetName="CoasterBlueprintPiece";target.interactionTargetValue="";
                }
                if(root.GetComponentsInChildren<Collider>(true).Any(c=>!c.isTrigger)||root.GetComponentsInChildren<Rigidbody>(true).Length>0)
                    throw new Exception("Planning preview gained physical collision: "+name);
                prefabs.Add(PrefabUtility.SaveAsPrefabAsset(root,Folder+"/"+name+".prefab"));Object.DestroyImmediate(root);
            }
            var scene=EditorSceneManager.OpenScene(Root+"/Scenes/EcoMinecarts.unity",OpenSceneMode.Single);
            var container=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ModkitPrefabContainer>(true)).Single();
            container.Prefabs=container.Prefabs.Where(p=>p!=null&&!p.name.StartsWith("CoasterGhost")).Concat(prefabs).ToArray();
            EditorUtility.SetDirty(container);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("COASTER_BLUEPRINT_ASSETS_OK: "+prefabs.Count+" native WorldObject previews; trigger-only rail spans; no physics bodies; curved transparent materials");
        }
    }
}
