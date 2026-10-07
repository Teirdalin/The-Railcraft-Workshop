using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    // Block.MinimapColor defaults to pure green. Terrain's distant representation
    // also needs an explicit LOD texture, separately from the close-up material.
    public static class RailBlockDistanceAppearance
    {
        const string Root = "Assets/EcoMinecarts";
        const string Folder = Root + "/DistanceTextures";
        // Eco 0.14 MapConfig combines these into a 128px sRGB DXT1 array.
        // An arbitrary thumbnail size/format is not compatible with that array.
        const int Size = 128;

        public static void Apply()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Root, "DistanceTextures");
            var appearances = new Dictionary<Material, (Texture2D Texture, Color Color)>();
            var count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:BlockSet", new[] { Root }))
            {
                var set = AssetDatabase.LoadAssetAtPath<BlockSet>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var block in set.Blocks)
                {
                    if (!block.Rendered || block.IsEmpty) continue;
                    if (block.Material == null) throw new InvalidOperationException("Missing block material: " + block.Name);
                    if (!appearances.TryGetValue(block.Material, out var appearance))
                        appearances.Add(block.Material, appearance = Bake(block.Material));
                    block.MinimapColor = appearance.Color;
                    block.LODTexture = appearance.Texture;
                    count++;
                }
                EditorUtility.SetDirty(set);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("RAIL_BLOCK_DISTANCE_APPEARANCE_OK: " + count + " blocks; " + appearances.Count + " material textures; explicit map colors and distant textures.");
        }

        private static (Texture2D Texture, Color Color) Bake(Material material)
        {
            // Read through a small render target so imported textures can remain
            // non-readable. Preserve their texture detail and apply the tint once.
            var target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
            Color[] pixels;
            try
            {
                Graphics.Blit(material.mainTexture != null ? material.mainTexture : Texture2D.whiteTexture, target);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); readback.Apply();
                pixels = readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
            }
            var tint = material.HasProperty("_Color") ? material.color : Color.white;
            var average = Color.black;
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(Mathf.Clamp01(pixels[i].r * tint.r), Mathf.Clamp01(pixels[i].g * tint.g), Mathf.Clamp01(pixels[i].b * tint.b), 1);
                average += pixels[i];
            }
            average /= pixels.Length; average.a = 1;
            if (average.g > .99f && average.r < .01f && average.b < .01f)
                throw new InvalidOperationException("Unexpected green fallback for " + material.name);
            var id = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material));
            var path = Folder + "/" + id + ".asset";
            var baked = new Texture2D(Size, Size, TextureFormat.RGBA32, true, false);
            baked.name = material.name + "_Distant";
            baked.wrapMode = TextureWrapMode.Repeat; baked.filterMode = FilterMode.Trilinear;
            baked.SetPixels(pixels); baked.Apply(true, false);
            EditorUtility.CompressTexture(baked, TextureFormat.DXT1, TextureCompressionQuality.Best);
            if (baked.width != Size || baked.height != Size || baked.format != TextureFormat.DXT1
                || !baked.isDataSRGB || baked.mipmapCount != 8)
                throw new InvalidOperationException("Invalid Eco LOD texture: " + baked.name);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                texture = baked;
                AssetDatabase.CreateAsset(texture, path);
            }
            else
            {
                // Replace the serialized texture data without changing its GUID,
                // including when updating an older 32px/uncompressed asset.
                EditorUtility.CopySerialized(baked, texture);
                UnityEngine.Object.DestroyImmediate(baked);
            }
            EditorUtility.SetDirty(texture);
            Debug.Log("RAIL_DISTANCE_MATERIAL: " + material.name + " = " + average);
            return (texture, average);
        }
    }
}
