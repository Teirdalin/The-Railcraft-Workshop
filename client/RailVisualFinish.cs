using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    // Render-only fittings. No collider, mount, network, occupancy or interaction
    // component is created or edited by this pass.
    public static class RailVisualFinish
    {
        static Transform Part(Transform parent,string name,PrimitiveType shape,Vector3 p,Vector3 size,Material m,Quaternion q)
        {
            var n=GameObject.CreatePrimitive(shape);n.name=name;n.transform.SetParent(parent,false);
            n.transform.localPosition=p;n.transform.localScale=size;n.transform.localRotation=q;
            n.GetComponent<Renderer>().sharedMaterial=m;Object.DestroyImmediate(n.GetComponent<Collider>());return n.transform;
        }
        static Transform Box(Transform p,string n,Vector3 v,Vector3 s,Material m)=>Part(p,n,PrimitiveType.Cube,v,s,m,Quaternion.identity);
        static Transform Pin(Transform p,string n,Vector3 v,float r,float length,Material m,Quaternion q)=>Part(p,n,PrimitiveType.Cylinder,v,new Vector3(r*2,length/2,r*2),m,q);
        static void Beam(Transform p,string n,Vector3 a,Vector3 b,float width,float height,Material m)
        {Part(p,n,PrimitiveType.Cube,(a+b)/2,new Vector3(width,height,Vector3.Distance(a,b)),m,Quaternion.LookRotation(b-a));}
        internal static Material Accent(string name,Material source,Color color)
        {
            var path="Assets/EcoMinecarts/Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(source);AssetDatabase.CreateAsset(m,path);}else m.CopyPropertiesFromMaterial(source);
            // Unity's built-in whiteTexture is not retained by the Eco scene
            // bundle exporter. Use a real asset so enamel/dark trim survive it.
            const string whitePath="Assets/EcoMinecarts/Materials/RailFinishWhite.asset";
            var white=AssetDatabase.LoadAssetAtPath<Texture2D>(whitePath);
            if(white==null){white=new Texture2D(2,2,TextureFormat.RGBA32,false);white.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});white.Apply();AssetDatabase.CreateAsset(white,whitePath);}
            m.SetTexture("_MainTex",white);m.color=color;m.SetFloat("_Glossiness",.18f);EditorUtility.SetDirty(m);return m;
        }
        static void Dial(Transform p,Vector3 c,float radius,Material steel,Material face,Material ink)
        {
            var q=Quaternion.Euler(90,0,0);
            Pin(p,"Gauge bezel",c,radius,.018f,steel,q);
            Pin(p,"Gauge enamel",c+Vector3.back*.011f,radius*.83f,.004f,face,q);
            for(var i=0;i<7;i++){
                var a=(-130+i*43)*Mathf.Deg2Rad;var radial=new Vector3(Mathf.Sin(a),Mathf.Cos(a),0);
                Beam(p,"Gauge tick",c+Vector3.back*.014f+radial*radius*.61f,c+Vector3.back*.014f+radial*radius*.75f,.003f,.003f,ink);
            }
            Beam(p,"Gauge needle",c+Vector3.back*.017f,c+new Vector3(-radius*.36f,radius*.43f,-.017f),.004f,.004f,ink);
        }
        static void Cabinet(Transform p,Transform cabinet,Material steel,Material dark,Material face)
        {
            var c=cabinet.localPosition;var s=cabinet.localScale;var z=c.z-s.z/2-.009f;
            // Put the service face away from the rear station support post.
            var front=new GameObject("Cabinet service face").transform;front.SetParent(p,false);
            front.localPosition=new Vector3(c.x*2,0,c.z*2);front.localRotation=Quaternion.Euler(0,180,0);p=front;
            foreach(var side in new[]{-1,1}){
                Box(p,"Cabinet door stile",new Vector3(c.x+side*s.x*.44f,c.y,z),new Vector3(.018f,s.y*.86f,.013f),dark);
                Box(p,"Cabinet door rail",new Vector3(c.x,c.y+side*s.y*.43f,z),new Vector3(s.x*.9f,.018f,.013f),dark);
                Pin(p,"Cabinet hinge",new Vector3(c.x-s.x*.40f,c.y+side*s.y*.29f,z-.008f),.012f,.06f,steel,Quaternion.identity);
            }
            Box(p,"Cabinet latch",new Vector3(c.x+s.x*.31f,c.y,z-.001f),new Vector3(.025f,.09f,.028f),steel);
            if(cabinet.name!="Toolbox")Dial(p,new Vector3(c.x,c.y+s.y*.20f,z+.001f),Mathf.Min(.065f,s.x*.14f),steel,face,dark);
        }
        public static void Apply(GameObject[] prefabs,IReadOnlyDictionary<string,Material> materials)
        {
            var steel=materials["MAT_IronBare"];var wood=materials["MAT_WoodRail"];var paint=materials["MAT_IronPainted"];
            var dark=Accent("MAT_RailInset",paint,new Color(.055f,.067f,.070f));
            var enamel=Accent("MAT_RailGauge",steel,new Color(.72f,.70f,.62f));
            foreach(var prefab in prefabs)
            {
                var path=AssetDatabase.GetAssetPath(prefab);var root=PrefabUtility.LoadPrefabContents(path);
                try{
                    var previous=root.transform.Find("VisualFinish");if(previous!=null)Object.DestroyImmediate(previous.gameObject);
                    var detail=new GameObject("VisualFinish").transform;detail.SetParent(root.transform,false);
                    var nodes=root.GetComponentsInChildren<Transform>(true);
                    var cab=root.transform.Find("CabFittings");
                    if(cab!=null){
                        var deck=cab.Find("Cab floor");var floor=deck.localPosition.y+deck.localScale.y/2;
                        var w=deck.localScale.x;var z=deck.localPosition.z;var d=deck.localScale.z;var front=z+d/2;
                        foreach(var end in new[]{-1,1}){
                            // Recess below the running board instead of leaving
                            // its top five millimetres from the board surface.
                            Box(detail,"Cab outrigger",new Vector3(0,floor-.115f,z+end*d*.32f),new Vector3(w-.05f,.10f,.09f),paint);
                            Box(detail,"Roof header",new Vector3(0,floor+2.055f,z+end*d/2),new Vector3(w,.06f,.055f),paint);
                            foreach(var side in new[]{-1,1})
                                Beam(detail,"Step hanger",new Vector3(side*w/2,floor-.04f,z+end*.13f),new Vector3(side*(w/2+.10f),floor-.20f,z+end*.13f),.035f,.04f,steel);
                        }
                        foreach(var side in new[]{-1,1}){
                            // The old sill top was exactly coplanar with the
                            // cab deck top, producing z-fighting along its edge.
                            Box(detail,"Cab sill",new Vector3(side*(w/2-.025f),floor-.065f,z),new Vector3(.06f,.10f,d),paint);
                            for(var i=0;i<5;i++)Pin(detail,"Cab panel rivet",new Vector3(side*(w/2-.08f),floor+.10f+i*.12f,z-d/2-.026f),.013f,.009f,steel,Quaternion.Euler(90,0,0));
                        }
                        Dial(detail,new Vector3(0,floor+1.18f,front-.081f),.055f,steel,enamel,dark);
                        foreach(var x in new[]{-.32f,-.11f,.11f,.32f})
                            Box(detail,"Control escutcheon",new Vector3(x*w,floor+1.263f,front-.065f),new Vector3(.07f,.006f,.07f),steel);
                    }
                    foreach(var bench in nodes.Where(n=>n.name=="Passenger bench")){
                        var p=bench.localPosition;var s=bench.localScale;
                        var tram=root.name=="HeritageTramObject";
                        var floor=tram?nodes.First(n=>n.name=="Passenger deck"):null;
                        var low=tram?floor.localPosition.y+floor.localScale.y/2:.46f;
                        var high=tram?p.y-s.y/2:.70f;
                        foreach(var side in new[]{-1,1})Box(detail,"Bench pedestal",
                            new Vector3(p.x+side*s.x*.32f,(low+high)/2,p.z),
                            new Vector3(.045f,high-low+.01f,.20f),paint);
                    }
                    foreach(var panel in nodes.Where(n=>n.name=="Cargo side"||n.name=="Coach side")){
                        var p=panel.localPosition;var s=panel.localScale;var side=Mathf.Sign(p.x);
                        Box(detail,"Body top cap",new Vector3(p.x,p.y+s.y/2,p.z),new Vector3(.075f,.035f,s.z),paint);
                        for(var z=-s.z/2+.12f;z<s.z/2;z+=.42f)
                            foreach(var dy in new[]{-.35f,.35f})Pin(detail,"Body seam rivet",new Vector3(p.x+side*(s.x/2+.007f),p.y+dy*s.y,p.z+z),.012f,.010f,steel,Quaternion.Euler(0,0,90));
                    }
                    foreach(var rib in nodes.Where(n=>n.name=="Hopper reinforcing rib"))rib.GetComponent<Renderer>().sharedMaterial=paint;
                    foreach(var box in nodes.Where(n=>n.name=="Control cabinet"||n.name=="Station control cabinet"||n.name=="Toolbox"))Cabinet(detail,box,steel,dark,enamel);
                    foreach(var sign in nodes.Where(n=>n.name=="Station board"||n.name=="Station sign")){
                        var c=sign.localPosition;var s=sign.localScale;
                        foreach(var face in new[]{-1,1}){
                            var faceZ=c.z+face*(s.z/2+.014f);
                            Box(detail,"Station sign fascia",new Vector3(c.x,c.y,faceZ),new Vector3(s.x,s.y,.012f),paint);
                            foreach(var side in new[]{-1,1})Box(detail,"Station rail emblem",new Vector3(c.x+side*.12f,c.y,faceZ+face*.014f),new Vector3(.018f,s.y*.62f,.016f),enamel);
                            for(var i=-1;i<=1;i++)Box(detail,"Station emblem sleeper",new Vector3(c.x,c.y+i*s.y*.19f,faceZ+face*.014f),new Vector3(.32f,.025f,.016f),enamel);
                        }
                    }
                    var boiler=nodes.FirstOrDefault(n=>n.name=="Boiler"||n.name=="Steam boiler");
                    if(boiler!=null){
                        var c=boiler.localPosition;var radius=boiler.localScale.x/2;var length=boiler.localScale.y*2;
                        if(root.name=="MineTrainObject")foreach(var z in new[]{.10f,.58f})Box(detail,"Compact boiler saddle",new Vector3(0,.405f,z),new Vector3(.33f,.08f,.10f),paint);
                        foreach(var side in new[]{-1,1}){
                            Beam(detail,"Boiler hand pipe",new Vector3(side*(radius+.035f),c.y+.07f,c.z-length*.38f),new Vector3(side*(radius+.035f),c.y+.07f,c.z+length*.38f),.02f,.02f,steel);
                            foreach(var end in new[]{-1,1})Box(detail,"Pipe bracket",new Vector3(side*(radius+.01f),c.y+.07f,c.z+end*length*.30f),new Vector3(.065f,.025f,.03f),paint);
                        }
                        var door=nodes.FirstOrDefault(n=>n.name=="SmokeboxDoor"||n.name=="Smokebox door");
                        if(door!=null){
                            // The circular plate is a thin cylinder whose local Y
                            // becomes the train's Z after rotation. Seat its back
                            // face inside the boiler instead of scaling the gap
                            // up with the length of a larger locomotive.
                            door.localPosition=new Vector3(c.x,c.y,c.z+length/2+.005f);
                            var dc=door.localPosition+Vector3.forward*(door.localScale.y+.014f);
                            var oldLatch=nodes.FirstOrDefault(n=>n.name=="DoorLatch");if(oldLatch!=null)Object.DestroyImmediate(oldLatch.gameObject);
                            Pin(detail,"Smokebox latch boss",dc,.035f,.025f,dark,Quaternion.Euler(90,0,0));
                            for(var i=0;i<8;i++){var a=i*Mathf.PI/4;Pin(detail,"Smokebox bolt",dc+new Vector3(Mathf.Sin(a),Mathf.Cos(a),0)*radius*.72f,.012f,.008f,steel,Quaternion.Euler(90,0,0));}
                            Beam(detail,"Smokebox locking bar",dc+Vector3.left*radius*.4f,dc+Vector3.right*radius*.4f,.025f,.025f,steel);
                        }
                    }
                    if(root.name=="RollerCoasterCartObject")foreach(var side in new[]{-1,1})foreach(var end in new[]{-1,1})
                        Box(detail,"Captive wheel carrier",new Vector3(side*.30f,.16f,end*.4f),new Vector3(.055f,.30f,.12f),paint);
                    if(root.name=="MinecartChainDriveObject"){
                        for(var i=0;i<5;i++)Box(detail,"Housing vent",new Vector3(0,-.14f+i*.055f,-.443f),new Vector3(.36f,.012f,.008f),dark);
                        Box(detail,"Service hatch grip",new Vector3(.24f,0,-.452f),new Vector3(.035f,.12f,.03f),steel);
                    }
                    if(detail.childCount==0)Object.DestroyImmediate(detail.gameObject);
                    RailRiderFit.Apply(root);
                    RailVehicleDetail.Apply(root,materials);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            Debug.Log("RAIL_VISUAL_FINISH_OK: structural details, cab fittings, carriage finish and passenger cushion alignment.");
        }
    }
}
