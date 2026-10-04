using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    public static class RailVehiclePhysicsAssetBuilder
    {
        public static bool ServerOnly(GameObject root)
        {
            if(root.name=="MineTrainObject")return true;
            if(root.name=="MinecartObject")return false;
            var spec=RailExpansionAssetBuilder.ReadCatalog().Vehicles.Single(s=>s.Key+"Object"==root.name);
            return !spec.Pullable&&!spec.HumanPowered;
        }
        public static void Configure(GameObject root)
        {
            if(root.GetComponent<Vehicle>()==null)return;
            if(ServerOnly(root))
            {
                var sync=root.GetComponent<SyncPhysics>();
                sync.SyncVelocity=false;sync.distanceToIgnorePhysics=0;
                var body=root.GetComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
                foreach(var wheel in root.GetComponentsInChildren<WheelCollider>(true))wheel.enabled=false;
            }
            // Child walking surfaces are distinct PhysX bodies. A transform
            // parent does NOT prevent them from colliding with their own chassis.
            // Use Eco's shipped Awake component so exclusions also run in game.
            var all=root.GetComponentsInChildren<Collider>(true);
            foreach(var body in root.GetComponentsInChildren<Rigidbody>(true).Where(b=>b!=root.GetComponent<Rigidbody>()))
            {
                var own=all.Where(c=>c.attachedRigidbody==body).ToArray();
                var other=all.Where(c=>c.attachedRigidbody!=body).ToArray();
                var ignore=body.GetComponent<IgnoreCollider>()??body.gameObject.AddComponent<IgnoreCollider>();
                var serialized=new SerializedObject(ignore);
                Set(serialized.FindProperty("ignoreColliders"),own);
                Set(serialized.FindProperty("otherColliders"),other);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        static void Set(SerializedProperty property,Collider[] colliders)
        {
            if(property==null)throw new Exception("Installed IgnoreCollider field mismatch");
            property.arraySize=colliders.Length;
            for(int i=0;i<colliders.Length;i++)property.GetArrayElementAtIndex(i).objectReferenceValue=colliders[i];
        }
        public static void RefreshAndBuildBundle()
        {
            int count=0;
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/EcoMinecarts/Prefabs"}))
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);
                if(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Vehicle>()==null)continue;
                var root=PrefabUtility.LoadPrefabContents(path);
                try { Configure(root);PrefabUtility.SaveAsPrefabAsset(root,path);count++; }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            MinecartAssetBuilder.BuildAuthoredClientBundle();
            Debug.Log("ECO_VEHICLE_PHYSICS_REFRESH_OK: "+count+" vehicles; native self-collision exclusions and server-only transform synchronization.");
        }
    }
}
