using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace EcoMinecarts.Editor
{
    [InitializeOnLoad]
    public static class RailWheelAnimationProbe
    {
        const string Pending="Railworks.ExportedWheelProbe";
        static AssetBundle bundle;
        static AsyncOperation loading;
        static RailWheelAnimationProbe(){EditorApplication.update+=Update;}
        public static void RunExported(){SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();}
        static void Update()
        {
            if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying)return;
            try
            {
                if(bundle==null)
                {
                    bundle=AssetBundle.LoadFromFile(System.IO.Path.GetFullPath("Build/EcoMinecarts.unity3d"));
                    Check(bundle!=null,"Could not load exported wheel bundle");
                    loading=SceneManager.LoadSceneAsync(bundle.GetAllScenePaths().Single(),LoadSceneMode.Single);return;
                }
                if(!loading.isDone)return;
                var icons=SceneManager.GetActiveScene().GetRootGameObjects().Single(n=>n.name=="Items");
                foreach(var key in new[]{"MineTrainDrivingComponent","TrainControllerComponent","RailConditionComponent","TrainFareComponent","RailVehicleAccessComponent","ChainDriveSpeedComponent","RailPowerConnectionComponent","TrainStationComponent","RailAutomationComponent","StationDepartureContext"}){
                    var icon=icons.transform.Find(key);Check(icon!=null&&icon.GetComponentsInChildren<UnityEngine.UI.Image>(true).Any(i=>i.name=="Foreground"&&i.sprite!=null),"Missing exported component icon "+key);
                }
                Debug.Log("COMPONENT_ICONS_EXPORTED_OK: ten reported component icon names have non-null bundled sprites");
                Verify(SceneManager.GetActiveScene().GetRootGameObjects().Single(n=>n.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
                RailMechanicalAnimationProbe.Verify(SceneManager.GetActiveScene().GetRootGameObjects().Single(n=>n.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
                var prefabs=SceneManager.GetActiveScene().GetRootGameObjects().Single(n=>n.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs;
                bool originals=OriginalVehicleReleaseBuilder.IsOriginalRelease(prefabs);
                if(originals)OriginalVehicleReleaseProbe.Verify(prefabs);
                else {VehicleIntegrationProbe.VerifyExported(prefabs);RailVehicleDesignProbe.Verify(prefabs);}
                MeshyInfrastructureProbe.Verify(SceneManager.GetActiveScene().GetRootGameObjects().Single(n=>n.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
                RailPresentationProbe.Verify(SceneManager.GetActiveScene().GetRootGameObjects().Single(n=>n.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs);
                if(!originals){RailVehicleFitRepairProbe.Verify(prefabs);RailVehicle63Probe.Verify(prefabs);}
                Debug.Log("EXPORTED_WHEEL_ANIMATION_OK: native events and Animator evaluation from the shipping bundle; live Eco client visuals remain unverified.");
                SessionState.SetBool(Pending,false);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogError("EXPORTED_WHEEL_ANIMATION_FAILED: "+e);SessionState.SetBool(Pending,false);EditorApplication.Exit(1);}
        }
        public static void BuildAndVerify()
        {
            RailWheelAnimationBuilder.Apply();
            Verify(AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/EcoMinecarts/Prefabs"})
                .Select(g=>AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).ToArray());
            MinecartAssetBuilder.BuildAuthoredClientBundle();
        }
        public static void Verify(GameObject[] prefabs)
        {
            int vehicles=0,wheels=0;
            foreach(var prefab in prefabs)
            {
                if(prefab.GetComponent<Vehicle>()==null)continue;
                var root=UnityEngine.Object.Instantiate(prefab);
                try
                {
                    root.SetActive(true);var world=root.GetComponent<WorldObject>();
                    int rate=Array.IndexOf(world.FloatStates,"RailWheelSpeed"),direction=Array.IndexOf(world.StringStates,"RailWheelDirection"),moving=Array.IndexOf(world.States,"RailWheelsMoving");
                    Check(rate>=0&&direction>=0,"Missing wheel states "+root.name);
                    // The production callbacks are RuntimeOnly. This probe evaluates
                    // cloned native Animators manually in the editor, so enable only
                    // the clone's callbacks for this evaluation.
                    for(int i=0;i<world.OnFloatStateChanged[rate].GetPersistentEventCount();i++)world.OnFloatStateChanged[rate].SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                    for(int i=0;i<world.OnStringStateChanged[direction].GetPersistentEventCount();i++)world.OnStringStateChanged[direction].SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                    for(int i=0;i<world.OnStateChangedEvents[moving].GetPersistentEventCount();i++)world.OnStateChangedEvents[moving].SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                    var animators=root.GetComponentsInChildren<Animator>(true).Where(a=>a.name.StartsWith("WheelRoll_")||a.name.StartsWith("UpstopRoll_")).ToArray();
                    Check(animators.Length==(prefab.name=="RollerCoasterCartObject"?8:4),"Wheel count "+root.name);
                    Check(animators.All(a=>!a.enabled),"Wheel animator runs while initially parked");
                    foreach(var a in animators)
                    {
                        world.OnStateChangedEvents[moving].Invoke(true);
                        a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.Rebind();a.Update(0);
                        var pivot=a.transform.Find("Spin");
                        var clip=a.runtimeAnimatorController.animationClips.Distinct().Single();float radius=clip.length/(2*Mathf.PI);
                        float sense=a.name.StartsWith("UpstopRoll_")?-1:1;
                        a.Play("RollForward",0,.25f);world.OnFloatStateChanged[rate].Invoke(2);a.Update(0);
                        Check(Mathf.Abs(a.speed-2)<.001f,"Native float callback did not set Animator speed");
                        var before=pivot.localRotation;a.Update(clip.length/8);
                        float moved=RotationDelta(before,pivot.localRotation);
                        Check(Mathf.Abs(moved-90*sense)<1,"Forward radius/speed "+root.name+a.name+" moved="+moved+" radius="+radius);
                        world.OnFloatStateChanged[rate].Invoke(0);before=pivot.localRotation;a.Update(.4f);
                        Check(Quaternion.Angle(before,pivot.localRotation)<.05f,"Stationary wheel rolls");
                        world.OnStateChangedEvents[moving].Invoke(false);Check(!a.enabled,"Parked animator stays active");
                        world.OnStateChangedEvents[moving].Invoke(true);
                        world.OnStringStateChanged[direction].Invoke("RollReverse");a.Update(0);
                        Debug.Log("WHEEL_REVERSAL_PHASE: "+root.name+a.name+" delta="+RotationDelta(before,pivot.localRotation));
                        world.OnFloatStateChanged[rate].Invoke(2);before=pivot.localRotation;a.Update(clip.length/8);
                        moved=RotationDelta(before,pivot.localRotation);
                        Check(Mathf.Abs(moved+90*sense)<1,"Reverse radius/speed "+root.name+a.name+" moved="+moved);
                        wheels++;
                    }
                    vehicles++;
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            Check(vehicles==14,"Expected fourteen vehicle definitions");
            Debug.Log("RAIL_WHEEL_ANIMATION_OK: "+vehicles+" vehicles, "+wheels+" wheels; actual native Animator evaluation, radius, speed, reverse and stationary checks.");
        }
        static void Check(bool ok,string error){if(!ok)throw new Exception(error);}
        static float RotationDelta(Quaternion before,Quaternion after)
        {
            var delta=Quaternion.Inverse(before)*after;delta.ToAngleAxis(out float angle,out var axis);
            return Mathf.DeltaAngle(0,angle)*Mathf.Sign(axis.x);
        }
    }
}
