using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    // Render the actual game meshes, never the asset-showcase assembly with extra track.
    public static class MinecartIconBuilder
    {
        private const string Root = "Assets/EcoMinecarts";
        // Classification is semantic: turnouts and complete coaster sections are
        // construction pieces even where older saves represent them as objects.
        private static readonly HashSet<string> ObjectIcons = new HashSet<string> {
            "Minecart","WoodenMinecart","MineTrain","ChainDrive","MinecartChainDrive",
            "CoalTender","LargeCoalTender","LargeCargoCar","PassengerCar","LargePassengerCar",
            "LargeTrainEngine","PassengerLocomotive","FreightLocomotive","RailroadHandcar",
            "RollerCoasterCart","TrainStation","CoasterStation","RailcraftWorkbench","HeritageTram","TramCableDrive","TramStop"
        };
        public static bool IsBuilding(string name)
        {
            if(ObjectIcons.Contains(name)) return false;
            if(name=="MinecartDumpRail" || name=="Rail" || name=="BrokenWoodenTrack" || name.StartsWith("MinecartTrack")
                || name.StartsWith("MinecartChain") || name.StartsWith("WoodenTrack")
                || name.StartsWith("IndustrialTrack") || name.StartsWith("IndustrialChain") || name.StartsWith("RailSwitch")
                || name.StartsWith("IndustrialRailSwitch") || name.StartsWith("WideRail")
                || name.StartsWith("Coaster") || name.StartsWith("RailSupport") || name.StartsWith("TramTrack")
                || name.StartsWith("TramRailSwitch") || name.StartsWith("TramWideRailSwitch") || name=="TramWideRailTurn") return true;
            throw new InvalidOperationException("Unclassified icon: "+name);
        }
        private static readonly Dictionary<bool,Texture2D> Backdrops = new Dictionary<bool,Texture2D>();
        private static Texture2D Backdrop(bool building)
        {
            // Unity can destroy runtime textures during an AssetDatabase refresh
            // between BuildClientContent and BuildSavedClientBundle in batch mode.
            if(Backdrops.TryGetValue(building,out var cached)&&cached!=null) return cached;
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            var path=Root+"/IconBackdrops/"+(building?"Brown Backdrop.png":"Blue Backdrop.png");
            if(!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("Cannot read "+path);
            Backdrops[building]=texture;return texture;
        }
        private static void ApplyBackdrop(Texture2D foreground,string name)
        {
            var bg=Backdrop(IsBuilding(name));
            var pixels=foreground.GetPixels();
            var result=new Color[pixels.Length];
            for(var y=0;y<foreground.height;y++) for(var x=0;x<foreground.width;x++)
            {
                var i=y*foreground.width+x;
                var backdrop=bg.GetPixelBilinear((x+.5f)/foreground.width,(y+.5f)/foreground.height);
                // Subtle offset shadow grounds the unchanged model artwork.
                var sx=x-2;var sy=y+3;
                var shadow=sx>=0 && sy<foreground.height ? pixels[sy*foreground.width+sx].a*.24f : 0;
                backdrop.r*=1-shadow;backdrop.g*=1-shadow;backdrop.b*=1-shadow;
                var a=pixels[i].a;
                var output=Color.Lerp(backdrop,pixels[i],a);
                output.a=a+backdrop.a*(1-a);
                result[i]=output;
            }
            foreground.SetPixels(result);foreground.Apply();
        }
        // Re-render from the same prefabs/camera, rather than painting over old
        // thumbnails. Repeated builds cannot stack frames or degrade artwork.
        public static void RefreshAll()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Root+"/Scenes/EcoMinecarts.unity");
            var prefabs=AssetDatabase.FindAssets("t:Prefab",new[]{Root})
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            var entries=new List<IconEntry>();
            foreach(var icon in Directory.GetFiles(Root+"/Icons","*.png").OrderBy(p=>p))
            {
                var name=Path.GetFileNameWithoutExtension(icon);
                var key=name=="ChainDrive"?"MinecartChainDrive":name=="Rail"?"MinecartTrackStraight":name;
                var path=prefabs.FirstOrDefault(p=>Path.GetFileNameWithoutExtension(p)==key+"Object")
                    ?? prefabs.FirstOrDefault(p=>Path.GetFileNameWithoutExtension(p)==key+"Block");
                if(path==null) throw new InvalidOperationException("No source model for icon "+name);
                Render(AssetDatabase.LoadAssetAtPath<GameObject>(path),name);
                entries.Add(new IconEntry{name=name,category=IsBuilding(name)?"Building":"Object",prefab=path});
            }
            File.WriteAllText(Root+"/Icons/icon-categories.json",JsonUtility.ToJson(new IconReport{icons=entries.ToArray()},true));
            AssetDatabase.SaveAssets();
            MinecartAssetBuilder.BuildSavedClientBundle();
            Debug.Log("ECO_ICON_BACKDROPS_OK: "+entries.Count+" icons classified and rebuilt.");
        }
        [Serializable] private class IconEntry { public string name,category,prefab; }
        [Serializable] private class IconReport { public IconEntry[] icons; }
        public static Bounds BoundsOf(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException(target.name + " has no enabled mesh renderers.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        public static void Build(IReadOnlyList<GameObject> worldPrefabs)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Icon rendering requires graphics: use Unity -batchmode without -nographics.");
            Render(worldPrefabs.First(p => p.name == "MinecartObject"), "Minecart");
            Render(worldPrefabs.First(p => p.name == "MinecartChainDriveObject"), "ChainDrive");
            Render(worldPrefabs.First(p => p.name == "MineTrainObject"), "MineTrain");
            Render(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/TrackBlocks/MinecartChainBendLeftBlock.prefab"), "MinecartChainBendLeft");
            foreach (var prefab in worldPrefabs.Where(p => p.name != "MinecartObject" && p.name != "MineTrainObject" && p.name != "MinecartChainDriveObject"))
                Render(prefab, prefab.name.Substring(0, prefab.name.Length - 6));
            foreach (var prefix in new[] { "MinecartTrack", "MinecartChain", "WoodenTrack", "TramTrack" })
            {
                var shapes = new[] { "Straight", "Bend", "Stopper", "Slope1", "Slope2", "Slope3", "Slope4", "RampTop", "RampTop1", "RampTop2", "RampTop3", "RampTop4" };
                foreach (var shape in prefix == "TramTrack" ? shapes.Concat(new[] { "Crossing" }) : shapes)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/TrackBlocks/" + prefix + shape + "Block.prefab");
                    Render(prefab, prefix + shape);
                }
            }
            Debug.Log("ECO_ITEM_ICONS_OK: isolated cart, chain drive and 24 track form thumbnails.");
        }

        public static void Render(GameObject prefab, string name)
        {
            var preview = new GameObject("IconPreview");
            var instance = Object.Instantiate(prefab, preview.transform);
            instance.SetActive(true);
            foreach(var animator in instance.GetComponentsInChildren<Animator>(true))animator.enabled=false;
            // Evaluate only the detailed model for a still thumbnail. Unity's
            // LOD selection otherwise waits for a frame and all levels can
            // overlap during the immediate Camera.Render call.
            foreach(var group in instance.GetComponentsInChildren<LODGroup>(true)){
                group.ForceLOD(0);var levels=group.GetLODs();
                for(int i=0;i<levels.Length;i++)foreach(var renderer in levels[i].renderers)renderer.enabled=i==0;
            }
            foreach (var child in instance.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            var bounds = BoundsOf(instance);
            var camera = new GameObject("IconCamera").AddComponent<Camera>();
            camera.transform.SetParent(preview.transform);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << 31;
            camera.transform.position = bounds.center + new Vector3(2, 1.7f, 2) * Mathf.Max(bounds.size.magnitude, 1);
            camera.transform.LookAt(bounds.center);
            camera.orthographic = true;
            var extent=0f;
            for(var x=-1;x<=1;x+=2) for(var y=-1;y<=1;y+=2) for(var z=-1;z<=1;z+=2)
            {
                var corner=camera.transform.InverseTransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
                extent=Mathf.Max(extent,Mathf.Abs(corner.x),Mathf.Abs(corner.y));
            }
            camera.orthographicSize = Mathf.Max(extent*(name.StartsWith("CoasterTrack")?1.35f:1.18f), .1f);
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 100;
            camera.allowHDR = false;
            var light = new GameObject("IconLight").AddComponent<Light>();
            light.transform.SetParent(preview.transform);
            light.type = LightType.Directional;
            light.intensity = 1;
            light.cullingMask = 1 << 31;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            var oldAmbient = RenderSettings.ambientMode;
            var oldColor = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.43f, .43f, .43f);
            var target = new RenderTexture(256,256,24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var oldTarget = RenderTexture.active;
            var texture = new Texture2D(256,256,TextureFormat.RGBA32,false);
            try
            {
                camera.targetTexture = target;
                var noCurve = Shader.IsKeywordEnabled("NO_CURVE");
                Shader.EnableKeyword("NO_CURVE");
                try { camera.Render(); }
                finally { if (!noCurve) Shader.DisableKeyword("NO_CURVE"); }
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0,0,256,256),0,0);
                texture.Apply();
                if (texture.GetPixels32().Count(p => p.a > 128) < 500) throw new InvalidOperationException("Empty icon: " + name+"; bounds="+bounds+"; active renderers="+instance.GetComponentsInChildren<MeshRenderer>().Count(r=>r.enabled));
                ApplyBackdrop(texture,name);
                var path = Root + "/Icons/" + name + ".png";
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                // Native Eco item icons: 128 px / 150 PPU. Our 256 px artwork
                // retains its resolution but must have the same physical sprite size.
                importer.spritePixelsPerUnit = 300;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }
            finally
            {
                RenderTexture.active = oldTarget;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(preview);
                RenderSettings.ambientMode = oldAmbient;
                RenderSettings.ambientLight = oldColor;
            }
        }
    }
}
