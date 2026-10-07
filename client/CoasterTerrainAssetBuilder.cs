using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class CoasterTerrainAssetBuilder
    {
        const string Root="Assets/EcoMinecarts/CoasterBlocks";
        [Serializable] public class TileDefinition:CoasterAssetBuilder.Path
        {public string SourceKey,MenuGroup;public int Section,Count,X,Y,Z;}
        static T Save<T>(T value,string path) where T:Object
        {
            var existing=AssetDatabase.LoadAssetAtPath<T>(path);
            if(existing==null){AssetDatabase.CreateAsset(value,path);return value;}
            EditorUtility.CopySerialized(value,existing);Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);return existing;
        }
        static void Box(Transform parent,Vector3 p,Vector3 size,Quaternion rotation,Material material)
        {
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.transform.SetParent(parent,false);
            box.transform.localPosition=p;box.transform.localScale=size;box.transform.localRotation=rotation;
            box.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(box.GetComponent<Collider>());
        }
        // Whole sections use this same swept profile so their rail faces meet
        // hammer-built blocks without a box-segment seam at the socket.
        public static GameObject Geometry(CoasterAssetBuilder.Path section,Material steel,Material paint)
        {
            var root=new GameObject(section.Key);
            foreach(var side in new[]{-1,1})Sweep(root.transform,section,side*.30f,-.035f,.055f,.07f,steel);
            Sweep(root.transform,section,0,-.16f,.10f,.12f,paint);
            CoasterAssetBuilder.AtSpacing(section.Points,.24f,(center,up,rotation,interval)=>
                Box(root.transform,center-up*.085f,new Vector3(.72f,.055f,Mathf.Min(.06f,interval*.5f)),rotation,paint));
            if(section.Chain)CoasterAssetBuilder.AtSpacing(section.Points,.10f,(center,up,rotation,interval)=>{
                var right=rotation*Vector3.right;
                var forward=rotation*Vector3.forward;
                // Match the minecart chain's open U links. The rear pin sits at
                // -Z and the open tips point along +Z, the chain's pull direction.
                // Short arms leave a visible gap before the next link's rear pin.
                var length=Mathf.Min(.06f,interval*.6f);
                foreach(var side in new[]{-1,1})Box(root.transform,center+right*side*.055f-up*.015f,new Vector3(.018f,.025f,length),rotation,steel);
                Box(root.transform,center-up*.015f-forward*(length*.5f),new Vector3(.11f,.025f,Mathf.Min(.025f,interval*.25f)),rotation,steel);
            });
            if(section.Key.Contains("ChainBrake"))
            {
                var mid=section.Points[section.Points.Length/2];
                foreach(var side in new[]{-1,1})
                    Box(root.transform,mid.Position+Vector3.right*(side*.20f)-mid.Up*.04f,
                        new Vector3(.07f,.05f,.86f),Quaternion.identity,paint);
            }
            return root;
        }
        static GameObject SnapEndGeometry(Material steel,Material paint)
        {
            var root=new GameObject("CoasterTrackSnapEnd");
            // These unobtrusive end shoes are actual terrain blocks. Their
            // lateral tabs remain visible and targetable beside the swept rail.
            Box(root.transform,new Vector3(0,-.51f,0),new Vector3(.83f,.06f,.27f),Quaternion.identity,steel);
            foreach(var side in new[]{-1,1})
                Box(root.transform,new Vector3(side*.40f,-.46f,0),new Vector3(.07f,.11f,.31f),Quaternion.identity,paint);
            return root;
        }
        // Shared rings make the running surface continuous, including twisted
        // banks. Separate short boxes leave wedge-shaped gaps at curved joints.
        static void Sweep(Transform parent,CoasterAssetBuilder.Path section,float side,float vertical,float width,float height,Material material)
        {
            var points=section.Points;var vertices=new Vector3[points.Length*8+8];var uv=new Vector2[vertices.Length];
            var normals=new Vector3[vertices.Length];
            var triangles=new List<int>();float distance=0;
            for(var i=0;i<points.Length;i++)
            {
                var tangent=points[Math.Min(i+1,points.Length-1)].Position-points[Math.Max(i-1,0)].Position;
                var up=points[i].Up.normalized;var right=Vector3.Cross(up,tangent.normalized).normalized;
                var centre=points[i].Position+right*side+up*vertical;
                if(i>0)distance+=Vector3.Distance(points[i-1].Position,points[i].Position);
                for(var face=0;face<4;face++)for(var endpoint=0;endpoint<2;endpoint++)
                {
                    var j=(face+endpoint)%4;var index=i*8+face*2+endpoint;
                    vertices[index]=centre+right*(j==1||j==2?width/2:-width/2)+up*(j>=2?height/2:-height/2);
                    normals[index]=face==0?-up:face==1?right:face==2?up:-right;
                    uv[index]=new Vector2(endpoint,distance);
                    if(i==0||endpoint!=0)continue;
                    var a=(i-1)*8+face*2;var next=a+1;var b=i*8+face*2;var bn=b+1;
                    triangles.AddRange(new[]{a,next,b,b,next,bn});
                }
            }
            // Separate cap normals keep square ends and a crisp rectangular
            // profile without disturbing the shared longitudinal surface.
            var start=points.Length*8;var end=start+4;
            for(var j=0;j<4;j++){
                vertices[start+j]=vertices[j*2];vertices[end+j]=vertices[(points.Length-1)*8+j*2];
                var firstRight=Vector3.Cross(points[0].Up,points[1].Position-points[0].Position).normalized;
                var lastRight=Vector3.Cross(points[points.Length-1].Up,points[points.Length-1].Position-points[points.Length-2].Position).normalized;
                normals[start+j]=-Vector3.Cross(firstRight,points[0].Up).normalized;
                normals[end+j]=Vector3.Cross(lastRight,points[points.Length-1].Up).normalized;
                uv[start+j]=uv[end+j]=new Vector2(j%2,j/2);
            }
            triangles.AddRange(new[]{start,start+2,start+1,start,start+3,start+2,end,end+1,end+2,end,end+2,end+3});
            var mesh=new Mesh();mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
            // Faceted endpoint averages depend on the first short edge. Use
            // the authored frame so both sides of a compact socket shade alike.
            if(section.Key.Contains("Compact"))mesh.normals=normals;
            var obj=new GameObject("Continuous rail",typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(parent,false);
            obj.GetComponent<MeshFilter>().sharedMesh=mesh;obj.GetComponent<MeshRenderer>().sharedMaterial=material;
        }
        public static void Register(BlockSet set,GameObject geometry,string key,Material steel,Material paint)
        {
            var name=key+"Block";
            var parts=new List<Mesh>();
            var renderMaterials=new[]{steel,paint};
            foreach(var material in renderMaterials)
            {
                var part=new Mesh();part.CombineMeshes(geometry.GetComponentsInChildren<MeshFilter>().Where(f=>f.GetComponent<Renderer>().sharedMaterial==material)
                    .Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=geometry.transform.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true);
                parts.Add(part);
            }
            var mesh=new Mesh{name=name+"TerrainMesh"};mesh.CombineMeshes(parts.Select(p=>new CombineInstance{mesh=p,transform=Matrix4x4.identity}).ToArray(),false);
            mesh.colors32=Enumerable.Repeat(new Color32(255,255,255,255),mesh.vertexCount).ToArray();mesh.RecalculateBounds();mesh.RecalculateTangents();
            mesh=Save(mesh,Root+"/"+name+"Mesh.asset");foreach(var part in parts)Object.DestroyImmediate(part);
            var collision=new Mesh{name=name+"Collision"};collision.CombineMeshes(Enumerable.Range(0,mesh.subMeshCount).Select(i=>new CombineInstance{mesh=mesh,subMeshIndex=i,transform=Matrix4x4.identity}).ToArray(),true);
            collision=Save(collision,Root+"/"+name+"Collision.asset");
            var lods=ScriptableObject.CreateInstance<BlockMeshLodGroup>();
            lods.LOD0=new[]{new MeshAndFlags{mesh=mesh,concaveFaces=PerFaceFlag.All}};
            lods.LOD1=new MeshAndFlags{mesh=collision,concaveFaces=PerFaceFlag.All};lods.LOD2=lods.LOD1;lods.Collider=collision;
            lods=Save(lods,Root+"/"+name+"LODs.asset");
            var source=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));source.GetComponent<MeshFilter>().sharedMesh=mesh;
            source.GetComponent<MeshRenderer>().sharedMaterials=renderMaterials;
            var prefab=PrefabUtility.SaveAsPrefabAsset(source,Root+"/"+name+".prefab");Object.DestroyImmediate(source);
            var builder=ScriptableObject.CreateInstance<CustomBuilder>();builder.usageCases=new List<MeshUsageCase>{new MeshUsageCase{
                enabled=true,mesh=prefab,blockMeshLodGroup=lods,applyConditionsToAllRotations=false,dontRotateBaseMesh=true,isMeshFacesConcave=PerFaceFlag.All}};
            builder.previewMaterial=steel;builder=Save(builder,Root+"/"+name+"Builder.asset");
            set.Blocks.Add(new Block{Name=key,Builder=builder,Material=steel,Materials=renderMaterials.Skip(1).ToArray(),OverrideSubMaterialsTransparency=new OverrideMaterialTransparency[renderMaterials.Length-1],
                Solid=true,WaterLoggable=true,BuildCollider=true,GenerateMeshCollider=true,Rendered=true,PrefabHeightOffset=-.5f,
                ActualHeight=Mathf.Max(.15f,mesh.bounds.max.y+.5f),Category="Roller Coaster Rail",AudioCategory="Metal",Tier=1});
        }
        public static BlockSet Build(IReadOnlyDictionary<string,Material> materials)
        {
            if(!AssetDatabase.IsValidFolder(Root))AssetDatabase.CreateFolder("Assets/EcoMinecarts","CoasterBlocks");
            var set=AssetDatabase.LoadAssetAtPath<BlockSet>(Root+"/CoasterTrack.asset");
            if(set==null){set=ScriptableObject.CreateInstance<BlockSet>();AssetDatabase.CreateAsset(set,Root+"/CoasterTrack.asset");}set.Blocks.Clear();
            var steel=materials["MAT_IronBare"];var paint=materials["MAT_IronPainted"];
            var sections=RailExpansionAssetBuilder.ReadCatalog().CoasterTerrain;
            var snapEnd=SnapEndGeometry(steel,paint);
            for(var turn=0;turn<4;turn++)
            {
                var rotated=new GameObject("Rotated Snap End");snapEnd.transform.SetParent(rotated.transform,false);
                snapEnd.transform.localRotation=Quaternion.Euler(0,turn*90,0);
                Register(set,rotated,"CoasterTrackSnapEnd"+(turn==0?"":"R"+(turn*90)),steel,paint);
                snapEnd.transform.SetParent(null,false);Object.DestroyImmediate(rotated);
            }
            Object.DestroyImmediate(snapEnd);
            foreach(var section in sections)
            {
                var geometry=Geometry(section,steel,paint);
                // Bake rotations explicitly: the native block registry selects
                // a variant and must not rotate the geometry a second time.
                for(var turn=0;turn<4;turn++)
                {
                    var rotated=new GameObject("Rotated");geometry.transform.SetParent(rotated.transform,false);
                    geometry.transform.localRotation=Quaternion.Euler(0,turn*90,0);
                    Register(set,rotated,section.Key+(turn==0?"":"R"+(turn*90)),steel,paint);
                    geometry.transform.SetParent(null,false);Object.DestroyImmediate(rotated);
                }
                geometry.transform.localRotation=Quaternion.identity;
                if(section.SourceKey!="CoasterStraight"&&section.SourceKey!="CoasterChainStraight"||section.Section==1)
                    MinecartIconBuilder.Render(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/"+section.Key+"Block.prefab"),section.Key);
                Object.DestroyImmediate(geometry);
            }
            var straight=sections.First(s=>s.SourceKey=="CoasterStraight");
            var origin=Geometry(straight,steel,paint);Register(set,origin,"CoasterTrack",steel,paint);Object.DestroyImmediate(origin);
            for(var count=1;count<=4;count++)
            {
                var stack=new GameObject("CoasterStack");
                for(var i=0;i<count;i++){var layer=Geometry(straight,steel,paint);layer.transform.SetParent(stack.transform,false);layer.transform.localPosition=Vector3.up*i*.18f;}
                Register(set,stack,"CoasterTrackStacked"+count,steel,paint);Object.DestroyImmediate(stack);
            }
            EditorUtility.SetDirty(set);Debug.Log("ECO_COASTER_TERRAIN_ASSETS_OK: "+sections.Length+" one-cell paths, four native rotations, no interactive rail entities.");return set;
        }
        public static void Verify(Block block)
        {
            var builder=(CustomBuilder)block.Builder;var mesh=builder.usageCases[0].blockMeshLodGroup.LOD0[0].mesh;
            if(mesh.subMeshCount!=2||!mesh.isReadable||mesh.vertexCount<24||mesh.uv.Length!=mesh.vertexCount)
                throw new Exception("Coaster native terrain mesh channels missing: "+block.Name);
            if(builder.usageCases[0].mesh.GetComponent<WorldObject>()!=null||builder.usageCases[0].mesh.GetComponent<SpecificInteractable>()!=null)
                throw new Exception("Coaster building shape must be terrain, not an interactive entity: "+block.Name);
            foreach(var vertex in mesh.vertices)
                if(float.IsNaN(vertex.x)||float.IsInfinity(vertex.x)||float.IsNaN(vertex.y)||float.IsInfinity(vertex.y)||float.IsNaN(vertex.z)||float.IsInfinity(vertex.z))throw new Exception("Invalid coaster mesh vertex: "+block.Name);
            if(block.Name=="CoasterTrack"||block.Name.Contains("Stacked")||block.Name.StartsWith("CoasterTrackSnapEnd"))return;
            var key=block.Name;var turn=0;
            foreach(var degrees in new[]{270,180,90})if(key.EndsWith("R"+degrees)){turn=degrees;key=key.Substring(0,key.Length-("R"+degrees).Length);break;}
            var section=RailExpansionAssetBuilder.ReadCatalog().CoasterTerrain.Single(s=>s.Key==key);
            var rotation=Quaternion.Euler(0,turn,0);
            var probe=new GameObject("CoasterCollisionProbe",typeof(MeshCollider));
            try
            {
                var collider=probe.GetComponent<MeshCollider>();collider.sharedMesh=builder.usageCases[0].blockMeshLodGroup.Collider;
                Physics.SyncTransforms();
                foreach(var index in new[]{2,16,30})
                {
                    // Cast inside a face rather than exactly through its shared
                    // ring edge: PhysX triangle raycasts can reject edge hits
                    // from roundoff on sloping faces even with shared vertices.
                    // Also avoid the quad's diagonal, which crosses its exact
                    // midpoint. Keep the ray strictly inside one triangle.
                    const float interior=.381966f;
                    var point=Vector3.Lerp(section.Points[index].Position,section.Points[index+1].Position,interior);
                    var up=rotation*Vector3.Slerp(section.Points[index].Up,section.Points[index+1].Up,interior).normalized;
                    var tangent=rotation*(section.Points[index+1].Position-section.Points[index].Position).normalized;
                    var right=Vector3.Cross(up,tangent).normalized;
                    foreach(var side in new[]{-1,1})
                    {
                        var from=rotation*point+right*side*.30f+up*.25f;
                        if(!collider.Raycast(new Ray(from,-up),out var hit,.50f))throw new Exception("Coaster running-rail collider gap: "+block.Name+" sample "+index);
                    }
                }
            }
            finally{Object.DestroyImmediate(probe);}
        }
        public static void RepairRotationAndRebuild()
        {
            foreach(var guid in AssetDatabase.FindAssets("t:CustomBuilder",new[]{Root}))
            {
                var builder=AssetDatabase.LoadAssetAtPath<CustomBuilder>(AssetDatabase.GUIDToAssetPath(guid));
                foreach(var usage in builder.usageCases){usage.applyConditionsToAllRotations=false;usage.dontRotateBaseMesh=true;}
                EditorUtility.SetDirty(builder);
            }
            AssetDatabase.SaveAssets();
            MinecartAssetBuilder.BuildSavedClientBundle();
        }
        public static void RebuildTerrainMeshes()
        {
            Build(new Dictionary<string,Material>{
                ["MAT_IronBare"]=AssetDatabase.FindAssets("MAT_IronBare t:Material").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).First(),
                ["MAT_IronPainted"]=AssetDatabase.FindAssets("MAT_IronPainted t:Material").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).First()});
            AssetDatabase.SaveAssets();MinecartAssetBuilder.BuildSavedClientBundle();
        }
        public static void RebuildCompactTransitions()
        {
            var sections=RailExpansionAssetBuilder.ReadCatalog().CoasterTerrain
                .Where(s=>s.SourceKey.Contains("Compact")&&(s.SourceKey.EndsWith("Entry")||s.SourceKey.EndsWith("Exit"))).ToArray();
            if(sections.Length!=8||sections.Any(s=>s.Points.Length!=129))throw new Exception("Expected eight refined compact transition paths");
            var set=AssetDatabase.LoadAssetAtPath<BlockSet>(Root+"/CoasterTrack.asset");
            var steel=AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronBare.mat");
            var paint=AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronPainted.mat");
            var names=new HashSet<string>(sections.SelectMany(s=>Enumerable.Range(0,4).Select(t=>s.Key+(t==0?"":"R"+t*90))));
            var oldCount=set.Blocks.Count;
            set.Blocks.RemoveAll(b=>names.Contains(b.Name));
            foreach(var section in sections)
            {
                var geometry=Geometry(section,steel,paint);
                for(var turn=0;turn<4;turn++)
                {
                    var rotated=new GameObject("Compact transition rotation");geometry.transform.SetParent(rotated.transform,false);
                    geometry.transform.localRotation=Quaternion.Euler(0,turn*90,0);
                    Register(set,rotated,section.Key+(turn==0?"":"R"+turn*90),steel,paint);
                    geometry.transform.SetParent(null,false);Object.DestroyImmediate(rotated);
                }
                Object.DestroyImmediate(geometry);
                MinecartIconBuilder.Render(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/"+section.Key+"Block.prefab"),section.Key);
            }
            if(set.Blocks.Count!=oldCount)throw new Exception("Compact transition rebuild changed registry size");
            EditorUtility.SetDirty(set);AssetDatabase.SaveAssets();MinecartAssetBuilder.BuildAuthoredClientBundle();
            Debug.Log("COMPACT_TRANSITIONS_REBUILT: eight paths, 32 rotations; other block registrations retained.");
        }
    }
}
