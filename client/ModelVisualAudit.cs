using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    // Inspection only: reopens the installed scene and never saves authoring assets.
    [InitializeOnLoad]
    public static class ModelVisualAudit
    {
        const string Pending="EcoMinecarts.ModelVisualAudit";
        static AssetBundle bundle;
        static AsyncOperation loading;
        static string Output => Environment.GetEnvironmentVariable("ECO_MODEL_AUDIT_DIR");
        static readonly List<string> issues=new List<string>();
        static readonly List<Entry> entries=new List<Entry>();
        [Serializable] public class Entry { public string name; public int meshes,triangles; public Vector3 size; public string[] images; }
        [Serializable] public class Report { public string bundle,sha256,boundary; public Entry[] models; public string[] issues; }
        static ModelVisualAudit() { EditorApplication.update+=Update; }
        public static void Run() {
            SessionState.SetBool("ModelAudit.OldReloadEnabled",EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt("ModelAudit.OldReloadOptions",(int)EditorSettings.enterPlayModeOptions);
            // Native bundle objects are instantiated afresh. Avoid a second
            // editor domain reload and unrelated IDE discovery during inspection.
            EditorSettings.enterPlayModeOptionsEnabled=true;
            EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
        }
        static void RestoreReload() {
            EditorSettings.enterPlayModeOptionsEnabled=SessionState.GetBool("ModelAudit.OldReloadEnabled",false);
            EditorSettings.enterPlayModeOptions=(EnterPlayModeOptions)SessionState.GetInt("ModelAudit.OldReloadOptions",0);
        }
        static void Update()
        {
            if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying) return;
            try {
                if(bundle==null) {
                    bundle=AssetBundle.LoadFromFile(Environment.GetEnvironmentVariable("ECO_MINECART_AUDIT_BUNDLE"));
                    loading=SceneManager.LoadSceneAsync(bundle.GetAllScenePaths().Single(),LoadSceneMode.Single); return;
                }
                if(!loading.isDone) return;
                Audit(); SessionState.SetBool(Pending,false); RestoreReload(); EditorApplication.Exit(0);
            } catch(Exception e) { Debug.LogException(e); SessionState.SetBool(Pending,false); RestoreReload(); EditorApplication.Exit(1); }
        }
        static void Audit()
        {
            Directory.CreateDirectory(Output);
            var roots=SceneManager.GetActiveScene().GetRootGameObjects();
            var prefabs=roots.Single(x=>x.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs;
            var blocks=roots.Single(x=>x.name=="BlockSets").GetComponent<BlockSetContainer>().blockSets.SelectMany(s=>s.Blocks).ToArray();
            foreach(var root in roots) root.SetActive(false);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.43f,.43f,.43f);
            Shader.EnableKeyword("NO_CURVE");
            // Bundled shader variants can omit NO_CURVE. Initialize Eco's world
            // globals too: a zero radius produces NaNs even with an unbent axis mask.
            Shader.SetGlobalFloat("_WorldRadius",10000f);
            Shader.SetGlobalVector("_WorldCenter",new Vector4(0,-10000,0,0));
            Shader.SetGlobalVector("_CurveAxisMask",Vector4.zero);
            var light=new GameObject("AuditLight").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1;
            light.transform.rotation=Quaternion.Euler(45,-30,0);
            var camera=new GameObject("AuditCamera").AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.15f,.17f,.19f);
            camera.orthographic=true; camera.nearClipPlane=.01f; camera.farClipPlane=200;
            var filter=Environment.GetEnvironmentVariable("ECO_MODEL_AUDIT_FILTER");
            if(filter!=null && filter.Contains("HeritageTram")){
                TramAssetProbe.Verify(prefabs);
                ItemIconAudit.Verify(roots);
                VehicleCurvatureAudit.ValidateMaterials(prefabs);
            }
            if(filter!=null && filter.Contains("CoasterStationObject"))StationGroundingAssetPatch.Verify(prefabs.Single(p=>p.name=="CoasterStationObject"));
            if(filter!=null && filter.Contains("TramRailSwitch"))TramSwitchSurfacePatch.Verify(prefabs);
            if(filter!=null && filter.Contains("ChainBrake"))BrakingChainAssetPatch.Verify(blocks);
            if(filter!=null && filter.Contains("RailroadHandcarObject")){
                VehicleRailFitPatch.Verify(prefabs);
                foreach(var vehicle in prefabs.Where(p=>p.GetComponent<Vehicle>()!=null)){
                    RailVehiclePhysicsAssetBuilder.VerifyGuidance(vehicle);
                    var copy=Object.Instantiate(vehicle);copy.SetActive(false);
                    var world=copy.GetComponent<WorldObject>();var body=copy.GetComponent<Rigidbody>();
                    var callback=world.OnStateChangedEvents[Array.IndexOf(world.States,"RailGuidedPhysics")];
                    callback.Invoke(true);
                    if(!body.isKinematic)throw new Exception("Guided follower remained dynamic: "+vehicle.name);
                    callback.Invoke(false);
                    if(body.isKinematic)throw new Exception("Native driving ownership did not restore: "+vehicle.name);
                    Object.DestroyImmediate(copy);
                }
                Debug.Log("CORNER_CONSIST_BUNDLE_OK: all vehicle ownership callbacks execute both transitions in exported bundle.");
            }
            Func<string,bool> matches=name=>string.IsNullOrEmpty(filter)||filter.Split('|').Any(pattern=>pattern.StartsWith("=")?name==pattern.Substring(1):pattern.EndsWith("Object")?name==pattern:name.Contains(pattern));
            foreach(var prefab in prefabs.Where(x=>matches(x.name)).OrderBy(x=>x.name)) {
                var copy=Object.Instantiate(prefab); copy.SetActive(true);
                foreach(var body in copy.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic=true;
                foreach(var audio in copy.GetComponentsInChildren<AudioSource>(true)) audio.enabled=false;
                foreach(var p in copy.GetComponentsInChildren<ParticleSystem>(true)) { p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); p.GetComponent<Renderer>().enabled=false; }
                copy.name=prefab.name;
                if(prefab.name=="RailcraftWorkbenchObject"){
                    try { RailcraftWorkbenchAssetBuilder.Verify(copy); }
                    catch(Exception e) { issues.Add(e.Message); }
                }
                try { var seats=RailRiderFit.Verify(copy); Debug.Log("RIDER_FIT_OK: "+prefab.name+" passenger seats="+seats); }
                catch(Exception e) { issues.Add(e.Message); }
                Inspect(copy,prefab.name,camera,true); Object.DestroyImmediate(copy);
            }
            foreach(var block in blocks.Where(x=>matches(x.Name)).OrderBy(x=>x.Name)) {
                var usage=((CustomBuilder)block.Builder).usageCases[0];
                var obj=new GameObject(block.Name);
                obj.AddComponent<MeshFilter>().sharedMesh=usage.blockMeshLodGroup.LOD0[0].mesh;
                obj.AddComponent<MeshRenderer>().sharedMaterials=new[]{block.Material}.Concat(block.Materials).ToArray();
                Inspect(obj,block.Name,camera,false); Object.DestroyImmediate(obj);
            }
            foreach(var group in RailExpansionAssetBuilder.ReadCatalog().CoasterTerrain.GroupBy(x=>x.SourceKey)) {
                var name="Assembly_"+group.Key;if(!matches(name))continue;
                var assembly=new GameObject(name);
                foreach(var section in group) {
                    var block=blocks.Single(b=>b.Name==section.Key);
                    var part=new GameObject(section.Key,typeof(MeshFilter),typeof(MeshRenderer));
                    part.transform.SetParent(assembly.transform,false);part.transform.localPosition=new Vector3(section.X,section.Y,section.Z);
                    part.GetComponent<MeshFilter>().sharedMesh=((CustomBuilder)block.Builder).usageCases[0].blockMeshLodGroup.LOD0[0].mesh;
                    part.GetComponent<MeshRenderer>().sharedMaterials=new[]{block.Material}.Concat(block.Materials).ToArray();
                }
                Inspect(assembly,name,camera,true);Object.DestroyImmediate(assembly);
            }
            var path=Environment.GetEnvironmentVariable("ECO_MINECART_AUDIT_BUNDLE");
            using(var sha=System.Security.Cryptography.SHA256.Create())
            File.WriteAllText(Path.Combine(Output,"models.json"),JsonUtility.ToJson(new Report {
                bundle=path,sha256=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-",""),
                boundary="Exported meshes in Unity lighting, not Eco in-game animation, camera or chunk renderer acceptance.",
                models=entries.ToArray(),issues=issues.ToArray()},true));
            Debug.Log("MODEL_VISUAL_AUDIT_OK: "+entries.Count+" models; "+issues.Count+" geometry/material issues.");
        }
        static void Inspect(GameObject obj,string name,Camera camera,bool multi)
        {
            var vehicleDetail=obj.transform.Find("VehicleDetail");
            if(vehicleDetail!=null){
                if(vehicleDetail.GetComponentsInChildren<Collider>(true).Length!=0 || vehicleDetail.GetComponentsInChildren<SpecificInteractable>(true).Length!=0)
                    issues.Add(name+": vehicle finish unexpectedly changes collision or interactions");
                var meshes=vehicleDetail.GetComponentsInChildren<MeshFilter>();
                if(meshes.Length==0 || meshes.Length>24)issues.Add(name+": vehicle finish missing or not batched");
                foreach(var filter in meshes){
                    var mesh=filter.sharedMesh;
                    if(mesh==null || mesh.vertexCount==0)issues.Add(name+": detail mesh did not survive export");
                    else if(mesh.isReadable){
                        var vertices=mesh.vertices;var indices=mesh.triangles;
                        for(var i=0;i<indices.Length;i+=3)
                            if(Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]],vertices[indices[i+2]]-vertices[indices[i]]).sqrMagnitude<1e-18f){
                                issues.Add(name+": detail mesh has a collapsed triangle");break;
                            }
                    }
                }
            }
            var finish=obj.transform.Find("VisualFinish");
            if(finish!=null){
                if(finish.GetComponentsInChildren<Collider>(true).Length!=0||finish.GetComponentsInChildren<SpecificInteractable>(true).Length!=0)
                    issues.Add(name+": visual-only fittings unexpectedly alter collision or interactions");
                var nodes=obj.GetComponentsInChildren<Transform>(true);
                bool Touches(Transform a,Transform b){
                    var bounds=a.GetComponent<Renderer>().bounds;bounds.Expand(.002f);
                    return bounds.Intersects(b.GetComponent<Renderer>().bounds);
                }
                foreach(var step in nodes.Where(t=>t.name=="Boarding step")){
                    var deck=obj.transform.Find("CabFittings/Cab floor");
                    if(nodes.Count(t=>t.name=="Step hanger"&&Touches(t,step)&&Touches(t,deck))!=2)
                        issues.Add(name+": boarding step is not attached to the floor by both hangers");
                }
                foreach(var leg in nodes.Where(t=>t.name=="Bench pedestal")){
                    if(!nodes.Any(t=>(t.name=="Deck"||t.name=="Passenger deck")&&Touches(t,leg))
                        ||!nodes.Any(t=>t.name=="Passenger bench"&&Touches(t,leg)))
                        issues.Add(name+": passenger bench pedestal is not attached at both ends");
                }
                var cab=obj.transform.Find("CabFittings");
                if(cab!=null){
                    var console=cab.Find("Cab console");
                    if(!nodes.Any(t=>t.name=="Gauge bezel"&&Touches(t,console)))issues.Add(name+": cab gauge floats clear of console");
                }
            }
            var renderers=obj.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            if(renderers.Length==0) { issues.Add(name+": no visible mesh"); return; }
            var bounds=renderers[0].bounds; int triangles=0;
            foreach(var r in renderers) {
                bounds.Encapsulate(r.bounds);
                // TextMeshPro generates its own display mesh at runtime; its
                // MeshFilter is not a stable authoring mesh to validate here.
                if(r.GetComponent<TMPro.TextMeshPro>()!=null)continue;
                var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh==null) { issues.Add(name+"/"+r.name+": missing mesh"); continue; }
                triangles+=(int)Enumerable.Range(0,mesh.subMeshCount).Sum(i=>(long)mesh.GetIndexCount(i)/3);
                if(mesh.subMeshCount!=r.sharedMaterials.Length) issues.Add(name+"/"+r.name+": submesh/material mismatch");
                foreach(var m in r.sharedMaterials) {
                    if(m==null||m.shader==null||!m.shader.isSupported) issues.Add(name+"/"+r.name+": missing/unsupported material");
                    else if(m.HasProperty("_MainTex")&&m.GetTexture("_MainTex")==null) issues.Add(name+"/"+r.name+": missing albedo "+m.name);
                }
                if(mesh.isReadable) {
                    if(mesh.vertices.Any(v=>float.IsNaN(v.x)||float.IsInfinity(v.x)||float.IsNaN(v.y)||float.IsInfinity(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.z))) issues.Add(name+": nonfinite vertices");
                    if(mesh.normals.Length!=mesh.vertexCount) issues.Add(name+"/"+r.name+": missing normals");
                    if(mesh.uv.Length!=mesh.vertexCount) issues.Add(name+"/"+r.name+": missing UVs");
                }
            }
            var directions=name=="HeritageTramObject"
                ? new[]{new Vector3(0,.2f,1),new Vector3(0,.2f,-1),new Vector3(1,.45f,1)}
                : multi?new[]{new Vector3(1,.65f,1),new Vector3(-1,.65f,-1),new Vector3(1,.08f,0)}:new[]{new Vector3(1,1.3f,1)};
            if(vehicleDetail!=null)directions=directions.Concat(new[]{new Vector3(0,.55f,1),new Vector3(1,.25f,1)}).ToArray();
            var files=new List<string>();
            for(int i=0;i<directions.Length;i++) {
                camera.transform.position=bounds.center+directions[i].normalized*Mathf.Max(5,bounds.size.magnitude*2);
                camera.transform.LookAt(bounds.center);
                // Frame all eight AABB corners in this view rather than waste space using a bounding sphere.
                float extent=0;
                foreach(var x in new[]{bounds.min.x,bounds.max.x}) foreach(var y in new[]{bounds.min.y,bounds.max.y}) foreach(var z in new[]{bounds.min.z,bounds.max.z}) {
                    var p=camera.transform.InverseTransformPoint(new Vector3(x,y,z)); extent=Mathf.Max(extent,Mathf.Abs(p.x),Mathf.Abs(p.y));
                }
                camera.orthographicSize=Mathf.Max(.15f,extent*1.10f);
                camera.orthographic=vehicleDetail==null || i!=directions.Length-1;
                if(!camera.orthographic){camera.fieldOfView=40;camera.transform.position=bounds.center+directions[i].normalized*45;camera.transform.LookAt(bounds.center);}
                var target=new RenderTexture(640,640,24); camera.targetTexture=target; camera.Render();
                RenderTexture.active=target; var image=new Texture2D(640,640,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,640,640),0,0); image.Apply();
                var pixels=image.GetPixels32(); var background=pixels[0];
                if(pixels.Count(p=>Math.Abs(p.r-background.r)+Math.Abs(p.g-background.g)+Math.Abs(p.b-background.b)>15)<100)
                    issues.Add(name+" view "+i+": empty render; visual acceptance failed");
                var file=name+"-"+i+".png"; File.WriteAllBytes(Path.Combine(Output,file),image.EncodeToPNG()); files.Add(file);
                camera.targetTexture=null; RenderTexture.active=null; Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target);
            }
            entries.Add(new Entry{name=name,meshes=renderers.Length,triangles=triangles,size=bounds.size,images=files.ToArray()});
        }
    }
}
