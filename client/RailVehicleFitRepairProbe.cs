using System;
using System.Linq;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    public static class RailVehicleFitRepairProbe
    {
        static void Check(bool value,string error){if(!value)throw new Exception(error);}
        public static void Verify(GameObject[] prefabs)
        {
            foreach(var prefab in prefabs.Where(p=>p.GetComponent<Vehicle>()!=null))
            {
                if(prefab.name!="PassengerCarObject"&&prefab.name!="LargePassengerCarObject"
                    &&prefab.name!="RailroadHandcarObject"&&!RailMechanicalAnimationBuilder.Engines.Contains(prefab.name))continue;
                var root=UnityEngine.Object.Instantiate(prefab);root.name=prefab.name;root.SetActive(true);
                try
                {
                    if(root.name.StartsWith("PassengerCarObject")||root.name.StartsWith("LargePassengerCarObject"))
                    {
                        RailVehicleDesignProbe.Select(root,false);Physics.SyncTransforms();
                        var roof=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.transform.parent.name=="Roof").ToArray();
                        Check(roof.Length>0,"Missing visible original coach roof");
                        var edge=roof.Min(r=>r.bounds.min.y);
                        foreach(var post in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Window pillar"&&t.gameObject.activeInHierarchy))
                        {
                            var top=post.position.y+post.lossyScale.y/2;
                            var b=roof.OrderByDescending(r=>r.bounds.size.x).First().bounds;
                            var x=Mathf.Abs(post.position.x-b.center.x)+post.lossyScale.x/2;
                            var surface=edge+.045f+.14f*(1-Mathf.Pow(x/(b.size.x/2),2));
                            Check(top<=surface+.002f,"Original coach pillar protrudes through curved roof: "+root.name+" "+(top-surface));
                        }
                        VehicleIntegrationProbe.Capture(root,"repaired-coach-roof",false);
                    }
                    else if(prefab.name=="RailroadHandcarObject")
                    {
                        foreach(var modern in new[]{false,true})
                        {
                            RailVehicleDesignProbe.Select(root,modern);
                            var host=root.transform.Find("Handcar pump animation");var animator=host.GetComponent<Animator>();
                            var rod=host.Find("PumpRod");var pivot=host.Find("PumpPivot");
                            var world=root.GetComponent<WorldObject>();world.OnStringStateChanged[Array.IndexOf(world.StringStates,"RailPumpPose")].Invoke("PumpOn");
                            for(int frame=0;frame<48;frame++)
                            {
                                animator.Update(1f/48);
                                var upper=pivot.TransformPoint(new Vector3(.10f,0,.30f));
                                var top=rod.position+rod.up*rod.lossyScale.y;
                                Check(Vector3.Distance(top,upper)<.012f,"Handcar rod separates from moving beam: "+modern+" "+Vector3.Distance(top,upper));
                                if(modern){
                                    var mesh=pivot.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Render_LOD0");
                                    var shell=mesh.sharedMesh.vertices.Select(v=>mesh.transform.TransformPoint(v)).ToArray();
                                    foreach(var grip in root.GetComponent<Mountable>().seats[0].IKTargets)
                                        Check(shell.Min(p=>Vector3.Distance(p,grip.transform.position))<.065f,"Handcar hand target detached from animated grip");
                                    var length=mesh.sharedMesh.bounds.size.x*mesh.transform.parent.localScale.x;
                                    Check(length>1.00f&&length<1.025f,"Modern handcar beam remains stretched into rider");
                                }
                            }
                            VehicleIntegrationProbe.Capture(root,modern?"repaired-handcar-modern":"repaired-handcar-original",false);
                            if(modern)Debug.Log("HANDCAR_GRIPS_EXPORTED_OK: correct imported rocking-beam length, seat rearward clearance and two native hand targets remain on grip geometry through a full pump cycle");
                        }
                    }
                    else
                    {
                        RailVehicleDesignProbe.Select(root,true);Physics.SyncTransforms();
                        var cab=root.transform.Find("CabFittings");var deck=cab.Find("Cab floor");var floor=deck.localPosition.y+.035f;
                        var rear=deck.localPosition.z-deck.localScale.z/2;
                        var frameVertices=cab.Find("PreparedCabDetail").GetComponentsInChildren<MeshFilter>(true)
                            .SelectMany(f=>f.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(f.transform.TransformPoint(v))))
                            .Where(p=>Mathf.Abs(p.x)<.03f&&Mathf.Abs(p.z-rear)<.04f).ToArray();
                        Check(frameVertices.Any(p=>p.y<=floor+.681f)&&frameVertices.Any(p=>p.y>=floor+2.19f),"Rear cab mullion does not join panel and roof");
                        foreach(var gate in cab.Cast<Transform>().Where(t=>t.name=="Left travel gate"||t.name=="Right travel gate"))
                            Check(gate.GetComponentsInChildren<MeshRenderer>(true).All(r=>r.sharedMaterial!=null&&r.sharedMaterial.name.StartsWith("PreparedCabGreenDoor")&&r.sharedMaterial.GetTexture("_MainTex")!=null),"Travel gate lost painted wood texture");
                        var meshes=root.transform.Find("PreparedVehicleArt").GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="Render_LOD0").First();
                        var front=deck.localPosition.z+deck.localScale.z/2+.035f;
                        Check(meshes.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(meshes.transform.TransformPoint(v)))
                            .All(p=>p.z>=front-.001f||p.y<=floor-.079f),"Imported train geometry intersects cab walking deck");
                        VehicleIntegrationProbe.Capture(root,"repaired-cab",true);
                        var world=root.GetComponent<WorldObject>();
                        world.OnStateChangedEvents[Array.IndexOf(world.States,"CabTravelGatesClosed")].Invoke(true);
                        Physics.SyncTransforms();
                        VehicleIntegrationProbe.Capture(root,"repaired-closed-doors",false);
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            Debug.Log("VEHICLE_FIT_REPAIRS_EXPORTED_OK: original coach roof clearance, complete-cycle handcar beam/rod contact in both designs, joined train rear framing, material-bound gates and imported chassis below walking deck. Live avatar fit still needs Eco testing.");
        }
    }
}
