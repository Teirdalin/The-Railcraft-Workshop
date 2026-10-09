using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class OriginalCoasterRestraintBuilder
    {
        const string Source="Assets/EcoMinecarts/LegacyVehicleSources/RollerCoasterCartObject.prefab";
        public const string AssemblyPrefix="LegacyDesignGeometry_VisibleRestraint_";
        public static void PrepareSource()
        {
            var root=PrefabUtility.LoadPrefabContents(Source);
            try
            {
                var materials=new Dictionary<string,Material>();
                foreach(var key in new[]{"MAT_IronBare","MAT_WoodRail"})
                    materials[key]=AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/"+key+".mat");
                RailVehicleDetail.Apply(root,materials);
                PrefabUtility.SaveAsPrefabAsset(root,Source);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        public static void Apply(GameObject root)
        {
            if(root.name!="RollerCoasterCartObject")return;
            var old=root.transform.Find("LegacyDesignGeometry/VehicleDetail");
            if(old==null)throw new Exception("Original coaster detail branch missing");
            Object.DestroyImmediate(old.gameObject);
            foreach(var previous in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith(AssemblyPrefix)).ToArray())
                Object.DestroyImmediate(previous.gameObject);
            var original=AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            var detail=Object.Instantiate(original.transform.Find("VehicleDetail").gameObject,root.transform.Find("LegacyDesignGeometry"),false).transform;
            detail.name="VehicleDetail";
            foreach(var side in new[]{"Left","Right"})
            {
                var assembly=detail.Find(side+" restraint assembly");
                var pivot=root.transform.Find("PreparedVehicleArt/RestraintAnimation/"+side+"Pivot");
                if(assembly==null||pivot==null)throw new Exception("Missing original restraint assembly or shared hinge "+side);
                // The replicated controller already blends -30 (secured) to
                // -105 (boarding). Preserve the closed artwork while putting
                // its rotation origin at the actual fixed seat hinge.
                pivot.position=assembly.position;
                pivot.rotation=root.transform.rotation*Quaternion.Euler(-30,0,0);
                assembly.SetParent(pivot,true);assembly.name=AssemblyPrefix+side;
                for(var node=assembly;node!=null&&node!=root.transform;node=node.parent)node.gameObject.SetActive(true);
            }
            // The original hinge is lower than the deferred model's hinge.
            // Thirty degrees raises this arm almost upright and clears entry
            // without swinging the padded bar through the fixed seat back.
            var host=root.transform.Find("PreparedVehicleArt/RestraintAnimation").GetComponent<Animator>();
            var boarding=host.runtimeAnimatorController.animationClips.Single(c=>c.name.EndsWith("RailRestraintPoseBoarding"));
            foreach(var side in new[]{"Left","Right"})
                boarding.SetCurve(side+"Pivot",typeof(Transform),"localEulerAnglesRaw.x",AnimationCurve.Constant(0,1,-60));
            EditorUtility.SetDirty(boarding);
            var controller=(AnimatorController)host.runtimeAnimatorController;
            foreach(var transition in controller.layers.SelectMany(l=>l.stateMachine.anyStateTransitions))
                transition.orderedInterruption=false;
            EditorUtility.SetDirty(controller);
            Debug.Log("ORIGINAL_COASTER_RESTRAINTS_AUTHORED_OK: complete visible swing arms, crossbars and padding attached to native boarding/secured hinges");
        }
    }
}
