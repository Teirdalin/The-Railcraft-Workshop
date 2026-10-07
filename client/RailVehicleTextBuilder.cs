using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    public static class RailVehicleTextBuilder
    {
        const string Root = "Assets/EcoMinecarts";
        const string GroupName = "Optional vehicle nameplates";
        static Texture2D PrepareGlyphAtlas()
        {
            // Standard reads coverage, whereas TMP's stock SDF shader reads a
            // distance field. Bake coverage once in the editor; retain identical
            // atlas dimensions/UVs and the existing static glyph definitions.
            var source = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset").atlasTexture;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            var bitmap = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, true);
            try
            {
                Graphics.Blit(source, target); RenderTexture.active = target;
                bitmap.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                var pixels = bitmap.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var coverage = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.46f, .54f, pixels[i].a / 255f));
                    pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255));
                }
                bitmap.SetPixels32(pixels); bitmap.Apply(true, false);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
            bitmap.name = "RailVehicleGlyphCoverage"; bitmap.wrapMode = TextureWrapMode.Clamp; bitmap.filterMode = FilterMode.Trilinear;
            const string path = Root + "/Materials/RailVehicleGlyphCoverage.asset";
            var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (saved == null) { AssetDatabase.CreateAsset(bitmap, path); saved = bitmap; }
            else { EditorUtility.CopySerialized(bitmap, saved); Object.DestroyImmediate(bitmap); EditorUtility.SetDirty(saved); }
            return saved;
        }
        static TMP_FontAsset PrepareFont()
        {
            const string path = Root + "/Materials/RailVehicleStaticFont.asset";
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) == null &&
                !AssetDatabase.CopyAsset("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset", path))
                throw new Exception("Unable to copy pre-baked vehicle font");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.fallbackFontAssetTable.Clear();
            for (int i = 0; i < font.fontWeightTable.Length; ++i) font.fontWeightTable[i] = default;
            var serializedFont = new SerializedObject(font);
            var units = serializedFont.FindProperty("m_FaceInfo.m_UnitsPerEM");
            if (units != null) units.intValue = 2048;
            serializedFont.FindProperty("m_SourceFontFile").objectReferenceValue = null;
            serializedFont.ApplyModifiedPropertiesWithoutUndo();
            // The copied atlas and material are embedded sub-assets. Do not retain
            // a dynamic fallback or ask the player's native FontEngine to bake it.
            font.atlasTextures = new[] { PrepareGlyphAtlas() };
            font.material.name = "MAT_DestinationText";
            RailWorldMaterialBuilder.TextMaterial(font.material, font.atlasTexture);
            font.ReadFontAssetDefinition();
            if (font.atlasTexture == null || font.characterTable.Count < 90 || font.atlasPopulationMode != AtlasPopulationMode.Static)
                throw new Exception("Static vehicle font is incomplete");
            EditorUtility.SetDirty(font); EditorUtility.SetDirty(font.material);
            return font;
        }

        public static void Apply()
        {
            var font = PrepareFont();
            var plateMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MAT_VehicleNameplate.mat");
            if (plateMaterial == null)
            {
                plateMaterial = new Material(Shader.Find("Curved/Standard"));
                AssetDatabase.CreateAsset(plateMaterial, Root + "/Materials/MAT_VehicleNameplate.mat");
            }
            plateMaterial.color = new Color(.045f, .055f, .06f); plateMaterial.enableInstancing = true;
            EditorUtility.SetDirty(plateMaterial);
            var count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root + "/Prefabs" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab.GetComponent<Vehicle>() == null || prefab.name.Contains("Coaster") || prefab.name.Contains("Handcar")) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var vehicle = root.GetComponent<Vehicle>();
                    var old = root.transform.Find(GroupName);
                    if (old != null)
                    {
                        foreach (var trim in old.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == "Destination frame" || r.name == "Destination stile").ToArray())
                            trim.transform.SetParent(root.transform, true);
                        Object.DestroyImmediate(old.gameObject);
                    }
                    foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) Object.DestroyImmediate(text.gameObject);
                    var group = new GameObject(GroupName); group.transform.SetParent(root.transform, false);
                    var labels = new List<TextMeshPro>();
                    void Plate(Vector3 position, float yaw, float width, float height)
                    {
                        var node = new GameObject("Vehicle lettering", typeof(RectTransform));
                        node.transform.SetParent(group.transform, false);
                        node.transform.localPosition = position;
                        node.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                        var text = node.AddComponent<TextMeshPro>();
                        text.font = font; text.fontSharedMaterial = font.material;
                        text.text = ""; text.richText = false;
                        text.alignment = TextAlignmentOptions.Center;
                        text.color = new Color(.95f, .89f, .68f);
                        text.enableAutoSizing = true; text.fontSize = 1.4f;
                        text.fontSizeMin = .35f; text.fontSizeMax = 1.4f;
                        text.overflowMode = TextOverflowModes.Ellipsis;
                        text.rectTransform.sizeDelta = new Vector2(width - .045f, height - .025f);
                        var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        backing.name = "Nameplate backing"; backing.transform.SetParent(node.transform, false);
                        backing.transform.localPosition = new Vector3(0, 0, .014f);
                        backing.transform.localScale = new Vector3(width, height, .022f);
                        Object.DestroyImmediate(backing.GetComponent<Collider>());
                        backing.GetComponent<Renderer>().sharedMaterial = plateMaterial;
                        labels.Add(text);
                    }
                    var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                    var destinationBoards = renderers.Where(r => r.name == "Destination board").ToArray();
                    var rearPanel = renderers.FirstOrDefault(r => r.name == "Cab rear panel");
                    var coachSides = renderers.Where(r => r.name == "Coach side" || r.name == "Cargo side").ToArray();
                    if (root.name == "HeritageTramObject")
                    {
                        foreach (var end in new[] { -1, 1 })
                            Plate(new Vector3(0, 2.22f, end * 1.347f), end > 0 ? 180 : 0, 1.1524f, .27f);
                        foreach (var board in destinationBoards) Object.DestroyImmediate(board.gameObject);
                        foreach (var trim in renderers.Where(r => r.name == "Destination frame" || r.name == "Destination stile"))
                            trim.transform.SetParent(group.transform, true);
                    }
                    else if (rearPanel != null)
                    {
                        var p = root.transform.InverseTransformPoint(rearPanel.bounds.center);
                        Plate(p + new Vector3(0, 0, -rearPanel.bounds.extents.z - .02f), 0, .72f, .19f);
                    }
                    else if (coachSides.Length > 0)
                    {
                        foreach (var panel in coachSides)
                        {
                            var p = root.transform.InverseTransformPoint(panel.bounds.center);
                            var side = Mathf.Sign(p.x);
                            // Mount on the outside of ribs and decorative straps,
                            // not behind them on the recessed sheet-metal panel.
                            var surface = renderers.Where(r => r.bounds.max.y >= p.y - .09f && r.bounds.min.y <= p.y + .09f
                                && r.bounds.max.z >= p.z - .35f && r.bounds.min.z <= p.z + .35f)
                                .Max(r => side > 0 ? r.bounds.max.x : -r.bounds.min.x);
                            Plate(new Vector3(side * (surface + .022f), p.y, p.z), side > 0 ? -90 : 90, .70f, .18f);
                        }
                    }
                    else if (root.name == "WoodenMinecartObject")
                    {
                        foreach (var side in new[] { -1, 1 })
                            Plate(new Vector3(side * .39f, .52f, .25f), side > 0 ? -90 : 90, .40f, .12f);
                    }
                    else
                    {
                        // Compact minecart: a small label centred on each bucket
                        // side, away from either coupling and the grab handles.
                        var bucket = renderers.FirstOrDefault(r => r.name == "Minecart_Body");
                        if (bucket == null) throw new Exception("No solid panel for vehicle text: " + root.name);
                        var p = root.transform.InverseTransformPoint(bucket.bounds.center);
                        foreach (var side in new[] { -1, 1 })
                            Plate(new Vector3(p.x + side * (bucket.bounds.extents.x + .015f), p.y, p.z), side > 0 ? -90 : 90, .46f, .14f);
                    }
                    vehicle.LicensePlate = labels[0]; vehicle.ExtraLicensePlates = labels.Skip(1).ToArray();
                    var states = vehicle.States; var changed = vehicle.OnStateChangedEvents;
                    var on = vehicle.OnStateEnabledEvents; var off = vehicle.OnStateDisabledEvents;
                    var index = Array.IndexOf(states, "VehicleTextVisible");
                    if (index < 0)
                    {
                        index = states.Length; Array.Resize(ref states, index + 1); Array.Resize(ref changed, index + 1);
                        Array.Resize(ref on, index + 1); Array.Resize(ref off, index + 1); states[index] = "VehicleTextVisible";
                    }
                    changed[index] = new ChangedStateEvent(); on[index] = new SetStateEvent(); off[index] = new SetStateEvent();
                    // Native Vehicle.BindLicensePlate starts a coroutine on each
                    // TMP object even for blank text. Keep those objects active;
                    // toggle the text components and backing renderers instead.
                    foreach(var label in labels) {
                        var setter=(UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),label,typeof(Behaviour).GetProperty("enabled").GetSetMethod());
                        UnityEventTools.AddPersistentListener(changed[index],setter);
                        label.enabled=false;
                    }
                    foreach(var renderer in group.GetComponentsInChildren<MeshRenderer>(true)) {
                        if(renderer.GetComponent<TextMeshPro>()!=null)continue;
                        var setter=(UnityEngine.Events.UnityAction<bool>)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction<bool>),renderer,typeof(Renderer).GetProperty("enabled").GetSetMethod());
                        UnityEventTools.AddPersistentListener(changed[index],setter);
                        renderer.enabled=false;
                    }
                    group.SetActive(true);
                    vehicle.States = states; vehicle.OnStateChangedEvents = changed;
                    vehicle.OnStateEnabledEvents = on; vehicle.OnStateDisabledEvents = off;
                    PrefabUtility.SaveAsPrefabAsset(root, path); count++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("RAIL_VEHICLE_TEXT_OK: " + count + " vehicles; static font; blank plates hidden");
        }
    }
}
