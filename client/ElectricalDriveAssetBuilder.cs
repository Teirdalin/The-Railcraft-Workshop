using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    public static class ElectricalDriveAssetBuilder
    {
        const string Root="Assets/EcoMinecarts";
        public static void BuildAndExport() => MinecartAssetBuilder.BuildSavedClientBundle();
        public static void Apply()
        {
            var pairs=new[]{("MinecartChainDriveObject","ElectricalRailChainDriveObject","MinecartChainDriveItem","ElectricalRailChainDriveItem"),
                ("TramCableDriveObject","ElectricalTramCableDriveObject","TramCableDriveItem","ElectricalTramCableDriveItem")};
            var additions=pairs.Select(pair=>{
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+pair.Item1+".prefab");
                if(source==null)throw new Exception("Missing mechanical drive: "+pair.Item1);
                var copy=Object.Instantiate(source);copy.name=pair.Item2;
                // Preserve native animation, connection smoke, curvature and collision.
                var prefab=PrefabUtility.SaveAsPrefabAsset(copy,Root+"/Prefabs/"+pair.Item2+".prefab");
                Object.DestroyImmediate(copy);return prefab;
            }).ToArray();
            var scene=EditorSceneManager.OpenScene(Root+"/Scenes/EcoMinecarts.unity",OpenSceneMode.Single);
            var container=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ModkitPrefabContainer>(true)).Single();
            container.Prefabs=container.Prefabs.Where(p=>p!=null&&!additions.Any(a=>a.name==p.name)).Concat(additions).ToArray();
            var items=scene.GetRootGameObjects().Single(r=>r.name=="Items").transform;
            foreach(var pair in pairs)
            {
                var existing=items.Find(pair.Item4);if(existing!=null)Object.DestroyImmediate(existing.gameObject);
                var source=items.Find(pair.Item3);if(source==null)throw new Exception("Missing item icon: "+pair.Item3);
                var copy=Object.Instantiate(source.gameObject,items);copy.name=pair.Item4;
            }
            EditorUtility.SetDirty(container);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("ELECTRICAL_DRIVE_ASSETS_OK: both native drive prefabs and item icons registered; authored meshes, curved materials and state events retained.");
        }
    }
}
