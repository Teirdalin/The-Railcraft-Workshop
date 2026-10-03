using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    // Visual steel rail: head, narrow web and foot. Its top follows the existing
    // simulation curve exactly; no collision or connectivity is authored here.
    public static class RailSectionMesh
    {
        public static void Create(Transform parent,string key,Vector3[] top,Vector3[] right,float width,float depth,Material material,bool timber=false)
        {
            var profile=timber?new[]{new Vector2(-.5f,0),new Vector2(.5f,0),new Vector2(.5f,-1),new Vector2(-.5f,-1)}:
                new[]{new Vector2(-.5f,0),new Vector2(.5f,0),new Vector2(.5f,-.30f),new Vector2(.17f,-.30f),new Vector2(.17f,-.74f),new Vector2(.60f,-.74f),
                    new Vector2(.60f,-1),new Vector2(-.60f,-1),new Vector2(-.60f,-.74f),new Vector2(-.17f,-.74f),new Vector2(-.17f,-.30f),new Vector2(-.5f,-.30f)};
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();var distance=0f;
            var stride=profile.Length*2;
            for(var i=0;i<top.Length;i++){
                if(i>0)distance+=Vector3.Distance(top[i-1],top[i]);
                for(var face=0;face<profile.Length;face++)for(var endpoint=0;endpoint<2;endpoint++){
                    var p=profile[(face+endpoint)%profile.Length];
                    vertices.Add(top[i]+right[i]*(p.x*width)+Vector3.up*(p.y*depth));uv.Add(new Vector2(endpoint,distance));
                    if(i==0||endpoint!=0)continue;
                    var a=(i-1)*stride+face*2;var b=a+1;var d=i*stride+face*2;var c=d+1;
                    triangles.AddRange(new[]{a,c,b,a,d,c});
                }
            }
            var rectangles=timber?new[]{new[]{0,1,2,3}}:new[]{new[]{0,1,2,11},new[]{3,4,9,10},new[]{5,6,7,8}};
            foreach(var endpoint in new[]{0,top.Length-1})foreach(var rectangle in rectangles){
                var start=vertices.Count;
                for(var j=0;j<4;j++){var p=profile[rectangle[j]];vertices.Add(top[endpoint]+right[endpoint]*(p.x*width)+Vector3.up*(p.y*depth));uv.Add(new Vector2(j%2,j/2));}
                triangles.AddRange(endpoint==0?new[]{start,start+1,start+2,start,start+2,start+3}:new[]{start,start+2,start+1,start,start+3,start+2});
            }
            var mesh=new Mesh{name=key};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            const string folder="Assets/EcoMinecarts/RailSectionMeshes";
            if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/EcoMinecarts","RailSectionMeshes");
            var path=folder+"/"+key+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);EditorUtility.SetDirty(saved);Object.DestroyImmediate(mesh);}
            var node=new GameObject("Rail",typeof(MeshFilter),typeof(MeshRenderer));node.transform.SetParent(parent,false);
            node.GetComponent<MeshFilter>().sharedMesh=saved;node.GetComponent<MeshRenderer>().sharedMaterial=material;
        }
    }
}
