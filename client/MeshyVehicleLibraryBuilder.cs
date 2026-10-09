using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    // Shared authoring path: Build previews candidates; Apply updates the existing
    // shipping prefab GUIDs without replacing native vehicle components.
    public static class MeshyVehicleLibraryBuilder
    {
        const string Root="Assets/EcoMinecarts";
        const string Art=Root+"/VehicleLibrary";
        const string Candidates=Root+"/VehicleCandidates";
        [Serializable] class Geometry {public string name;public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles;}
        [Serializable] class Anchor {public string name;public Vector3 position;}
        [Serializable] class Collision {public string name;public Vector3 center,size;}
        [Serializable] class Asset {public string key,role;public Vector3 size;public Anchor[] anchors;public Collision[] collision;}
        [Serializable] class Part {public string vehicle,id,role;public Vector3 center,size;}
        [Serializable] class References {public Part[] parts;}
        [Serializable] class Receipt {public string boundary;public int parts,vehicles;public string[] prefabs;}
        static Dictionary<string,Asset> assets;
        static References references;
        static string Repo=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.."));
        static string Output=>Path.Combine(Repo,"validation/vehicle-library-2026-10-07/assemblies");
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static Transform Node(Transform parent,string name,Vector3 position)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
        static string PathOf(Transform t,Transform root)=>t==root?"":PathOf(t.parent,root)+"/"+t.name;
        static Mesh LoadMesh(string key,int lod)
        {
            string path=Art+"/"+key+"/mesh-lod"+lod;
            var d=JsonUtility.FromJson<Geometry>(File.ReadAllText(path+".json"));
            Require(d.vertices.Length==d.normals.Length&&d.vertices.Length==d.uv.Length,"Invalid vertex attributes "+key);
            Require(d.triangles.All(i=>i>=0&&i<d.vertices.Length),"Bad index "+key);
            Require(d.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Non-finite vertices "+key);
            var m=AssetDatabase.LoadAssetAtPath<Mesh>(path+".asset");if(m==null){m=new Mesh();AssetDatabase.CreateAsset(m,path+".asset");}else m.Clear();
            m.indexFormat=d.vertices.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16;m.name=key+"_LOD"+lod;
            m.vertices=d.vertices;m.normals=d.normals;m.uv=d.uv;m.triangles=d.triangles;m.colors32=Enumerable.Repeat(new Color32(255,255,255,255),d.vertices.Length).ToArray();
            m.RecalculateTangents();m.RecalculateBounds();EditorUtility.SetDirty(m);return m;
        }
        static Texture2D Texture(string key,string name,bool linear)
        {
            var path=Art+"/"+key+"/"+name+".png";var imp=(TextureImporter)AssetImporter.GetAtPath(path);
            Require(imp!=null,"Missing atlas "+path);
            if(imp.sRGBTexture!=!linear||!imp.mipmapEnabled||imp.maxTextureSize!=2048||imp.textureCompression!=TextureImporterCompression.CompressedHQ||imp.wrapMode!=TextureWrapMode.Clamp){
                imp.sRGBTexture=!linear;imp.mipmapEnabled=true;imp.maxTextureSize=2048;
                imp.textureCompression=TextureImporterCompression.CompressedHQ;imp.wrapMode=TextureWrapMode.Clamp;imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static Material Material(string key)
        {
            string path=Art+"/"+key+"/Railworks.mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader=Shader.Find(RailVehiclePaintBuilder.ShaderName);Require(shader!=null,"Native curved paint shader missing");
            if(m==null){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}m.shader=shader;m.enableInstancing=true;
            m.SetColor("_Color",Color.white);m.SetTexture("_MainTex",Texture(key,"albedo",false));m.SetTexture("_PaintCombinedTexture",Texture(key,"paint-mask",true));
            m.SetFloat("_Metallic",.18f);m.SetFloat("_Glossiness",.22f);m.SetFloat("_PaintedAmount",0);
            if(File.Exists(Art+"/"+key+"/normal.png")){
                var normalPath=Art+"/"+key+"/normal.png";var importer=(TextureImporter)AssetImporter.GetAtPath(normalPath);
                if(importer.textureType!=TextureImporterType.NormalMap){importer.textureType=TextureImporterType.NormalMap;importer.sRGBTexture=false;importer.maxTextureSize=2048;importer.SaveAndReimport();}
                m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));m.SetFloat("_BumpScale",.55f);
            }
            m.SetTexture("_MetallicGlossMap",Texture(key,"surface",true));m.SetFloat("_UsePackedSurface",1);m.EnableKeyword("RAILWORKS_SURFACE_ATLAS");
            foreach(var channel in new[]{"Red","Green","Blue"})m.SetColor("_Channel"+channel+"Color",Color.clear);
            EditorUtility.SetDirty(m);return m;
        }
        static GameObject PartObject(string key)
        {
            var data=assets[key];var obj=new GameObject(key);var mat=Material(key);var lods=new LOD[3];
            for(int i=0;i<3;i++){
                var child=Node(obj.transform,"Render_LOD"+i,Vector3.zero);child.gameObject.AddComponent<MeshFilter>().sharedMesh=LoadMesh(key,i);
                var r=child.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=mat;
                var so=new SerializedObject(r);var small=so.FindProperty("m_SmallMeshCulling");if(small!=null){small.boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();}
                lods[i]=new LOD(new[]{.28f,.10f,.012f}[i],new Renderer[]{r});
            }
            var group=obj.AddComponent<LODGroup>();group.SetLODs(lods);group.RecalculateBounds();
            var anchors=Node(obj.transform,"Anchors",Vector3.zero);
            foreach(var a in data.anchors)Node(anchors,a.name,a.position);
            return obj;
        }
        static GameObject AddPart(Transform parent,string key,Vector3 center,Vector3 size)
        {
            var obj=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Art+"/"+key+"/"+key+".prefab"));
            PrefabUtility.UnpackPrefabInstance(obj,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            obj.name="PreparedPart_"+key;
            obj.transform.SetParent(parent,false);obj.transform.localPosition=center;
            var d=assets[key].size;obj.transform.localScale=new Vector3(size.x/d.x,size.y/d.y,size.z/d.z);return obj;
        }
        static void FitCoasterSeatingShell(GameObject body)
        {
            // Expand the passenger tub, not its axle housings or couplers.
            // The old whole-body bounds include those exterior fittings.
            float low=(.46f-body.transform.localPosition.y)/body.transform.localScale.y;
            float high=(.56f-body.transform.localPosition.y)/body.transform.localScale.y;
            foreach(var filter in body.GetComponentsInChildren<MeshFilter>()){
                var original=filter.sharedMesh;var vertices=original.vertices;var normals=original.normals;
                for(int i=0;i<vertices.Length;i++){
                    var p=vertices[i];float t=Mathf.Clamp01((p.y-low)/(high-low));
                    float width=1+.64f*t*t*(3-2*t);
                    float derivative=.64f*6*t*(1-t)/(high-low);
                    var n=normals[i];normals[i]=new Vector3(n.x/width,n.y-p.x*derivative*n.x/width,n.z).normalized;
                    p.x*=width;vertices[i]=p;
                }
                string path=Candidates+"/Coaster-seat-bay-"+filter.name+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null){mesh=Object.Instantiate(original);AssetDatabase.CreateAsset(mesh,path);}else EditorUtility.CopySerialized(original,mesh);
                mesh.name="Coaster_seat_bay_"+filter.name;mesh.vertices=vertices;mesh.normals=normals;mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);filter.sharedMesh=mesh;
            }
            body.GetComponent<LODGroup>().RecalculateBounds();
        }
        static Part Reference(string vehicle,string id)=>references.parts.Single(r=>r.vehicle==vehicle&&r.id==id&&r.role=="Isolated replacement part");
        static void FitPart(GameObject root,Transform parent,string key,string id)
        {var p=Reference(root.name,id);bool engine=id=="fixed-body"&&RailMechanicalAnimationBuilder.Engines.Contains(root.name);
            var size=engine?new Vector3(assets[key].size.x*p.size.z/assets[key].size.z,assets[key].size.y,p.size.z):p.size;
            if(engine&&root.name=="MineTrainObject")size.y*=.85f;
            var center=p.center;if(engine)center.y=p.center.y-p.size.y/2+size.y/2;
            if(id=="dump-bucket")center.y=.42f+size.y/2;
            if(engine&&root.name=="MineTrainObject"){
                // The compact engine reserves most of its old footprint for a
                // walk-in cab. Fit the complete boiler ahead of that cab instead
                // of cutting its dome and firebox away at the console plane.
                size.z=p.size.z;
                center.z=p.center.z+p.size.z/2-size.z/2;
                center.y=.35f+size.y/2;
            }
            var obj=AddPart(parent,key,Vector3.zero,size);obj.transform.position=root.transform.TransformPoint(center);
            if(engine)obj.transform.localRotation=Quaternion.Euler(0,180,0);
        }
        static void Beam(Transform parent,string name,Vector3 center,Vector3 size)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(parent,false);o.transform.localPosition=center;o.transform.localScale=size;
            Object.DestroyImmediate(o.GetComponent<Collider>());o.GetComponent<Renderer>().sharedMaterial=Fittings();
        }
        static Material Fittings()
        {
            string path=Candidates+"/PreparedFittings.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(Shader.Find(RailVehiclePaintBuilder.ShaderName));AssetDatabase.CreateAsset(mat,path);}
            mat.SetColor("_Color",new Color(.13f,.15f,.14f));mat.SetFloat("_Metallic",.18f);mat.SetFloat("_Glossiness",.20f);
            mat.SetTexture("_PaintCombinedTexture",AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Materials/VehiclePaintRegion2.asset"));mat.enableInstancing=true;EditorUtility.SetDirty(mat);return mat;
        }
        static void FitRollingRadius(GameObject wheel,string vehicle,float radius)
        {
            // Match the visible tyre to the native wheel animator's circumference.
            // A flange extends 12 mm below contact; it is not the rolling radius.
            int level=0;
            foreach(var filter in wheel.GetComponentsInChildren<MeshFilter>()){
                var mesh=Object.Instantiate(filter.sharedMesh);var vertices=mesh.vertices;
                float outer=assets[wheel.name.Replace("(Clone)","").Replace("PreparedPart_","")].size.y/2;
                float originalTread=outer*.95f,localTread=radius/wheel.transform.localScale.y;
                for(int i=0;i<vertices.Length;i++){
                    var p=vertices[i];float r=Mathf.Sqrt(p.y*p.y+p.z*p.z);if(r<.000001f)continue;
                    float fitted=r<=originalTread?r*localTread/originalTread:Mathf.Lerp(localTread,outer,Mathf.InverseLerp(originalTread,outer,r));
                    p.y*=fitted/r;p.z*=fitted/r;vertices[i]=p;
                }
                mesh.vertices=vertices;mesh.RecalculateBounds();mesh.RecalculateTangents();
                string path=Candidates+"/"+vehicle+"-wheel-lod"+level+++".asset";
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing==null)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}
                filter.sharedMesh=mesh;
            }
        }
        static readonly Dictionary<string,string> Bodies=new Dictionary<string,string>{
            {"MineTrainObject","Minetrain"},{"PassengerLocomotiveObject","PassengerLocomotive"},{"FreightLocomotiveObject","FreightLocomotive"},{"LargeTrainEngineObject","LargeEngine"},
            {"MinecartObject","MinecartChassis"},{"WoodenMinecartObject","MinecartChassis"},{"CoalTenderObject","MinecartChassis"},{"LargeCoalTenderObject","CargoChassis"},{"LargeCargoCarObject","CargoChassis"},
            {"PassengerCarObject","PassengerCarriage"},{"LargePassengerCarObject","PassengerCarriage"},{"HeritageTramObject","HeritageTram"},{"RailroadHandcarObject","HandcarBody"},{"RollerCoasterCartObject","CoasterBody"}};
        static void ValidateMotion(GameObject root)
        {
            var world=root.GetComponent<WorldObject>();var drive=root.GetComponent<RCCCarControllerV2>();
            int rate=Array.IndexOf(world.FloatStates,"RailWheelSpeed");Require(rate>=0,"Missing signed wheel speed state");
            var callbacks=Enumerable.Range(0,world.OnFloatStateChanged[rate].GetPersistentEventCount()).Select(i=>world.OnFloatStateChanged[rate].GetPersistentTarget(i)).ToArray();
            foreach(var suffix in new[]{"FL","FR","RL","RR"}){
                var host=root.transform.Find("WheelRoll_"+suffix);var animator=host.GetComponent<Animator>();Require(callbacks.Contains(animator),"Wheel lost native speed event "+root.name+suffix);
                var controller=(AnimatorController)animator.runtimeAnimatorController;var states=controller.layers[0].stateMachine.states.Select(s=>s.state).ToArray();
                Require(states.Single(s=>s.name=="RollForward").speed==1&&states.Single(s=>s.name=="RollReverse").speed==-1,"Wheel direction contract changed");
                var clip=(AnimationClip)states.Single(s=>s.name=="RollForward").motion;
                var nodes=host.GetComponentsInChildren<Transform>(true);var positions=nodes.Select(t=>t.localPosition).ToArray();var rotations=nodes.Select(t=>t.localRotation).ToArray();var scales=nodes.Select(t=>t.localScale).ToArray();
                try{clip.SampleAnimation(host.gameObject,clip.length*.25f);Require(Mathf.Abs(Mathf.DeltaAngle(host.Find("Spin").localEulerAngles.x,90))<.2f,"Wheel quarter-turn sample failed");}
                finally{for(int i=0;i<nodes.Length;i++){nodes[i].localPosition=positions[i];nodes[i].localRotation=rotations[i];nodes[i].localScale=scales[i];}}
            }
            if(RailDumpAnimationBuilder.Buckets.Contains(root.name)){
                var host=root.transform.Find("DumpAnimation");var controller=(AnimatorController)host.GetComponent<Animator>().runtimeAnimatorController;
                var nodes=host.GetComponentsInChildren<Transform>(true);var p=nodes.Select(t=>t.localPosition).ToArray();var q=nodes.Select(t=>t.localRotation).ToArray();
                try{
                    foreach(var side in new[]{"Left","Right"}){
                        var clip=(AnimationClip)controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Dump"+side+"28").state.motion;clip.SampleAnimation(host.gameObject,0);
                        Require(Mathf.Abs(Vector3.Angle(host.Find("DumpHinge/Bucket").up,root.transform.up)-70)<.2f,"Dump side hinge sample failed");
                    }
                }finally{for(int i=0;i<nodes.Length;i++){nodes[i].localPosition=p[i];nodes[i].localRotation=q[i];}}
            }
            Debug.Log("VEHICLE_CANDIDATE_MOTION_OK: "+root.name+" native wheel event targets, signed clips, quarter-turn and applicable dump side pivots");
        }
        static void Candidate(string vehicle,string key,bool shipping=false)
        {
            string source=Root+"/Prefabs/"+vehicle+".prefab";var root=PrefabUtility.LoadPrefabContents(source);
            try{
                root.name=vehicle;
                // Repeated exports rebuild replacements, never accumulate meshes.
                foreach(var n in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="PreparedVehicleArt"||t.name=="PreparedCabDetail"||t.name.StartsWith("PreparedPart_")||t.name.StartsWith("LegacyDesignGeometry")).ToArray())
                    if(n!=null)Object.DestroyImmediate(n.gameObject);
                foreach(var n in root.GetComponentsInChildren<Transform>(true).Where(t=>PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)&&AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject)).StartsWith(Art+"/")).ToArray())
                    if(n!=null)Object.DestroyImmediate(n.gameObject);
                var oldRenderers=root.GetComponentsInChildren<MeshRenderer>(true);
                foreach(var r in oldRenderers){
                    var path=PathOf(r.transform,root.transform);
                    // Runtime text/cargo/coupling and shared rod animation remain.
                    if(path.Contains("lettering")||path.Contains("Sign")||path.Contains("CargoContents")||path.Contains("CoupledCoupler")||path.Contains("MechanicalLinkage")||path.Contains("Animated crank pin")||path.Contains("UpstopRoll_")||path.Contains("PumpRod")||path.Contains("PumpSlider"))continue;
                    if(path.Contains("CabFittings/")){r.enabled=true;continue;}
                    // Eco's archetype/instanced rendering can register disabled
                    // renderers. Retire the render components, keeping native
                    // transforms, physics and clickable targets intact.
                    var filter=r.GetComponent<MeshFilter>();Object.DestroyImmediate(r);
                    if(filter!=null)Object.DestroyImmediate(filter);
                }
                var art=Node(root.transform,"PreparedVehicleArt",Vector3.zero);
                bool dump=RailDumpAnimationBuilder.Buckets.Contains(vehicle);
                if(vehicle=="RollerCoasterCartObject"){
                    // The imported body has its tall rear seat bay at +Z;
                    // chairs and native riders face +Z. Turn the body so its
                    // low footwell is forward, and fit the interior walls to
                    // the adult chairs, excluding exterior coupling fittings.
                    var coasterBody=AddPart(art,key,new Vector3(0,.56f,0),new Vector3(1.50f,.66f,1.95f));
                    FitCoasterSeatingShell(coasterBody);
                    coasterBody.transform.localRotation=Quaternion.Euler(0,180,0);
                    foreach(var x in new[]{-.34f,.34f}){
                        AddPart(art,"CoasterSeat",new Vector3(x,.937f,-.20f),new Vector3(.62f,1.14f,.78f));
                        // The chair includes its own pedestal. Its mounting
                        // plate belongs at deck level, below the cushion.
                        Beam(art,"Seat mounting pedestal",new Vector3(x,.49f,-.20f),new Vector3(.45f,.04f,.46f));
                        var restraint=Node(art,"RestraintPivot_"+(x<0?"Left":"Right"),new Vector3(x,.99f,-.43f));
                        AddPart(restraint,"LapRestraint",new Vector3(0,-.14f,.28f),new Vector3(.59f,.36f,.67f));
                    }
                }else if(dump){
                    var p=Reference(vehicle,"fixed-chassis");
                    // Cargo volume may overhang the narrow gauge. Do not stretch
                    // its axle bearings to the full width of a two-metre hopper.
                    AddPart(art,key,new Vector3(0,.285f,0),new Vector3(Mathf.Min(p.size.x,.82f),.24f,p.size.z));
                    if(p.size.x>1)foreach(var z in new[]{-p.size.z*.28f,0,p.size.z*.28f})Beam(art,"Hopper cross bearer",new Vector3(0,.395f,z),new Vector3(p.size.x*.90f,.075f,.11f));
                }else FitPart(root,art,key,"fixed-body");
                if(dump){
                    var bucket=root.transform.Find("DumpAnimation/DumpHinge/Bucket");Require(bucket!=null,"Missing shared dump pivot "+vehicle);
                    string part=vehicle=="MinecartObject"?"SteelBucket":vehicle=="WoodenMinecartObject"?"WoodenBucket":"MetalBucket";
                    FitPart(root,bucket,part,"dump-bucket");
                    var hardware=Node(bucket,"PreparedPart_DumpHardware",Vector3.zero);
                    var shape=Reference(vehicle,"dump-bucket");float half=RailDumpAnimationBuilder.HalfWidth(root);
                    foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1}){
                        float z=end*shape.size.z*.34f;
                        Beam(art,"Dump bearing support",new Vector3(side*half,.40f,z),new Vector3(.10f,.10f,.10f));
                        var pin=GameObject.CreatePrimitive(PrimitiveType.Cylinder);pin.name="Bucket trunnion";pin.transform.SetParent(hardware,false);
                        pin.transform.localPosition=new Vector3(side*half,.42f,z);pin.transform.localRotation=Quaternion.Euler(90,0,0);pin.transform.localScale=new Vector3(.095f,.08f,.095f);
                        Object.DestroyImmediate(pin.GetComponent<Collider>());pin.GetComponent<Renderer>().sharedMaterial=Fittings();
                        Beam(hardware,"Bucket hinge saddle",new Vector3(side*half,.45f,z),new Vector3(.10f,.08f,.10f));
                    }
                }
                if(vehicle=="RailroadHandcarObject"){
                    var pivot=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="PumpPivot");
                    FitPart(root,pivot,"HandcarHandle","pump-handle");
                    var handle=pivot.Find("PreparedPart_HandcarHandle");
                    // The source crossbar was authored along X; operators stand
                    // at the two Z ends of the rocking beam.
                    handle.localRotation=Quaternion.Euler(0,90,0);
                    var fitted=Reference(vehicle,"pump-handle").size;var original=assets["HandcarHandle"].size;
                    handle.localScale=new Vector3(fitted.z/original.x,fitted.y/original.y,fitted.x/original.z);
                }
                if(vehicle=="PassengerCarObject"||vehicle=="LargePassengerCarObject")RaiseCoachRoof(root,art,key);
                var drive=root.GetComponent<RCCCarControllerV2>();
                var cols=new[]{drive.FrontLeftWheelCollider,drive.FrontRightWheelCollider,drive.RearLeftWheelCollider,drive.RearRightWheelCollider};
                for(int i=0;i<4;i++){
                    var pivot=root.transform.Find("WheelRoll_"+new[]{"FL","FR","RL","RR"}[i]+"/Spin");Require(pivot!=null,"Missing shared wheel pivot "+vehicle);
                    var p=Reference(vehicle,"wheel");float diameter=2*(cols[i].radius+.012f);
                    var wheel=AddPart(pivot,vehicle=="RollerCoasterCartObject"?"CoasterWheel":"FlangedWheel",Vector3.zero,new Vector3(p.size.x,diameter,diameter));
                    FitRollingRadius(wheel,vehicle,cols[i].radius);
                    // Source axle is X; flip the right side so the flange faces in.
                    if(cols[i].transform.localPosition.x>0)wheel.transform.localRotation=Quaternion.Euler(0,180,0);
                    if(vehicle=="RollerCoasterCartObject"){
                        var p0=root.transform.InverseTransformPoint(cols[i].transform.position);float x=Mathf.Sign(p0.x)*.405f;
                        Beam(art,"Upstop carrier vertical",new Vector3(x,.055f,p0.z),new Vector3(.035f,.38f,.075f));
                        Beam(art,"Upstop carrier bearing",new Vector3((x+p0.x)/2,-.11f,p0.z),new Vector3(Mathf.Abs(x-p0.x)+.05f,.04f,.075f));
                    }
                }
                foreach(int i in new[]{0,2}){
                    var p0=root.transform.InverseTransformPoint(cols[i].transform.position);
                    Beam(art,"Axle crossmember",new Vector3(0,p0.y,p0.z),new Vector3(.60f,.055f,.055f));
                    if(vehicle!="RollerCoasterCartObject")foreach(int side in new[]{-1,1})Beam(art,"Attached axle bearing",new Vector3(side*.22f,(p0.y+.41f)/2,p0.z),new Vector3(.055f,.41f-p0.y,.09f));
                }
                var attachment=Node(art,"VehicleAnchors",Vector3.zero);
                foreach(var c in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="RailCouplerFront"||t.name=="RailCouplerRear").ToArray())
                    Node(attachment,c.name.Replace("RailCoupler","Coupler_"),root.transform.InverseTransformPoint(c.position));
                for(int i=0;i<4;i++)Node(attachment,"Wheel_"+new[]{"FL","FR","RL","RR"}[i],root.transform.InverseTransformPoint(cols[i].transform.position));
                if(vehicle=="HeritageTramObject"){
                    var mounts=root.GetComponent<Mountable>();var seats=mounts.seats.Where(s=>s.name.StartsWith("PassengerSeat")).ToArray();Require(seats.Length==6,"Tram must retain six passengers");
                    for(int i=0;i<6;i++){
                        int side=i<3?-1:1;float z=(i%3-1)*.62f;
                        // Central benches face the open sides. Native seated hip datum.
                        var p=new Vector3(side*.23f,.98f-RailRiderFit.SeatedHipHeight+RailRiderFit.TramRiderLift,z);
                        seats[i].transform.localPosition=p;seats[i].transform.localRotation=Quaternion.Euler(0,side*90,0);
                        Node(attachment,"PassengerSeat_"+(i+1),p).localRotation=seats[i].transform.localRotation;
                    }
                }
                if(vehicle=="WoodenMinecartObject")RailVehicleDesignBuilder.SingleLod(root);
                RailPreparedVehicleFit.Configure(root,art);
                RailVehiclePhysicsAssetBuilder.Configure(root);
                Require(root.GetComponentsInChildren<MeshCollider>(true).All(c=>c.sharedMesh==null||!AssetDatabase.GetAssetPath(c.sharedMesh).StartsWith(Art)),"Render mesh used as physics");
                ValidateMotion(root);
                PrefabUtility.SaveAsPrefabAsset(root,Candidates+"/"+vehicle+".prefab");
                if(shipping){PrefabUtility.SaveAsPrefabAsset(root,source);MinecartIconBuilder.Render(root,vehicle.Substring(0,vehicle.Length-6));}
                Render(root,vehicle);
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static void Render(GameObject root,string name)
        {
            var scene=EditorSceneManager.NewPreviewScene();var copy=Object.Instantiate(root);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy,scene);
            foreach(var c in copy.GetComponentsInChildren<Behaviour>(true))c.enabled=false;
            foreach(var g in copy.GetComponentsInChildren<LODGroup>(true))g.ForceLOD(0);
            var renderers=copy.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            var cg=new GameObject("Camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cg,scene);var camera=cg.AddComponent<Camera>();camera.scene=scene;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.18f,.20f);camera.orthographic=true;camera.orthographicSize=bounds.size.magnitude*.56f;
            var lg=new GameObject("Key");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lg,scene);var light=lg.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.35f;lg.transform.rotation=Quaternion.Euler(40,-35,0);
            var fill=new GameObject("Fill");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fill,scene);var fl=fill.AddComponent<Light>();fl.type=LightType.Directional;fl.intensity=.65f;fill.transform.rotation=Quaternion.Euler(25,130,0);
            Shader.EnableKeyword("NO_CURVE");Shader.SetGlobalVector("_CurveAxisMask",Vector4.zero);
            Directory.CreateDirectory(Output);
            foreach(var view in new[]{"front","rear","side"}){
                var direction=view=="front"?new Vector3(1,.55f,1):view=="rear"?new Vector3(-1,.55f,-1):new Vector3(1,.1f,0);
                camera.transform.position=bounds.center+direction.normalized*bounds.size.magnitude*3;camera.transform.LookAt(bounds.center);
                var rt=RenderTexture.GetTemporary(1200,900,24);camera.targetTexture=rt;camera.Render();var prior=RenderTexture.active;RenderTexture.active=rt;
                var image=new Texture2D(1200,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1200,900),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,name+"-"+view+".png"),image.EncodeToPNG());
                Object.DestroyImmediate(image);RenderTexture.active=prior;camera.targetTexture=null;RenderTexture.ReleaseTemporary(rt);
            }
            EditorSceneManager.ClosePreviewScene(scene);
        }
        static void RaiseCoachRoof(GameObject root,Transform art,string key)
        {
            int lod=0;
            foreach(var filter in art.Find("PreparedPart_"+key).GetComponentsInChildren<MeshFilter>()){
                var mesh=Object.Instantiate(filter.sharedMesh);var vertices=mesh.vertices;
                for(int i=0;i<vertices.Length;i++){
                    var p=root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                    p.y+=.35f*Mathf.Clamp01((p.y-1.15f)/.8f);
                    vertices[i]=filter.transform.InverseTransformPoint(root.transform.TransformPoint(p));
                }
                mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
                string path=Candidates+"/"+root.name+"-tall-cabin-lod"+lod+++".asset";
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing==null)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}filter.sharedMesh=mesh;
            }
            var vehicle=root.GetComponent<Vehicle>();var size=vehicle.size;
            size.y=art.Find("PreparedPart_"+key).GetComponentsInChildren<MeshFilter>()
                .SelectMany(f=>f.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(f.transform.TransformPoint(v)).y)).Max()+.10f;
            vehicle.size=size;
            art.Find("PreparedPart_"+key).GetComponent<LODGroup>().RecalculateBounds();
        }
        public static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);Directory.CreateDirectory(Candidates);
            assets=Directory.GetFiles(Art,"unity.json",SearchOption.AllDirectories).Select(p=>JsonUtility.FromJson<Asset>(File.ReadAllText(p))).ToDictionary(a=>a.key);
            Require(assets.Count==20,"Expected all twenty source assets");
            references=JsonUtility.FromJson<References>(File.ReadAllText(Path.Combine(Repo,"art/Meshy-Vehicle-References-2026-10-07-v1/part-manifest.json")));
            foreach(var a in assets.Values){var obj=PartObject(a.key);try{PrefabUtility.SaveAsPrefabAsset(obj,Art+"/"+a.key+"/"+a.key+".prefab");}finally{Object.DestroyImmediate(obj);}}
            foreach(var pair in Bodies)Candidate(pair.Key,pair.Value);
            AssetDatabase.SaveAssets();
            var receipt=new Receipt{boundary="Offline native prefab authoring and renders. Production prefabs and installed game unchanged; live acceptance pending.",parts=assets.Count,vehicles=Bodies.Count,prefabs=Bodies.Keys.ToArray()};
            File.WriteAllText(Path.Combine(Output,"receipt.json"),JsonUtility.ToJson(receipt,true));
            // Import into the Railworks authoring project, which supplies native
            // Eco components and the existing animation controllers. Do not pack
            // proprietary game SDK dependencies into an artwork library.
            AssetDatabase.ExportPackage(new[]{Art,Candidates},Path.Combine(Repo,"art/Railworks-Vehicle-Library-2026-10-07/Railworks-Vehicle-Candidates.unitypackage"),ExportPackageOptions.Recurse);
            Debug.Log("RAILWORKS_VEHICLE_LIBRARY_OK: 20 prepared parts, 14 native vehicle candidates; production unchanged");
        }
        public static void Apply()
        {
            RailVehicleDesignBuilder.Prepare();
            Directory.CreateDirectory(Candidates);
            assets=Directory.GetFiles(Art,"unity.json",SearchOption.AllDirectories).Select(p=>JsonUtility.FromJson<Asset>(File.ReadAllText(p))).ToDictionary(a=>a.key);
            Require(assets.Count==20,"The complete prepared library is required for a release build");
            references=JsonUtility.FromJson<References>(File.ReadAllText(Path.Combine(Repo,"art/Meshy-Vehicle-References-2026-10-07-v1/part-manifest.json")));
            foreach(var a in assets.Values){var obj=PartObject(a.key);try{PrefabUtility.SaveAsPrefabAsset(obj,Art+"/"+a.key+"/"+a.key+".prefab");}finally{Object.DestroyImmediate(obj);}}
            foreach(var pair in Bodies)Candidate(pair.Key,pair.Value,true);
            AssetDatabase.SaveAssets();
            Debug.Log("RAILWORKS_MODELS_INTEGRATED: fourteen existing vehicle prefab identities, shared motion and native mounts");
        }
        public static void ApplyCoasterSeatFit()
        {
            Directory.CreateDirectory(Candidates);
            assets=Directory.GetFiles(Art,"unity.json",SearchOption.AllDirectories).Select(p=>JsonUtility.FromJson<Asset>(File.ReadAllText(p))).ToDictionary(a=>a.key);
            references=JsonUtility.FromJson<References>(File.ReadAllText(Path.Combine(Repo,"art/Meshy-Vehicle-References-2026-10-07-v1/part-manifest.json")));
            Candidate("RollerCoasterCartObject","CoasterBody",true);AssetDatabase.SaveAssets();
        }
        public static void ApplyHandcarFit()
        {
            Directory.CreateDirectory(Candidates);
            assets=Directory.GetFiles(Art,"unity.json",SearchOption.AllDirectories).Select(p=>JsonUtility.FromJson<Asset>(File.ReadAllText(p))).ToDictionary(a=>a.key);
            references=JsonUtility.FromJson<References>(File.ReadAllText(Path.Combine(Repo,"art/Meshy-Vehicle-References-2026-10-07-v1/part-manifest.json")));
            Candidate("RailroadHandcarObject","HandcarBody",true);AssetDatabase.SaveAssets();
        }
        public static void VerifyCandidates()
        {
            foreach(var pair in Bodies){
                var root=PrefabUtility.LoadPrefabContents(Candidates+"/"+pair.Key+".prefab");
                try{ValidateMotion(root);}finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            Debug.Log("RAILWORKS_CANDIDATE_MOTION_OK: all fourteen native candidates; offline animation sampling only");
        }
        public static void FinishCandidates()
        {
            var material=Fittings();
            foreach(var pair in Bodies){
                var path=Candidates+"/"+pair.Key+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
                try{
                    var art=root.transform.Find("PreparedVehicleArt");
                    foreach(var renderer in art.GetComponentsInChildren<MeshRenderer>(true))
                        if(renderer.sharedMaterial!=null&&renderer.sharedMaterial.name=="MAT_IronBare")renderer.sharedMaterial=material;
                    ValidateMotion(root);PrefabUtility.SaveAsPrefabAsset(root,path);Render(root,pair.Key);
                }finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.ExportPackage(new[]{Art,Candidates},Path.Combine(Repo,"art/Railworks-Vehicle-Library-2026-10-07/Railworks-Vehicle-Candidates.unitypackage"),ExportPackageOptions.Recurse);
            Debug.Log("RAILWORKS_CANDIDATES_FINISHED: 14 render and motion checks; offline only");
        }
    }
}
