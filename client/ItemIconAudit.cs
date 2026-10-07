using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    public static class ItemIconAudit
    {
        public static void Verify(GameObject[] roots)
        {
            var items=roots.Single(r=>r.name=="Items");
            var images=items.GetComponentsInChildren<Image>(true).Where(i=>i.name=="Foreground").ToArray();
            if(images.Length==0 || images.Any(i=>i.sprite==null)) throw new Exception("Missing exported item icon.");
            foreach(var image in images){
                var sprite=image.sprite;
                if(Mathf.Abs(sprite.rect.width/sprite.pixelsPerUnit-128f/150f)>.001f
                    || Mathf.Abs(sprite.rect.height/sprite.pixelsPerUnit-128f/150f)>.001f)
                    throw new Exception("Exported icon differs from native Eco sprite dimensions: "+sprite.name);
            }
            Debug.Log("ECO_EXPORTED_ICON_SCALE_OK: all item sprites match native 128px/150PPU dimensions");
            var output=Environment.GetEnvironmentVariable("ECO_MINECART_AUDIT_DIR")??Environment.GetEnvironmentVariable("ECO_MODEL_AUDIT_DIR");
            var directory=Path.Combine(output,"icons");Directory.CreateDirectory(directory);
            var categories=new List<Entry>();
            foreach(var image in images.GroupBy(i=>i.sprite.texture.name).Select(g=>g.First()))
            {
                var sprite=image.sprite;var source=sprite.texture;
                var building=MinecartIconBuilder.IsBuilding(source.name);
                var target=RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                var before=RenderTexture.active;var copy=new Texture2D(256,256,TextureFormat.RGBA32,false);
                try
                {
                    Graphics.Blit(source,target);RenderTexture.active=target;
                    copy.ReadPixels(new Rect(0,0,256,256),0,0);copy.Apply();
                    // Sample the clear inner border; projected model framing leaves it free.
                    foreach(var p in new[]{new Vector2Int(18,128),new Vector2Int(237,128),new Vector2Int(128,18),new Vector2Int(128,237)})
                    {
                        var c=copy.GetPixel(p.x,p.y);
                        if(c.a<.9f || (building ? c.r<c.b+.02f : c.b<c.r+.08f))
                            throw new Exception("Wrong or missing "+(building?"brown":"blue")+" backdrop: "+source.name+" at "+p+" "+c);
                    }
                    File.WriteAllBytes(Path.Combine(directory,source.name+".png"),copy.EncodeToPNG());
                    categories.Add(new Entry{name=source.name,category=building?"Building":"Object"});
                }
                finally { RenderTexture.active=before;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(copy); }
            }
            File.WriteAllText(Path.Combine(directory,"icons.json"),JsonUtility.ToJson(new Report{icons=categories.ToArray(),itemCount=images.Length},true));
            Debug.Log("ECO_EXPORTED_ICONS_OK: "+images.Length+" item views; "+categories.Count+" unique thumbnails; classified backgrounds sampled from exported textures.");
        }
        [Serializable] private class Entry { public string name,category; }
        [Serializable] private class Report { public int itemCount;public Entry[] icons; }
    }
}
