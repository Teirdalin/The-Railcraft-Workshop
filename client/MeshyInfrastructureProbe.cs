using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;
namespace EcoMinecarts.Editor
{
    public static class MeshyInfrastructureProbe
    {
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
        public static void Verify(GameObject[] prefabs)
        {
            if(!prefabs.Any(p=>p.transform.Find("Meshy station")!=null))return;
            int count=0;
            foreach(var name in new[]{"MinecartChainDriveObject","ElectricalRailChainDriveObject","RailcraftWorkbenchObject","CoasterStationObject"})
            {
                var root=Object.Instantiate(prefabs.Single(p=>p.name==name));
                try
                {
                    root.SetActive(true);
                    var imported=root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name.StartsWith("Meshy ")).ToArray();
                    Check(imported.Length==(name=="CoasterStationObject"?1:2),"Imported visual parts missing: "+name);
                    foreach(var r in imported)
                    {
                        var mesh=r.GetComponent<MeshFilter>().sharedMesh;
                        Check(mesh!=null&&mesh.uv.Length==mesh.vertexCount&&mesh.normals.Length==mesh.vertexCount,"Missing imported UV/normals "+name);
                        Check(r.enabled&&r.GetComponent<Collider>()==null,"Imported visual changed collider or was disabled "+name);
                        Check(r.sharedMaterial.shader.name==RailWorldMaterialBuilder.SurfaceShader,"Imported art bypasses native curved shader");
                        Check(r.sharedMaterial.mainTexture.width<=2048&&r.sharedMaterial.mainTexture.height<=2048,"Unbounded imported textures");
                    }
                    var animation=root.GetComponent<Animation>();var world=root.GetComponent<WorldObject>();
                    Check(world.size.x>0&&world.size.y>0&&world.size.z>0,"Invalid occupancy size "+name);
                    if(name.Contains("Drive"))
                    {
                        var shaft=root.transform.Find("ChainDriveVisual/ShaftIndicator");
                        var backing=root.transform.Find("Drive dark interior");Check(backing!=null&&backing.GetComponent<Collider>()==null&&backing.GetComponent<MeshRenderer>().enabled,"Drive interior backing missing");
                        var moving=shaft.GetComponentsInChildren<MeshRenderer>().Single(r=>r.enabled);
                        var fixedPart=imported.Single(r=>r!=moving);
                        var fixedPosition=fixedPart.transform.position;var fixedRotation=fixedPart.transform.rotation;
                        Check(!animation.enabled,"Idle drive animation enabled");
                        int slot=Array.IndexOf(world.States,"ChainRunning");Check(slot>=0,"Missing native ChainRunning event");
                        for(int i=0;i<world.OnStateChangedEvents[slot].GetPersistentEventCount();i++)world.OnStateChangedEvents[slot].SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                        world.OnStateChangedEvents[slot].Invoke(true);Check(animation.enabled,"Drive native state did not start motion");
                        animation.clip.SampleAnimation(root,0);var before=moving.transform.rotation;
                        animation.clip.SampleAnimation(root,.5f);Check(Quaternion.Angle(before,moving.transform.rotation)>89,"Imported spindle does not rotate");
                        Check(Vector3.Distance(fixedPosition,fixedPart.transform.position)<.0001f&&Quaternion.Angle(fixedRotation,fixedPart.transform.rotation)<.001f,"Drive housing rotates with spindle");
                        world.OnStateChangedEvents[slot].Invoke(false);Check(!animation.enabled,"Idle drive keeps animating");
                    }
                    if(name=="RailcraftWorkbenchObject")
                    {
                        RailcraftWorkbenchAssetBuilder.Verify(root);
                        var moving=root.transform.Find("Workbench/PressRam/Meshy workbench-ram");
                        animation.clip.SampleAnimation(root,0);var y=moving.position.y;
                        animation.clip.SampleAnimation(root,.35f);Check(Mathf.Abs(y-moving.position.y-.105f)<.002f,"Imported press ram did not follow native stroke");
                        animation.clip.SampleAnimation(root,1);Check(Mathf.Abs(y-moving.position.y)<.002f,"Press ram did not return to rest");
                    }
                    if(name=="CoasterStationObject"){
                        StationGroundingAssetPatch.Verify(root);var backing=root.transform.Find("Continuous timber platform");
                        Check(backing!=null&&backing.GetComponent<MeshRenderer>().enabled&&backing.localScale.x>=.9f&&backing.localScale.z>=1,"Station deck hole remains unbacked");
                    }
                    count++;
                }
                finally{Object.DestroyImmediate(root);}
            }
            var station=prefabs.Single(p=>p.name=="TrainStationObject").GetComponent<WorldObject>();
            Check(station.size.x>0&&station.size.y>0&&station.size.z>0,"Train station invalid occupancy size");
            Debug.Log("MESHY_INFRASTRUCTURE_EXPORTED_OK: "+count+" native objects; separate moving spindles and press ram, start/stop callbacks, curved textured visuals and native station grounding. Live Eco verification remains pending.");
        }
    }
}
