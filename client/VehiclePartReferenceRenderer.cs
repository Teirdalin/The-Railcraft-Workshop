using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    // Read-only reference photography of the shipping models. Never saves
    // prefabs or changes the installed game. Each Meshy input contains ONE part.
    [InitializeOnLoad]
    public static class VehiclePartReferenceRenderer
    {
        const string Pending="Railworks.VehiclePartReferences";
        static AssetBundle bundle;static AsyncOperation loading;
        static string Output=>Environment.GetEnvironmentVariable("ECO_VEHICLE_REFERENCE_DIR");
        [Serializable] public class Part {public string vehicle,id,role;public Vector3 center,size,pivot;public int triangles;public string[] images;}
        [Serializable] public class Report {public string bundle,bundleHash,boundary;public int vehicles;public Part[] parts;}
        static readonly List<Part> report=new List<Part>();
        class Piece {public Mesh mesh;public Material material;public Bounds bounds;public Transform source;public string path;}
        static VehiclePartReferenceRenderer(){EditorApplication.update+=Update;}
        public static void Run(){SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();}
        static void Update()
        {
            if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying)return;
            try{
                if(bundle==null){bundle=AssetBundle.LoadFromFile(Environment.GetEnvironmentVariable("ECO_VEHICLE_REFERENCE_BUNDLE"));loading=SceneManager.LoadSceneAsync(bundle.GetAllScenePaths().Single(),LoadSceneMode.Single);return;}
                if(!loading.isDone)return;
                RenderAll();SessionState.SetBool(Pending,false);EditorApplication.Exit(0);
            }catch(Exception e){Debug.LogError("VEHICLE_PART_REFERENCES_FAILED: "+e);SessionState.SetBool(Pending,false);EditorApplication.Exit(1);}
        }
        static string PathOf(Transform t,Transform root){return t==root?"":PathOf(t.parent,root)+"/"+t.name;}
        static Mesh Readable(Mesh mesh)
        {
            // Editor inspection can read imported GPU-only geometry without
            // changing its production import settings or rebuilding the bundle.
            using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
            using(var vertices=new NativeArray<Vector3>(mesh.vertexCount,Allocator.Temp))
            using(var normals=new NativeArray<Vector3>(mesh.vertexCount,Allocator.Temp))
            using(var uv=new NativeArray<Vector2>(mesh.vertexCount,Allocator.Temp))
            {
                data[0].GetVertices(vertices);data[0].GetNormals(normals);data[0].GetUVs(0,uv);
                var result=new Mesh();result.vertices=vertices.ToArray();result.normals=normals.ToArray();result.uv=uv.ToArray();result.subMeshCount=mesh.subMeshCount;
                for(int sub=0;sub<mesh.subMeshCount;sub++)using(var indices=new NativeArray<int>((int)mesh.GetIndexCount(sub),Allocator.Temp)){data[0].GetIndices(indices,sub);result.SetTriangles(indices.ToArray(),sub);}
                return result;
            }
        }
        static IEnumerable<Piece> Pieces(GameObject root)
        {
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeSelf&&r.GetComponent<TMPro.TMP_Text>()==null))
            {
                var path=PathOf(renderer.transform,root.transform);
                if(path.Contains("lettering")||path.Contains("Sign")||path.Contains("CargoContents"))continue;
                var original=renderer.GetComponent<MeshFilter>()?.sharedMesh;if(original==null)continue;
                bool copied=!original.isReadable;if(copied)original=Readable(original);
                var matrix=root.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                var positions=original.vertices.Select(v=>matrix.MultiplyPoint3x4(v)).ToArray();
                var normalMatrix=matrix.inverse.transpose;
                var normals=original.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray();var uv=original.uv;
                for(int sub=0;sub<original.subMeshCount;sub++)
                {
                    var indices=original.GetTriangles(sub);if(matrix.determinant<0)for(int i=0;i<indices.Length;i+=3){int tmp=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=tmp;}
                    var parents=Enumerable.Range(0,positions.Length).ToArray();
                    int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
                    void Union(int a,int b){parents[Find(a)]=Find(b);}
                    var welded=new Dictionary<Vector3Int,int>();
                    foreach(var i in indices){var p=positions[i];var k=new Vector3Int(Mathf.RoundToInt(p.x*100000),Mathf.RoundToInt(p.y*100000),Mathf.RoundToInt(p.z*100000));if(welded.TryGetValue(k,out int other))Union(i,other);else welded[k]=i;}
                    for(int i=0;i<indices.Length;i+=3){Union(indices[i],indices[i+1]);Union(indices[i+1],indices[i+2]);}
                    var groups=new Dictionary<int,List<int>>();
                    for(int i=0;i<indices.Length;i+=3){int key=Find(indices[i]);if(!groups.TryGetValue(key,out var tris))groups[key]=tris=new List<int>();tris.AddRange(new[]{indices[i],indices[i+1],indices[i+2]});}
                    foreach(var group in groups.Values)
                    {
                        var used=group.Distinct().ToArray();var remap=used.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i);
                        var mesh=new Mesh();mesh.vertices=used.Select(i=>positions[i]).ToArray();mesh.normals=used.Select(i=>normals[i]).ToArray();
                        mesh.uv=used.Select(i=>uv.Length==positions.Length?uv[i]:Vector2.zero).ToArray();mesh.triangles=group.Select(i=>remap[i]).ToArray();mesh.RecalculateBounds();mesh.RecalculateTangents();
                        yield return new Piece{mesh=mesh,material=renderer.sharedMaterials[sub],bounds=mesh.bounds,source=renderer.transform,path=path};
                    }
                }
                if(copied)Object.DestroyImmediate(original);
            }
        }
        static string Group(GameObject root,Piece p,Bounds all)
        {
            var path=p.path;var b=p.bounds;var c=b.center;
            if(path.Contains("WheelRoll_"))
            {
                if(path.Contains("MechanicalLinkage"))return path.Contains("WheelRoll_FL/")?"mechanism-"+p.source.name.ToLowerInvariant():null;
                if(path.Contains("Animated crank pin"))return path.Contains("WheelRoll_FL/")?"mechanism-crank-pin":null;
                return path.Contains("WheelRoll_FL/Spin")?"wheel":null;
            }
            if(path.Contains("UpstopRoll_"))return path.Contains("UpstopRoll_0/")?"upstop-wheel":null;
            if(path.Contains("DumpHinge/Bucket"))return "dump-bucket";
            if(path.Contains("PumpPivot"))return "pump-handle";
            if(path.Contains("PumpRod"))return "pump-link-rod";
            if(path.Contains("PumpSlider"))return "pump-slider";
            if(RailMechanicalAnimationBuilder.Engines.Contains(root.name))
            {
                if((c.y>all.max.y-.22f&&b.size.y<.45f&&b.size.x>.4f)||path.Contains("/VehicleDetail/Roof/"))return "cab-roof";
                if(c.y<.40f||b.max.y<.50f)return "chassis";
                var floor=root.transform.Find("CabFittings/Cab floor")?.GetComponent<Renderer>();
                float divide=floor!=null?floor.bounds.max.z+.04f:-all.size.z*.13f;
                return c.z<divide?"cab-body":"boiler-assembly";
            }
            if(root.name=="RollerCoasterCartObject")
            {
                if(c.y>.77f&&Mathf.Abs(c.x)>.05f)
                    return c.x<0?((c.z>.13f&&c.y>1.02f)||(c.z>-.02f&&c.y>.85f&&b.size.y>.20f&&b.size.z>.20f)?"lap-restraint":"seat"):null;
                if(c.y<.25f)return "chassis-and-wheel-carriers";
                return "body-shell";
            }
            if((path.Contains("/VehicleDetail/Roof/")||c.y>all.max.y-.16f)&&b.size.y<.6f&&b.size.z>.6f)return "roof";
            if(root.name=="HeritageTramObject")
            {
                if(path.Contains("Passenger bench")||path.Contains("BenchSlats")||p.material.name.Contains("Timber")&&c.y>.65f&&c.y<1.30f&&b.size.z>.7f)return "passenger-benches";
                return c.y<.53f?"chassis":"body-and-posts";
            }
            if(root.name=="RailroadHandcarObject")return c.y>.60f?"pump-tower-and-toolbox":"chassis-and-deck";
            if(RailDumpAnimationBuilder.Buckets.Contains(root.name))return "chassis";
            return c.y<.48f?"chassis":"body-and-seats";
        }
        static void RenderAll()
        {
            Directory.CreateDirectory(Output);
            var roots=SceneManager.GetActiveScene().GetRootGameObjects();var prefabs=roots.Single(g=>g.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs.Where(p=>p.GetComponent<Vehicle>()!=null).OrderBy(p=>p.name).ToArray();
            string only=Environment.GetEnvironmentVariable("ECO_VEHICLE_REFERENCE_FILTER");
            if(!string.IsNullOrEmpty(only)&&File.Exists(Path.Combine(Output,"part-manifest.json")))report.AddRange(JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(Output,"part-manifest.json"))).parts.Where(p=>p.vehicle!=only));
            foreach(var root in roots)root.SetActive(false);
            Shader.SetGlobalFloat("_WorldRadius",10000);Shader.SetGlobalVector("_WorldCenter",new Vector4(0,-10000,0,0));Shader.SetGlobalVector("_CurveAxisMask",Vector4.zero);Shader.EnableKeyword("NO_CURVE");
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.72f,.72f,.72f);
            var light=new GameObject("Reference key light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.35f;light.transform.rotation=Quaternion.Euler(40,-35,0);
            var fill=new GameObject("Reference fill light").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.35f;fill.transform.rotation=Quaternion.Euler(25,145,0);
            var camera=new GameObject("Reference camera").AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.88f,.89f,.90f);camera.nearClipPlane=.001f;camera.farClipPlane=1000;
            foreach(var prefab in prefabs)
            {
                if(!string.IsNullOrEmpty(only)&&prefab.name!=only)continue;
                var root=Object.Instantiate(prefab);root.SetActive(true);root.name=prefab.name;
                foreach(var animation in root.GetComponentsInChildren<Animator>(true))animation.enabled=false;
                foreach(var animation in root.GetComponentsInChildren<Animation>(true))animation.enabled=false;
                try
                {
                    var pieces=Pieces(root).ToArray();var bounds=pieces[0].bounds;foreach(var p in pieces)bounds.Encapsulate(p.bounds);
                    var selections=pieces.GroupBy(p=>Group(root,p,bounds)).Where(g=>g.Key!=null).Select(g=>(key:g.Key,parts:g.ToArray(),section:!(g.Key.StartsWith("mechanism-")||new[]{"wheel","upstop-wheel","dump-bucket","pump-handle","pump-link-rod","pump-slider","seat","lap-restraint"}.Contains(g.Key)))).ToList();
                    var fixedParts=pieces.Where(p=>!p.path.Contains("WheelRoll_")&&!p.path.Contains("UpstopRoll_")&&!p.path.Contains("DumpHinge/Bucket")&&!p.path.Contains("PumpPivot")&&!p.path.Contains("PumpRod")&&!p.path.Contains("PumpSlider")
                        &&!(root.name=="RollerCoasterCartObject"&&p.bounds.center.y>.77f&&Mathf.Abs(p.bounds.center.x)>.05f)).ToArray();
                    selections.Insert(0,(key:RailDumpAnimationBuilder.Buckets.Contains(root.name)?"fixed-chassis":"fixed-body",parts:fixedParts,section:false));
                    foreach(var group in selections)
                    {
                        var visible=new GameObject("Isolated "+group.key);var partBounds=group.parts.First().bounds;
                        foreach(var p in group.parts){partBounds.Encapsulate(p.bounds);var n=new GameObject(p.source.name,typeof(MeshFilter),typeof(MeshRenderer));n.transform.SetParent(visible.transform,false);n.GetComponent<MeshFilter>().sharedMesh=p.mesh;n.GetComponent<MeshRenderer>().sharedMaterial=p.material;}
                        var nodes=root.GetComponentsInChildren<MeshRenderer>(true);var old=nodes.Select(r=>r.enabled).ToArray();foreach(var r in nodes)r.enabled=false;
                        try
                        {
                            var dir=Path.Combine(Output,group.section?"detail-guides":"parts",prefab.name.Replace("Object",""),group.key);Directory.CreateDirectory(dir);
                            var views=group.key.StartsWith("mechanism-")||group.key=="wheel"||group.key=="upstop-wheel"?new[]{"three-quarter","side"}:new[]{"front-three-quarter","rear-three-quarter","left","right","front","rear","top"};
                            var files=views.Select(v=>Shot(visible,partBounds,camera,dir,v)).ToArray();
                            Vector3 pivot=Vector3.zero;if(group.key=="wheel")pivot=root.transform.Find("WheelRoll_FL").localPosition;
                            if(group.key=="dump-bucket")pivot=root.transform.Find("DumpAnimation/DumpHinge").localPosition;
                            if(group.key=="pump-handle")pivot=root.transform.Find("PumpPivot").localPosition;
                            if(group.key.StartsWith("mechanism-")||group.key.StartsWith("pump-link")||group.key=="pump-slider")pivot=root.transform.InverseTransformPoint(group.parts.First().source.position);
                            if(group.key=="lap-restraint")pivot=new Vector3(-.533f,.80f,-.13f);
                            report.Add(new Part{vehicle=prefab.name,id=group.key,role=group.section?"Assembly detail only":group.key.StartsWith("mechanism-")?"Reusable moving linkage reference":"Isolated replacement part",center=partBounds.center,size=partBounds.size,pivot=pivot,triangles=group.parts.Sum(p=>p.mesh.triangles.Length/3),images=files.Select(f=>Path.GetRelativePath(Output,f).Replace('\\','/')).ToArray()});
                        }finally{for(int i=0;i<nodes.Length;i++)nodes[i].enabled=old[i];Object.DestroyImmediate(visible);}
                    }
                    var assemblyDir=Path.Combine(Output,"assembly-guides",prefab.name.Replace("Object",""));Directory.CreateDirectory(assemblyDir);
                    foreach(var view in new[]{"front-three-quarter","rear-three-quarter","left","right"})Shot(root,bounds,camera,assemblyDir,view);
                    foreach(var p in pieces)Object.DestroyImmediate(p.mesh);
                    Debug.Log("VEHICLE_REFERENCE_OK: "+prefab.name);
                }finally{Object.DestroyImmediate(root);}
            }
            var path=Environment.GetEnvironmentVariable("ECO_VEHICLE_REFERENCE_BUNDLE");
            using(var sha=System.Security.Cryptography.SHA256.Create())File.WriteAllText(Path.Combine(Output,"part-manifest.json"),JsonUtility.ToJson(new Report{bundle=path,bundleHash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-",""),boundary="Read-only isolated Unity renders of current shipping geometry. Parts are separate image files; assembly guides are not Meshy inputs.",vehicles=prefabs.Length,parts=report.ToArray()},true));
            Debug.Log("VEHICLE_PART_REFERENCES_OK: "+prefabs.Length+" vehicles, "+report.Count+" isolated parts");
        }
        static string Shot(GameObject root,Bounds bounds,Camera camera,string directory,string view)
        {
            var direction=view=="left"||view=="side"?Vector3.left:view=="right"?Vector3.right:view=="front"?Vector3.forward:view=="rear"?Vector3.back:view=="top"?new Vector3(0,1,.0001f):view=="rear-three-quarter"?new Vector3(-1,.65f,-1):new Vector3(1,.65f,1);
            camera.transform.position=bounds.center+direction.normalized*Mathf.Max(5,bounds.size.magnitude*2);camera.transform.LookAt(bounds.center);
            float extent=0;foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float y in new[]{bounds.min.y,bounds.max.y})foreach(float z in new[]{bounds.min.z,bounds.max.z}){var p=camera.transform.InverseTransformPoint(new Vector3(x,y,z));extent=Mathf.Max(extent,Mathf.Abs(p.x),Mathf.Abs(p.y));}
            camera.orthographicSize=Mathf.Max(.015f,extent*1.16f);
            var target=new RenderTexture(1280,1280,24);var image=new Texture2D(1280,1280,TextureFormat.RGB24,false);camera.targetTexture=target;camera.Render();var old=RenderTexture.active;RenderTexture.active=target;
            try{image.ReadPixels(new Rect(0,0,1280,1280),0,0);image.Apply();var pixels=image.GetPixels32();var bg=pixels[0];if(pixels.Count(p=>Math.Abs(p.r-bg.r)+Math.Abs(p.g-bg.g)+Math.Abs(p.b-bg.b)>20)<100)throw new Exception("Empty part photo: "+directory+view);var path=Path.Combine(directory,view+".png");File.WriteAllBytes(path,image.EncodeToPNG());return path;}
            finally{camera.targetTexture=null;RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
        }
    }
}
