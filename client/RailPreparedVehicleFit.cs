using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    // Authoring only: no custom client scripts, no second movement authority.
    public static class RailPreparedVehicleFit
    {
        const string Folder="Assets/EcoMinecarts/Animations";
        static Transform Node(Transform p,string name,Vector3 pos)
        {var t=new GameObject(name).transform;t.SetParent(p,false);t.localPosition=pos;return t;}
        static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/"+name+".mat");
        static Material Finish(string name,Color color,float metallic)
        {
            string path="Assets/EcoMinecarts/VehicleCandidates/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(Shader.Find(RailVehiclePaintBuilder.ShaderName));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_Color",color);material.SetFloat("_Metallic",metallic);material.SetFloat("_Glossiness",.22f);material.enableInstancing=true;EditorUtility.SetDirty(material);return material;
        }
        static Transform Solid(Transform p,string name,PrimitiveType type,Vector3 pos,Vector3 size,Material mat)
        {
            var o=GameObject.CreatePrimitive(type);o.name=name;o.transform.SetParent(p,false);o.transform.localPosition=pos;o.transform.localScale=size;
            Object.DestroyImmediate(o.GetComponent<Collider>());o.GetComponent<Renderer>().sharedMaterial=mat;return o.transform;
        }
        // Each state holds a physical pose. Native transitions interpolate from
        // the current pose, including interrupted closes and late-join updates.
        static void Poses(GameObject root,Transform host,string network, string[] names,float[] angles,float seconds,string axis="x",string binding="Pivot",string otherBinding=null)
        {
            var path=Folder+"/"+root.name+network+".controller";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path)??AnimatorController.CreateAnimatorControllerAtPath(path);
            if(controller.layers.Length==0)controller.AddLayer("Base Layer");
            var machine=controller.layers[0].stateMachine;
            foreach(var t in machine.anyStateTransitions)machine.RemoveAnyStateTransition(t);
            for(int i=0;i<names.Length;i++){
                var clipPath=Folder+"/"+root.name+network+names[i]+".anim";
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,clipPath);}
                foreach(var curve in AnimationUtility.GetCurveBindings(clip))AnimationUtility.SetEditorCurve(clip,curve,null);
                clip.SetCurve(binding,typeof(Transform),"localEulerAnglesRaw."+axis,AnimationCurve.Constant(0,1,angles[i]));
                if(otherBinding!=null)clip.SetCurve(otherBinding,typeof(Transform),"localEulerAnglesRaw."+axis,AnimationCurve.Constant(0,1,angles[i]));
                var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==names[i])??machine.AddState(names[i]);state.motion=clip;state.speed=0;state.writeDefaultValues=false;
                if(i==0)machine.defaultState=state;
                if(!controller.parameters.Any(p=>p.name==names[i]))controller.AddParameter(names[i],AnimatorControllerParameterType.Trigger);
                var transition=machine.AddAnyStateTransition(state);transition.hasExitTime=false;transition.hasFixedDuration=true;transition.duration=seconds;transition.canTransitionToSelf=true;
                transition.interruptionSource=TransitionInterruptionSource.SourceThenDestination;transition.AddCondition(AnimatorConditionMode.If,0,names[i]);EditorUtility.SetDirty(clip);
                // Replicated snapshots have equal priority. A later close must
                // be able to interrupt an earlier open regardless of list order.
                transition.orderedInterruption=false;
            }
            var animator=host.GetComponent<Animator>();if(animator==null)animator=host.gameObject.AddComponent<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var world=root.GetComponent<WorldObject>();int slot=RailWheelAnimationBuilder.StringIndex(world,network);
            // Multiple restraints share one message, with independent hinges.
            // Native triggers otherwise queue an old default-state request and
            // can replay it after a later close/reverse. The newest snapshot wins.
            foreach(var name in names)UnityEventTools.AddStringPersistentListener(world.OnStringStateChanged[slot],animator.ResetTrigger,name);
            UnityEventTools.AddPersistentListener(world.OnStringStateChanged[slot],animator.SetTrigger);EditorUtility.SetDirty(controller);
        }
        static void Seat(GameObject root,MountSpot seat,Vector3 surface,float yaw)
        {
            // Prepared art replaced the old bench-fitting path. Keep the rider
            // lift here too, rather than overwriting it with the nominal datum.
            seat.transform.position=root.transform.TransformPoint(surface+Vector3.up*(PassengerLift(root)-RailRiderFit.SeatedHipHeight));
            seat.transform.rotation=root.transform.rotation*Quaternion.Euler(0,yaw,0);
            seat.setAsParent=true;seat.cameraTarget=null;seat.overrideAvatarState=(Eco.Animation.AnimationStateManager.AvatarState)3;
            var marker=Node(root.transform.Find("PreparedVehicleArt"),"SeatSurface_"+seat.name,surface);marker.localRotation=Quaternion.Euler(0,yaw,0);
            // Keep the existing seat index parameter on the clickable target.
            var index=Array.IndexOf(root.GetComponent<Mountable>().seats,seat)-1;
            var target=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="PassengerSeatTarget"+index);
            if(target!=null){target.position=root.transform.TransformPoint(surface+Vector3.up*.08f);target.rotation=seat.transform.rotation;}
        }
        public static void Configure(GameObject root,Transform art)
        {
            var mounts=root.GetComponent<Mountable>();mounts.freezeWhenUnmounted=false;
            if(root.name=="RollerCoasterCartObject"){
                var world=root.GetComponent<WorldObject>();int slot=RailWheelAnimationBuilder.StringIndex(world,"RailRestraintPose");world.OnStringStateChanged[slot]=new ChangedStringStateEvent();
                var chairs=art.Cast<Transform>().Where(t=>t.name=="PreparedPart_CoasterSeat").OrderBy(t=>t.localPosition.x).ToArray();
                if(chairs.Length!=2)throw new InvalidOperationException("Expected two authored coaster chairs");
                int i=0;foreach(var seat in mounts.seats.Skip(1)){
                    var chair=chairs[i++];var cushion=chair.Find("Anchors/PassengerSeat");
                    Seat(root,seat,root.transform.InverseTransformPoint(cushion.position),0);
                }
                var host=Node(art,"RestraintAnimation",Vector3.zero);
                foreach(var pivot in art.Cast<Transform>().Where(t=>t.name.StartsWith("RestraintPivot_")).ToArray()){
                    var side=pivot.name.EndsWith("Left")?"Left":"Right";pivot.SetParent(host,true);pivot.name=side+"Pivot";pivot.localRotation=Quaternion.Euler(-105,0,0);
                }
                // Eco registers every Animator trigger as a world-object event.
                // Two controllers with Boarding/Secured throw on registration.
                Poses(root,host,"RailRestraintPose",new[]{"Boarding","Secured"},new[]{-105f,-30f},.85f,binding:"LeftPivot",otherBinding:"RightPivot");
                // Old seat/restraint boxes belonged to the larger procedural
                // chairs. Retain body/underframe collision, not phantom chairs.
                foreach(var c in root.GetComponentsInChildren<Collider>(true).Where(c=>c.name.Contains("Seat")&&c.GetComponent<SpecificInteractable>()==null&&c.GetComponent<ColliderPlacementOptions>()==null))c.enabled=false;
            }
            else if(root.name=="HeritageTramObject"){
                int i=0;foreach(var seat in mounts.seats.Skip(1)){
                    int side=i<3?-1:1;Seat(root,seat,new Vector3(side*.23f,.995f,(i%3-1)*.62f),side*90);i++;
                }
                // Deck collider follows the rebuilt wooden floor, not old seats.
                var deck=root.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Passenger deck");var p=deck.localPosition;p.y=.61f;deck.localPosition=p;
                foreach(var c in root.GetComponentsInChildren<Collider>(true).Where(c=>c.name=="Passenger bench"))c.enabled=false;
            }
            else if(root.name=="PassengerCarObject"||root.name=="LargePassengerCarObject"){
                bool large=root.name=="LargePassengerCarObject";int i=0;int rows=(mounts.seats.Length-1)/2;
                foreach(var seat in mounts.seats.Skip(1)){
                    int side=i%2==0?-1:1;float z=rows==1?0:Mathf.Lerp(large?-1.45f:-.48f,large?1.45f:.48f,(i/2)/(float)(rows-1));
                    Seat(root,seat,new Vector3(side*(large?.69f:.29f),1.044f,z+(large?0:side*.16f)),-side*90);i++;
                }
                var deck=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Passenger deck");if(deck!=null){var p=deck.localPosition;p.y=.7045f;deck.localPosition=p;}
                foreach(var c in root.GetComponentsInChildren<Collider>(true).Where(c=>c.name=="Passenger bench"))c.enabled=false;
            }
            if(RailMechanicalAnimationBuilder.Engines.Contains(root.name))Cab(root,art);
            RailRiderInteractionAssetBuilder.Configure(root,root.transform.Find("CabFittings")!=null);
            root.GetComponent<Vehicle>().AllVehicleColliders=root.GetComponentsInChildren<Collider>(true).Where(c=>c!=null).ToArray();
            Verify(root);
        }
        static void Cab(GameObject root,Transform art)
        {
            var cab=root.transform.Find("CabFittings");var deck=cab.Find("Cab floor");float floor=deck.localPosition.y+.035f;
            float front=deck.localPosition.z+deck.localScale.z/2;
            // Cut away the generated rear cabin above the deck. The playable
            // cab retains verified entry/standing clearance and native targets.
            var body=art.GetComponentsInChildren<LODGroup>().First();
            foreach(var filter in body.GetComponentsInChildren<MeshFilter>()){
                // Imported chassis stays below the collision deck's underside.
                // Leaving it 25 mm above the walking surface caused overlap.
                var mesh=CutCabMesh(root,filter,front+.035f,floor-.08f,deck.localPosition.z-deck.localScale.z/2-.045f);
                string path="Assets/EcoMinecarts/VehicleCandidates/"+root.name+"-cab-cut-"+filter.name+".asset";
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing==null)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}filter.sharedMesh=mesh;
            }
            // Preserve both native running-state emitters, but put their origin
            // on the replacement chimney rather than the old procedural stack.
            var visibleBody=body.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Render_LOD0");
            var points=visibleBody.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(visibleBody.transform.TransformPoint(v))).ToArray();
            float nose=points.Max(v=>v.z);
            var stack=points.Where(v=>Mathf.Abs(v.x)<.22f&&v.z>Mathf.Lerp(front,nose,.4f)).ToArray();
            float top=stack.Max(v=>v.y);var lip=stack.Where(v=>v.y>top-.025f).ToArray();
            var smoke=new Vector3(lip.Average(v=>v.x),top+.025f,lip.Average(v=>v.z));
            foreach(var name in new[]{"SteamExhaust","AutomaticSteamExhaust"}){var fx=root.transform.Find(name);if(fx!=null)fx.localPosition=smoke;}
            var detail=Node(cab,"PreparedCabDetail",Vector3.zero);
            var brass=Finish("PreparedBrass",new Color(.43f,.32f,.15f),.65f);
            var steel=Finish("PreparedCabIron",new Color(.10f,.12f,.11f),.45f);
            var padding=Finish("PreparedCabCushion",new Color(.10f,.15f,.15f),0);
            var green=Finish("PreparedCabGreen",new Color(.14f,.23f,.16f),.18f);
            var door=Finish("PreparedCabGreenDoor",new Color(.28f,.46f,.32f),.18f);
            var wood=Mat("MAT_WoodRail");
            door.SetTexture("_MainTex",wood.GetTexture("_MainTex"));
            door.SetTextureScale("_MainTex",wood.GetTextureScale("_MainTex"));
            door.SetTextureOffset("_MainTex",wood.GetTextureOffset("_MainTex"));
            // The retired procedural renderer cannot supply the roof during a
            // repeated export. Author this roof with the current cab assembly.
            RailVehicleDetail.ArchedRoof(detail,"Prepared cab roof",new Vector3(0,floor+2.15f,deck.localPosition.z),deck.localScale.x+.20f,deck.localScale.z+.20f,.08f,steel);
            if(root.name=="MineTrainObject")
            {
                // Join the textured smokebox to the playable cab backhead.
                // The generated cab consumes most of the source model's length;
                // its cut must not leave a thin disk in place of a steam boiler.
                float axis=floor+.68f;
                var barrel=Solid(detail,"Fitted green boiler barrel",PrimitiveType.Cylinder,new Vector3(0,axis,front+.315f),new Vector3(.71f,.25f,.71f),green);
                barrel.localRotation=Quaternion.Euler(90,0,0);
                foreach(float z in new[]{front+.20f,front+.53f})
                    Solid(detail,"Boiler barrel band",PrimitiveType.Cylinder,new Vector3(0,axis,z),new Vector3(.723f,.014f,.723f),steel).localRotation=barrel.localRotation;
            }
            var drive=root.GetComponent<RCCCarControllerV2>();float wheelbase=Mathf.Abs(drive.FrontLeftWheelCollider.transform.localPosition.z-drive.RearLeftWheelCollider.transform.localPosition.z);
            foreach(int side in new[]{-1,1}){
                var cylinder=Solid(detail,"Working steam cylinder",PrimitiveType.Cylinder,new Vector3(side*.371f,root.name=="MineTrainObject"?.26f:.29f,wheelbase*.36f),new Vector3(.18f,.15f,.18f),steel);cylinder.localRotation=Quaternion.Euler(90,0,0);
                Solid(detail,"Cylinder mounting saddle",PrimitiveType.Cube,new Vector3(side*.32f,.375f,wheelbase*.36f),new Vector3(.15f,.12f,.20f),steel);
            }
            foreach(var r in cab.GetComponentsInChildren<MeshRenderer>(true)){
                if(r.name.Contains("roof")||r.name.Contains("post")||r.name.Contains("shaft")||r.name.Contains("support")||r.name.Contains("rail"))r.sharedMaterial=steel;
                else if(r.name=="Cab rear panel"||r.name=="Cab console")r.sharedMaterial=green;
            }
            // Reuse the curved roof and fitted casing from the shared detail rig.
            var visibleCab=new[]{"Arched locomotive roof","Cab rear corner casing","Rear window sill","Rear window header","Rear panel lining","Cab grab rail","Grab rail bracket","Rain gutter","Rear centre mullion","Cab front apron","Front window sill","Front window mullion","Front window header","Firebox door frame","Firebox latch","Boarding tread"};
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>visibleCab.Contains(r.name))){r.enabled=true;r.sharedMaterial=r.name.Contains("sill")||r.name.Contains("gutter")||r.name.Contains("latch")?brass:r.name.Contains("roof")?steel:green;}
            // The prepared cab has its own complete roof. The procedural roof
            // also carries old trim and must not overlap this assembly.
            var curvedRoof=root.transform.Find("VehicleDetail/Roof");if(curvedRoof!=null)foreach(var r in curvedRoof.GetComponentsInChildren<MeshRenderer>())r.enabled=false;
            foreach(var renderer in cab.Find("Cab roof").GetComponentsInChildren<MeshRenderer>(true))renderer.enabled=false;
            foreach(var post in cab.Cast<Transform>().Where(t=>t.name=="Cab window post")){
                var p=post.localPosition;p.y=floor+1.075f;post.localPosition=p;
                var s=post.localScale;s.y=2.15f;post.localScale=s;
            }
            foreach(var gate in cab.Cast<Transform>().Where(t=>t.name=="Left travel gate"||t.name=="Right travel gate"))
                foreach(var renderer in gate.GetComponentsInChildren<MeshRenderer>(true))renderer.sharedMaterial=door;
            foreach(var step in cab.Cast<Transform>().Where(t=>t.name=="Boarding step"))
                foreach(var renderer in step.GetComponentsInChildren<MeshRenderer>(true))renderer.sharedMaterial=steel;
            var rearPanel=cab.Find("Cab rear panel");
            var panelSize=rearPanel.localScale;panelSize.x=deck.localScale.x-.025f;rearPanel.localScale=panelSize;
            float rear=deck.localPosition.z-deck.localScale.z/2,half=deck.localScale.x/2;
            foreach(int side in new[]{-1,1}){
                Solid(detail,"Rear window lining",PrimitiveType.Cube,new Vector3(side*half*.5f,floor+.85f,rear-.026f),new Vector3(half-.01f,.055f,.05f),brass);
                Solid(detail,"Rear header",PrimitiveType.Cube,new Vector3(side*half*.5f,floor+2.05f,rear-.026f),new Vector3(half-.01f,.075f,.05f),green);
                Solid(detail,"Cab corner casing",PrimitiveType.Cube,new Vector3(side*(half-.027f),floor+1.4075f,rear),new Vector3(.10f,1.475f,.07f),green);
            }
            Solid(detail,"Rear window mullion",PrimitiveType.Cube,new Vector3(0,floor+1.44f,rear),new Vector3(.055f,1.52f,.075f),green);
            Solid(detail,"Boiler backhead",PrimitiveType.Cube,new Vector3(0,floor+.62f,front+.07f),new Vector3(deck.localScale.x-.16f,1.24f,.14f),steel);
            var panel=cab.Find("Cab console");foreach(var renderer in panel.GetComponentsInChildren<MeshRenderer>(true)){renderer.enabled=true;renderer.sharedMaterial=green;}
            // Framed instrument panel bridges the boiler backhead and levers.
            Solid(detail,"Instrument fascia",PrimitiveType.Cube,new Vector3(0,floor+1.39f,front+.055f),new Vector3(deck.localScale.x-.20f,.26f,.055f),steel);
            foreach(float x in new[]{-.23f,0,.23f}){
                var rim=Solid(detail,"Gauge brass bezel",PrimitiveType.Cylinder,new Vector3(x,floor+1.40f,front+.015f),new Vector3(.14f,.015f,.14f),brass);rim.localRotation=Quaternion.Euler(90,0,0);
                var face=Solid(detail,"Gauge dial",PrimitiveType.Cylinder,new Vector3(x,floor+1.40f,front-.003f),new Vector3(.115f,.003f,.115f),Mat("MAT_VehicleCream")??brass);face.localRotation=rim.localRotation;
                Solid(detail,"Gauge needle",PrimitiveType.Cube,new Vector3(x+.014f,floor+1.41f,front-.008f),new Vector3(.005f,.04f,.003f),steel).localRotation=Quaternion.Euler(0,0,-35);
                for(int tick=0;tick<8;tick++){float a=(tick*30+60)*Mathf.Deg2Rad;Solid(detail,"Gauge tick",PrimitiveType.Cube,new Vector3(x+Mathf.Sin(a)*.046f,floor+1.40f+Mathf.Cos(a)*.046f,front-.009f),new Vector3(.004f,.009f,.003f),steel).localRotation=Quaternion.Euler(0,0,-a*Mathf.Rad2Deg);}
            }
            foreach(var name in new[]{"Throttle","Brake","Reverser"}){
                var old=cab.Find(name+"Animation");if(old!=null){foreach(var t in old.GetComponentsInChildren<Transform>().Where(t=>t.name==name+" shaft"||t.name==name+" grip").ToArray())t.SetParent(cab,true);Object.DestroyImmediate(old.gameObject);}
                var shaft=cab.Find(name+" shaft");var grip=cab.Find(name+" grip");
                // Rebuild from the shared cab's authored rest pose. A previous
                // export may have sampled the lever's neutral animation; keeping
                // that world pose here would apply the same tilt again each time.
                float x=deck.localScale.x*(name=="Throttle"?-.32f:name=="Brake"?-.11f:.11f);
                shaft.localPosition=new Vector3(x,floor+1.37f,front-.065f);
                grip.localPosition=new Vector3(x,floor+1.495f,front-.065f);
                shaft.localRotation=grip.localRotation=Quaternion.identity;
                var host=Node(cab,name+"Animation",shaft.localPosition-Vector3.up*.10f);var pivot=Node(host,"Pivot",Vector3.zero);shaft.SetParent(pivot,true);grip.SetParent(pivot,true);
                Solid(detail,name+" quadrant base",PrimitiveType.Cube,host.localPosition,new Vector3(.10f,.045f,.14f),brass);
                var world=root.GetComponent<WorldObject>();var key="Rail"+(name=="Reverser"?"Reverser":name)+"Pose";int stateIndex=RailWheelAnimationBuilder.StringIndex(world,key);world.OnStringStateChanged[stateIndex]=new ChangedStringStateEvent();
                if(name=="Throttle")Poses(root,host,key,Enumerable.Range(0,11).Select(n=>"Throttle"+n).ToArray(),Enumerable.Range(0,11).Select(n=>-25f+n*5).ToArray(),.2f);
                else if(name=="Brake")Poses(root,host,key,new[]{"BrakeOff","BrakeOn"},new[]{-20f,25f},.2f);
                else Poses(root,host,key,new[]{"Forward","Reverse"},new[]{-25f,25f},.2f);
            }
            // Preserve the proven operator cushion/mount relationship, with an
            // explicit surface marker so future model edits are checked.
            var seat=root.GetComponent<Mountable>().seats[1];var cushion=cab.Find("Operator cushion");foreach(var renderer in cushion.GetComponentsInChildren<MeshRenderer>(true))renderer.sharedMaterial=padding;
            Seat(root,seat,new Vector3(cushion.localPosition.x,cushion.localPosition.y+.04f,cushion.localPosition.z),0);
            BatchStatic(root,detail);
        }
        static void BatchStatic(GameObject root,Transform detail)
        {
            var filters=detail.GetComponentsInChildren<MeshFilter>();int i=0;
            foreach(var set in filters.GroupBy(f=>f.GetComponent<Renderer>().sharedMaterial)){
                var mesh=new Mesh();mesh.CombineMeshes(set.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=detail.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
                var path="Assets/EcoMinecarts/VehicleCandidates/"+root.name+"-Console-"+i+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);}
                var t=Node(detail,"Console finish "+i++,Vector3.zero);t.gameObject.AddComponent<MeshFilter>().sharedMesh=saved;t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=set.Key;
            }
            foreach(var f in filters)Object.DestroyImmediate(f.gameObject);
        }
        struct V {public Vector3 p,n;public Vector2 uv;public V(Vector3 p,Vector3 n,Vector2 uv){this.p=p;this.n=n;this.uv=uv;}}
        static Mesh CutCabMesh(GameObject root,MeshFilter filter,float front,float floor,float rear)
        {
            var source=filter.sharedMesh;var points=source.vertices;var normals=source.normals;var uv=source.uv;var indices=source.triangles;
            if(root.name=="MineTrainObject")
            {
                // The generated rear cabin is replaced by the playable cab.
                // Map its boiler section into the available nose space before
                // clipping, instead of chopping the boiler at the cab console.
                float sourceCab=root.transform.InverseTransformPoint(filter.transform.parent.position).z-.15f*filter.transform.lossyScale.z;
                var rootPoints=points.Select(v=>root.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
                float min=rootPoints.Min(v=>v.z),max=rootPoints.Max(v=>v.z);
                for(int i=0;i<points.Length;i++){
                    var p=rootPoints[i];p.z=p.z>=sourceCab?Mathf.Lerp(front,max,Mathf.InverseLerp(sourceCab,max,p.z))
                        :Mathf.Lerp(rear,front,Mathf.InverseLerp(min,sourceCab,p.z));
                    points[i]=filter.transform.InverseTransformPoint(root.transform.TransformPoint(p));
                }
            }
            var vertices=new List<Vector3>();var ns=new List<Vector3>();var uvs=new List<Vector2>();var output=new List<int>();
            Vector3 Position(V v)=>root.transform.InverseTransformPoint(filter.transform.TransformPoint(v.p));
            List<V> Clip(List<V> poly,Func<V,float> distance){
                var result=new List<V>();if(poly.Count==0)return result;var a=poly[poly.Count-1];float da=distance(a);
                foreach(var b in poly){float db=distance(b);if((da>=0)!=(db>=0)){float t=da/(da-db);result.Add(new V(Vector3.Lerp(a.p,b.p,t),Vector3.Lerp(a.n,b.n,t).normalized,Vector2.Lerp(a.uv,b.uv,t)));}if(db>=0)result.Add(b);a=b;da=db;}return result;
            }
            void Add(List<V> p){for(int i=1;i+1<p.Count;i++){if(Vector3.Cross(p[i].p-p[0].p,p[i+1].p-p[0].p).sqrMagnitude<1e-16f)continue;foreach(var v in new[]{p[0],p[i],p[i+1]}){output.Add(vertices.Count);vertices.Add(v.p);ns.Add(v.n);uvs.Add(v.uv);}}}
            for(int i=0;i<indices.Length;i+=3){var poly=new List<V>();for(int j=0;j<3;j++){int k=indices[i+j];poly.Add(new V(points[k],normals[k],uv[k]));}
                poly=Clip(poly,v=>Position(v).z-rear);
                Add(Clip(poly,v=>Position(v).z-front));Add(Clip(Clip(poly,v=>front-Position(v).z),v=>floor-Position(v).y));}
            var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(ns);mesh.SetUVs(0,uvs);mesh.SetColors(Enumerable.Repeat(new Color32(255,255,255,255),vertices.Count).ToArray());mesh.SetTriangles(output,0);mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
        }
        public static void Verify(GameObject root)
        {
            var mount=root.GetComponent<Mountable>();if(mount.freezeWhenUnmounted)throw new Exception("Passenger exit freezes vehicle "+root.name);
            foreach(var seat in mount.seats.Skip(1)){
                if(!seat.setAsParent||seat.exitPosition==null||seat.alternativeExitPosition==null)throw new Exception("Missing parent/exit "+root.name+seat.name);
                var marker=root.transform.Find("PreparedVehicleArt/SeatSurface_"+seat.name);
                if(marker!=null&&Vector3.Distance(seat.transform.position+root.transform.up*(RailRiderFit.SeatedHipHeight-PassengerLift(root)),marker.position)>.002f)throw new Exception("Seat surface fit "+root.name+seat.name);
            }
            RailRiderInteractionAssetBuilder.Verify(root,root.transform.Find("CabFittings")!=null);
            var eventNames=new HashSet<string>(root.GetComponent<WorldObject>().Events);
            foreach(var animator in root.GetComponentsInChildren<Animator>(true))
                foreach(var parameter in animator.parameters.Where(p=>p.type==AnimatorControllerParameterType.Trigger))
                    if(!eventNames.Add(parameter.name))throw new Exception("Duplicate native Animator event "+root.name+": "+parameter.name);
            Debug.Log("PREPARED_VEHICLE_FIT_OK: "+root.name+" native mount parenting, explicit seat surfaces and interaction exemptions; live avatar acceptance pending");
        }
        public static float PassengerLift(GameObject root)=>root.name=="HeritageTramObject"?.25f:
            root.name=="PassengerCarObject"||root.name=="LargePassengerCarObject"?.40f:root.name=="RollerCoasterCartObject"?.20f:0;
    }
}
