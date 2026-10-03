using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    // Model construction only. Contact plane, track gauge and coupler anchors stay fixed.
    public static class RailModelPolish
    {
        static Transform Part(Transform parent,string name,PrimitiveType type,Vector3 p,Vector3 scale,Material m,Quaternion q)
        {
            var o=GameObject.CreatePrimitive(type); o.name=name; o.transform.SetParent(parent,false);
            o.transform.localPosition=p; o.transform.localScale=scale; o.transform.localRotation=q;
            o.GetComponent<Renderer>().sharedMaterial=m; Object.DestroyImmediate(o.GetComponent<Collider>()); return o.transform;
        }
        static Transform Box(Transform p,string n,Vector3 v,Vector3 s,Material m) => Part(p,n,PrimitiveType.Cube,v,s,m,Quaternion.identity);
        static Transform Pin(Transform p,string n,Vector3 v,float r,float length,Material m) => Part(p,n,PrimitiveType.Cylinder,v,new Vector3(2*r,length/2,2*r),m,Quaternion.Euler(0,0,90));
        static Material Timber(Material source,string name,float brightness)
        {
            var path="Assets/EcoMinecarts/Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null) { m=new Material(source); AssetDatabase.CreateAsset(m,path); }
            else m.CopyPropertiesFromMaterial(source);
            var c=source.color; m.color=new Color(c.r*brightness,c.g*brightness,c.b*brightness,c.a);
            m.name=name; EditorUtility.SetDirty(m); return m;
        }
        public static void WoodenBody(GameObject root,Material wood)
        {
            Object.DestroyImmediate(root.transform.Find("Minecart_Visual").gameObject);
            var model=new GameObject("WoodenPlankBody").transform; model.SetParent(root.transform,false);
            var dark=Timber(wood,"MAT_TimberBrace",.72f);
            var pale=Timber(wood,"MAT_TimberPlank",1.12f);
            foreach(var side in new[]{-1,1}) {
                Box(model,"Timber chassis",new Vector3(side*.21f,.21f,0),new Vector3(.10f,.13f,1.17f),dark);
                for(int row=0;row<4;row++) {
                    var y=.33f+row*.12f; var x=.30f+row*.023f;
                    var plank=Box(model,"Side plank",new Vector3(side*x,y,0),new Vector3(.045f,.113f,1.18f),row%2==0?wood:pale);
                    plank.localRotation=Quaternion.Euler(0,0,-side*10.85f);
                    Box(model,"End plank",new Vector3(0,y,side*.58f),new Vector3(2*x,.113f,.045f),row%2==0?pale:wood);
                }
                foreach(var z in new[]{-.49f,0f,.49f}) {
                    var brace=Box(model,"Timber brace",new Vector3(side*.358f,.51f,z),new Vector3(.06f,.51f,.055f),dark);
                    brace.localRotation=Quaternion.Euler(0,0,-side*10.85f);
                    foreach(var y in new[]{.33f,.67f}) Pin(model,"Wooden dowel",new Vector3(side*(.322f+(y-.33f)*.19f),y,z),.013f,.065f,pale);
                }
                foreach(var x in new[]{-.13f,.13f})
                    Box(model,"Handle arm",new Vector3(x,.57f,side*.632f),new Vector3(.035f,.065f,.11f),dark);
                Pin(model,"Wooden pull grip",new Vector3(0,.60f,side*.674f),.022f,.30f,dark);
                Pin(model,"Timber axle",new Vector3(0,.18f,side*.41f),.035f,.64f,dark);
                Box(model,"Wooden coupling drawbar",new Vector3(0,.27f,side*.72f),new Vector3(.10f,.07f,.22f),dark);
                Part(model,"Wooden coupling peg",PrimitiveType.Cylinder,new Vector3(0,.30f,side*.806f),new Vector3(.04f,.055f,.04f),pale,Quaternion.identity);
            }
            for(int i=0;i<5;i++) Box(model,"Floor plank",new Vector3((i-2)*.115f,.268f,0),new Vector3(.109f,.04f,1.17f),i%2==0?wood:pale);
        }
        public static void Wheel(Transform wheel,float radius,bool wooden,Material wood,Material iron,Material paint)
        {
            foreach(Transform child in wheel.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            var side=Mathf.Sign(wheel.localPosition.x);
            var face=wooden?Timber(wood,"MAT_TimberBrace",.72f):paint;
            Pin(wheel,"Wheel tyre",Vector3.zero,radius,.07f,face);
            Pin(wheel,"Wheel flange",new Vector3(-side*.039f,0,0),radius+.012f,.018f,wooden?wood:iron);
            Pin(wheel,"Wheel hub",new Vector3(side*.048f,0,0),radius*.29f,.036f,wooden?wood:iron);
            // Recessed dark disc and radial raised spokes add readable construction.
            Pin(wheel,"Recessed wheel web",new Vector3(side*.037f,0,0),radius*.77f,.009f,face);
            for(int i=0;i<8;i++) {
                var spoke=Box(wheel,wooden?"Wooden spoke":"Cast spoke",new Vector3(side*.045f,0,0),new Vector3(.018f,radius*1.55f,.021f),wooden?wood:iron);
                spoke.localRotation=Quaternion.Euler(i*22.5f,0,0);
            }
        }
        public static void Axles(GameObject root,float wheelbase,float radius,Material iron,float halfGauge=.30f)
        {
            var model=new GameObject("RunningGear").transform; model.SetParent(root.transform,false);
            foreach(var end in new[]{-1,1}) {
                Pin(model,"Wheel axle",new Vector3(0,radius,end*wheelbase/2),.035f,2*halfGauge+.05f,iron);
                foreach(var side in new[]{-1,1}) {
                    Box(model,"Axle bearing",new Vector3(side*(halfGauge-.08f),.28f,end*wheelbase/2),new Vector3(.085f,.18f,.13f),iron);
                    for(int leaf=0;leaf<3;leaf++)
                        Box(model,"Leaf spring",new Vector3(side*(halfGauge-.08f),.38f+leaf*.012f,end*wheelbase/2),new Vector3(.09f,.012f,.22f+leaf*.07f),iron);
                }
            }
        }
        public static void EngineSupports(Transform model,float floor,float cabZ,float boilerZ,float radius,float length,float width,Material iron,Material paint)
        {
            // Deck top is .46. Saddle tops enter the boiler slightly, avoiding floating seams.
            var height=Mathf.Max(.08f,floor-.46f+.10f);
            foreach(var z in new[]{boilerZ-length*.16f,boilerZ+length*.17f})
                Box(model,"Boiler saddle",new Vector3(0,.46f+height/2,z),new Vector3(radius*1.35f,height,.17f),iron);
            if(floor>.55f) {
                Box(model,"Cab riser",new Vector3(0,(.45f+floor-.075f)/2,cabZ),new Vector3(width*.9f,floor-.075f-.45f,length*.30f),paint);
                foreach(var side in new[]{-1,1}) {
                    Box(model,"Cab step",new Vector3(side*(width*.5f+.055f),.36f,cabZ),new Vector3(.20f,.045f,.38f),iron);
                    Box(model,"Step support",new Vector3(side*width*.46f,.43f,cabZ),new Vector3(.045f,.15f,.28f),iron);
                }
            }
            Box(model,"Firebox",new Vector3(0,.46f+height/2,boilerZ-length*.23f),new Vector3(radius*1.55f,height+.12f,.28f),paint);
        }
        public static void PumpLinkage(GameObject root,Transform pivot,AnimationClip clip,Material iron)
        {
            Pin(pivot,"Linkage upper pin",new Vector3(.10f,0,.30f),.025f,.22f,iron);
            var rod=Part(root.transform,"PumpRod",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.035f,.275f,.035f),iron,Quaternion.identity);
            var slider=new GameObject("PumpSlider").transform; slider.SetParent(root.transform,false);
            Pin(slider,"Linkage lower pin",Vector3.zero,.025f,.10f,iron);
            Box(slider,"Sliding pushrod",new Vector3(0,-.13f,0),new Vector3(.035f,.29f,.035f),iron);
            Box(root.transform,"Crank housing",new Vector3(.10f,.48f,.24f),new Vector3(.13f,.30f,.17f),iron);
            var times=Enumerable.Range(0,129).Select(i=>i/128f).ToArray();
            float Angle(float t)=>-12f*Mathf.Cos(2*Mathf.PI*t);
            Vector3 Upper(float t)=>new Vector3(0,1.22f,0)+Quaternion.Euler(Angle(t),0,0)*new Vector3(.10f,0,.30f);
            Vector3 Lower(float t) { var a=Upper(t); return new Vector3(.10f,a.y-Mathf.Sqrt(.55f*.55f-(a.z-.24f)*(a.z-.24f)),.24f); }
            void Curve(string path,string prop,Func<float,float> value) {
                var c=new AnimationCurve(times.Select(t=>new Keyframe(t,value(t))).ToArray());
                for(int k=0;k<c.length;k++) { AnimationUtility.SetKeyLeftTangentMode(c,k,AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(c,k,AnimationUtility.TangentMode.Linear); }
                clip.SetCurve(path,typeof(Transform),prop,c);
            }
            Curve("PumpPivot","localEulerAngles.x",Angle);
            foreach(var axis in new[]{0,1,2}) {
                var a=axis; var suffix=new[]{"x","y","z"}[a];
                Curve("PumpRod","localPosition."+suffix,t=>((Upper(t)+Lower(t))*.5f)[a]);
                Curve("PumpSlider","localPosition."+suffix,t=>Lower(t)[a]);
            }
            Curve("PumpRod","localEulerAngles.x",t=>Mathf.Atan2(Upper(t).z-Lower(t).z,Upper(t).y-Lower(t).y)*Mathf.Rad2Deg);
            // Author the parked pose at the same phase as the clip's first frame.
            pivot.localRotation=Quaternion.Euler(Angle(0),0,0); slider.localPosition=Lower(0);
            rod.localPosition=(Upper(0)+Lower(0))*.5f;
            rod.localRotation=Quaternion.FromToRotation(Vector3.up,Upper(0)-Lower(0));
        }
    }
}
