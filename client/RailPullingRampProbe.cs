using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EcoMinecarts.Editor
{
    // Isolate actual Unity wheel/contact physics; this is not a live Eco input
    // or networking test. Both configurations receive identical total torque.
    [InitializeOnLoad]
    public static class RailPullingRampProbe
    {
        private const string Pending = "EcoMinecarts.PullRampPending", BuildPending = "EcoMinecarts.PullRampBuild";
        static RailPullingRampProbe() { EditorApplication.update += Update; }
        public static void CompareAndBuild()
        {
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
        private static void Update()
        {
            try
            {
                if (SessionState.GetBool(Pending, false) && EditorApplication.isPlaying)
                {
                    SessionState.SetBool(Pending, false);
                    Compare();
                    SessionState.SetBool(BuildPending, true);
                    EditorApplication.ExitPlaymode();
                }
                else if (SessionState.GetBool(BuildPending, false) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
                {
                    SessionState.SetBool(BuildPending, false);
                    RailPullingAssetBuilder.RefreshAndBuild();
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception e) { Debug.LogError("ECO_PULL_RAMP_FAILED: " + e); EditorApplication.Exit(1); }
        }
        private static void Compare()
        {
            foreach (var key in new[] { "Minecart", "WoodenMinecart" })
            {
                var empty = key == "Minecart" ? 280f : 80f;
                var cargo = key == "Minecart" ? 2500f : 400f;
                foreach (var load in new[] { 0f, cargo })
                {
                    var old = Run(key, empty, cargo, load, false);
                    var improved = Run(key, empty, cargo, load, true);
                    Debug.Log($"ECO_PULL_RAMP_CONTACT: {key}, cargo={load}, old uphill metres={old:F3}, revised uphill metres={improved:F3}");
                    if (!float.IsFinite(improved) || improved < 1.0f)
                        throw new InvalidOperationException("Revised native wheel rig cannot ascend ramp: " + key + " cargo " + load);
                }
            }
        }
        private static float Run(string key, float empty, float cargo, float load, bool revised)
        {
            var scene = SceneManager.CreateScene("Pull ramp " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physics = scene.GetPhysicsScene();
            try
            {
                var flat = new GameObject("Flat approach"); SceneManager.MoveGameObjectToScene(flat, scene);
                flat.transform.position = new Vector3(0, -.1f, -5);
                flat.AddComponent<BoxCollider>().size = new Vector3(3, .2f, 10);
                var ramp = new GameObject("One-in-four ramp"); SceneManager.MoveGameObjectToScene(ramp, scene);
                var rotation = Quaternion.LookRotation(new Vector3(0, .25f, 1));
                ramp.transform.rotation = rotation;
                ramp.transform.position = new Vector3(0, 1, 4) - rotation * Vector3.up * .1f;
                ramp.AddComponent<BoxCollider>().size = new Vector3(3, .2f, Mathf.Sqrt(68));
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/Prefabs/" + key + "Object.prefab");
                var root = UnityEngine.Object.Instantiate(source);
                SceneManager.MoveGameObjectToScene(root, scene);
                if (revised) RailPullingAssetBuilder.Configure(root, empty, cargo);
                root.transform.position = new Vector3(0, .04f, -1.6f);
                var body = root.GetComponent<Rigidbody>(); body.mass = empty + load + 80;
                body.isKinematic = false; body.useGravity = true; body.sleepThreshold = 0;
                root.GetComponentInChildren<MountSpotPulled>(true).occupiedCollider.enabled = true;
                var drive = root.GetComponent<RCCCarControllerV2>();
                for (var i = 0; i < 500; i++)
                {
                    var torque = body.linearVelocity.magnitude < 3 ? 14000f : 0;
                    drive.FrontLeftWheelCollider.motorTorque = drive.FrontRightWheelCollider.motorTorque = revised ? torque / 2 : torque;
                    drive.RearLeftWheelCollider.motorTorque = drive.RearRightWheelCollider.motorTorque = revised ? torque / 2 : 0;
                    physics.Simulate(.01f);
                    if (root.transform.position.z > 5) break;
                }
                return root.transform.position.z;
            }
            finally { SceneManager.UnloadSceneAsync(scene); }
        }
    }
}
