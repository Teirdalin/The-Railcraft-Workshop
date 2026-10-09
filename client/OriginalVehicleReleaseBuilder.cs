using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    // Keep the saved prefab identities and native mechanism rig. Deferred
    // vehicle art remains in the authoring project, outside scene dependencies.
    public static class OriginalVehicleReleaseBuilder
    {
        public const string Marker="Original vehicle release";
        const string Root="Assets/EcoMinecarts";
        public static void AuditDeferredReferences()
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/FreightLocomotiveObject.prefab");
            foreach(var p in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(asset),true).Where(p=>p.Contains("VehicleLibrary")||p.Contains("VehicleCandidates")))Debug.Log("DEFERRED_DEPENDENCY: "+p);
            foreach(var r in asset.GetComponentsInChildren<Renderer>(true))
            foreach(var material in r.sharedMaterials.Where(m=>m!=null))
            foreach(var property in material.GetTexturePropertyNames())
            {
                var texture=material.GetTexture(property);var p=AssetDatabase.GetAssetPath(texture);
                if(p.Contains("VehicleLibrary")||p.Contains("VehicleCandidates"))Debug.Log("DEFERRED_RENDERER_TEXTURE: "+AnimationUtility.CalculateTransformPath(r.transform,asset.transform)+"; "+material.name+"; "+property+"; "+p);
            }
        }
        public static bool IsOriginalRelease(GameObject[] prefabs)=>prefabs.Where(p=>p.GetComponent<Vehicle>()!=null).All(p=>p.transform.Find(Marker)!=null);
        public static void Apply()
        {
            OriginalCoasterRestraintBuilder.PrepareSource();
            int count=0,removed=0;
            var paths=AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"}).Select(g=>AssetDatabase.GUIDToAssetPath(g))
                .Where(p=>AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponent<Vehicle>()!=null).ToArray();
            foreach(var path in paths)
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var world=root.GetComponent<WorldObject>();int flag=Array.IndexOf(world.States,"RailLegacyDesign");
                    if(flag<0)throw new Exception("Missing archived original design "+root.name);
                    var legacy=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animations/Designs/"+root.name+"LegacyDesign.anim");
                    if(legacy==null)throw new Exception("Missing original native fits "+root.name);
                    legacy.SampleAnimation(root,0);
                    var modern=world.OnStateDisabledEvents[flag];
                    var targets=Enumerable.Range(0,modern.GetPersistentEventCount()).Select(i=>modern.GetPersistentTarget(i)).OfType<GameObject>().Distinct().ToArray();
                    foreach(var target in targets)
                    {
                        var renderer=target.GetComponent<MeshRenderer>();if(renderer==null)continue;
                        bool originalRenderer=false;
                        for(var t=renderer.transform;t!=null&&t!=root.transform;t=t.parent)
                            if(t.name.StartsWith("LegacyDesignGeometry"))originalRenderer=true;
                        if(originalRenderer)continue;
                        var filter=target.GetComponent<MeshFilter>();if(filter!=null)Object.DestroyImmediate(filter);
                        Object.DestroyImmediate(renderer);removed++;
                    }
                    foreach(var group in root.GetComponentsInChildren<LODGroup>(true))Object.DestroyImmediate(group);
                    // A stale NewDesign=true setting must not hide the originals.
                    // Both state messages now use the same original native fit.
                    world.OnStateEnabledEvents[flag]=new SetStateEvent();
                    world.OnStateDisabledEvents[flag]=new SetStateEvent();
                    var animator=root.GetComponent<Animator>();var controller=(AnimatorController)animator.runtimeAnimatorController;
                    foreach(var layer in controller.layers)
                    foreach(var state in layer.stateMachine.states)
                        if(state.state.name=="ModernDesign"||state.state.name=="LegacyDesign")state.state.motion=legacy;
                    EditorUtility.SetDirty(controller);
                    foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        bool original=false;
                        for(var t=renderer.transform;t!=null&&t!=root.transform;t=t.parent)
                            if(t.name.StartsWith("LegacyDesignGeometry"))original=true;
                        if(!original)continue;
                        for(var t=renderer.transform;t!=null&&t!=root.transform;t=t.parent)t.gameObject.SetActive(true);
                    }
                    OriginalCoasterRestraintBuilder.Apply(root);
                    // Shared native controls use a few flat-colour materials
                    // authored alongside the deferred models. Preserve those
                    // finishes without retaining their source-library dependency.
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var materials=renderer.sharedMaterials;
                        for(int i=0;i<materials.Length;i++)
                        {
                            var material=materials[i];if(material==null)continue;
                            var source=AssetDatabase.GetAssetPath(material);
                            if(!source.StartsWith(Root+"/VehicleCandidates/")&&!source.StartsWith(Root+"/VehicleLibrary/"))continue;
                            if(material.GetTexturePropertyNames().Any(n=>AssetDatabase.GetAssetPath(material.GetTexture(n)).StartsWith(Root+"/VehicleLibrary/")))
                                throw new Exception("Original shared material retains deferred texture "+material.name);
                            string folder=Root+"/Materials/OriginalRelease";System.IO.Directory.CreateDirectory(folder);AssetDatabase.Refresh();
                            string destination=folder+"/"+material.name+"-"+AssetDatabase.AssetPathToGUID(source).Substring(0,8)+".mat";
                            var copy=AssetDatabase.LoadAssetAtPath<Material>(destination);
                            if(copy==null){copy=new Material(material);copy.name=material.name;AssetDatabase.CreateAsset(copy,destination);}
                            materials[i]=copy;
                        }
                        renderer.sharedMaterials=materials;
                    }
                    if(root.transform.Find(Marker)==null)new GameObject(Marker).transform.SetParent(root.transform,false);
                    PrefabUtility.SaveAsPrefabAsset(root,path);count++;
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
                var dependencies=AssetDatabase.GetDependencies(path,true);
                if(dependencies.Any(p=>p.StartsWith(Root+"/VehicleLibrary/")||p.StartsWith(Root+"/VehicleCandidates/")))
                    throw new Exception("Deferred vehicle art still referenced by "+path);
            }
            if(count!=14)throw new Exception("Original release requires fourteen vehicles");
            AssetDatabase.SaveAssets();
            var prefabs=paths.Select(p=>AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToArray();
            RailVehiclePaintBuilder.Apply(prefabs);
            foreach(var prefab in prefabs)MinecartIconBuilder.Render(prefab,prefab.name.Substring(0,prefab.name.Length-6));
            AssetDatabase.SaveAssets();
            Debug.Log("ORIGINAL_VEHICLES_AUTHORED_OK: "+count+" original-only vehicles; "+removed+" deferred mesh renderers removed; native physics/mechanism components retained");
        }
    }
}
