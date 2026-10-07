using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    public static class MinecartVisualProbe
    {
        public static void Run()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/Models/Minecart.fbx");
            Debug.Log("FBX_ROOT scale=" + model.transform.localScale + " rotation=" + model.transform.localEulerAngles);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EcoMinecarts/Prefabs/MinecartObject.prefab");
            MinecartAssetBuilder.ValidateCartGeometry(prefab);
            var cart = Object.Instantiate(prefab);
            cart.SetActive(true);
            var renderers = cart.GetComponentsInChildren<MeshRenderer>(true);
            var bounds = new Bounds();
            var first = true;
            foreach (var renderer in renderers)
            {
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                Debug.Log($"CART_VISUAL {renderer.name}: active={renderer.gameObject.activeInHierarchy} enabled={renderer.enabled} scale={renderer.transform.lossyScale} bounds={renderer.bounds} vertices={mesh?.vertexCount} submeshes={mesh?.subMeshCount} materials={string.Join(",", renderer.sharedMaterials.Select(m => m == null ? "NULL" : m.name + ":" + m.shader.name))}");
                if (first) { bounds = renderer.bounds; first = false; } else bounds.Encapsulate(renderer.bounds);
            }
            Debug.Log("CART_BOUNDS " + bounds);
            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            RenderSettings.ambientLight = new Color(.45f, .45f, .45f);
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.1f,.12f,.15f,1);
            camera.transform.position = bounds.center + new Vector3(2,1.5f,2) * Mathf.Max(bounds.size.magnitude, .1f);
            camera.transform.LookAt(bounds.center);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.size.magnitude * .6f,.1f);
            camera.nearClipPlane = .001f;
            camera.farClipPlane = 1000;
            var target = new RenderTexture(768,768,24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(768,768,TextureFormat.RGBA32,false);
            texture.ReadPixels(new Rect(0,0,768,768),0,0);
            texture.Apply();
            var output = Path.GetFullPath("../../validation/cart-prefab-fixed.png");
            File.WriteAllBytes(output, texture.EncodeToPNG());
            Debug.Log("CART_RENDER_OK " + output);
        }
    }
}
