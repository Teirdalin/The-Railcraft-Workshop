using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class CompactCoasterTransitionProbe
    {
        public static void BuildAndCheck()
        {
            CoasterTerrainAssetBuilder.RebuildCompactTransitions();
            var catalog=RailExpansionAssetBuilder.ReadCatalog().CoasterTerrain;
            var output=Path.GetFullPath("../../validation/compact-coaster-0.2.25");Directory.CreateDirectory(output);
            var steel=AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronBare.mat");
            var paint=AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/MAT_IronPainted.mat");
            foreach(var section in catalog.Where(p=>p.Key.Contains("Compact")))
            {
                if(section.Points.Length!=(section.SourceKey.EndsWith("Entry")||section.SourceKey.EndsWith("Exit")?129:65))throw new Exception("Compact curve not refined: "+section.Key);
                var geometry=CoasterTerrainAssetBuilder.Geometry(section,steel,paint);
                try
                {
                    foreach(var filter in geometry.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Continuous rail"))
                    {
                        var normals=filter.sharedMesh.normals;
                        if(Vector3.Dot(normals[4],section.Points[0].Up)<.99999f
                            ||Vector3.Dot(normals[(section.Points.Length-1)*8+4],section.Points.Last().Up)<.99999f)
                            throw new Exception("Compact endpoint shading does not match socket frame: "+section.Key);
                    }
                    var prefix=section.Chain?"CoasterChain":"Coaster";
                    var first=section.Points[0];
                    var firstSlope=-first.UpZ/first.UpY;
                    var previous=catalog.Single(p=>p.SourceKey==prefix+(firstSlope>.1?"GradeUp":firstSlope<-.1?"GradeDown":"Straight"));
                    var last=section.Points.Last();var lastSlope=-last.UpZ/last.UpY;
                    var next=catalog.Single(p=>p.SourceKey==prefix+(lastSlope>.1?"GradeUp":lastSlope<-.1?"GradeDown":"Straight"));
                    var before=CoasterTerrainAssetBuilder.Geometry(previous,steel,paint);
                    var after=CoasterTerrainAssetBuilder.Geometry(next,steel,paint);
                    before.transform.SetParent(geometry.transform,false);after.transform.SetParent(geometry.transform,false);
                    before.transform.localPosition=first.Position-previous.Points.Last().Position;
                    after.transform.localPosition=last.Position-next.Points[0].Position;
                    Render(geometry,Path.Combine(output,section.Key+".png"));
                }
                finally{Object.DestroyImmediate(geometry);}
            }
            Debug.Log("COMPACT_COASTER_GEOMETRY_OK: 12 refined curves; matching endpoint surface normals; adjoining rail renders.");
        }
        static void Render(GameObject root,string path)
        {
            foreach(var t in root.GetComponentsInChildren<Transform>())t.gameObject.layer=30;
            var bounds=root.GetComponentsInChildren<Renderer>().First().bounds;
            foreach(var r in root.GetComponentsInChildren<Renderer>())bounds.Encapsulate(r.bounds);
            var camera=new GameObject("Compact rail preview").AddComponent<Camera>();
            camera.transform.position=bounds.center+new Vector3(-4,2,-3);camera.transform.LookAt(bounds.center);
            camera.orthographic=true;camera.orthographicSize=1.9f;camera.cullingMask=1<<30;
            camera.backgroundColor=new Color(.2f,.23f,.27f);camera.clearFlags=CameraClearFlags.SolidColor;
            var light=new GameObject("Compact rail light").AddComponent<Light>();light.type=LightType.Directional;
            light.transform.rotation=Quaternion.Euler(35,-35,0);light.intensity=1;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.6f,.6f,.6f);
            var rt=new RenderTexture(800,600,24);var image=new Texture2D(800,600,TextureFormat.RGBA32,false);
            var previous=RenderTexture.active;
            Shader.SetGlobalFloat("_WorldRadius",10000);Shader.SetGlobalVector("_WorldCenter",new Vector4(0,-10000,0,0));
            Shader.SetGlobalVector("_CurveAxisMask",Vector4.zero);Shader.EnableKeyword("NO_CURVE");
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,600),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=previous;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);Shader.DisableKeyword("NO_CURVE");}
        }
    }
}
