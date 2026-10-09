using System;
using System.Linq;
using UnityEngine;
namespace EcoMinecarts.Editor
{
    // Authoring adapter only. Current art owns its fit; original fits are read
    // independently from LegacyVehicleSources by the design builder.
    public static class RailVehicleModernFit
    {
        static Bounds BoundsOf(GameObject root,Transform branch)
        {
            var bounds=new Bounds();bool first=true;
            foreach(var mesh in branch.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.name=="Render_LOD0")){
                var b=mesh.sharedMesh.bounds;
                for(int i=0;i<8;i++){
                    var p=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    p=root.transform.InverseTransformPoint(mesh.transform.TransformPoint(p));
                    if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
                }
            }
            if(first)throw new Exception("Missing prepared collision geometry "+root.name+branch.name);
            return bounds;
        }
        static void Box(GameObject root,BoxCollider box,Vector3 center,Vector3 size)
        {
            box.transform.position=root.transform.TransformPoint(center);box.transform.rotation=root.transform.rotation;box.transform.localScale=Vector3.one;box.center=Vector3.zero;box.size=size;
        }
        public static void Configure(GameObject root)
        {
            var art=root.transform.Find("PreparedVehicleArt");
            var fixedPart=art.Cast<Transform>().First(t=>t.name.StartsWith("PreparedPart_"));
            var body=BoundsOf(root,fixedPart);
            var underframe=root.transform.Find("COL_Minecart_Underframe")?.GetComponent<BoxCollider>();
            if(underframe!=null){var drive=root.GetComponent<RCCCarControllerV2>();float wheelbase=Mathf.Abs(drive.FrontLeftWheelCollider.transform.position.z-drive.RearLeftWheelCollider.transform.position.z);
                Box(root,underframe,new Vector3(0,.285f,0),new Vector3(Mathf.Min(body.size.x,.82f),.16f,Mathf.Max(.92f,wheelbase+.20f)));}
            if(RailDumpAnimationBuilder.Buckets.Contains(root.name)){
                var bucket=root.transform.Find("DumpAnimation/DumpHinge/Bucket");
                var prepared=bucket.Cast<Transform>().First(t=>t.name.StartsWith("PreparedPart_")&&t.name!="PreparedPart_DumpHardware");var b=BoundsOf(root,prepared);
                var storage=root.transform.Find("COL_Minecart_Body")?.GetComponent<BoxCollider>();
                if(storage!=null)Box(root,storage,b.center,b.size);
                foreach(var box in root.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.name=="CargoInteraction"))Box(root,box,b.center,b.size);
            }
            if(root.name=="RollerCoasterCartObject")foreach(var target in root.GetComponentsInChildren<SpecificInteractable>(true).Where(t=>t.name.StartsWith("PassengerSeatTarget"))){
                var c=target.GetComponent<BoxCollider>();if(c!=null)c.size=new Vector3(.46f,.40f,.48f);
            }
            bool tram=root.name=="HeritageTramObject",coach=root.name=="PassengerCarObject"||root.name=="LargePassengerCarObject";
            if(tram||coach){
                foreach(var box in root.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.transform.parent.name=="ExteriorPlatforms")){
                    var p=root.transform.InverseTransformPoint(box.transform.position);
                    if(box.name=="Tram passenger deck"||box.name=="Passenger deck")p.y=tram?.61f:.7045f;
                    else if(box.name.StartsWith("Passenger roof")||box.name.StartsWith("Tram canopy"))p.y=body.max.y-.012f;
                    else if(box.name.StartsWith("Tram clerestory"))p.y=body.max.y-.012f;
                    box.transform.position=root.transform.TransformPoint(p);
                }
            }
            foreach(var box in root.GetComponentsInChildren<BoxCollider>(true))if(box.size.x<=0||box.size.y<=0||box.size.z<=0)throw new Exception("Invalid modern collider "+root.name+box.name);
        }
    }
}
