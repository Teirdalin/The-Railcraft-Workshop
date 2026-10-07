using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    [InitializeOnLoad]
    public static class PoweredWheelPlayProbe
    {
        const string Pending="EcoMinecarts.PoweredWheelPlayProbe";
        static readonly List<GameObject> rigs=new List<GameObject>();
        static double began;
        static PoweredWheelPlayProbe(){EditorApplication.update+=Update;}
        public static void Run(){SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();}
        static void Update()
        {
            if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying)return;
            try
            {
                if(rigs.Count==0)
                {
                    // Let native WheelCollider initialization and FixedUpdate run;
                    // a tight Physics.Simulate loop before initialization is not a vehicle test.
                    Physics.simulationMode=SimulationMode.FixedUpdate;Physics.IgnoreLayerCollision(0,0,false);
                    var specs=RailExpansionAssetBuilder.ReadCatalog().Vehicles.Where(s=>s.Powered)
                        .Select(s=>(Name:s.Key+"Object",Empty:s.EmptyKg,Rated:s.EmptyKg+s.CargoKg+500))
                        .Concat(new[]{(Name:"MineTrainObject",Empty:600f,Rated:1350f)});
                    foreach(var spec in specs)foreach(var mass in new[]{spec.Empty,spec.Rated})
                    {
                        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/Prefabs/"+spec.Name+".prefab");
                        var p=new Vector3(200+rigs.Count*15,20.1f,200);
                        var floor=new GameObject("Test floor");floor.transform.position=p-new Vector3(0,.6f,0);floor.AddComponent<BoxCollider>().size=new Vector3(10,1,10);
                        var rig=RailWheelSuspension.CreateSettlingRig(prefab.transform,mass,p);rig.name=spec.Name+" "+mass+"kg";rigs.Add(rig);
                    }
                    Physics.SyncTransforms();began=EditorApplication.timeSinceStartup;return;
                }
                if(EditorApplication.timeSinceStartup-began<8)return;
                foreach(var rig in rigs)
                {
                    var body=rig.GetComponent<Rigidbody>();
                    Debug.Log("WHEEL_SETTLE: "+rig.name+" offset="+(body.position.y-20)+" velocity="+body.linearVelocity.y);
                    if(Mathf.Abs(body.position.y-20)>.03f||Mathf.Abs(body.linearVelocity.y)>.05f||rig.GetComponentsInChildren<WheelCollider>().Any(w=>!w.isGrounded))throw new Exception("Wheel settling failed: "+rig.name);
                }
                Debug.Log("POWERED_WHEEL_PLAY_OK: eight empty/rated engine rigs settled on native WheelColliders.");SessionState.SetBool(Pending,false);EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);SessionState.SetBool(Pending,false);EditorApplication.Exit(1);}
        }
    }
}
