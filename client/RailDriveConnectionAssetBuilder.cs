using System;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Rendering;

namespace EcoMinecarts.Editor
{
    // Native state events only: no per-frame custom client component.
    public static class RailDriveConnectionAssetBuilder
    {
        const string Root = "Assets/EcoMinecarts";
        public static void BuildAndExport()
        {
            var texturePath = Root + "/Materials/DriveStalledSmoke.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                texture = new Texture2D(32,32,TextureFormat.RGBA32,false) { name="DriveStalledSmoke",wrapMode=TextureWrapMode.Clamp };
                for(int y=0;y<32;y++) for(int x=0;x<32;x++)
                {
                    var r=new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;
                    texture.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-r),2)));
                }
                texture.Apply(); AssetDatabase.CreateAsset(texture,texturePath);
            }
            var materialPath=Root+"/Materials/MAT_DriveStalledSmoke.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null)
            {
                material=new Material(Shader.Find(RailWorldMaterialBuilder.ParticleShader));
                AssetDatabase.CreateAsset(material,materialPath);
            }
            RailWorldMaterialBuilder.Particle(material,false); material.mainTexture=texture;
            foreach(var name in new[]{"MinecartChainDriveObject","TramCableDriveObject"})
            {
                var path=Root+"/Prefabs/"+name+".prefab";
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var previous=root.transform.Find("StalledSmoke");
                    if(previous!=null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
                    var node=new GameObject("StalledSmoke"); node.transform.SetParent(root.transform,false);
                    node.transform.localPosition=name=="TramCableDriveObject"?new Vector3(.26f,.59f,.15f):new Vector3(.25f,.45f,0);
                    var particles=node.AddComponent<ParticleSystem>();
                    var main=particles.main; main.loop=true;main.playOnAwake=true;main.maxParticles=12;
                    main.startLifetime=2;main.startSpeed=.18f;main.startSize=new ParticleSystem.MinMaxCurve(.12f,.24f);
                    main.startColor=new Color(.23f,.23f,.23f,.6f);main.gravityModifier=-.025f;
                    main.simulationSpace=ParticleSystemSimulationSpace.Local;
                    main.cullingMode=ParticleSystemCullingMode.Automatic;
                    var emission=particles.emission;emission.rateOverTime=2;
                    var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=12;shape.radius=.03f;
                    node.transform.localRotation=Quaternion.Euler(-90,0,0);
                    var color=particles.colorOverLifetime;color.enabled=true;
                    var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                        new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(0,1)});
                    color.color=gradient;
                    var renderer=node.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
                    renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                    node.SetActive(false);
                    var world=root.GetComponent<WorldObject>();var index=Array.IndexOf(world.States,"DriveStalled");
                    if(index<0)
                    {
                        index=world.States.Length;
                        var states=world.States;var changed=world.OnStateChangedEvents;var enabled=world.OnStateEnabledEvents;var disabled=world.OnStateDisabledEvents;
                        Array.Resize(ref states,index+1);Array.Resize(ref changed,index+1);Array.Resize(ref enabled,index+1);Array.Resize(ref disabled,index+1);
                        states[index]="DriveStalled";changed[index]=new ChangedStateEvent();enabled[index]=new SetStateEvent();disabled[index]=new SetStateEvent();
                        world.States=states;world.OnStateChangedEvents=changed;world.OnStateEnabledEvents=enabled;world.OnStateDisabledEvents=disabled;
                    }
                    world.OnStateChangedEvents[index]=new ChangedStateEvent();
                    UnityEventTools.AddPersistentListener(world.OnStateChangedEvents[index],node.SetActive);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                    Debug.Log("RAIL_DRIVE_STALLED_SMOKE_OK: "+name+"; native state event; max 12 particles; curved shader");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();MinecartAssetBuilder.BuildAuthoredClientBundle();
        }
    }
}
