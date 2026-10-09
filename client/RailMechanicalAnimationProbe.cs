using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class RailMechanicalAnimationProbe
    {
        static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
        public static void Verify(GameObject[] prefabs)
        {
            var directory=Environment.GetEnvironmentVariable("ECO_ANIMATION_PREVIEW_DIR")??Path.GetFullPath("../../validation/mechanical-0.2.47/previews");Directory.CreateDirectory(directory);
            foreach(var key in RailMechanicalAnimationBuilder.Engines)
            {
                var root=Object.Instantiate(prefabs.Single(p=>p.name==key));
                try
                {
                    root.SetActive(true);
                    var wheels=root.GetComponentsInChildren<Animator>(true).Where(a=>a.name.StartsWith("WheelRoll_")).ToArray();
                    foreach(var a in wheels){a.enabled=true;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.Rebind();}
                    foreach(float phase in new[]{0f,.13f,.37f,.62f,.87f})
                    {
                        foreach(var a in wheels){a.Play("RollForward",0,phase);a.Update(0);}
                        foreach(string side in new[]{"L","R"})
                        {
                            var host=root.transform.Find("WheelRoll_F"+side);var gear=host.Find("MechanicalLinkage");
                            Check(gear!=null,"Missing mechanism "+key+side);
                            var front=host.Find("Spin/Animated crank pin");var rear=root.transform.Find("WheelRoll_R"+side+"/Spin/Animated crank pin");
                            Check(front!=null&&rear!=null,"Missing quartered crank pins "+key+side);
                            var coupling=gear.Find("CouplingRod");var rod=gear.Find("DriveRod");var cross=gear.Find("Crosshead");
                            Check(Vector3.Distance(coupling.TransformPoint(Vector3.forward*.5f),front.position)<.003f,"Coupling rod misses front crank "+key+side);
                            Check(Vector3.Distance(coupling.TransformPoint(Vector3.back*.5f),rear.position)<.003f,"Coupling rod misses rear crank "+key+side);
                            Check(Vector3.Distance(rod.TransformPoint(Vector3.back*.5f),rear.position)<.003f,"Drive rod misses crank "+key+side);
                            Check(Vector3.Distance(rod.TransformPoint(Vector3.forward*.5f),cross.position)<.003f,"Drive rod misses crosshead "+key+side);
                        }
                    }
                    Render(root,Path.Combine(directory,key+"-mechanism.png"),false);
                }
                finally{Object.DestroyImmediate(root);}
            }
            foreach(var key in RailDumpAnimationBuilder.Buckets)
            {
                var root=Object.Instantiate(prefabs.Single(p=>p.name==key));
                try
                {
                    root.SetActive(true);var hinge=root.transform.Find("DumpAnimation/DumpHinge");var animator=hinge.parent.GetComponent<Animator>();animator.Rebind();
                    var fixedWheels=root.GetComponentsInChildren<WheelCollider>(true).Select(w=>(w.transform,w.transform.position)).ToArray();
                    var world=root.GetComponent<WorldObject>();int slot=Array.IndexOf(world.StringStates,"RailDumpPose");
                    Check(slot>=0&&world.OnStringStateChanged[slot].GetPersistentTarget(0)==animator&&world.OnStringStateChanged[slot].GetPersistentMethodName(0)=="SetTrigger","Dump state not wired to native Animator "+key);
                    world.OnStringStateChanged[slot].SetPersistentListenerState(0,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                    foreach(var pose in new[]{"DumpRight00","DumpRight18","DumpRight28","DumpLeft28","DumpLeft00","DumpRight00"})
                    {
                        world.OnStringStateChanged[slot].Invoke(pose);animator.Update(0);animator.Update(.03f);animator.Update(.12f);
                        var angle=pose.EndsWith("28")?70:pose.EndsWith("18")?45:0;
                        var expected=Quaternion.Euler(0,0,pose.Contains("Right")?-angle:angle);
                        Check(Quaternion.Angle(hinge.localRotation,expected)<.2f,"Native dump event reached wrong angle "+key+pose+" actual="+hinge.localEulerAngles);
                        foreach(var wheel in fixedWheels)Check(Vector3.Distance(wheel.Item1.position,wheel.Item2)<.0001f,"Dumping moved a wheel "+key);
                        if(pose=="DumpRight28"||pose=="DumpRight00")Render(root,Path.Combine(directory,key+"-"+pose+".png"),true);
                    }
                }
                finally{Object.DestroyImmediate(root);}
            }
            Debug.Log("RAIL_MECHANICAL_ANIMATION_OK: four steam-engine rigs; eight quartered slider-cranks at five wheel phases; five bucket hinges, both dumping sides, native replicated event transitions and fixed wheel/chassis transforms.");
        }
        static void Render(GameObject root,string path,bool dump)
        {
            var preview=new GameObject("Rail animation preview");var camera=preview.AddComponent<Camera>();
            var light=new GameObject("Preview light").AddComponent<Light>();light.transform.SetParent(preview.transform);light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(35,-40,0);
            var target=new RenderTexture(960,640,24);var image=new Texture2D(960,640,TextureFormat.RGB24,false);
            var old=RenderTexture.active;var ambient=RenderSettings.ambientLight;
            var ambientMode=RenderSettings.ambientMode;var radius=Shader.GetGlobalFloat("_WorldRadius");var center=Shader.GetGlobalVector("_WorldCenter");var axes=Shader.GetGlobalVector("_CurveAxisMask");
            try
            {
                root.transform.position=Vector3.zero;foreach(var node in root.GetComponentsInChildren<Transform>(true))node.gameObject.layer=31;
                camera.cullingMask=1<<31;light.cullingMask=1<<31;
                Shader.SetGlobalFloat("_WorldRadius",10000);Shader.SetGlobalVector("_WorldCenter",new Vector4(0,-10000,0,0));Shader.SetGlobalVector("_CurveAxisMask",Vector4.zero);
                RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
                var bounds=new Bounds(root.transform.position+Vector3.up*.6f,Vector3.one);
                foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.GetComponent<MeshFilter>()?.sharedMesh!=null&&r.bounds.size.sqrMagnitude>.000001f))bounds.Encapsulate(renderer.bounds);
                var size=Mathf.Max(bounds.size.z,bounds.size.y*1.5f);camera.orthographic=true;camera.orthographicSize=size*.54f;
                camera.transform.position=bounds.center+new Vector3(-1,dump?.9f:0,dump?-.75f:-.20f).normalized*size*3;camera.transform.LookAt(bounds.center);
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.25f,.30f,.34f);camera.targetTexture=target;
                RenderSettings.ambientLight=Color.white*.7f;bool keyword=Shader.IsKeywordEnabled("NO_CURVE");Shader.EnableKeyword("NO_CURVE");
                try{camera.Render();}finally{if(!keyword)Shader.DisableKeyword("NO_CURVE");}
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();
                Check(image.GetPixels32().Select(p=>(p.r,p.g,p.b)).Distinct().Count()>100,"Empty model render: "+path);
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally{RenderSettings.ambientMode=ambientMode;RenderSettings.ambientLight=ambient;Shader.SetGlobalFloat("_WorldRadius",radius);Shader.SetGlobalVector("_WorldCenter",center);Shader.SetGlobalVector("_CurveAxisMask",axes);RenderTexture.active=old;camera.targetTexture=null;target.Release();Object.DestroyImmediate(image);Object.DestroyImmediate(target);Object.DestroyImmediate(preview);}
        }
    }
}
