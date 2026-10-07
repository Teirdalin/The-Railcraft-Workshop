using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    [InitializeOnLoad]
    public static class RailSelfCollisionProbe
    {
        const string Pending="EcoMinecarts.RailSelfCollisionProbe";
        static AssetBundle bundle;
        static AsyncOperation loading;
        static RailSelfCollisionProbe() { EditorApplication.update+=Update; }
        public static void Run() { SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode(); }
        static void Update()
        {
            if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying)return;
            try {
                if(bundle==null) {
                    bundle=AssetBundle.LoadFromFile(Environment.GetEnvironmentVariable("ECO_MINECART_AUDIT_BUNDLE"));
                    loading=SceneManager.LoadSceneAsync(bundle.GetAllScenePaths().Single(),LoadSceneMode.Single);return;
                }
                if(!loading.isDone)return;
                Audit();SessionState.SetBool(Pending,false);EditorApplication.Exit(0);
            } catch(Exception e) { Debug.LogException(e);SessionState.SetBool(Pending,false);EditorApplication.Exit(1); }
        }
        static void Audit()
        {
            var roots=SceneManager.GetActiveScene().GetRootGameObjects();
            var prefabs=roots.Single(x=>x.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs;
            foreach(var root in roots)root.SetActive(false);
            Physics.simulationMode=SimulationMode.Script;
            var report=new List<string>();
            var requireSafe=Environment.GetEnvironmentVariable("ECO_SELF_COLLISION_REQUIRE_SAFE")=="1";
            foreach(var prefab in prefabs.Where(p=>p.GetComponent<Vehicle>()!=null))
            {
                var obj=Object.Instantiate(prefab);obj.SetActive(true);
                obj.transform.position=new Vector3(100,100,100);
                foreach(var wheel in obj.GetComponentsInChildren<WheelCollider>(true))wheel.enabled=false;
                var body=obj.GetComponent<Rigidbody>();
                // Reproduce the native ReceiveUpdate dynamic-body transition.
                // No terrain, passenger, gravity or wheel forces are present.
                body.isKinematic=false;body.useGravity=false;
                var colliders=obj.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&!c.isTrigger&&!(c is WheelCollider)).ToArray();
                Physics.SyncTransforms();
                int overlaps=0,unsafePairs=0;
                foreach(var a in colliders.Where(c=>c.attachedRigidbody==body))
                foreach(var b in colliders.Where(c=>c.attachedRigidbody!=body))
                {
                    if(!Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out var direction,out var depth))continue;
                    overlaps++;
                    if(Physics.GetIgnoreCollision(a,b))continue;
                    unsafePairs++;
                    report.Add(prefab.name+": self contact "+a.name+" / "+b.name+" depth="+depth.ToString("F5"));
                }
                float peak=0;
                for(int step=0;step<40;step++)
                {
                    foreach(var child in obj.GetComponentsInChildren<Rigidbody>().Where(b=>b!=body))
                    { child.MovePosition(body.position);child.MoveRotation(body.rotation); }
                    Physics.Simulate(.02f);peak=Mathf.Max(peak,body.linearVelocity.magnitude);
                }
                report.Add(prefab.name+": overlaps="+overlaps+" unfiltered="+unsafePairs+" peakSelfImpulseSpeed="+peak.ToString("F5"));
                if(requireSafe&&RailVehiclePhysicsAssetBuilder.ServerOnly(prefab)
                    &&(prefab.GetComponent<SyncPhysics>().SyncVelocity||!prefab.GetComponent<Rigidbody>().isKinematic
                        ||prefab.GetComponent<Rigidbody>().useGravity||prefab.GetComponentsInChildren<WheelCollider>(true).Any(w=>w.enabled)))
                    throw new Exception("Server-only prefab still uses dynamic velocity synchronization: "+prefab.name);
                if(requireSafe&&(unsafePairs>0||peak>.001f))throw new Exception("Vehicle collides with its walking platform: "+prefab.name);
                Object.DestroyImmediate(obj);
            }
            var path=Environment.GetEnvironmentVariable("ECO_SELF_COLLISION_REPORT");
            File.WriteAllLines(path,report);
            Debug.Log("ECO_SELF_COLLISION_AUDIT_OK: "+path+"; actual exported colliders, empty vehicle, no ground/gravity/wheel forces; native networking not simulated.");
        }
    }
}
