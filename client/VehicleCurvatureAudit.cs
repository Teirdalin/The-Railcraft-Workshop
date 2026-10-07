using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    // Runs the exported shader on the GPU with real curvature and Eco's indirect buffer layout.
    [InitializeOnLoad]
    public static class VehicleCurvatureAudit
    {
        const string Pending="EcoMinecarts.VehicleCurvatureAudit";
        static AssetBundle bundle; static AsyncOperation loading;
        [StructLayout(LayoutKind.Sequential)] struct Instance { public Matrix4x4 objectToWorld,worldToObject; public float lodFade; public Vector2 extra; }
        static VehicleCurvatureAudit(){EditorApplication.update+=Update;}
        public static void Run(){SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();}
        public static void RunTextOnly(){SessionState.SetBool(Pending+".TextOnly",true);Run();}
        static void Update(){
            if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying)return;
            try{
                if(bundle==null){bundle=AssetBundle.LoadFromFile(Environment.GetEnvironmentVariable("ECO_MINECART_AUDIT_BUNDLE"));loading=SceneManager.LoadSceneAsync(bundle.GetAllScenePaths().Single());return;}
                if(!loading.isDone)return;
                if(SessionState.GetBool(Pending+".TextOnly",false)) VerifyTextOnly(); else Verify();
                SessionState.SetBool(Pending+".TextOnly",false);SessionState.SetBool(Pending,false);EditorApplication.Exit(0);
            }catch(Exception e){Debug.LogException(e);SessionState.SetBool(Pending,false);EditorApplication.Exit(1);}
        }
        static Vector3 Bent(Vector3 p,float radius,Vector2 axes){
            var d=Vector2.Scale(new Vector2(p.x,p.z),axes).magnitude;
            var angle=d/radius;var f=d>1e-5f?Mathf.Sin(angle)/d:0;
            return new Vector3(Mathf.Lerp(p.x,f*p.x*(p.y+radius),axes.x),Mathf.Cos(angle)*(p.y+radius)-radius,Mathf.Lerp(p.z,f*p.z*(p.y+radius),axes.y));
        }
        static void VerifyTextOnly(){
            var roots=SceneManager.GetActiveScene().GetRootGameObjects();
            var prefabs=roots.Single(r=>r.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs;
            foreach(var root in roots)root.SetActive(false);
            Shader.DisableKeyword("NO_CURVE");Shader.DisableKeyword("MINIMAP_NO_CURVE");
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white;
            var light=new GameObject("Text curvature light").AddComponent<Light>();light.type=LightType.Directional;
            light.intensity=1;light.transform.rotation=Quaternion.Euler(40,-30,0);RenderSettings.sun=light;
            var camera=new GameObject("Text curvature camera").AddComponent<Camera>();camera.enabled=false;
            camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var output=Environment.GetEnvironmentVariable("ECO_MODEL_AUDIT_DIR");Directory.CreateDirectory(output);
            var lines=new System.Collections.Generic.List<string>();
            VerifySpecialGraphics(prefabs,camera,output,lines,true);
            File.WriteAllLines(output+"/text-curvature.txt",lines);
            Debug.Log("NATIVE_TEXT_CURVATURE_GPU_OK: exported lettering at near and distant curved positions.");
        }
        static void Verify(){
            var roots=SceneManager.GetActiveScene().GetRootGameObjects();
            var prefabs=roots.Single(r=>r.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs;
            ValidateMaterials(prefabs);
            var tram=prefabs.Single(p=>p.name=="HeritageTramObject");
            var source=tram.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).First(m=>m.shader.name==(RailWorldMaterialBuilder.NativeSurfaceTest ? RailWorldMaterialBuilder.SurfaceShader : RailVehiclePaintBuilder.ShaderName));
            var native=prefabs.SelectMany(r=>r.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).First(m=>m!=null&&m.shader.name=="Curved/Standard");
            foreach(var r in roots)r.SetActive(false);
            Shader.DisableKeyword("NO_CURVE");Shader.DisableKeyword("MINIMAP_NO_CURVE");
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.white;
            var light=new GameObject("CurvedGpuLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(40,-30,0);RenderSettings.sun=light;
            var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);var mesh=primitive.GetComponent<MeshFilter>().sharedMesh;Object.DestroyImmediate(primitive);
            var cam=new GameObject("CurvedGpuCamera").AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.orthographicSize=3;cam.nearClipPlane=.1f;cam.farClipPlane=100;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;
            var output=Environment.GetEnvironmentVariable("ECO_MODEL_AUDIT_DIR");Directory.CreateDirectory(output);
            var lines=new System.Collections.Generic.List<string>();
            foreach(var axes in new[]{Vector2.one,new Vector2(1,0),new Vector2(0,1),Vector2.zero})
            foreach(var distance in new[]{0f,30f,60f,120f}){
                const float radius=300;
                Shader.SetGlobalFloat("_WorldRadius",radius);Shader.SetGlobalVector("_WorldCenter",new Vector4(0,-radius,0,0));Shader.SetGlobalVector("_CurveAxisMask",new Vector4(axes.x,axes.y,0,0));
                var pos=new Vector3(distance,2,distance*.4f);var target=Bent(pos,radius,axes);
                cam.transform.position=target+new Vector3(0,3,-10);cam.transform.LookAt(target);
                var matrix=Matrix4x4.TRS(pos,Quaternion.Euler(0,23,0),new Vector3(1.2f,1.2f,1.2f));
                var reference=Render(cam,mesh,native,matrix,false,output+"/native.png");
                foreach(var indirect in new[]{false,true}){
                    var actual=Render(cam,mesh,source,matrix,indirect,output+"/vehicle-"+distance+"-"+axes.x+axes.y+"-"+indirect+".png");
                    var error=Vector2.Distance(actual,reference);
                    lines.Add("axes="+axes+" distance="+distance+" indirect="+indirect+" centroidErrorPixels="+error);
                    if(error>2)throw new Exception("Exported vehicle curvature diverges from native: "+lines.Last());
                }
            }
            VerifyAssemblies(prefabs,native.shader,cam,output,lines);
            VerifySpecialGraphics(prefabs,cam,output,lines);
            File.WriteAllLines(output+"/curvature.txt",lines);Debug.Log("VEHICLE_CURVATURE_GPU_OK: 32 primitive cases, 30 complete-vehicle cases, curved destination lettering and particle surfaces.");
        }
        static void VerifySpecialGraphics(GameObject[] prefabs,Camera camera,string output,System.Collections.Generic.List<string> lines,bool textOnly=false)
        {
            var tram=prefabs.Single(p=>p.name=="HeritageTramObject");
            var sources=prefabs.SelectMany(p=>p.GetComponentsInChildren<ParticleSystemRenderer>(true))
                .Select(r=>r.sharedMaterial).Distinct().ToArray();
            foreach(var distance in new[]{0f,120f})
            foreach(var kind in textOnly ? new[]{"text"} : new[]{"text","MAT_TrainExhaust","MAT_BrakeSparks"})
            {
                const float radius=300;
                Shader.SetGlobalFloat("_WorldRadius",radius);Shader.SetGlobalVector("_WorldCenter",new Vector4(0,-radius,0,0));
                Shader.SetGlobalVector("_CurveAxisMask",new Vector4(1,1,0,0));
                var pos=new Vector3(distance,2,0);var target=Bent(pos,radius,Vector2.one);
                camera.orthographicSize=3;camera.transform.position=target+Vector3.back*10;camera.transform.LookAt(target);
                GameObject node;
                if(kind=="text")
                {
                    node=Object.Instantiate(tram.GetComponent<Vehicle>().LicensePlate.gameObject);
                    node.transform.SetParent(null,false);node.transform.localScale=Vector3.one;
                    var text=node.GetComponent<TMPro.TMP_Text>();text.enableAutoSizing=false;text.fontSize=3;
                    if(textOnly){
                        foreach(var child in node.GetComponentsInChildren<Renderer>(true))if(child.gameObject!=node)child.enabled=false;
                        if(text.fontSharedMaterial.shader.name!="Curved/Standard")throw new Exception("Exported lettering is not native Curved/Standard");
                    }
                    text.rectTransform.sizeDelta=new Vector2(4,1);text.alignment=TMPro.TextAlignmentOptions.Center;
                    text.text="CITY LINE";node.SetActive(true);text.ForceMeshUpdate();
                }
                else
                {
                    node=GameObject.CreatePrimitive(PrimitiveType.Quad);
                    var mesh=Object.Instantiate(node.GetComponent<MeshFilter>().sharedMesh);
                    mesh.colors=Enumerable.Repeat(Color.white,mesh.vertexCount).ToArray();node.GetComponent<MeshFilter>().sharedMesh=mesh;
                    node.GetComponent<Renderer>().sharedMaterial=sources.Single(m=>m.name==kind);
                }
                node.transform.position=pos;node.transform.rotation=Quaternion.identity;
                // Unity's ordinary CPU frustum test does not know Eco's vertex
                // displacement. Include both positions for this isolated GPU
                // test; Eco's live object/chunk culling is a separate check.
                var renderer=node.GetComponent<Renderer>();
                var bounds=renderer.bounds;bounds.Encapsulate(new Bounds(target,Vector3.one*8));renderer.bounds=bounds;
                var rt=new RenderTexture(256,256,24);var image=new Texture2D(256,256,TextureFormat.RGB24,false);
                try
                {
                    camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                    File.WriteAllBytes(output+"/special-"+kind+"-"+distance+".png",image.EncodeToPNG());
                    double x=0,y=0;int count=0;var pixels=image.GetPixels32();
                    for(int i=0;i<pixels.Length;i++)if(pixels[i].r>20||pixels[i].g>20||pixels[i].b>20){x+=i%256;y+=i/256;count++;}
                    if(count<20)throw new Exception("Curved special renderer invisible: "+kind+" at "+distance);
                    var error=Vector2.Distance(new Vector2((float)(x/count),(float)(y/count)),new Vector2(127.5f,127.5f));
                    if(error>8)throw new Exception("Special renderer missed curved position: "+kind+" at "+distance+" error="+error);
                    lines.Add("special="+kind+" distance="+distance+" pixels="+count+" centerError="+error);
                }
                finally { camera.targetTexture=null;RenderTexture.active=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(node); }
            }
        }
        static void VerifyAssemblies(GameObject[] prefabs,Shader native,Camera camera,string output,System.Collections.Generic.List<string> lines)
        {
            foreach(var name in new[]{"MineTrainObject","HeritageTramObject","PassengerCarObject","LargePassengerCarObject","RollerCoasterCartObject"})
            foreach(var distance in new[]{0f,60f,120f})
            {
                var prefab=prefabs.Single(p=>p.name==name);
                const float radius=300;
                // Shift the curvature origin too: the earlier primitive test
                // exercised only a synthetic world centred at XZ=(0,0).
                var origin=new Vector3(180,0,210);
                Shader.SetGlobalFloat("_WorldRadius",radius);
                Shader.SetGlobalVector("_WorldCenter",origin+new Vector3(0,-radius,0));
                Shader.SetGlobalVector("_CurveAxisMask",new Vector4(1,1,0,0));
                var local=new Vector3(distance,2,distance*.4f);
                var target=origin+Bent(local,radius,Vector2.one);
                camera.orthographicSize=5;camera.transform.position=target+new Vector3(7,4,-10);camera.transform.LookAt(target+Vector3.up);
                var root=Matrix4x4.TRS(origin+local,Quaternion.Euler(0,37,0),Vector3.one)*prefab.transform.worldToLocalMatrix;
                var expected=RenderAssembly(camera,prefab,root,native,false,output+"/assembly-native-"+name+"-"+distance+".png");
                foreach(var indirect in new[]{false,true})
                {
                    var actual=RenderAssembly(camera,prefab,root,null,indirect,output+"/assembly-"+name+"-"+distance+"-"+indirect+".png");
                    var error=Vector2.Distance(expected,actual);
                    lines.Add("assembly="+name+" distance="+distance+" indirect="+indirect+" centroidErrorPixels="+error);
                    if(error>2)throw new Exception("Complete vehicle differs from native curvature: "+lines.Last());
                }
            }
        }
        static Vector2 RenderAssembly(Camera camera,GameObject prefab,Matrix4x4 root,Shader replacement,bool indirect,string path)
        {
            var target=new RenderTexture(384,384,24,RenderTextureFormat.ARGB32);
            var texture=new Texture2D(384,384,TextureFormat.RGB24,false);
            var commands=new CommandBuffer();
            var materials=new System.Collections.Generic.List<Material>();
            var buffers=new System.Collections.Generic.List<ComputeBuffer>();
            try
            {
                commands.SetGlobalVector("unity_SHAr",new Vector4(0,0,0,1));commands.SetGlobalVector("unity_SHAg",new Vector4(0,0,0,1));commands.SetGlobalVector("unity_SHAb",new Vector4(0,0,0,1));
                commands.SetGlobalVector("_LightColor0",Vector4.one);commands.SetGlobalVector("_WorldSpaceLightPos0",new Vector4(0,1,0,0));
                foreach(var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.GetComponent<TMPro.TMP_Text>()==null))
                {
                    var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)continue;
                    var matrix=root*renderer.transform.localToWorldMatrix;
                    for(int sub=0;sub<Math.Min(mesh.subMeshCount,renderer.sharedMaterials.Length);sub++)
                    {
                        var source=renderer.sharedMaterials[sub];if(source==null)continue;
                        var material=new Material(source);materials.Add(material);
                        if(replacement!=null)material.shader=replacement;
                        // Preserve the actual exported keywords, properties and
                        // instancing setting instead of replacing them with white.
                        if(indirect)
                        {
                            var instances=new ComputeBuffer(1,Marshal.SizeOf<Instance>());buffers.Add(instances);
                            instances.SetData(new[]{new Instance{objectToWorld=matrix,worldToObject=matrix.inverse,lodFade=1}});
                            material.SetBuffer("renderingParamsBuffer",instances);
                            var args=new ComputeBuffer(1,20,ComputeBufferType.IndirectArguments);buffers.Add(args);
                            args.SetData(new uint[]{mesh.GetIndexCount(sub),1,mesh.GetIndexStart(sub),mesh.GetBaseVertex(sub),0});
                            commands.DrawMeshInstancedIndirect(mesh,sub,material,0,args);
                        }
                        else commands.DrawMesh(mesh,matrix,material,sub,0);
                    }
                }
                camera.targetTexture=target;camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque,commands);camera.Render();
                RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,384,384),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
                double x=0,y=0;int count=0;var pixels=texture.GetPixels32();
                for(int i=0;i<pixels.Length;i++)if(pixels[i].r>3||pixels[i].g>3||pixels[i].b>3){x+=i%384;y+=i/384;count++;}
                if(count<20)throw new Exception("Vehicle assembly was not rendered: "+path);
                return new Vector2((float)(x/count),(float)(y/count));
            }
            finally
            {
                camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque,commands);camera.targetTexture=null;RenderTexture.active=null;commands.Release();
                foreach(var b in buffers)b.Release();foreach(var m in materials)Object.DestroyImmediate(m);
                Object.DestroyImmediate(texture);Object.DestroyImmediate(target);
            }
        }
        public static void ValidateMaterials(GameObject[] prefabs){
            var materials=prefabs.SelectMany(p=>p.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null&&m.shader.name==(RailWorldMaterialBuilder.NativeSurfaceTest ? RailWorldMaterialBuilder.SurfaceShader : RailVehiclePaintBuilder.ShaderName)).Distinct().ToArray();
            if(materials.Length==0||materials.Any(m=>!m.enableInstancing))throw new Exception("Exported vehicle material lacks enabled instancing");
            Debug.Log("VEHICLE_DISTANCE_MATERIALS_OK: "+materials.Length+" exported paint materials retain instancing.");
        }
        static Vector2 Render(Camera camera,Mesh mesh,Material source,Matrix4x4 matrix,bool indirect,string path){
            var material=new Material(source.shader);material.enableInstancing=true;material.DisableKeyword("NO_CURVE");material.DisableKeyword("MINIMAP_NO_CURVE");material.SetColor("_Color",Color.white);material.SetTexture("_MainTex",Texture2D.whiteTexture);material.SetFloat("_Metallic",0);
            var target=new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);var texture=new Texture2D(256,256,TextureFormat.RGB24,false);
            var commands=new CommandBuffer();ComputeBuffer instances=null,args=null;
            try{
                commands.SetGlobalVector("unity_SHAr",new Vector4(0,0,0,1));commands.SetGlobalVector("unity_SHAg",new Vector4(0,0,0,1));commands.SetGlobalVector("unity_SHAb",new Vector4(0,0,0,1));
                commands.SetGlobalVector("_LightColor0",Vector4.one);commands.SetGlobalVector("_WorldSpaceLightPos0",new Vector4(0,1,0,0));
                if(material.HasProperty("_SrcBlend")){material.SetFloat("_SrcBlend",1);material.SetFloat("_DstBlend",0);material.SetFloat("_ZTest",8);}
                if(indirect){instances=new ComputeBuffer(1,Marshal.SizeOf<Instance>());instances.SetData(new[]{new Instance{objectToWorld=matrix,worldToObject=matrix.inverse,lodFade=1}});material.SetBuffer("renderingParamsBuffer",instances);args=new ComputeBuffer(1,20,ComputeBufferType.IndirectArguments);args.SetData(new uint[]{mesh.GetIndexCount(0),1,mesh.GetIndexStart(0),mesh.GetBaseVertex(0),0});commands.DrawMeshInstancedIndirect(mesh,0,material,0,args);}
                else commands.DrawMesh(mesh,matrix,material,0,0);
                camera.targetTexture=target;camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque,commands);camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,256,256),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
                double x=0,y=0;int count=0;var pixels=texture.GetPixels32();for(int i=0;i<pixels.Length;i++)if(pixels[i].r>20||pixels[i].g>20||pixels[i].b>20){x+=i%256;y+=i/256;count++;}
                if(count<10)throw new Exception("Curved GPU draw missing: "+path+" pixels="+count);
                return new Vector2((float)(x/count),(float)(y/count));
            }finally{camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque,commands);camera.targetTexture=null;RenderTexture.active=null;commands.Release();instances?.Release();args?.Release();Object.DestroyImmediate(texture);Object.DestroyImmediate(target);Object.DestroyImmediate(material);}
        }
    }
}
