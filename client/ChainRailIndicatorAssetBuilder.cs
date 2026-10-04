using System;
using UnityEditor;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    public static class ChainRailIndicatorAssetBuilder
    {
        const string Root = "Assets/EcoMinecarts";
        public static GameObject[] Build() => new[] { Build(false), Build(true) };
        public static void RefreshAndBuildBundle()
        {
            Build(); AssetDatabase.SaveAssets(); MinecartAssetBuilder.BuildAuthoredClientBundle();
        }
        private static GameObject Build(bool powered)
        {
            var name = powered ? "RailChainPoweredIndicator" : "RailChainUnpoweredIndicator";
            var path = Root + "/Materials/MAT_" + name + ".mat";
            var shader = Shader.Find("Curved/Standard");
            if (shader == null) throw new InvalidOperationException("Missing native curved shader");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.color = powered ? new Color(.08f,.95f,.16f) : new Color(1,.08f,.035f);
            material.SetFloat("_Metallic", 0); material.SetFloat("_Glossiness", .15f);
            material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", material.color * .65f);
            material.DisableKeyword("NO_CURVE"); material.DisableKeyword("MINIMAP_NO_CURVE");
            EditorUtility.SetDirty(material);

            // Point along +Z, matching the increasing rail-path parameter used
            // by ChainLift. No collider, rigidbody, world-object view or save data.
            var root = new GameObject(name, typeof(NetObjComponent), typeof(WrapTarget));
            root.tag = "ModObject";
            var node = new GameObject("DirectionArrow", typeof(MeshFilter), typeof(MeshRenderer));
            node.transform.SetParent(root.transform, false);
            var vertices = new[] {
                new Vector3(-.065f,0,-.28f),new Vector3(.065f,0,-.28f),
                new Vector3(.065f,0,.06f),new Vector3(.20f,0,.06f),
                new Vector3(0,0,.31f),new Vector3(-.20f,0,.06f),new Vector3(-.065f,0,.06f)
            };
            var meshPath = Root + "/Prefabs/ChainDirectionArrowMesh.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, meshPath); }
            var bothFaces = new Vector3[vertices.Length * 2];
            Array.Copy(vertices, 0, bothFaces, 0, vertices.Length);
            Array.Copy(vertices, 0, bothFaces, vertices.Length, vertices.Length);
            mesh.Clear(); mesh.name = "ChainDirectionArrow"; mesh.vertices = bothFaces;
            // Both faces remain visible on sloped, banked and inverted rail.
            mesh.triangles = new[] {0,6,2,0,2,1,5,4,3, 7,9,13,7,8,9,12,10,11};
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            node.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
