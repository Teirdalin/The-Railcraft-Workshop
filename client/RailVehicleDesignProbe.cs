using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace EcoMinecarts.Editor
{
    public static class RailVehicleDesignProbe
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        public static void Select(GameObject root,bool modern)
        {
            var world=root.GetComponent<WorldObject>();int flag=Array.IndexOf(world.States,"RailLegacyDesign");
            var callback=modern?world.OnStateDisabledEvents[flag]:world.OnStateEnabledEvents[flag];
            for(int i=0;i<callback.GetPersistentEventCount();i++)callback.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            var evt=world.OnStringStateChanged[Array.IndexOf(world.StringStates,"RailDesignPose")];
            for(int i=0;i<evt.GetPersistentEventCount();i++)evt.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            callback.Invoke();evt.Invoke(modern?"ModernDesign":"LegacyDesign");var animator=root.GetComponent<Animator>();animator.Update(0);animator.Update(.02f);animator.Update(1.1f);
        }
        internal static void Mechanisms(GameObject root,bool legacy)
        {
            var world=root.GetComponent<WorldObject>();
            void Pose(string key,string value){
                int slot=Array.IndexOf(world.StringStates,key);var evt=world.OnStringStateChanged[slot];
                for(int i=0;i<evt.GetPersistentEventCount();i++)evt.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                var animators=Enumerable.Range(0,evt.GetPersistentEventCount()).Where(i=>evt.GetPersistentMethodName(i)=="SetTrigger").Select(i=>(Animator)evt.GetPersistentTarget(i)).Distinct().ToArray();
                evt.Invoke(value);foreach(var animator in animators){animator.Update(0);animator.Update(.02f);animator.Update(1.1f);}
            }
            if(root.name=="RollerCoasterCartObject"){
                Pose("RailRestraintPose","Secured");
                var pivot=root.transform.Find("PreparedVehicleArt/RestraintAnimation/LeftPivot");var closed=pivot.rotation;
                var grips=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name.StartsWith("Seat restraint grip")).ToArray();
                if(legacy)Check(grips.Length==2&&grips.All(r=>r.gameObject.activeInHierarchy&&r.transform.parent.parent==pivot||r.gameObject.activeInHierarchy&&r.transform.parent.parent.name=="RightPivot"),"Original restraints not on shared hinges");
                bool visibleOriginal=pivot.Find(OriginalCoasterRestraintBuilder.AssemblyPrefix+"Left")!=null;
                Pose("RailRestraintPose","Boarding");Check(Quaternion.Angle(closed,pivot.rotation)>(visibleOriginal?29:70),"Design lacks opening restraint "+root.name+legacy);
            }
            if(RailDumpAnimationBuilder.Buckets.Contains(root.name)){
                var wheels=root.GetComponentsInChildren<WheelCollider>(true).Select(w=>w.transform.position).ToArray();
                Pose("RailDumpPose","DumpRight28");var bucket=root.transform.Find("DumpAnimation/DumpHinge/Bucket");Check(Mathf.Abs(Vector3.Angle(root.transform.up,bucket.up)-70)<.3f,"Design lacks dumping motion "+root.name+legacy);
                Check(root.GetComponentsInChildren<WheelCollider>(true).Select(w=>w.transform.position).SequenceEqual(wheels),"Dump moved fixed wheels "+root.name);
                Pose("RailDumpPose","DumpRight00");Check(Vector3.Angle(root.transform.up,bucket.up)<.3f,"Dump failed to return "+root.name+legacy);
            }
            if(RailMechanicalAnimationBuilder.Engines.Contains(root.name)){
                foreach(var mode in new[]{"BrakeOn","BrakeOff"})Pose("RailBrakePose",mode);
                foreach(var mode in new[]{"Throttle10","Throttle0"})Pose("RailThrottlePose",mode);
                foreach(var mode in new[]{"Reverse","Forward"})Pose("RailReverserPose",mode);
            }
            if(root.name=="RailroadHandcarObject"){
                var seat=root.GetComponent<Mountable>().seats[0];
                Check(Mathf.Abs(seat.transform.localPosition.y-(legacy?.61f:.69f))<.001f
                    &&Mathf.Abs(seat.transform.localPosition.z-(legacy?-.69f:-.91f))<.001f
                    &&seat.IKTargets.All(t=>Mathf.Abs(t.transform.localPosition.y-(legacy?.12f:.067f))<.001f),"Handcar rider and hand fit lost "+legacy);
                var host=root.transform.Find("Handcar pump animation");var animator=host.GetComponent<Animator>();var pivot=host.Find("PumpPivot");
                Pose("RailPumpPose","PumpOn");var before=pivot.rotation;animator.Update(.23f);
                Check(Quaternion.Angle(before,pivot.rotation)>3,"Handcar pump does not animate "+legacy);
                Pose("RailPumpPose","PumpOff");before=pivot.rotation;animator.Update(.25f);
                Check(Quaternion.Angle(before,pivot.rotation)<.05f&&animator.enabled,"Handcar does not park or disables shared design controller "+legacy);
            }
        }
        public static void Verify(GameObject[] prefabs)
        {
            int count=0;
            foreach(var prefab in prefabs.Where(p=>p.GetComponent<Vehicle>()!=null))
            {
                var root=UnityEngine.Object.Instantiate(prefab);root.name=prefab.name;
                var original=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/LegacyVehicleSources/"+root.name+".prefab"));original.name=root.name;
                RailPresentationPolish.Original(original);
                try
                {
                    root.SetActive(true);var animator=root.GetComponent<Animator>();Check(animator!=null,"Missing design selector "+root.name);
                    animator.Rebind();animator.Update(0);
                    var world=root.GetComponent<WorldObject>();var evt=world.OnStringStateChanged[Array.IndexOf(world.StringStates,"RailDesignPose")];
                    for(int i=0;i<evt.GetPersistentEventCount();i++)evt.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                    int flag=Array.IndexOf(world.States,"RailLegacyDesign");
                    foreach(var callback in new[]{world.OnStateEnabledEvents[flag],world.OnStateDisabledEvents[flag]})for(int i=0;i<callback.GetPersistentEventCount();i++)callback.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                    var cab=root.transform.Find("CabFittings");
                    var meshNodes=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.transform!=root.transform&&(cab==null||!r.transform.IsChildOf(cab))).ToArray();
                    bool Legacy(Transform t){for(var p=t;p!=null&&p!=root.transform;p=p.parent)if(p.name.StartsWith("LegacyDesignGeometry"))return true;return false;}
                    var oldMeshes=meshNodes.Where(r=>Legacy(r.transform)).ToArray();
                    Check(oldMeshes.Length>0,"Missing legacy meshes "+root.name);
                    Check(!root.GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("Dump hinge bearing ")),"Unwanted dump bearing decorations remain");
                    if(root.name=="HeritageTramObject")Check(root.GetComponentsInChildren<MeshRenderer>(true).Count(r=>r.name=="Centre chair cushion")==2,"Tram needs two physical middle chairs");
                    var oldSeats=original.GetComponent<Mountable>().seats;var seats=root.GetComponent<Mountable>().seats;
                    Check(oldMeshes.All(r=>r.gameObject.activeInHierarchy),"Fresh prefab does not default to originals "+root.name);
                    Select(root,true);
                    var modernSeats=seats.Select(s=>s.transform.position-root.transform.position).ToArray();
                    var colliders=root.GetComponentsInChildren<BoxCollider>(true);
                    var modernCollision=colliders.Select(c=>(c.transform.localPosition,c.transform.localRotation,c.transform.localScale,c.center,c.size,c.enabled)).ToArray();
                    var controllers=root.GetComponentsInChildren<Vehicle>(true).Length;var bodies=root.GetComponentsInChildren<Rigidbody>(true).Length;
                    foreach(var mode in new[]{"LegacyDesign","ModernDesign","LegacyDesign","ModernDesign"}){
                        bool legacy=mode=="LegacyDesign";
                        (legacy?world.OnStateEnabledEvents[flag]:world.OnStateDisabledEvents[flag]).Invoke();
                        evt.Invoke(mode);animator.Update(0);animator.Update(.02f);animator.Update(1.1f);
                        // Nested wheel Animators and LOD evaluation must not
                        // reactivate the other design while it is in motion.
                        var rolling=root.GetComponentsInChildren<Animator>(true).Where(a=>a.name.StartsWith("WheelRoll_")||a.name.StartsWith("UpstopRoll_")).ToArray();
                        foreach(var a in rolling){a.enabled=true;a.speed=2;a.Play("RollForward",0,.1f);a.Update(.13f);}
                        foreach(var group in root.GetComponentsInChildren<LODGroup>(true))for(int i=0;i<group.lodCount;i++)group.ForceLOD(i);
                        Check(oldMeshes.All(r=>r.gameObject.activeInHierarchy==legacy),"Wrong legacy visibility "+root.name+mode+" nodes "+string.Join(",",oldMeshes.Where(r=>r.gameObject.activeInHierarchy!=legacy).Take(4).Select(r=>AnimationUtility.CalculateTransformPath(r.transform,root.transform))));
                        foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name=="Render_LOD0"&&!Legacy(r.transform)))Check(r.gameObject.activeInHierarchy!=legacy,"Modern LOD remains visible "+root.name+mode);
                        for(int i=0;i<seats.Length;i++){
                            var expected=legacy?oldSeats[i].transform.position-original.transform.position:modernSeats[i];
                            Check(Vector3.Distance(seats[i].transform.position-root.transform.position,expected)<.004f,"Design-specific seat fit "+root.name+mode+" seat "+i);
                            if(legacy){
                                Check(Quaternion.Angle(seats[i].transform.rotation,oldSeats[i].transform.rotation)<.1f,"Legacy mount orientation "+root.name);
                                foreach(var pair in new[]{(seats[i].exitPosition,oldSeats[i].exitPosition),(seats[i].alternativeExitPosition,oldSeats[i].alternativeExitPosition)})if(pair.Item1!=null&&pair.Item2!=null)Check(Vector3.Distance(pair.Item1.position-root.transform.position,pair.Item2.position-original.transform.position)<.004f,"Legacy dismount fit "+root.name);
                            }
                        }
                        for(int i=0;i<colliders.Length;i++){
                            var c=colliders[i];var archived=RailVehicleDesignBuilder.OriginalCollider(original,c,root.transform);
                            if(legacy){
                                if(archived==null){Check(!c.enabled,"Modern-only collider left active "+root.name+c.name);continue;}
                                Check(c.enabled==archived.enabled&&c.isTrigger==archived.isTrigger&&Vector3.Distance(c.size,archived.size)<.002f&&Vector3.Distance(c.center,archived.center)<.002f,"Legacy collision dimensions "+root.name+c.name);
                                if(!RailVehicleDesignBuilder.AnimatedControl(c.transform,root.transform))Check(Vector3.Distance(c.transform.position-root.transform.position,archived.transform.position-original.transform.position)<.004f&&Quaternion.Angle(c.transform.rotation,archived.transform.rotation)<.1f,"Legacy collision transform "+root.name+c.name);
                                else {Physics.SyncTransforms();var mesh=c.GetComponentInChildren<Renderer>(true);Check(mesh!=null&&Vector3.Distance(c.bounds.center,mesh.bounds.center)<.035f,"Animated control target detached from its visible handle "+root.name+c.name+" collider="+c.bounds.center+" renderer="+(mesh==null?Vector3.zero:mesh.bounds.center)+" active="+c.gameObject.activeInHierarchy);}
                            }else{
                                var expected=modernCollision[i];Check(c.enabled==expected.Item6&&Vector3.Distance(c.size,expected.Item5)<.002f&&Vector3.Distance(c.center,expected.Item4)<.002f&&Vector3.Distance(c.transform.localScale,expected.Item3)<.002f,"Modern collision fit lost "+root.name+c.name);
                            }
                        }
                        Check(colliders.Where(c=>c.enabled&&c.GetComponent<ColliderPlacementOptions>()==null&&!c.name.Contains("travel gate")).All(c=>c.gameObject.activeInHierarchy),"Visual switch disabled a native collision/interaction node "+root.name+mode);
                        Check(root.GetComponentsInChildren<Vehicle>(true).Length==controllers&&root.GetComponentsInChildren<Rigidbody>(true).Length==bodies,"Design duplicated native controllers "+root.name);
                        Mechanisms(root,legacy);
                        if(root.transform.Find("CabFittings")!=null)StandingCabAssetBuilder.Verify(root,legacy);
                        if(root.name=="WoodenMinecartObject")Check(root.GetComponentsInChildren<LODGroup>(true).Length==0,"Wooden cart retains competing LODs");
                        foreach(var a in rolling)a.enabled=false;
                        foreach(var group in root.GetComponentsInChildren<LODGroup>(true))group.ForceLOD(0);
                        if(!legacy)RailPreparedVehicleFit.Verify(root); // Shared native event registrations remain unique.
                        if(mode=="LegacyDesign"&&(root.name=="WoodenMinecartObject"||root.name=="MineTrainObject"||root.name=="HeritageTramObject"||root.name=="RollerCoasterCartObject"))VehicleIntegrationProbe.Capture(root,"legacy-design",false);
                    }
                    if(root.name=="RollerCoasterCartObject"){
                        var left=seats[1].transform.position;var right=seats[2].transform.position;
                        Check(Mathf.Abs(Mathf.Abs(left.x-right.x)-.68f)<.004f&&Mathf.Abs(left.y-(.80f-RailRiderFit.SeatedHipHeight+.20f))<.004f,"Adult coaster rider fit lost");
                    }
                    count++;
                }
                finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(original);}
            }
            Check(count==14,"Design audit missing vehicles");Debug.Log("VEHICLE_DESIGNS_EXPORTED_OK: fourteen original-default prefabs; independent collisions, mounts, exits and repeated moving design switches; shared native controllers and wood without LOD switching");
        }
    }
}
