using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    // Native replicated states, lights and emission; no custom behaviours.
    public static class TrainLampPresentation
    {
        public static void Configure(GameObject root)
        {
            if(!RailMechanicalAnimationBuilder.Engines.Contains(root.name))return;
            var old=root.transform.Find("Train automatic lamps");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var deck=root.transform.Find("CabFittings/Cab floor");float floor=deck.localPosition.y+.035f,rear=deck.localPosition.z-deck.localScale.z/2;
            var body=root.transform.Find("PreparedVehicleArt");
            var source=(body??root.transform).GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name=="Render_LOD0"||r.name=="Steam boiler").ToArray();
            float frontHeight=root.name=="MineTrainObject"?1.28f:1.43f;
            float nose=source.Length>0?source.Max(r=>root.transform.InverseTransformPoint(r.bounds.max).z):deck.localPosition.z+deck.localScale.z/2+.5f;
            // Locate the smokebox surface at the lamp's height. The overall
            // body bounds also include protruding buffers below the boiler.
            var face=(body??root.transform).GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="Render_LOD0"||f.name=="Steam boiler")
                .SelectMany(f=>f.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(f.transform.TransformPoint(v))))
                .Where(p=>Mathf.Abs(p.x)<.3f&&Mathf.Abs(p.y-(floor+frontHeight))<.12f).ToArray();
            if(face.Length>0)nose=face.Max(p=>p.z);
            var host=new GameObject("Train automatic lamps").transform;host.SetParent(root.transform,false);
            const string path="Assets/EcoMinecarts/Materials/MAT_TrainLitLens.mat";
            var lit=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(lit==null){lit=new Material(Shader.Find(RailWorldMaterialBuilder.SurfaceShader));AssetDatabase.CreateAsset(lit,path);}
            var warm=new Color(1,.82f,.52f);lit.color=warm;lit.SetColor("_EmissionColor",warm*2);lit.EnableKeyword("_EMISSION");EditorUtility.SetDirty(lit);
            var casing=AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronBare.mat");
            var world=root.GetComponent<WorldObject>();var index=RailWheelAnimationBuilder.BoolIndex(world,"RailAutomaticLights");
            world.OnStateChangedEvents[index]=new ChangedStateEvent();world.OnStateEnabledEvents[index]=new SetStateEvent();world.OnStateDisabledEvents[index]=new SetStateEvent();
            UnityEventTools.AddStringPersistentListener(world.OnStateChangedEvents[index],lit.EnableKeyword,"_EMISSION");
            foreach(var end in new[]{1,-1})
            {
                var lamp=new GameObject(end>0?"Front headlamp":"Rear headlamp").transform;lamp.SetParent(host,false);
                lamp.localPosition=new Vector3(0,floor+(end>0?frontHeight:.60f),end>0?nose+.04f:rear-.07f);lamp.localRotation=Quaternion.LookRotation(Vector3.forward*end);
                var light=lamp.gameObject.AddComponent<Light>();light.type=LightType.Spot;light.range=24;light.spotAngle=71.5f;light.intensity=1.2f;light.color=warm;light.shadows=LightShadows.None;light.enabled=false;lamp.gameObject.AddComponent<EcoLight>();
                var shell=GameObject.CreatePrimitive(PrimitiveType.Cylinder);shell.name="Headlamp casing";shell.transform.SetParent(lamp,false);shell.transform.localPosition=Vector3.back*.016f;shell.transform.localRotation=Quaternion.Euler(90,0,0);shell.transform.localScale=new Vector3(.16f,.012f,.16f);Object.DestroyImmediate(shell.GetComponent<Collider>());shell.GetComponent<MeshRenderer>().sharedMaterial=casing;
                var lens=GameObject.CreatePrimitive(PrimitiveType.Cylinder);lens.name="Luminous headlamp lens";lens.transform.SetParent(lamp,false);lens.transform.localPosition=Vector3.back*.003f;lens.transform.localRotation=Quaternion.Euler(90,0,0);lens.transform.localScale=new Vector3(.14f,.001f,.14f);Object.DestroyImmediate(lens.GetComponent<Collider>());var glow=lens.GetComponent<MeshRenderer>();glow.sharedMaterial=lit;glow.enabled=false;
                UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[index],(UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),light,typeof(Behaviour).GetProperty("enabled").GetSetMethod()));
                UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[index],(UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),glow,typeof(Renderer).GetProperty("enabled").GetSetMethod()));
            }
            root.GetComponent<RCCCarControllerV2>().headLights=Array.Empty<Light>();
        }
    }
}
