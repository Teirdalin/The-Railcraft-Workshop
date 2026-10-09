using System;
using System.Linq;
using UnityEngine;
namespace EcoMinecarts.Editor
{
    public static class OriginalCoasterRestraintProbe
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        public static void Verify(GameObject root)
        {
            if(root.name!="RollerCoasterCartObject")return;
            var world=root.GetComponent<WorldObject>();var evt=world.OnStringStateChanged[Array.IndexOf(world.StringStates,"RailRestraintPose")];
            for(int i=0;i<evt.GetPersistentEventCount();i++)evt.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            var host=root.transform.Find("PreparedVehicleArt/RestraintAnimation");var animator=host.GetComponent<Animator>();
            var pivots=new[]{host.Find("LeftPivot"),host.Find("RightPivot")};
            var padding=pivots.Select(p=>p.Find(OriginalCoasterRestraintBuilder.AssemblyPrefix+(p.name.StartsWith("Left")?"Left":"Right"))
                .GetComponentsInChildren<MeshRenderer>().Single(r=>r.sharedMaterial.name.StartsWith("MAT_CoasterPadding"))).ToArray();
            Vector3 Centre(MeshRenderer renderer)=>root.transform.InverseTransformPoint(renderer.transform.TransformPoint(renderer.GetComponent<MeshFilter>().sharedMesh.bounds.center));
            void Pose(string name){evt.Invoke(name);animator.Update(0);animator.Update(.02f);}
            // Evaluate interrupted blends at ordinary render-frame intervals.
            void Advance(float seconds){while(seconds>0){float step=Mathf.Min(seconds,1f/60);animator.Update(step);seconds-=step;}}
            var fixedNodes=root.GetComponent<Mountable>().seats.Select(s=>s.transform)
                .Concat(root.GetComponentsInChildren<WheelCollider>().Select(w=>w.transform)).ToArray();
            var fixedPoints=fixedNodes.Select(t=>root.transform.InverseTransformPoint(t.position)).ToArray();
            Pose("Secured");Advance(1.1f);
            var closed=padding.Select(Centre).ToArray();
            for(int i=0;i<2;i++)Check(Vector3.Distance(closed[i],new Vector3(i==0?-.288f:.288f,1.115f,.205f))<.003f,"Original padded bar closed fit changed");
            VehicleIntegrationProbe.Capture(root,"original-restraints-lowered",false);
            Pose("Boarding");Advance(.25f);
            var partial=pivots.Select(p=>Quaternion.Angle(p.localRotation,Quaternion.Euler(-30,0,0))).ToArray();
            Check(partial.All(a=>a>1&&a<29),"Visible restraints snapped or failed to start raising");
            Advance(1.1f);
            var open=padding.Select(Centre).ToArray();
            for(int i=0;i<2;i++)
            {
                Check(open[i].y>closed[i].y+.1f&&open[i].z<closed[i].z-.19f,"Visible padded bar did not rise away from entry");
                Check(Quaternion.Angle(pivots[i].localRotation,Quaternion.Euler(-60,0,0))<.1f,"Original hinge opening angle changed");
                Check(padding[i].bounds.min.z>-0.0895f,"Raised padding intersects original seat back");
            }
            VehicleIntegrationProbe.Capture(root,"original-restraints-raised",false);
            var carRotation=root.transform.rotation;root.transform.rotation=Quaternion.Euler(165,37,28);
            for(int i=0;i<2;i++)Check(Vector3.Distance(Centre(padding[i]),open[i])<.001f,"Loop rotation changed local restraint fit");
            root.transform.rotation=carRotation;
            Pose("Secured");Advance(.25f);
            Check(pivots.All(p=>Quaternion.Angle(p.localRotation,Quaternion.Euler(-30,0,0))>1),"Restraint lowering snapped");
            // Reverse during a transition and then close again: the latest
            // replicated snapshot must win without replaying a stale request.
            Pose("Boarding");Advance(.1f);Pose("Secured");Advance(1.1f);
            for(int i=0;i<2;i++)Check(Vector3.Distance(Centre(padding[i]),closed[i])<.001f,"Original restraint failed to return after interrupted motion: "+pivots[i].localRotation.eulerAngles+" / "+Centre(padding[i]));
            for(int i=0;i<fixedNodes.Length;i++)Check(Vector3.Distance(root.transform.InverseTransformPoint(fixedNodes[i].position),fixedPoints[i])<.001f,"Restraints moved seats or native wheels");
            Debug.Log("ORIGINAL_COASTER_RESTRAINTS_EXPORTED_OK: visible bars, padding and swing arms raise/lower smoothly on replicated station states; interruption, loop pose, seat clearance and fixed native rig verified");
        }
    }
}
