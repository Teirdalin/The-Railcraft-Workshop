using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class RailVehicleTextProbe
    {
        public static void BuildAndCheck()
        {
            MinecartAssetBuilder.BuildAuthoredClientBundle();
            Run();
        }
        public static void Run()
        {
            var dir = Path.GetFullPath("../../validation/vehicle-text-0.2.3"); Directory.CreateDirectory(dir);
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/EcoMinecarts/Prefabs" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var v = prefab.GetComponent<Vehicle>(); if (v == null || v.LicensePlate == null) continue;
                var root = Object.Instantiate(prefab); root.SetActive(true);
                try
                {
                    var vehicle = root.GetComponent<Vehicle>(); var index = Array.IndexOf(vehicle.States, "VehicleTextVisible");
                    var group = root.transform.Find("Optional vehicle nameplates");
                    if (!group.gameObject.activeSelf || index < 0 || group.GetComponentsInChildren<Collider>(true).Length != 0 ||
                        group.GetComponentsInChildren<TextMeshPro>(true).Any(t=>t.enabled) || group.GetComponentsInChildren<MeshRenderer>(true).Any(r=>r.enabled))
                        throw new Exception("Blank plate or collision mismatch: " + prefab.name);
                    foreach (var text in group.GetComponentsInChildren<TextMeshPro>(true))
                    {
                        if (text.text != "" || text.font.atlasPopulationMode != AtlasPopulationMode.Static || text.font.fallbackFontAssetTable.Count != 0)
                            throw new Exception("Invalid saved text/font: " + prefab.name);
                        text.text = "NORTH LINE 24";
                    }
                    for(var listener=0;listener<vehicle.OnStateChangedEvents[index].GetPersistentEventCount();listener++)
                        vehicle.OnStateChangedEvents[index].SetPersistentListenerState(listener, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                    vehicle.OnStateChangedEvents[index].Invoke(true);
                    foreach (var text in group.GetComponentsInChildren<TextMeshPro>(true))
                    {
                        text.ForceMeshUpdate();
                        if (!text.gameObject.activeInHierarchy || text.textInfo.characterCount < 10 || text.mesh.vertexCount < 4)
                            throw new Exception("Filled label did not render: " + prefab.name);
                    }
                    Render(root, vehicle.LicensePlate, Path.Combine(dir, prefab.name + ".png"));
                    foreach (var text in group.GetComponentsInChildren<TextMeshPro>(true)) text.text = "";
                    vehicle.OnStateChangedEvents[index].Invoke(false);
                    if (!group.gameObject.activeSelf || group.GetComponentsInChildren<TextMeshPro>(true).Any(t=>t.enabled) ||
                        group.GetComponentsInChildren<MeshRenderer>(true).Any(r=>r.enabled)) throw new Exception("Cleared plate visibility mismatch");
                    count++;
                }
                finally { Object.DestroyImmediate(root); }
            }
            Debug.Log("RAIL_TEXT_FOCUSED_CHECK_OK: " + count + " blank/filled/cleared; static glyph meshes; no plate colliders. Live Eco binding unverified.");
        }
        static void Render(GameObject root, TextMeshPro text, string path)
        {
            foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 30;
            foreach (var particle in root.GetComponentsInChildren<ParticleSystemRenderer>(true)) particle.enabled = false;
            var camera = new GameObject("Nameplate preview camera").AddComponent<Camera>();
            camera.transform.position = text.transform.position - text.transform.forward * 2 + Vector3.up * .12f;
            camera.transform.LookAt(text.transform.position);
            camera.orthographic = true; camera.orthographicSize = .60f; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.20f,.23f,.27f);
            camera.nearClipPlane = .01f; camera.farClipPlane = 100;
            var light = new GameObject("Nameplate preview light").AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.rotation = camera.transform.rotation; light.intensity = 1;
            var target = new RenderTexture(800, 480, 24); var image = new Texture2D(800, 480, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            Shader.SetGlobalFloat("_WorldRadius",10000); Shader.SetGlobalVector("_WorldCenter",new Vector4(0,-10000,0,0));
            Shader.SetGlobalVector("_CurveAxisMask",Vector4.zero); Shader.EnableKeyword("NO_CURVE");
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0,0,800,480),0,0); image.Apply(); File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null; target.Release();
                Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(light.gameObject);
                Shader.DisableKeyword("NO_CURVE");
            }
        }
    }
}
