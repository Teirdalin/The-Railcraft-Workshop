using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
namespace EcoMinecarts.Editor
{
    // Authoring only. The installed native PaintableComponent/WorldObject own
    // permissions, inventory consumption, coating, replication and persistence.
    public static class RailVehiclePaintBuilder
    {
        public const string ShaderName="EcoMinecarts/Curved Paintable Vehicle";
        const string Root="Assets/EcoMinecarts";
        static readonly Dictionary<string,Material> Cache=new Dictionary<string,Material>();
        static bool Has(string value,params string[] words)=>words.Any(w=>value.IndexOf(w,StringComparison.OrdinalIgnoreCase)>=0);
        // Native paint channels: 1 bodywork, 2 underframe/running gear, 3 roof.
        // Use the authored part rather than its source material; several zones
        // intentionally share an iron or wood material before they are painted.
        internal static int RegionName(string name)
        {
            if(Has(name,"Roof","Canopy","Clerestory","Destination frame","Destination stile")) return 3;
            if(Has(name,"Frame","Chassis","Underframe","RunningGear","Running board","RunningBoard",
                "Wheel","Spoke","Hub","Flange","Tyre","Tire","Axle","Bearing","Spring","Brake",
                "Piston","Cylinder","Crosshead","Connecting rod","Rod crank","Valve linkage",
                "Boiler band","BoilerBand","Boiler saddle","Rivet","Hinge","Bezel",
                "Drawbar","Coupler","Coupling","Buffer","Footplate","Deck","Floor",
                "Cab sill","Cab outrigger","Cab riser","Step","Tread","Handrail","Rail","Grip",
                "Lever","Handle","Control","Escutcheon","Gauge","needle","tick","Seat","Bench")) return 2;
            return 1;
        }
        internal static int Region(Renderer renderer)
        {
            if(renderer is ParticleSystemRenderer || renderer.GetComponent<TMP_Text>()!=null) return 0;
            // Static finish meshes are batched by material. Their batch names
            // alone lose the original part names, so the group is authoritative.
            var group=renderer.transform.parent;
            if(group!=null && group.parent!=null && group.parent.name=="VehicleDetail")
                return group.name=="Roof"?3:group.name=="Frame"?2:1;
            return RegionName(renderer.name);
        }
        static Material PaintMaterial(Material source,int channel)
        {
            var key=source.name+"_Paint"+channel;
            if(Cache.TryGetValue(key,out var cached)) return cached;
            var shader=Shader.Find(ShaderName)??throw new InvalidOperationException("Missing native-compatible vehicle paint shader.");
            var maskPath=Root+"/Materials/VehiclePaintRegion"+channel+".asset";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            if(texture==null){
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                texture.name="VehiclePaintRegion"+channel;texture.wrapMode=TextureWrapMode.Repeat;texture.filterMode=FilterMode.Point;
                var color=channel==1?Color.red:channel==2?Color.green:Color.blue;
                texture.SetPixels(new[]{color,color,color,color});texture.Apply();AssetDatabase.CreateAsset(texture,maskPath);
            }
            // Load persistent assets after creation/import. Keep the texture
            // dependency alive across Unity's synchronous prefab/icon imports.
            AssetDatabase.ImportAsset(maskPath,ImportAssetOptions.ForceSynchronousImport);
            texture=AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            if(texture==null||!EditorUtility.IsPersistent(texture)) throw new InvalidOperationException("Paint mask was not imported: "+maskPath);
            var path=Root+"/Materials/"+key+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
            material=AssetDatabase.LoadAssetAtPath<Material>(path);
            // Do not copy the Standard shader's entire property sheet: Unity 6
            // can retain its texture layout and silently reject new mask slots.
            var authored=new Material(shader);
            authored.name=key;
            authored.SetColor("_Color",source.GetColor("_Color"));
            foreach(var property in new[]{"_MainTex","_BumpMap"}){
                authored.SetTexture(property,source.GetTexture(property));
                authored.SetTextureScale(property,source.GetTextureScale(property));
                authored.SetTextureOffset(property,source.GetTextureOffset(property));
            }
            foreach(var property in new[]{"_BumpScale","_Metallic","_Glossiness"}) authored.SetFloat(property,source.GetFloat(property));
            foreach(var name in new[]{"_ChannelRedColor","_ChannelGreenColor","_ChannelBlueColor"}) authored.SetColor(name,Color.clear);
            authored.SetFloat("_PaintedAmount",0);authored.SetTexture("_PaintCombinedTexture",texture);
            if(authored.GetTexture("_PaintCombinedTexture")==null)
                throw new InvalidOperationException("Paint mask slot rejected texture: "+shader.GetPropertyType(shader.FindPropertyIndex("_PaintCombinedTexture")));
            EditorUtility.CopySerialized(authored,material);UnityEngine.Object.DestroyImmediate(authored);EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            if(material.GetTexture("_PaintCombinedTexture")==null) throw new InvalidOperationException("Paint material lost its mask: "+key);
            Cache[key]=material;return material;
        }
        public static void Apply(GameObject[] prefabs)
        {
            Cache.Clear();var vehicles=0;var surfaces=0;
            foreach(var prefab in prefabs.Where(p=>p.GetComponent<RCCCarControllerV2>()!=null))
            {
                var path=AssetDatabase.GetAssetPath(prefab);var root=PrefabUtility.LoadPrefabContents(path);
                try{
                    var count=0;
                    foreach(var renderer in root.GetComponentsInChildren<Renderer>(true)){
                        if(renderer is MeshRenderer){
                            // These small, separate meshes are otherwise culled
                            // independently at distance, leaving a skeleton of
                            // boiler/cab while wheels and fittings disappear.
                            var flags=new SerializedObject(renderer);
                            var small=flags.FindProperty("m_SmallMeshCulling");
                            if(small==null) throw new InvalidOperationException("Unity renderer small-mesh culling flag unavailable: "+renderer.name);
                            small.boolValue=false;flags.ApplyModifiedPropertiesWithoutUndo();
                        }
                        var materials=renderer.sharedMaterials;
                        for(var i=0;i<materials.Length;i++){
                            var source=materials[i];if(source==null) continue;
                            // Idempotent refreshes reclassify from original assets.
                            var suffix=source.name.LastIndexOf("_Paint",StringComparison.Ordinal);
                            if(suffix>=0) source=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+source.name.Substring(0,suffix)+".mat")??source;
                            var region=Region(renderer);
                            materials[i]=region==0?source:PaintMaterial(source,region);
                            if(region!=0) count++;
                        }
                        renderer.sharedMaterials=materials;
                    }
                    if(count==0) throw new InvalidOperationException("Rail vehicle has no paintable body: "+prefab.name);
                    // Alpha zero means native paint removal restores the exact
                    // original material, not an arbitrary default paint colour.
                    var world=new SerializedObject(root.GetComponent<WorldObject>());
                    foreach(var field in new[]{"defaultRedColor","defaultGreenColor","defaultBlueColor"}) world.FindProperty(field).colorValue=Color.clear;
                    world.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root,path);vehicles++;surfaces+=count;
                }finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            Debug.Log("ECO_VEHICLE_PAINT_AUTHORING_OK: vehicles="+vehicles+" surfaces="+surfaces);
        }
        public static void Refresh()
        {
            EditorSceneManager.OpenScene(Root+"/Scenes/EcoMinecarts.unity",OpenSceneMode.Single);
            var prefabs=UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .Single(n=>n.name=="Objects").GetComponent<ModkitPrefabContainer>().Prefabs;
            Apply(prefabs);
            // Only vehicle art changes; the reviewed Eco-style framing is retained.
            foreach(var prefab in prefabs.Where(p=>p.GetComponent<RCCCarControllerV2>()!=null))
                MinecartIconBuilder.Render(prefab,prefab.name.Substring(0,prefab.name.Length-6));
            AssetDatabase.SaveAssets();MinecartAssetBuilder.BuildSavedClientBundle();
        }
    }
}
