using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    public static class RailVehiclePlacementBuilder
    {
        public static void Apply()
        {
            var count=0;
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/EcoMinecarts/Prefabs"}))
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);
                if(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Vehicle>()==null)continue;
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var old=root.transform.Find("Rail vehicle placement volume");
                    if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                    var renderers=root.GetComponentsInChildren<MeshRenderer>(true);
                    var top=renderers.Select(r=>root.transform.InverseTransformPoint(r.bounds.max).y).DefaultIfEmpty(1).Max();
                    var spec=RailExpansionAssetBuilder.ReadCatalog().Vehicles.SingleOrDefault(s=>s.Key+"Object"==root.name);
                    var length=root.name=="MinecartObject"?1.612f:root.name=="MineTrainObject"?1.86f:spec?.Length??throw new Exception("Missing vehicle specification: "+root.name);
                    var width=root.name=="MinecartObject"?.8f:root.name=="MineTrainObject"?.84f:spec.BodyWidth;
                    // Wheel flanges, pull handles, walking decks and interaction
                    // targets penetrate the track in a valid placement. Eco's
                    // native placement volume excludes them from its preview.
                    // Native snap overlap checks solid rail cells up to y=.5.
                    // Keep the preview volume above that cell while actual wheel
                    // and chassis colliders retain their authored dimensions.
                    var bottom=Mathf.Max(.55f,length*.125f+.15f);
                    var height=Mathf.Max(.25f,top-bottom);
                    var node=new GameObject("Rail vehicle placement volume");
                    node.transform.SetParent(root.transform,false);
                    var box=node.AddComponent<BoxCollider>();
                    box.center=new Vector3(0,bottom+height/2,0);
                    box.size=new Vector3(width,height,length-.06f);
                    var options=node.AddComponent<ColliderPlacementOptions>();
                    options.ColliderType=PlacementColliderType.UseAsPlacementVolume;
                    options.RemoveColliderAfterPlacement=true;
                    // Replacing preview colliders can leave serialized references
                    // to destroyed components. Native IgnoreCollider.Awake passes
                    // every pair to Physics.IgnoreCollision without a null guard.
                    var vehicle=root.GetComponent<Vehicle>();
                    vehicle.AllVehicleColliders=vehicle.AllVehicleColliders.Where(c=>c!=null&&c!=box).ToArray();
                    foreach(var ignore in root.GetComponentsInChildren<IgnoreCollider>(true))
                    {
                        var serialized=new SerializedObject(ignore);
                        foreach(var field in new[]{"ignoreColliders","otherColliders"})
                        {
                            var array=serialized.FindProperty(field);
                            if(array==null||!array.isArray)throw new Exception("IgnoreCollider contract changed: "+field);
                            var kept=Enumerable.Range(0,array.arraySize)
                                .Select(i=>array.GetArrayElementAtIndex(i).objectReferenceValue)
                                .Where(c=>c!=null&&c!=box).ToArray();
                            array.arraySize=kept.Length;
                            for(var i=0;i<kept.Length;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=kept[i];
                        }
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    if(root.GetComponentsInChildren<ColliderPlacementOptions>(true).Count(o=>o.ColliderType==PlacementColliderType.UseAsPlacementVolume)!=1)
                        throw new Exception("Vehicle needs one native placement volume: "+root.name);
                    PrefabUtility.SaveAsPrefabAsset(root,path);count++;
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            if(count!=RailExpansionAssetBuilder.ReadCatalog().Vehicles.Length+2)throw new Exception("Not every rail vehicle received its placement volume: "+count);
            Debug.Log("ECO_VEHICLE_PLACEMENT_VOLUMES_OK: "+count+" vehicles; native preview-only volumes, removed on actual placement.");
        }
    }
}
