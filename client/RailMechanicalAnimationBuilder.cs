using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace EcoMinecarts.Editor
{
    // One configured slider-crank rig for each steam engine. The same clip
    // drives wheels and rods, so their phase cannot drift from one another.
    public static class RailMechanicalAnimationBuilder
    {
        internal static readonly string[] Engines={"MineTrainObject","PassengerLocomotiveObject","FreightLocomotiveObject","LargeTrainEngineObject"};
        internal static void Prepare(GameObject root)
        {
            if(!Engines.Contains(root.name))return;
            if(root.transform.Find("PreparedVehicleArt")==null)RailVehicleDetail.Apply(root,new Dictionary<string,Material>{
                {"MAT_IronBare",Material("MAT_IronBare")},{"MAT_WoodRail",Material("MAT_WoodRail")}});
            foreach(var n in root.GetComponentsInChildren<Transform>(true).Where(n=>n.name=="Connecting rod"))
                if(n.GetComponent<Renderer>()!=null)n.GetComponent<Renderer>().enabled=false;
        }
        static Material Material(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/EcoMinecarts/Materials/"+name+".mat");
        static Transform Box(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            var n=GameObject.CreatePrimitive(PrimitiveType.Cube);n.name=name;n.transform.SetParent(parent,false);
            n.transform.localPosition=position;n.transform.localScale=scale;n.GetComponent<Renderer>().sharedMaterial=material;
            UnityEngine.Object.DestroyImmediate(n.GetComponent<Collider>());return n.transform;
        }
        internal static AnimationClip Motion(GameObject root,Transform host,int index,float radius)
        {
            if(index>1||!Engines.Contains(root.name))return null;
            var old=host.Find("MechanicalLinkage");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var gear=new GameObject("MechanicalLinkage").transform;gear.SetParent(host,false);
            var drive=root.GetComponent<RCCCarControllerV2>();
            var wb=Mathf.Abs(drive.FrontLeftWheelCollider.transform.localPosition.z-drive.RearLeftWheelCollider.transform.localPosition.z);
            int side=index==0?-1:1;float crank=radius*.43f,quarter=side<0?0:Mathf.PI/2;
            var steel=Material("MAT_IronBare");var brass=Material("MAT_VehicleBrass");
            float x=side*.071f;
            Box(gear,"CouplingRod",new Vector3(x,0,-wb/2),new Vector3(.028f,.045f,wb),steel);
            Box(gear,"DriveRod",Vector3.zero,new Vector3(.035f,.045f,1),steel);
            Box(gear,"Crosshead",Vector3.zero,new Vector3(.070f,.080f,.120f),steel);
            Box(gear,"PistonRod",Vector3.zero,new Vector3(.024f,.024f,.32f),steel);
            Box(gear,"ValveStem",Vector3.zero,new Vector3(.016f,.016f,.36f),steel);
            float crossY=(root.name=="MineTrainObject"?.26f:.29f)-radius;
            float cylinderZ=wb*.36f-wb/2;
            float restZ=cylinderZ-.28f;
            float link=Mathf.Sqrt(Mathf.Pow(restZ+wb,2)+crossY*crossY);
            var clip=new AnimationClip();float length=2*Mathf.PI*radius;
            Vector3 Crank(float t)=>new Vector3(x,crank*Mathf.Cos(t*2*Mathf.PI+quarter),-wb+crank*Mathf.Sin(t*2*Mathf.PI+quarter));
            Vector3 Slider(float t){var a=Crank(t);return new Vector3(x,crossY,a.z+Mathf.Sqrt(Mathf.Max(.0001f,link*link-Mathf.Pow(crossY-a.y,2))));}
            void Curve(string node,string property,Func<float,float> value)
            {
                var curve=new AnimationCurve(Enumerable.Range(0,129).Select(i=>new Keyframe(i*length/128,value(i/128f))).ToArray());
                for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
                clip.SetCurve("MechanicalLinkage/"+node,typeof(Transform),property,curve);
            }
            foreach(var axis in new[]{0,1,2})
            {
                var a=axis;var prop="localPosition."+new[]{"x","y","z"}[a];
                Curve("CouplingRod",prop,t=>new Vector3(x,Crank(t).y,Crank(t).z+wb/2)[a]);
                Curve("DriveRod",prop,t=>((Crank(t)+Slider(t))*.5f)[a]);
                Curve("Crosshead",prop,t=>Slider(t)[a]);
                Curve("PistonRod",prop,t=>(Slider(t)+Vector3.forward*.16f)[a]);
                Curve("ValveStem",prop,t=>new Vector3(x,crossY+.12f,cylinderZ-.13f+.04f*Mathf.Sin(t*2*Mathf.PI+quarter))[a]);
            }
            gear.Find("DriveRod").localScale=new Vector3(.035f,.045f,link);
            Curve("DriveRod","localEulerAnglesRaw.x",t=>-Mathf.Atan2(Slider(t).y-Crank(t).y,Slider(t).z-Crank(t).z)*Mathf.Rad2Deg);
            clip.SampleAnimation(host.gameObject,0);
            return clip;
        }
        internal static void CrankPins(GameObject root)
        {
            if(!Engines.Contains(root.name))return;
            var radius=root.GetComponent<RCCCarControllerV2>().FrontLeftWheelCollider.radius;
            foreach(var side in new[]{-1,1})foreach(var axle in new[]{"F","R"})
            {
                var spin=root.transform.Find("WheelRoll_"+axle+(side<0?"L":"R")+"/Spin");
                foreach(var prior in spin.Cast<Transform>().Where(t=>t.name=="Animated crank pin").ToArray())UnityEngine.Object.DestroyImmediate(prior.gameObject);
                float phase=side<0?0:Mathf.PI/2;
                Box(spin,"Animated crank pin",new Vector3(side*.071f,Mathf.Cos(phase)*radius*.43f,Mathf.Sin(phase)*radius*.43f),new Vector3(.045f,.06f,.06f),Material("MAT_VehicleBrass"));
            }
        }
        internal static void Paint(GameObject root)
        {
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var region=RailVehiclePaintBuilder.Region(renderer);if(region==0)continue;
                renderer.sharedMaterials=renderer.sharedMaterials.Select(material=>
                {
                    if(material==null)return material;
                    var suffix=material.name.LastIndexOf("_Paint",StringComparison.Ordinal);
                    if(suffix>=0)material=Material(material.name.Substring(0,suffix))??material;
                    return RailVehiclePaintBuilder.PaintMaterial(material,region);
                }).ToArray();
            }
        }
    }
}
