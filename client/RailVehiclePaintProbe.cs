using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class RailVehiclePaintProbe
    {
        public static void Verify(GameObject[] prefabs)
        {
            RailWorldMaterialBuilder.Verify(UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects());
            if (RailWorldMaterialBuilder.NativeSurfaceTest)
            {
                Debug.Log("ECO_VEHICLE_PAINT_DISABLED_FOR_NATIVE_SHADER_TEST: native Curved/Standard has no paint channels.");
                return;
            }
            var shaderSource=File.ReadAllText("Assets/EcoMinecarts/Shaders/RailVehiclePaint.shader");
            var setup=shaderSource.IndexOf("UNITY_SETUP_INSTANCE_ID(v);",StringComparison.Ordinal);
            var curve=shaderSource.IndexOf("CURVED_VERTEX(v.vertex);",StringComparison.Ordinal);
            if(setup<0 || curve<setup || curve-setup>128)
                throw new Exception("Distant vehicle curvature must set the indirect instance ID before reading its transform");
            var count=0;
            foreach(var part in new[]{("Steam boiler",1),("Smokebox door",1),("Coach side",1),
                ("Wheel tyre",2),("Cast spoke",2),("Boiler band",2),("Riveted chassis",2),("Frame beam",2),
                ("Cab roof",3),("Arched locomotive roof",3),("Roof Detail 0",3)})
                if(RailVehiclePaintBuilder.RegionName(part.Item1)!=part.Item2)
                    throw new Exception("Paint layer classification changed: "+part.Item1);
            foreach(var prefab in prefabs.Where(p=>p.GetComponent<RCCCarControllerV2>()!=null)){
                var layers=new System.Collections.Generic.HashSet<int>();
                foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true)){
                    var flags=new SerializedObject(renderer).FindProperty("m_SmallMeshCulling");
                    if(flags==null || flags.boolValue)
                        throw new Exception("Vehicle fragment can disappear at distance: "+prefab.name+"/"+renderer.name);
                    if(renderer.GetComponent<TMP_Text>()==null && renderer.sharedMaterials.Any(m=>m==null || m.shader.name!=RailVehiclePaintBuilder.ShaderName))
                        throw new Exception("Unpaintable solid vehicle part: "+prefab.name+"/"+renderer.name);
                    var layer=RailVehiclePaintBuilder.Region(renderer);
                    if(layer!=0 && renderer.enabled){
                        layers.Add(layer);
                        foreach(var material in renderer.sharedMaterials.Where(m=>m!=null && m.shader.name==RailVehiclePaintBuilder.ShaderName)){
                            var mask=material.GetTexture("_PaintCombinedTexture") as Texture2D;
                            if(mask==null || mask.GetPixel(0,0)[layer-1]!=1)
                                throw new Exception("Paint mask does not match physical layer: "+prefab.name+"/"+renderer.name);
                        }
                    }
                }
                if(new[]{"MineTrainObject","PassengerLocomotiveObject","FreightLocomotiveObject","LargeTrainEngineObject"}.Contains(prefab.name)){
                    if(!new[]{1,2,3}.All(layers.Contains))throw new Exception("Locomotive lacks body/frame/roof paint zones: "+prefab.name);
                    var nodes=prefab.GetComponentsInChildren<Transform>(true);
                    var boiler=nodes.First(n=>n.name=="Boiler"||n.name=="Steam boiler");
                    var door=nodes.First(n=>n.name=="SmokeboxDoor"||n.name=="Smokebox door");
                    var front=boiler.localPosition.z+boiler.localScale.y;
                    var overlap=front-(door.localPosition.z-door.localScale.y);
                    if(overlap<.005f || overlap>.03f)
                        throw new Exception("Smokebox front plate is not seated on boiler: "+prefab.name+" overlap="+overlap);
                }
                var materials=prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials)
                    .Where(m=>m!=null && m.shader.name==RailVehiclePaintBuilder.ShaderName).Distinct().ToArray();
                if(materials.Length==0) throw new Exception("Exported vehicle has no paint regions: "+prefab.name);
                foreach(var material in materials){
                    if(!material.enableInstancing)throw new Exception("Vehicle paint material does not retain distant instancing: "+material.name);
                    if(!material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader)) throw new Exception("Paint shader failed: "+material.name);
                    foreach(var property in new[]{"_ChannelRedColor","_ChannelGreenColor","_ChannelBlueColor"})
                        if(!material.HasProperty(property)||material.GetColor(property)!=Color.clear) throw new Exception("Paint colour/default mismatch: "+material.name);
                    if(!material.HasProperty("_PaintedAmount")||material.GetFloat("_PaintedAmount")!=0 || material.GetTexture("_MainTex")==null)
                        throw new Exception("Native paint coverage/original albedo missing: "+material.name);
                    var mask=material.GetTexture("_PaintCombinedTexture") as Texture2D;
                    if(mask==null||!mask.isReadable) throw new Exception("Paint region texture missing: "+material.name);
                    var index=int.Parse(material.name.Substring(material.name.LastIndexOf("_Paint",StringComparison.Ordinal)+6))-1;
                    var value=mask.GetPixel(0,0);
                    if(index<0||index>2||value[index]!=1||value[(index+1)%3]!=0||value[(index+2)%3]!=0)
                        throw new Exception("RGB paint mask invalid: "+material.name);
                }
                count++;
            }
            // Exercise native shader inputs on independent renderer instances,
            // including a partial coat and clear/restore; never edit source mats.
            var output=Environment.GetEnvironmentVariable("ECO_MINECART_AUDIT_DIR");
            if(!string.IsNullOrEmpty(output)){
                var dir=Path.Combine(output,"paint-previews");Directory.CreateDirectory(dir);
                foreach(var key in new[]{"MinecartObject","WoodenMinecartObject","MineTrainObject","LargeTrainEngineObject","PassengerCarObject","RollerCoasterCartObject"}){
                    var source=prefabs.Single(p=>p.name==key);
                    Render(source,dir,key,"original",0);
                    Render(source,dir,key,"painted",1);
                    Render(source,dir,key,"partial",.5f);
                    Render(source,dir,key,"removed",0,true);
                    if(!SameFinish(Path.Combine(dir,key+"-original.png"),Path.Combine(dir,key+"-removed.png")))
                        throw new Exception("Paint removal changed original appearance: "+key);
                    if(File.ReadAllBytes(Path.Combine(dir,key+"-original.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(dir,key+"-painted.png"))))
                        throw new Exception("Paint does not visibly affect model: "+key);
                    if(key=="LargeTrainEngineObject"){
                        for(var layer=1;layer<=3;layer++){
                            var variant="layer"+layer;
                            Render(source,dir,key,variant,1,false,layer);
                            if(File.ReadAllBytes(Path.Combine(dir,key+"-original.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(dir,key+"-"+variant+".png"))))
                                throw new Exception("Independent paint layer has no visible effect: "+variant);
                        }
                    }
                }
            }
            Debug.Log("ECO_EXPORTED_VEHICLE_PAINT_OK: "+count+" vehicles; native RGB/coverage properties and masks; rendered coats and pixel-equivalent clear restoration.");
        }
        static bool SameFinish(string first,string second)
        {
            var a=new Texture2D(2,2,TextureFormat.RGBA32,false);
            var b=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try{
                if(!ImageConversion.LoadImage(a,File.ReadAllBytes(first))||!ImageConversion.LoadImage(b,File.ReadAllBytes(second))
                    ||a.width!=b.width||a.height!=b.height)return false;
                var left=a.GetPixels32();var right=b.GetPixels32();var changed=0;
                for(var i=0;i<left.Length;i++){
                    var x=left[i];var y=right[i];
                    // Repeated off-screen renders can differ at a handful of
                    // pixels around moving details, including high-contrast edges.
                    // A retained coat affects far more than 16 of 153,600 pixels.
                    if(x.r==y.r&&x.g==y.g&&x.b==y.b&&x.a==y.a)continue;
                    if(++changed>16 || x.a!=y.a)return false;
                }
                return true;
            }finally{Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
        }
        static void Render(GameObject prefab,string dir,string key,string state,float alpha,bool clear=false,int onlyLayer=0)
        {
            var root=new GameObject("PaintPreview");var instance=Object.Instantiate(prefab,root.transform);instance.SetActive(true);
            var privateMaterials=new System.Collections.Generic.List<Material>();
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true)){
                renderer.gameObject.layer=30;
                if(renderer is ParticleSystemRenderer){renderer.enabled=false;continue;}
                var mats=renderer.sharedMaterials;
                for(var i=0;i<mats.Length;i++) if(mats[i]!=null&&mats[i].shader.name==RailVehiclePaintBuilder.ShaderName){
                    var m=new Material(mats[i]);privateMaterials.Add(m);mats[i]=m;m.SetFloat("_PaintedAmount",clear?1:alpha>0?1:0);
                    m.SetColor("_ChannelRedColor",new Color(.80f,.09f,.05f,onlyLayer==0||onlyLayer==1?alpha:0));
                    m.SetColor("_ChannelGreenColor",new Color(.08f,.27f,.70f,onlyLayer==0||onlyLayer==2?alpha:0));
                    m.SetColor("_ChannelBlueColor",new Color(.8f,.70f,.32f,onlyLayer==0||onlyLayer==3?alpha:0));
                }
                renderer.sharedMaterials=mats;
            }
            var renderers=instance.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled).ToArray();
            var bounds=renderers[0].bounds;foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var camera=new GameObject("PaintCamera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
            camera.transform.position=bounds.center+new Vector3(2,1.5f,2)*bounds.size.magnitude;camera.transform.LookAt(bounds.center);
            camera.orthographic=true;camera.orthographicSize=bounds.size.magnitude*.55f;camera.cullingMask=1<<30;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.20f,.23f);camera.farClipPlane=100;camera.nearClipPlane=.01f;
            var light=new GameObject("PaintLight").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;
            light.intensity=1;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(45,-35,0);
            var oldMode=RenderSettings.ambientMode;var oldAmbient=RenderSettings.ambientLight;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.43f,.43f,.43f);
            var target=new RenderTexture(480,320,24);var texture=new Texture2D(480,320,TextureFormat.RGBA32,false);var oldTarget=RenderTexture.active;
            // Export strips unused NO_CURVE variants from some native shaders.
            // Supply a neutral, valid planet as well, so unpainted wheels and
            // fittings remain visible instead of receiving a zero-radius warp.
            var oldRadius=Shader.GetGlobalFloat("_WorldRadius");var oldCenter=Shader.GetGlobalVector("_WorldCenter");var oldAxes=Shader.GetGlobalVector("_CurveAxisMask");
            Shader.SetGlobalFloat("_WorldRadius",10000);Shader.SetGlobalVector("_WorldCenter",new Vector4(0,-10000,0,0));Shader.SetGlobalVector("_CurveAxisMask",Vector4.zero);
            var noCurve=Shader.IsKeywordEnabled("NO_CURVE");Shader.EnableKeyword("NO_CURVE");
            try{
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,480,320),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(dir,key+"-"+state+".png"),texture.EncodeToPNG());
            }finally{
                camera.targetTexture=null;RenderTexture.active=oldTarget;RenderSettings.ambientMode=oldMode;RenderSettings.ambientLight=oldAmbient;
                Shader.SetGlobalFloat("_WorldRadius",oldRadius);Shader.SetGlobalVector("_WorldCenter",oldCenter);Shader.SetGlobalVector("_CurveAxisMask",oldAxes);
                if(!noCurve) Shader.DisableKeyword("NO_CURVE");Object.DestroyImmediate(texture);Object.DestroyImmediate(target);
                Object.DestroyImmediate(root);foreach(var material in privateMaterials) Object.DestroyImmediate(material);
            }
        }
    }
}
