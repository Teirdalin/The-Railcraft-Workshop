using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class TramLampPresentation
    {
        public static void Configure(GameObject root)
        {
            var old=root.transform.Find("Tram night lamps");if(old!=null)Object.DestroyImmediate(old.gameObject);
            foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Lamp support")&&!AnimationUtility.CalculateTransformPath(t,root.transform).Contains("LegacyDesignGeometry")).ToArray())Object.DestroyImmediate(t.gameObject);
            var lenses=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name.StartsWith("Lamp glass")&&!AnimationUtility.CalculateTransformPath(r.transform,root.transform).Contains("LegacyDesignGeometry")).ToArray();
            var canopy=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Canopy roof"&&!AnimationUtility.CalculateTransformPath(t,root.transform).Contains("LegacyDesignGeometry"));
            if(canopy==null)canopy=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/LegacyVehicleSources/HeritageTramObject.prefab").GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Canopy roof");
            float length=canopy.localScale.z;
            // The original detail meshes were baked by material. Use their
            // authored lamp-face datum when no separate lens transform remains.
            var centres=lenses.Length==2?lenses.Select(r=>r.bounds.center).ToArray():new[]{-1,1}.Select(end=>root.transform.TransformPoint(new Vector3(0,1.38f,end*(length/2+.063f)))).ToArray();
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("Body Detail")&&!AnimationUtility.CalculateTransformPath(f.transform,root.transform).Contains("LegacyDesignGeometry"))){
                var source=filter.sharedMesh;if(source==null)continue;var vertices=source.vertices;var triangles=source.triangles;
                bool Support(int i){var p=root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));return Mathf.Abs(p.x)<.024f&&p.y>1.22f&&p.y<1.40f&&Mathf.Abs(Mathf.Abs(p.z)-(length/2+.006f))<.024f;}
                var kept=Enumerable.Range(0,triangles.Length/3).Where(i=>!(Support(triangles[i*3])&&Support(triangles[i*3+1])&&Support(triangles[i*3+2]))).SelectMany(i=>new[]{triangles[i*3],triangles[i*3+1],triangles[i*3+2]}).ToArray();
                if(kept.Length==triangles.Length)continue;
                var meshPath="Assets/EcoMinecarts/Animations/Designs/TramLampBody-"+source.name+".asset";var trimmed=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if(trimmed==null){trimmed=Object.Instantiate(source);AssetDatabase.CreateAsset(trimmed,meshPath);}else EditorUtility.CopySerialized(source,trimmed);
                trimmed.triangles=kept;trimmed.RecalculateBounds();EditorUtility.SetDirty(trimmed);filter.sharedMesh=trimmed;
            }
            var path="Assets/EcoMinecarts/Materials/MAT_TramLitLens.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find(RailWorldMaterialBuilder.SurfaceShader));AssetDatabase.CreateAsset(material,path);}
            var warm=new Color(1,.7970588f,.4926471f,1);material.color=warm;material.SetColor("_EmissionColor",warm*1.5f);material.EnableKeyword("_EMISSION");material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive;EditorUtility.SetDirty(material);
            var host=new GameObject("Tram night lamps").transform;host.SetParent(root.transform,false);
            var world=root.GetComponent<WorldObject>();int index=Array.IndexOf(world.States,"TramNightLights");
            if(index<0){
                var states=world.States;var changed=world.OnStateChangedEvents;var on=world.OnStateEnabledEvents;var off=world.OnStateDisabledEvents;index=states.Length;
                Array.Resize(ref states,index+1);Array.Resize(ref changed,index+1);Array.Resize(ref on,index+1);Array.Resize(ref off,index+1);states[index]="TramNightLights";
                world.States=states;world.OnStateChangedEvents=changed;world.OnStateEnabledEvents=on;world.OnStateDisabledEvents=off;
            }
            world.OnStateChangedEvents[index]=new ChangedStateEvent();world.OnStateEnabledEvents[index]=new SetStateEvent();world.OnStateDisabledEvents[index]=new SetStateEvent();
            // Eco's MaterialEvents documents Unity's emission-keyword reset.
            // Refresh it through a native callback whenever the night state arrives.
            UnityEventTools.AddStringPersistentListener(world.OnStateChangedEvents[index],material.EnableKeyword,"_EMISSION");
            foreach(var centre in centres){
                int end=root.transform.InverseTransformPoint(centre).z>0?1:-1;
                var lamp=new GameObject(end>0?"Front warm headlamp":"Rear warm headlamp").transform;lamp.SetParent(host,false);
                lamp.position=centre+root.transform.forward*(end*.025f);lamp.rotation=root.transform.rotation*Quaternion.LookRotation(Vector3.forward*end);
                // Match the installed SteamTruck's face-mounted spotlight settings.
                var light=lamp.gameObject.AddComponent<Light>();light.type=LightType.Spot;light.color=warm;light.range=20;light.spotAngle=71.5f;light.intensity=.9f;light.shadows=LightShadows.None;light.enabled=false;lamp.gameObject.AddComponent<EcoLight>();
                var glow=GameObject.CreatePrimitive(PrimitiveType.Cylinder);glow.name="Tram luminous lens";glow.transform.SetParent(lamp,false);
                glow.transform.localPosition=Vector3.back*.021f;glow.transform.localRotation=Quaternion.Euler(90,0,0);glow.transform.localScale=new Vector3(.146f,.001f,.146f);
                Object.DestroyImmediate(glow.GetComponent<Collider>());var renderer=glow.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.enabled=false;
                var setLight=(UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),light,typeof(Behaviour).GetProperty("enabled").GetSetMethod());
                var setGlow=(UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),renderer,typeof(Renderer).GetProperty("enabled").GetSetMethod());
                UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[index],setLight);UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[index],setGlow);
            }
            root.GetComponent<RCCCarControllerV2>().headLights=Array.Empty<Light>();
        }
    }
}
