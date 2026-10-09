using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    // Imported artwork is visual-only. Native object identities, collision,
    // operating callbacks and authored animation targets remain authoritative.
    public static class MeshyInfrastructureAssetBuilder
    {
        const string Root="Assets/EcoMinecarts";
        const string Art=Root+"/MeshyArt";
        [Serializable] class Geometry {public string name;public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles;}
        static Mesh Mesh(string key)
        {
            var data=JsonUtility.FromJson<Geometry>(File.ReadAllText(Art+"/"+key+".json"));
            var path=Art+"/"+key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
            mesh.name="Meshy-"+key;mesh.vertices=data.vertices;mesh.normals=data.normals;mesh.uv=data.uv;mesh.triangles=data.triangles;
            mesh.colors32=Enumerable.Repeat(new Color32(255,255,255,255),data.vertices.Length).ToArray();
            mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.Optimize();EditorUtility.SetDirty(mesh);return mesh;
        }
        static Material Material(string key)
        {
            var max=key=="station"||key=="workbench"?2048:1024;
            Texture2D Import(string suffix,bool linear,bool readable)
            {
                var path=Art+"/"+key+"-"+suffix+".png";var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=!linear;importer.maxTextureSize=max;
                importer.isReadable=readable;importer.mipmapEnabled=true;importer.alphaSource=TextureImporterAlphaSource.None;
                importer.textureCompression=readable?TextureImporterCompression.Uncompressed:TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var albedo=Import("Albedo",false,false);var metallic=Import("Metallic",true,true);var rough=Import("Roughness",true,true);
            var pixels=metallic.GetPixels32();var roughPixels=rough.GetPixels32();
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(pixels[i].r,0,0,(byte)(255-roughPixels[i].r));
            var packed=new Texture2D(metallic.width,metallic.height,TextureFormat.RGBA32,true,true);packed.SetPixels32(pixels);packed.Apply();
            var packedPath=Art+"/"+key+"-MetalGloss.png";File.WriteAllBytes(packedPath,packed.EncodeToPNG());Object.DestroyImmediate(packed);
            AssetDatabase.ImportAsset(packedPath,ImportAssetOptions.ForceSynchronousImport);
            var packedImporter=(TextureImporter)AssetImporter.GetAtPath(packedPath);packedImporter.sRGBTexture=false;packedImporter.alphaSource=TextureImporterAlphaSource.FromInput;
            packedImporter.maxTextureSize=max;packedImporter.mipmapEnabled=true;packedImporter.textureCompression=TextureImporterCompression.CompressedHQ;packedImporter.SaveAndReimport();
            // CPU copies of the source maps are not required in a shipping bundle.
            Import("Metallic",true,false);Import("Roughness",true,false);
            var pathMaterial=Art+"/MAT_Meshy_"+key+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(pathMaterial);
            if(material==null){material=new Material(Shader.Find(RailWorldMaterialBuilder.SurfaceShader));AssetDatabase.CreateAsset(material,pathMaterial);}
            material.shader=Shader.Find(RailWorldMaterialBuilder.SurfaceShader);material.SetColor("_Color",Color.white);
            material.SetTexture("_MainTex",albedo);material.SetTextureScale("_MainTex",Vector2.one);material.SetTextureOffset("_MainTex",Vector2.zero);
            material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath));material.SetFloat("_GlossMapScale",.65f);
            material.EnableKeyword("_METALLICGLOSSMAP");material.enableInstancing=true;EditorUtility.SetDirty(material);return material;
        }
        static void Part(Transform parent,string key,Material material,Vector3 position)
        {
            var node=new GameObject("Meshy "+key,typeof(MeshFilter),typeof(MeshRenderer));node.transform.SetParent(parent,false);node.transform.localPosition=position;
            node.GetComponent<MeshFilter>().sharedMesh=Mesh(key);node.GetComponent<MeshRenderer>().sharedMaterial=material;
            var renderer=new SerializedObject(node.GetComponent<MeshRenderer>());renderer.FindProperty("m_SmallMeshCulling").boolValue=false;renderer.ApplyModifiedPropertiesWithoutUndo();
        }
        static void Prefab(string name,Action<GameObject> action)
        {
            var path=Root+"/Prefabs/"+name+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(var node in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Meshy ")).ToArray())Object.DestroyImmediate(node.gameObject);
                action(root);PrefabUtility.SaveAsPrefabAsset(root,path);
            }finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        static void Backing(GameObject root,string name,PrimitiveType type,Vector3 position,Vector3 size,Quaternion rotation,Material material)
        {
            var existing=root.transform.Find(name);if(existing!=null)Object.DestroyImmediate(existing.gameObject);
            var part=GameObject.CreatePrimitive(type);part.name=name;part.transform.SetParent(root.transform,false);
            part.transform.localPosition=position;part.transform.localScale=size;part.transform.localRotation=rotation;
            Object.DestroyImmediate(part.GetComponent<Collider>());part.GetComponent<Renderer>().sharedMaterial=material;
        }
        public static void RepairPresentation()
        {
            var darkPath=Root+"/Materials/MAT_DriveInterior.mat";
            var dark=AssetDatabase.LoadAssetAtPath<Material>(darkPath);
            if(dark==null){dark=new Material(Shader.Find(RailWorldMaterialBuilder.SurfaceShader));AssetDatabase.CreateAsset(dark,darkPath);}
            dark.color=new Color(.018f,.018f,.021f,1);dark.SetFloat("_Glossiness",.05f);EditorUtility.SetDirty(dark);
            foreach(var name in new[]{"MinecartChainDriveObject","ElectricalRailChainDriveObject","CoasterStationObject","TrainStationObject","RailcraftWorkbenchObject"}){
                string path=Root+"/Prefabs/"+name+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
                try{
                    var world=root.GetComponent<WorldObject>();if(world.size.x<=0||world.size.y<=0||world.size.z<=0)world.size=Vector3.one;
                    if(name.Contains("Drive"))Backing(root,"Drive dark interior",PrimitiveType.Cylinder,new Vector3(.018f,-.137f,-.22f),new Vector3(.30f,.006f,.30f),Quaternion.Euler(90,0,0),dark);
                    if(name=="CoasterStationObject"){
                        foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                            renderer.enabled=renderer.name=="Meshy station"||renderer.name=="Continuous timber platform"
                                ||renderer.transform.IsChildOf(root.transform.Find("CoasterStation"));
                        // Replace the perforated imported deck with a closed
                        // timber volume, rather than leaving a thin inset plane.
                        var source=AssetDatabase.LoadAssetAtPath<Mesh>(Art+"/station.asset");
                        var shellPath=Art+"/station-closed-shell.asset";var shell=AssetDatabase.LoadAssetAtPath<Mesh>(shellPath);
                        if(shell==null){shell=Object.Instantiate(source);AssetDatabase.CreateAsset(shell,shellPath);}else EditorUtility.CopySerialized(source,shell);
                        var vertices=source.vertices;var triangles=source.triangles;
                        // The generated artwork also baked two rails into the
                        // left half of the deck. The cabinet is entirely at
                        // x >= .08 below its sign; trim the rail/deck region
                        // while retaining the cabinet, controls and sign.
                        shell.triangles=Enumerable.Range(0,triangles.Length/3).Where(i=>{
                            var a=vertices[triangles[i*3]];var b=vertices[triangles[i*3+1]];var c=vertices[triangles[i*3+2]];
                            return !(a.y<=-.24f&&b.y<=-.24f&&c.y<=-.24f)
                                && !(Mathf.Max(a.y,Mathf.Max(b.y,c.y))<.75f&&Mathf.Min(a.x,Mathf.Min(b.x,c.x))<.06f);
                        }).SelectMany(i=>new[]{triangles[i*3],triangles[i*3+1],triangles[i*3+2]}).ToArray();
                        // Flatten the flared cabinet feet into its side walls,
                        // retaining the original triangles so no new holes open.
                        // Widen the cabinet and controls; leave post/sign intact.
                        for(var i=0;i<vertices.Length;i++)if(vertices[i].y<.62f){
                            bool cheek=vertices[i].x<.112f||vertices[i].x>.308f;
                            vertices[i].x=.209f+(vertices[i].x-.209f)*1.4f;
                            if(vertices[i].y<.30f){
                                if(vertices[i].z<-.435f)vertices[i].z=-.375f;
                                if(vertices[i].z>.115f)vertices[i].z=.100f;
                                if(vertices[i].y<-.17f&&vertices[i].z<-.38f)vertices[i].z=-.38f;
                                if(cheek&&vertices[i].z<-.28f&&vertices[i].z>-.399f)vertices[i].z=-.375f;
                            }
                        }
                        // Give the repaired side/heel faces independent UVs.
                        // Sharing their atlas coordinates with the door corners
                        // stretches unrelated texture islands across the wall.
                        var indices=shell.triangles;var points=vertices.ToList();var coords=source.uv.ToList();
                        for(var i=0;i<indices.Length;i+=3){
                            var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                            var normal=Vector3.Cross(b-a,c-a).normalized;
                            bool side=Mathf.Abs(normal.x)>.65f&&(a.z+b.z+c.z)/3>-.32f&&Mathf.Max(a.y,Mathf.Max(b.y,c.y))<.58f;
                            bool heel=Mathf.Max(a.y,Mathf.Max(b.y,c.y))<-.17f&&(Mathf.Min(a.x,Mathf.Min(b.x,c.x))<.073f||Mathf.Max(a.x,Mathf.Max(b.x,c.x))>.346f);
                            if(!side&&!heel)continue;
                            for(var j=0;j<3;j++){points.Add(vertices[indices[i+j]]);coords.Add(new Vector2(.708161f,.134340f));indices[i+j]=points.Count-1;}
                        }
                        shell.Clear();shell.vertices=points.ToArray();shell.uv=coords.ToArray();shell.triangles=indices;
                        shell.colors32=Enumerable.Repeat(new Color32(255,255,255,255),points.Count).ToArray();
                        shell.RecalculateNormals();shell.RecalculateTangents();
                        shell.name="Station closed shell";shell.RecalculateBounds();EditorUtility.SetDirty(shell);
                        root.transform.Find("Meshy station").GetComponent<MeshFilter>().sharedMesh=shell;
                        Backing(root,"Continuous timber platform",PrimitiveType.Cube,new Vector3(0,-.375f,0),new Vector3(.9f,.25f,1),Quaternion.identity,AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/MAT_WoodRail.mat"));
                        StationSignTextBuilder.Configure(root);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();Debug.Log("INFRASTRUCTURE_PRESENTATION_OK: backed drive faces, closed timber platform, positive station occupancy sizes");
        }
        static void Drive(string prefab,string key)
        {
            var material=Material(key);
            Prefab(prefab,root=>
            {
                var visual=root.transform.Find("ChainDriveVisual");
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))r.enabled=false;
                Part(root.transform,key+"-fixed",material,Vector3.zero);
                var shaft=visual.Find("ShaftIndicator");var mesh=Mesh(key+"-shaft");
                var center=mesh.bounds.center;shaft.localPosition=visual.InverseTransformPoint(root.transform.TransformPoint(center));
                // Source part coordinates are native root space; compensate the
                // original pivot while retaining the existing ChainLoop binding.
                Part(shaft,key+"-shaft",material,-center);
            });
        }
        static void Station()
        {
            var material=Material("station");Prefab("CoasterStationObject",root=>
            {
                foreach(var name in new[]{"Loading platform","Station control cabinet","Station sign post","Station sign"})root.transform.Find(name).GetComponent<MeshRenderer>().enabled=false;
                Part(root.transform,"station",material,Vector3.zero);
            });
        }
        static void Workbench()
        {
            var material=Material("workbench");Prefab("RailcraftWorkbenchObject",root=>
            {
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))r.enabled=false;
                Part(root.transform,"workbench-fixed",material,Vector3.zero);
                var ram=root.transform.Find("Workbench/PressRam");var origin=root.transform.InverseTransformPoint(ram.position);
                Part(ram,"workbench-ram",material,-origin);
            });
        }
        static Mesh SaveSupport(Mesh source,string name,Quaternion rotation,int phase,bool top)
        {
            var path=Art+"/"+name+".asset";var result=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(result==null){result=Object.Instantiate(source);AssetDatabase.CreateAsset(result,path);}else EditorUtility.CopySerialized(source,result);
            var delta=phase>0?(phase-1)*.25f:0;
            result.vertices=source.vertices.Select(p=>{
                if(top&&phase>0&&p.y>.40f)p=Quaternion.Euler(-Mathf.Atan(.25f)*Mathf.Rad2Deg,0,0)*(p-Vector3.up*.40f)+Vector3.up*(.40f+delta);
                else p.y+=delta*Mathf.Clamp01((p.y+.2f)/.6f);
                return rotation*p;
            }).ToArray();
            result.normals=source.normals.Select(n=>rotation*n).ToArray();result.RecalculateBounds();result.RecalculateTangents();result.name="Meshy-"+name;EditorUtility.SetDirty(result);return result;
        }
        static void Supports()
        {
            var material=Material("support");var top=Mesh("support");var middle=Mesh("support-middle");
            var set=AssetDatabase.LoadAssetAtPath<BlockSet>(Root+"/CoasterBlocks/RailSupports.asset");int count=0;
            foreach(var block in set.Blocks.Where(b=>b.Name.StartsWith("RailSupportSteel")&&!b.Name.Contains("Stacked")))
            {
                var usage=((CustomBuilder)block.Builder).usageCases[0];var turn=block.Name.EndsWith("R90")?90:block.Name.EndsWith("R180")?180:block.Name.EndsWith("R270")?270:0;
                int phase=0;var marker=block.Name.IndexOf("TopSlope",StringComparison.Ordinal);if(marker>=0)phase=block.Name[marker+8]-'0';
                var isTop=block.Name.Contains("Top");var source=isTop?top:middle;
                var rotation=Quaternion.Inverse(Quaternion.Euler(usage.importRotation))*Quaternion.Euler(0,turn,0);
                var model=SaveSupport(source,block.Name+"Visual",rotation,phase,isTop);
                var lods=usage.blockMeshLodGroup;lods.LOD0=new[]{new MeshAndFlags{mesh=model,concaveFaces=PerFaceFlag.All}};
                var sourceKey=isTop?"support":"support-middle";
                lods.LOD1=new MeshAndFlags{mesh=SaveSupport(Mesh(sourceKey+"-lod1"),block.Name+"VisualLod1",rotation,phase,isTop),concaveFaces=PerFaceFlag.All};
                lods.LOD2=new MeshAndFlags{mesh=SaveSupport(Mesh(sourceKey+"-lod2"),block.Name+"VisualLod2",rotation,phase,isTop),concaveFaces=PerFaceFlag.All};
                // Retain the exact native climb/landing collider. Artwork never
                // defines physics; the visual LODs share the imported UV atlas.
                block.Material=material;block.Materials=Array.Empty<Material>();
                if(usage.mesh!=null)
                {
                    var path=AssetDatabase.GetAssetPath(usage.mesh);var preview=PrefabUtility.LoadPrefabContents(path);
                    try{preview.GetComponent<MeshFilter>().sharedMesh=model;preview.GetComponent<MeshRenderer>().sharedMaterial=material;PrefabUtility.SaveAsPrefabAsset(preview,path);}
                    finally{PrefabUtility.UnloadPrefabContents(preview);}
                }
                EditorUtility.SetDirty(lods);EditorUtility.SetDirty(block.Builder);count++;
            }
            EditorUtility.SetDirty(set);Debug.Log("MESHY_SUPPORT_ART_OK: "+count+" steel variants; native climb collision retained");
        }
        public static bool Available=>File.Exists(Art+"/station.json");
        public static void Apply()
        {
            if(!Available)return;
            AssetDatabase.Refresh();Drive("MinecartChainDriveObject","mechanical");Drive("ElectricalRailChainDriveObject","electrical");Station();Workbench();Supports();
            RepairPresentation();
            AssetDatabase.SaveAssets();Debug.Log("MESHY_INFRASTRUCTURE_ART_OK: five supplied models; retained native identities and animation/collision targets");
        }
        public static void ApplyAndExport()=>MinecartAssetBuilder.BuildAuthoredClientBundle();
    }
}
