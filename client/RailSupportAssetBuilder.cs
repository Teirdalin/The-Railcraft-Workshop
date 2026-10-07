using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class RailSupportAssetBuilder
    {
        const string Root="Assets/EcoMinecarts";
        public static void RebuildSideLaddersAndExport()
        {
            Build();
            AssetDatabase.SaveAssets();
            MinecartAssetBuilder.BuildAuthoredClientBundle();
            Debug.Log("RAIL_SUPPORT_SIDE_LADDERS_OK: rungs and native climb face on the +X side, clear side corridor, all rotated variants.");
        }
        // Native terrain climbing works on Block.IsLadder. Keep loose carried
        // stacks ordinary blocks; every installed column variant is climbable.
        public static void RefreshClimbing()
        {
            var set = AssetDatabase.LoadAssetAtPath<BlockSet>(Root + "/CoasterBlocks/RailSupports.asset");
            if (set == null) throw new Exception("Rail support block set is missing");
            int count = 0;
            foreach (var block in set.Blocks)
            {
                block.IsLadder = !block.Name.Contains("Stacked");
                if (block.IsLadder) {
                    ConfigureClimbFacing(block);
                    var collision=((CustomBuilder)block.Builder).usageCases[0].blockMeshLodGroup.Collider;
                    if(!collision.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)) {
                        collision.uv=collision.vertices.Select(v=>new Vector2(v.x,v.z)).ToArray();
                        EditorUtility.SetDirty(collision);
                    }
                    count++;
                }
            }
            EditorUtility.SetDirty(set);
            Debug.Log("RAIL_SUPPORT_CLIMBING_OK: " + count + " installed support variants; rotation selects climb face; loose stacks excluded");
        }
        static void ConfigureClimbFacing(Block block)
        {
            // Eco reads the first usage case's importRotation for ladder facing,
            // rather than the rotation baked into our mesh. Supply that facing
            // and undo it in the source meshes to preserve the rendered support.
            var builder=block.Builder as CustomBuilder;
            if(builder==null || builder.usageCases.Count!=1)throw new Exception("Unexpected support builder: "+block.Name);
            var usage=builder.usageCases[0];
            var turn=block.Name.EndsWith("R90")?90:block.Name.EndsWith("R180")?180:block.Name.EndsWith("R270")?270:0;
            // Native climb interaction approaches the positive facing side.
            // The rungs sit on +X, perpendicular to the track's local Z axis.
            var desired=new Vector3(0,(turn+90)%360,0);
            var change=Quaternion.Inverse(Quaternion.Euler(desired))*Quaternion.Euler(usage.importRotation);
            if(Quaternion.Angle(change,Quaternion.identity)>.01f)
            {
                var meshes=new HashSet<Mesh>();
                if(usage.mesh!=null)foreach(var filter in usage.mesh.GetComponentsInChildren<MeshFilter>(true))if(filter.sharedMesh!=null)meshes.Add(filter.sharedMesh);
                var lods=usage.blockMeshLodGroup;
                if(lods==null)throw new Exception("Missing support LODs: "+block.Name);
                foreach(var lod in lods.LOD0)if(lod.mesh!=null)meshes.Add(lod.mesh);
                if(lods.LOD1.mesh!=null)meshes.Add(lods.LOD1.mesh);
                if(lods.LOD2.mesh!=null)meshes.Add(lods.LOD2.mesh);
                if(lods.Collider!=null)meshes.Add(lods.Collider);
                foreach(var mesh in meshes)
                {
                    var original=mesh.vertices;
                    var oldRotation=Quaternion.Euler(usage.importRotation);
                    var newRotation=Quaternion.Euler(desired);
                    var vertices=original.Select(p=>change*p).ToArray();
                    for(var i=0;i<vertices.Length;i++)if((newRotation*vertices[i]-oldRotation*original[i]).sqrMagnitude>1e-8f)
                        throw new Exception("Support world geometry changed: "+block.Name);
                    mesh.vertices=vertices;
                    mesh.normals=mesh.normals.Select(n=>change*n).ToArray();
                    mesh.tangents=mesh.tangents.Select(t=>{var n=change*new Vector3(t.x,t.y,t.z);return new Vector4(n.x,n.y,n.z,t.w);}).ToArray();
                    mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
                }
            }
            usage.importRotation=desired;
            EditorUtility.SetDirty(builder);
            if(usage.importRotation!=desired)throw new Exception("Incorrect support climb direction: "+block.Name);
        }
        static void Box(Transform root,string name,Vector3 p,Vector3 size,Material material,Quaternion rotation)
        {
            var o=GameObject.CreatePrimitive(PrimitiveType.Cube);o.name=name;o.transform.SetParent(root,false);
            o.transform.localPosition=p;o.transform.localScale=size;o.transform.localRotation=rotation;
            o.GetComponent<Renderer>().sharedMaterial=material;Object.DestroyImmediate(o.GetComponent<Collider>());
        }
        static void Beam(Transform root,string name,Vector3 a,Vector3 b,float thickness,Material material)
        {Box(root,name,(a+b)/2,new Vector3(thickness,(b-a).magnitude,thickness),material,Quaternion.FromToRotation(Vector3.up,b-a));}
        static GameObject Geometry(string tier,string part,int phase,Material structure,Material fittings)
        {
            var root=new GameObject("Support");var upper=.43f+(part=="Top"&&phase>0?(phase-1)*.25f:0);
            foreach(var x in new[]{-.28f,.28f})foreach(var z in new[]{-.28f,.28f})
            {
                Beam(root.transform,"Column",new Vector3(x,-.5f,z),new Vector3(x,part=="Top"?upper:.5f,z),tier=="Wood"?.12f:.09f,structure);
                if(part=="Base"){
                    Box(root.transform,"Anchored foot plate",new Vector3(x,-.45f,z),new Vector3(.25f,.10f,.25f),fittings,Quaternion.identity);
                    foreach(var dx in new[]{-.075f,.075f})Box(root.transform,"Anchor bolt",new Vector3(x+dx,-.385f,z),new Vector3(.025f,.03f,.025f),fittings,Quaternion.identity);
                }
            }
            foreach(var z in new[]{-.28f,.28f}){
                Beam(root.transform,"Diagonal brace",new Vector3(-.28f,-.35f,z),new Vector3(.28f,upper-.12f,z),.055f,structure);
                Beam(root.transform,"Diagonal brace",new Vector3(.28f,-.35f,z),new Vector3(-.28f,upper-.12f,z),.055f,structure);
            }
            foreach(var y in new[]{-.46f,upper-.07f})Box(root.transform,"Joint collar",new Vector3(0,y,0),new Vector3(.72f,.065f,.72f),fittings,Quaternion.identity);
            // Put the ladder on the side of the track, not beneath its length.
            // Continue the rungs through raised slope saddles as well.
            for(var y=-.32f;y<upper+.02f;y+=.20f)
                Box(root.transform,"Climbing rung",new Vector3(.385f,y,0),new Vector3(.045f,.045f,.54f),fittings,Quaternion.identity);
            if(part=="Top"){
                var pitch=phase>0?Quaternion.Euler(-Mathf.Atan(.25f)*Mathf.Rad2Deg,0,0):Quaternion.identity;
                Box(root.transform,"Rail saddle crossbeam",new Vector3(0,upper,0),new Vector3(.94f,.11f,.22f),structure,pitch);
                foreach(var x in new[]{-.32f,.32f}){
                    Box(root.transform,"Rail clamp shoe",new Vector3(x,upper+.067f,0),new Vector3(.18f,.035f,.30f),fittings,pitch);
                    Box(root.transform,"Clamp bolt",new Vector3(x,upper+.10f,.09f),new Vector3(.035f,.05f,.035f),fittings,pitch);
                }
            }
            return root;
        }
        static void ClearClimbingCorridor(Block block,int turn)
        {
            // Preserve the full visual LODs. Only the physical collider is cut
            // back from the ladder face so collars and saddle overhangs cannot
            // knock a climber off before they reach the rail-level landing.
            var lods=((CustomBuilder)block.Builder).usageCases[0].blockMeshLodGroup;
            var inverse=Quaternion.Inverse(Quaternion.Euler(0,turn,0));
            var forward=Quaternion.Euler(0,turn,0);
            var source=lods.Collider;
            var original=source.vertices.Select(v=>inverse*v).ToArray();
            var vertices=new List<Vector3>();var triangles=new List<int>();
            var indices=source.triangles;
            const float edge=.10f;
            for(var i=0;i<indices.Length;i+=3)
            {
                var polygon=new List<Vector3>{original[indices[i]],original[indices[i+1]],original[indices[i+2]]};
                var clipped=new List<Vector3>();
                for(var j=0;j<polygon.Count;j++)
                {
                    var a=polygon[j];var b=polygon[(j+1)%polygon.Count];
                    var inside=a.x<=-edge;var nextInside=b.x<=-edge;
                    if(inside)clipped.Add(a);
                    if(inside!=nextInside)clipped.Add(Vector3.Lerp(a,b,(-edge-a.x)/(b.x-a.x)));
                }
                if(clipped.Count<3)continue;
                var start=vertices.Count;vertices.AddRange(clipped.Select(v=>forward*v));
                for(var j=1;j<clipped.Count-1;j++)triangles.AddRange(new[]{start,start+j,start+j+1});
            }
            if(vertices.Count==0)throw new Exception("Support landing collider missing: "+block.Name);
            var mesh=new Mesh{name=block.Name+"BlockClimbCollision"};
            mesh.SetVertices(vertices);mesh.SetUVs(0, vertices.Select(v => new Vector2(v.x,v.z)).ToList());mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            var path=Root+"/CoasterBlocks/"+block.Name+"BlockClimbCollision.asset";
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}
            else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
            lods.Collider=saved;EditorUtility.SetDirty(lods);
            // A capsule-sized lane remains clear all the way through the saddle;
            // rear-facing horizontal triangles still provide the exit landing.
            if(saved.vertices.Any(v=>(inverse*v).x>-edge+.0001f))throw new Exception("Blocked support climb corridor: "+block.Name);
            if(!saved.normals.Any(n=>n.y>.9f))throw new Exception("Support has no landing surface: "+block.Name);
        }
        public static BlockSet Build()
        {
            var path=Root+"/CoasterBlocks/RailSupports.asset";
            var set=AssetDatabase.LoadAssetAtPath<BlockSet>(path);
            if(set==null){set=ScriptableObject.CreateInstance<BlockSet>();AssetDatabase.CreateAsset(set,path);}set.Blocks.Clear();
            foreach(var tier in new[]{"Wood","Iron","Steel"}){
                var structure=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+(tier=="Wood"?"MAT_WoodRail":tier=="Iron"?"MAT_IronBare":"MAT_IronPainted")+".mat");
                var fittings=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+(tier=="Iron"?"MAT_IronPainted":"MAT_IronBare")+".mat");
                if(structure==null||fittings==null)throw new Exception("Support finish materials missing");
                var key="RailSupport"+tier;
                Action<string,string,int,int> add=(name,part,phase,turn)=>{
                    var root=Geometry(tier,part,phase,structure,fittings);root.transform.rotation=Quaternion.Euler(0,turn,0);
                    // Bake the rotation into a parent, matching the native rotated variant contract.
                    var parent=new GameObject("BakedSupport");root.transform.SetParent(parent.transform,false);
                    CoasterTerrainAssetBuilder.Register(set,parent,name,structure,fittings);
                    ClearClimbingCorridor(set.Blocks.Last(),turn);
                    set.Blocks.Last().IsLadder=true;
                    set.Blocks.Last().Category="Rail Supports";set.Blocks.Last().AudioCategory=tier=="Wood"?"Wood":"Metal";
                    Object.DestroyImmediate(parent);
                };
                add(key,"Middle",0,0);
                foreach(var part in new[]{"Base","Middle","Top"}){
                    foreach(var turn in new[]{0,90,180,270})add(key+part+(turn==0?"":"R"+turn),part,0,turn);
                    MinecartIconBuilder.Render(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/CoasterBlocks/"+key+part+"Block.prefab"),key+part);
                }
                foreach(var phase in new[]{1,2,3,4})foreach(var turn in new[]{0,90,180,270})add(key+"TopSlope"+phase+(turn==0?"":"R"+turn),"Top",phase,turn);
                for(var n=1;n<=4;n++){
                    var root=new GameObject("Carried support stack");
                    for(var i=0;i<n;i++)Box(root.transform,"Bundled beams",new Vector3(0,-.4f+i*.17f,0),new Vector3(.68f,.14f,.68f),structure,Quaternion.identity);
                    Box(root.transform,"Stack binding",new Vector3(0,-.4f,0),new Vector3(.74f,.04f,.1f),fittings,Quaternion.identity);
                    CoasterTerrainAssetBuilder.Register(set,root,key+"Stacked"+n,structure,fittings);set.Blocks.Last().Category="Rail Supports";Object.DestroyImmediate(root);
                }
            }
            EditorUtility.SetDirty(set);AssetDatabase.SaveAssets();Debug.Log("RAIL_SUPPORT_ASSETS_OK: "+set.Blocks.Count+" registered brown-icon support blocks");return set;
        }
    }
}
