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
            AttachGuidance(root);
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
        static void AttachGuidance(GameObject root)
        {
            var world=root.GetComponent<WorldObject>();
            var index=Array.IndexOf(world.States,"RailGuidedPhysics");
            if(index<0)
            {
                var states=world.States;var changed=world.OnStateChangedEvents;
                var enabled=world.OnStateEnabledEvents;var disabled=world.OnStateDisabledEvents;
                index=states.Length;
                Array.Resize(ref states,index+1);Array.Resize(ref changed,index+1);
                Array.Resize(ref enabled,index+1);Array.Resize(ref disabled,index+1);
                states[index]="RailGuidedPhysics";changed[index]=new ChangedStateEvent();
                enabled[index]=new SetStateEvent();disabled[index]=new SetStateEvent();
                world.States=states;world.OnStateChangedEvents=changed;
                world.OnStateEnabledEvents=enabled;world.OnStateDisabledEvents=disabled;
            }
            var body=root.GetComponent<Rigidbody>();
            var setter=(UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),body,typeof(Rigidbody).GetProperty("isKinematic").GetSetMethod());
            world.OnStateChangedEvents[index]=new ChangedStateEvent();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[index],setter);
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
                try { Configure(root);VerifyGuidance(root);PrefabUtility.SaveAsPrefabAsset(root,path);count++; }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            MinecartAssetBuilder.BuildAuthoredClientBundle();
            Debug.Log("ECO_VEHICLE_PHYSICS_REFRESH_OK: "+count+" vehicles; native self-collision exclusions and server-only transform synchronization.");
        }
        public static void VerifyGuidance(GameObject root)
        {
            var world=root.GetComponent<WorldObject>();var index=Array.IndexOf(world.States,"RailGuidedPhysics");
            if(index<0)throw new Exception("Missing guided physics binding: "+root.name);
            var change=world.OnStateChangedEvents[index];var body=root.GetComponent<Rigidbody>();
            if(change.GetPersistentEventCount()!=1 || change.GetPersistentTarget(0)!=body
                || change.GetPersistentMethodName(0)!="set_isKinematic")throw new Exception("Invalid guided physics callback: "+root.name);
        }
        public static void BuildCornerConsistPatch()
        {
            var spec=RailExpansionAssetBuilder.ReadCatalog().Vehicles.Single(s=>s.HumanPowered);
            var path="Assets/EcoMinecarts/Prefabs/"+spec.Key+"Object.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try { VehicleRailFitPatch.ConfigureHandcar(root,spec);PrefabUtility.SaveAsPrefabAsset(root,path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            RefreshAndBuildBundle();
            Debug.Log("CORNER_CONSIST_ASSETS_OK: native Rigidbody guided ownership binding on all vehicles; handcar maximum 8 m/s.");
        }
    }
}
