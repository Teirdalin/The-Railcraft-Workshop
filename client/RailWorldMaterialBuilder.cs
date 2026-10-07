using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EcoMinecarts.Editor
{
    // Explicit native-surface test mode. Keep paint authoring available for a
    // future comparison, but never mix the two surface shaders in this export.
    public static class RailWorldMaterialBuilder
    {
        public static readonly bool NativeSurfaceTest = true;
        public const string SurfaceShader = "Curved/Standard";
        public const string ParticleShader = "Curved/Particles/Standard Surface";
        public const string TextShader = SurfaceShader;
        const string Root = "Assets/EcoMinecarts";

        public static void RemoveVehicleTextForRecovery()
        {
            // The 0.1.0 client crash enters TMP_FontAsset.ReadFontAssetDefinition
            // and FontEngine.GetFaceInfo during vehicle instantiation. Keep the
            // boards but omit their bundled-font text until font loading is fixed.
            var removed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root + "/Prefabs" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab.GetComponent<Vehicle>() == null || prefab.GetComponentsInChildren<TMP_Text>(true).Length == 0) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var vehicle = root.GetComponent<Vehicle>();
                    vehicle.LicensePlate = null; vehicle.ExtraLicensePlates = Array.Empty<TextMeshPro>();
                    foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        UnityEngine.Object.DestroyImmediate(text.gameObject);
                        removed++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log("RAIL_RECOVERY_TEXT_REMOVED: " + removed);
        }

        public static void Particle(Material material, bool additive)
        {
            material.shader = Shader.Find(ParticleShader) ?? throw new Exception("Missing Eco curved particle shader");
            material.shaderKeywords = Array.Empty<string>();
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Mode", additive ? 4 : 2);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", 0);
            material.SetFloat("_LightingEnabled", 0);
            material.SetFloat("_Metallic", 0); material.SetFloat("_Glossiness", 0);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
        }

        public static void Text(TMP_Text text)
        {
            var original = text.fontSharedMaterial ?? text.font.material;
            var path = Root + "/Materials/MAT_DestinationText.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(original); AssetDatabase.CreateAsset(material, path); }
            TextMaterial(material, text.font.atlasTexture);
            text.fontSharedMaterial = material;
            EditorUtility.SetDirty(material);
        }

        public static void TextMaterial(Material material, Texture atlas)
        {
            material.shader = Shader.Find(TextShader) ?? throw new Exception("Missing native Curved/Standard text shader");
            material.shaderKeywords = Array.Empty<string>();
            material.SetTexture("_MainTex", atlas);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            material.SetColor("_Color", new Color(.95f, .89f, .68f));
            material.SetFloat("_Mode", 2); // Native Fade: the atlas supplies glyph coverage.
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0); material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            material.SetFloat("_Metallic", 0); material.SetFloat("_Glossiness", 0);
            material.SetColor("_EmissionColor", Color.black);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = 3000;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        public static void NormalizeMaterials()
        {
            var standard = Shader.Find(SurfaceShader) ?? throw new Exception("Missing native Curved/Standard");
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material.name == "MAT_DestinationText") { TextMaterial(material, material.mainTexture); continue; }
                if (material.name == "MAT_TrainExhaust" || material.name == "MAT_BrakeSparks" || material.name == "MAT_DriveStalledSmoke")
                { Particle(material, material.name == "MAT_BrakeSparks"); continue; }
                if (!NativeSurfaceTest && material.shader.name == RailVehiclePaintBuilder.ShaderName) continue;
                material.shader = standard;
                material.DisableKeyword("NO_CURVE"); material.DisableKeyword("MINIMAP_NO_CURVE");
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
            }
        }

        public static void RefreshAndBuildBundle()
        {
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { Root + "/Prefabs" })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(p => p.GetComponent<Vehicle>() != null).ToArray();
            var materials = new[] { "MAT_IronBare", "MAT_IronPainted", "MAT_WoodRail" }
                .ToDictionary(k => k, k => AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + k + ".mat"));
            RailVisualFinish.Apply(prefabs, materials);
            RailVehiclePaintBuilder.Apply(prefabs);
            foreach (var prefab in prefabs)
            {
                var path = AssetDatabase.GetAssetPath(prefab);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    RailExpansionAssetBuilder.ConfigureCoasterEndShove(root);
                    RailRiderInteractionAssetBuilder.Configure(root,root.transform.Find("CabFittings")!=null);
                    root.GetComponent<Vehicle>().AllVehicleColliders=root.GetComponentsInChildren<Collider>(true);
                    RailVehiclePhysicsAssetBuilder.Configure(root);
                    foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) Text(text);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            NormalizeMaterials(); AssetDatabase.SaveAssets();
            MinecartAssetBuilder.BuildAuthoredClientBundle();
            Debug.Log("ECO_NATIVE_CURVED_SEATING_BUILD_OK: native surfaces, curved text/particles, seated riders raised 0.10m.");
        }

        public static void Verify(GameObject[] roots)
        {
            var rows = new System.Collections.Generic.HashSet<string>();
            var prefabs = roots.SelectMany(r => r.GetComponentsInChildren<ModkitPrefabContainer>(true))
                .SelectMany(c => c.Prefabs);
            foreach (var root in roots.Concat(prefabs).Distinct())
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) throw new Exception("Missing world material: " + root.name + "/" + renderer.name);
                var expected = renderer is ParticleSystemRenderer ? ParticleShader
                    : renderer.GetComponent<TMP_Text>() != null ? TextShader
                    : NativeSurfaceTest ? SurfaceShader : material.shader.name;
                if (material.shader.name != expected || material.IsKeywordEnabled("NO_CURVE") || material.IsKeywordEnabled("MINIMAP_NO_CURVE"))
                    throw new Exception("Uncurved/unexpected world material: " + root.name + "/" + renderer.name + ": " + material.shader.name);
                if (!material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                    throw new Exception("World shader failed: " + material.shader.name);
                if (expected == SurfaceShader && !material.enableInstancing)
                    throw new Exception("Native material lost instancing: " + material.name);
                rows.Add(material.name + " | " + material.shader.name);
            }
            foreach (var block in roots.SelectMany(r => r.GetComponentsInChildren<BlockSetContainer>(true))
                .SelectMany(c => c.blockSets).SelectMany(s => s.Blocks))
            foreach (var material in new[] { block.Material }.Concat(block.Materials ?? Array.Empty<Material>()).Where(m => m != null))
            {
                if (material.shader.name != SurfaceShader || material.IsKeywordEnabled("NO_CURVE") || !material.enableInstancing)
                    throw new Exception("Terrain material shader/keyword/instancing mismatch: " + block.Name + "/" + material.name);
                rows.Add(material.name + " | " + material.shader.name);
            }
            var output = Environment.GetEnvironmentVariable("ECO_MINECART_AUDIT_DIR");
            if (!string.IsNullOrEmpty(output)) File.WriteAllLines(Path.Combine(output, "world-materials.txt"), rows.OrderBy(s => s));
            Debug.Log("ECO_ALL_WORLD_MATERIALS_CURVED_OK: " + rows.Count + " materials; surface test=" + NativeSurfaceTest);
        }
    }
}
