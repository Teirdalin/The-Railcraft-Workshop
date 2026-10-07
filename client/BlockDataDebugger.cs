using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EcoMinecarts.Editor
{
    /// <summary>Read-only export from the loaded shipping bundle, not the authoring prefab.</summary>
    public static class BlockDataDebugger
    {
        [Serializable] public class Report
        {
            public string capturedUtc, unityVersion, sourceBundle, sourceBundleHash, boundary;
            public string[] sceneRoots;
            public List<BlockData> blocks = new List<BlockData>();
            public List<string> failures = new List<string>();
        }
        [Serializable] public class BlockData
        {
            public string name, builderType, serializedBlock, serializedBuilder;
            public bool rendered, solid, buildCollider, generateMeshCollider;
            public float actualHeight, prefabHeightOffset;
            public List<CaseData> cases = new List<CaseData>();
            public List<MaterialData> materials = new List<MaterialData>();
            public List<string> prefabMeshes = new List<string>();
        }
        [Serializable] public class CaseData
        {
            public bool enabled, rotateConditions, dontRotateBaseMesh;
            public string serializedCase, collider;
            public List<string> lod0 = new List<string>();
            public string lod1, lod2;
        }
        [Serializable] public class MaterialData
        {
            public string name, shader, serializedMaterial;
            public bool supported;
            public int renderQueue;
            public List<string> properties = new List<string>();
        }
        [Serializable] public class SubmeshData { public string topology; public int[] indices; }
        [Serializable] public class MeshData
        {
            public string name;
            public bool readable;
            public int vertexCount, subMeshCount, blendShapeCount;
            public Bounds bounds;
            public Vector3[] vertices, normals;
            public Vector4[] tangents;
            public Color32[] colors;
            public Vector2[] uv, uv2, uv3, uv4, uv5, uv6, uv7, uv8;
            public List<SubmeshData> submeshes = new List<SubmeshData>();
        }

        public static void Export(Scene scene, string sourceBundle)
        {
            var directory = Environment.GetEnvironmentVariable("ECO_MINECART_AUDIT_DIR");
            if (string.IsNullOrWhiteSpace(directory)) directory = Path.GetFullPath("../../validation/block-debugger");
            Directory.CreateDirectory(Path.Combine(directory, "meshes"));
            var report = new Report { capturedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                sourceBundle = sourceBundle, boundary = "Serialized exported-bundle data. Does not execute Eco's IL2CPP chunk builder or prove in-game visibility.",
                sceneRoots = scene.GetRootGameObjects().Select(r => r.name).ToArray() };
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var input = File.OpenRead(sourceBundle))
                report.sourceBundleHash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
            var meshes = new Dictionary<int, string>();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var container in root.GetComponentsInChildren<BlockSetContainer>(true))
            foreach (var set in container.blockSets)
            foreach (var block in set.Blocks)
            {
                if (block == null) { report.failures.Add("Null block entry in " + set.name); continue; }
                var data = new BlockData { name = block.Name, builderType = block.Builder?.GetType().FullName,
                    serializedBlock = EditorJsonUtility.ToJson(block, true), serializedBuilder = block.Builder == null ? null : EditorJsonUtility.ToJson(block.Builder, true),
                    rendered = block.Rendered, solid = block.Solid, buildCollider = block.BuildCollider,
                    generateMeshCollider = block.GenerateMeshCollider, actualHeight = block.ActualHeight, prefabHeightOffset = block.PrefabHeightOffset };
                report.blocks.Add(data);
                foreach (var material in new[] { block.Material }.Concat(block.Materials ?? Array.Empty<Material>()))
                {
                    if (material == null) { report.failures.Add(block.Name + ": null material"); continue; }
                    var m = new MaterialData { name = material.name, shader = material.shader?.name, supported = material.shader != null && material.shader.isSupported,
                        renderQueue = material.renderQueue, serializedMaterial = EditorJsonUtility.ToJson(material, true) };
                    if (material.shader != null)
                    for (var i = 0; i < material.shader.GetPropertyCount(); i++)
                    {
                        var property = material.shader.GetPropertyName(i);
                        var kind = material.shader.GetPropertyType(i);
                        string value;
                        if (kind == ShaderPropertyType.Texture)
                        {
                            var texture = material.GetTexture(property);
                            value = texture == null ? "null" : texture.name + " " + texture.width + "x" + texture.height + " " + texture.dimension;
                        }
                        else if (kind == ShaderPropertyType.Color) value = material.GetColor(property).ToString();
                        else if (kind == ShaderPropertyType.Vector) value = material.GetVector(property).ToString();
                        else if (kind == ShaderPropertyType.Int) value = material.GetInteger(property).ToString();
                        else value = material.GetFloat(property).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        m.properties.Add(property + " [" + kind + "] = " + value);
                    }
                    data.materials.Add(m);
                    if (!m.supported) report.failures.Add(block.Name + ": material shader unsupported in this editor: " + m.shader);
                }
                if (!(block.Builder is CustomBuilder builder))
                {
                    report.failures.Add(block.Name + ": no CustomBuilder terrain mesh path");
                    if (block.Builder is WeightedPrefabBlockBuilder weighted)
                    foreach (var entry in weighted.prefabs)
                    {
                        if (entry.prefab == null) { report.failures.Add(block.Name + ": null prefab"); continue; }
                        foreach (var filter in entry.prefab.GetComponentsInChildren<MeshFilter>(true))
                            data.prefabMeshes.Add(DumpMesh(filter.sharedMesh, directory, meshes, report));
                    }
                    continue;
                }
                foreach (var usage in builder.usageCases)
                {
                    var c = new CaseData { enabled = usage.enabled, rotateConditions = usage.applyConditionsToAllRotations,
                        dontRotateBaseMesh = usage.dontRotateBaseMesh, serializedCase = JsonUtility.ToJson(usage, true) };
                    data.cases.Add(c);
                    if (usage.mesh == null) report.failures.Add(block.Name + ": missing source mesh (installed pipe contract)");
                    if (!usage.enabled) report.failures.Add(block.Name + ": disabled mesh usage case");
                    var lods = usage.blockMeshLodGroup;
                    if (lods == null) { report.failures.Add(block.Name + ": missing mesh LOD group"); continue; }
                    foreach (var lod in lods.LOD0) c.lod0.Add(DumpMesh(lod.mesh, directory, meshes, report));
                    if (lods.LOD0.Length == 0) report.failures.Add(block.Name + ": empty LOD0");
                    foreach (var lod in lods.LOD0)
                        if (lod.mesh != null && lod.mesh.subMeshCount > 1 + (block.Materials?.Length ?? 0))
                            report.failures.Add(block.Name + ": insufficient main + additional submesh materials");
                    c.lod1 = DumpMesh(lods.GetLodMesh(1, 0), directory, meshes, report);
                    c.lod2 = DumpMesh(lods.GetLodMesh(2, 0), directory, meshes, report);
                    c.collider = DumpMesh(lods.Collider, directory, meshes, report);
                    if (usage.applyConditionsToAllRotations || !usage.dontRotateBaseMesh) report.failures.Add(block.Name + ": unexpected automatic rotation on pre-rotated mesh");
                }
                if (builder.usageCases.Count == 0) report.failures.Add(block.Name + ": no mesh usage cases");
                if (!block.Rendered || block.IsEmpty) report.failures.Add(block.Name + ": hidden/empty block flag");
            }
            var expectedKeys = File.ReadAllLines(Path.GetFullPath("../../validation/server-client-block-keys.txt"));
            var actualKeys = report.blocks.Select(b => b.name).ToArray();
            if (actualKeys.Length != expectedKeys.Length || actualKeys.Distinct().Count() != expectedKeys.Length ||
                !actualKeys.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(expectedKeys.OrderBy(x=>x,StringComparer.Ordinal)))
                report.failures.Add("Exported unique block entries do not match the native registration keys.");
            File.WriteAllText(Path.Combine(directory, "client-data.json"), JsonUtility.ToJson(report, true));
            Debug.Log("BLOCK_DEBUG_DATA_WRITTEN: " + directory + "; blocks=" + report.blocks.Count + "; fullMeshes=" + meshes.Count + "; failures=" + report.failures.Count);
            if (report.failures.Count != 0) throw new InvalidOperationException(string.Join("\n", report.failures));
        }

        private static string DumpMesh(Mesh mesh, string directory, Dictionary<int, string> exported, Report report)
        {
            if (mesh == null) { report.failures.Add("Missing mesh reference"); return null; }
            if (exported.TryGetValue(mesh.GetInstanceID(), out var existing)) return existing;
            var file = "meshes/" + exported.Count.ToString("D3") + "-" + mesh.name + ".json";
            exported.Add(mesh.GetInstanceID(), file);
            var data = new MeshData { name = mesh.name, readable = mesh.isReadable, bounds = mesh.bounds, vertexCount = mesh.vertexCount,
                subMeshCount = mesh.subMeshCount, blendShapeCount = mesh.blendShapeCount };
            if (mesh.vertexCount == 0 || mesh.subMeshCount == 0) report.failures.Add(mesh.name + ": empty mesh");
            if (!mesh.isReadable) report.failures.Add(mesh.name + ": CPU mesh data is not readable");
            else
            {
                data.vertices = mesh.vertices; data.normals = mesh.normals; data.tangents = mesh.tangents; data.colors = mesh.colors32;
                data.uv = mesh.uv; data.uv2 = mesh.uv2; data.uv3 = mesh.uv3; data.uv4 = mesh.uv4;
                data.uv5 = mesh.uv5; data.uv6 = mesh.uv6; data.uv7 = mesh.uv7; data.uv8 = mesh.uv8;
                for (var i = 0; i < mesh.subMeshCount; i++)
                {
                    var indices = mesh.GetIndices(i);
                    data.submeshes.Add(new SubmeshData { topology = mesh.GetTopology(i).ToString(), indices = indices });
                    if (indices.Length == 0 || indices.Any(n => n < 0 || n >= mesh.vertexCount)) report.failures.Add(mesh.name + ": invalid triangle indices");
                }
                if (data.vertices.Any(v => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))) report.failures.Add(mesh.name + ": nonfinite vertices");
                if (data.normals.Length != mesh.vertexCount) report.failures.Add(mesh.name + ": missing normals");
                if (data.uv.Length != mesh.vertexCount) report.failures.Add(mesh.name + ": missing texture coordinates (required by Eco even for collision-only meshes)");
            }
            File.WriteAllText(Path.Combine(directory, file), JsonUtility.ToJson(data, true));
            return file;
        }
    }
}
