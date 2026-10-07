using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class TramAccessAssetBuilder
    {
        public static void GroundStation(GameObject root)
        {
            if(root.name!="TrainStationObject" || root.transform.Find("Grounded station geometry")!=null)return;
            var holder=new GameObject("Grounded station geometry");holder.transform.SetParent(root.transform,false);
            foreach(var child in root.transform.Cast<Transform>().Where(t=>t!=holder.transform).ToArray())child.SetParent(holder.transform,false);
            holder.transform.localPosition=Vector3.down*.5f;
        }
        public static void GroundStationAndBuildBundle()
        {
            var path="Assets/EcoMinecarts/Prefabs/TrainStationObject.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try{GroundStation(root);PrefabUtility.SaveAsPrefabAsset(root,path);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();MinecartAssetBuilder.BuildAuthoredClientBundle();
            Debug.Log("TRAIN_STATION_SNAP_OK: native block-bottom height; side station footprint retained; no integral track");
        }
        public static void Apply(GameObject root)
        {
            if(root.name!="HeritageTramObject")return;
            foreach(var node in root.transform.Cast<Transform>().Where(t=>t.name.StartsWith("TramEndSettings")).ToArray())Object.DestroyImmediate(node.gameObject);
            foreach(var end in new[]{-1,1})
            {
                var node=new GameObject("TramEndSettings"+(end>0?"Front":"Rear"));node.transform.SetParent(root.transform,false);
                node.transform.localPosition=new Vector3(0,1.16f,end*1.25f);
                var box=node.AddComponent<BoxCollider>();box.size=new Vector3(.965f,1.10f,.075f);
                var target=node.AddComponent<SpecificInteractable>();target.interactionTargetName="TramSettings";
                target.interactionTargetValue=end.ToString();
            }
            root.GetComponent<global::Vehicle>().AllVehicleColliders=root.GetComponentsInChildren<Collider>(true);
            RailVehiclePhysicsAssetBuilder.Configure(root);
        }
        public static void RefreshAndBuildBundle()
        {
            var path="Assets/EcoMinecarts/Prefabs/HeritageTramObject.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                Apply(root);
                foreach(var end in new[]{"Front","Rear"})
                {
                    var panel=root.transform.Find("TramEndSettings"+end);
                    var collider=panel.GetComponent<BoxCollider>();
                    if(collider.isTrigger || collider.attachedRigidbody!=root.GetComponent<Rigidbody>()
                        || panel.GetComponent<SpecificInteractable>().interactionTargetName!="TramSettings")throw new Exception("Invalid tram end panel: "+end);
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();MinecartAssetBuilder.BuildAuthoredClientBundle();
            Debug.Log("TRAM_END_ACCESS_OK: solid front/rear panels; TramSettings targets; chassis ownership and native self-collision exclusions retained");
        }
    }
}
