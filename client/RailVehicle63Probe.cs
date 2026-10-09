using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace EcoMinecarts.Editor
{
    public static class RailVehicle63Probe
    {
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        public static void Verify(GameObject[] prefabs)
        {
            var counts=new List<string>();
            foreach(var prefab in prefabs.Where(p=>p.name=="RollerCoasterCartObject"||RailMechanicalAnimationBuilder.Engines.Contains(p.name)))
            {
                var root=UnityEngine.Object.Instantiate(prefab);root.name=prefab.name;
                try
                {
                    RailVehicleDesignProbe.Select(root,true);Physics.SyncTransforms();
                    foreach(var lod in root.GetComponentsInChildren<LODGroup>(true))lod.ForceLOD(0);
                    if(root.name=="RollerCoasterCartObject")
                    {
                        var art=root.transform.Find("PreparedVehicleArt");var body=art.Find("PreparedPart_CoasterBody").GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Render_LOD0").bounds;
                        var chairs=art.Cast<Transform>().Where(t=>t.name=="PreparedPart_CoasterSeat"||t.name.StartsWith("PreparedPart_CoasterSeat_Design")).ToArray();
                        Check(body.size.x>1.70f&&body.size.x<1.80f&&body.size.z<1.96f,"Coaster seating bay or coupling envelope");
                        Check(chairs.Length==2&&chairs.All(t=>t.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Render_LOD0").bounds.size.y>1.13f),"Coaster chairs still undersized");
                        Check(Mathf.Abs(root.GetComponent<Mountable>().seats[1].transform.position.x-root.GetComponent<Mountable>().seats[2].transform.position.x)>.67f,"Coaster passenger shoulders overlap");
                        VerifySeatBay(root,art,chairs);
                        VehicleIntegrationProbe.Capture(root,"adult-coaster",false);
                    }
                    else
                    {
                        var body=root.transform.Find("PreparedVehicleArt").GetComponentsInChildren<MeshFilter>(true).First(f=>f.name=="Render_LOD0");
                        Check(Mathf.Abs(body.transform.lossyScale.x/body.transform.lossyScale.z-1)<.002f,"Engine stretched horizontally");
                        var triangles=root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.gameObject.activeInHierarchy&&f.GetComponent<MeshRenderer>()!=null&&f.GetComponent<MeshRenderer>().enabled&&!f.name.StartsWith("Render_LOD1")&&!f.name.StartsWith("Render_LOD2")).Sum(f=>f.sharedMesh.triangles.Length/3);
                        counts.Add(root.name+": "+triangles+" visible assembled LOD0 triangles; body "+body.sharedMesh.triangles.Length/3);
                        var world=root.GetComponent<WorldObject>();var lights=world.OnStateChangedEvents[Array.IndexOf(world.States,"RailAutomaticLights")];
                        Check(body.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_BumpMap")!=null,"Original engine normal map missing");
                        foreach(Transform lamp in root.transform.Find("Train automatic lamps")){
                            var shell=lamp.Find("Headlamp casing");var lens=lamp.Find("Luminous headlamp lens");
                            Check(lens.localPosition.z+lens.localScale.y>shell.localPosition.z+shell.localScale.y+.001f,"Headlamp glow and casing z-fight");
                        }
                        foreach(var modern in new[]{false,true})
                        {
                            RailVehicleDesignProbe.Select(root,modern);
                            lights.Invoke(true);Check(root.transform.Find("Train automatic lamps").GetComponentsInChildren<Light>().Length==2&&root.transform.Find("Train automatic lamps").GetComponentsInChildren<Light>().All(l=>l.enabled&&l.type==LightType.Spot),"Train lights do not illuminate in both designs");
                            lights.Invoke(false);Check(root.transform.Find("Train automatic lamps").GetComponentsInChildren<Light>().All(l=>!l.enabled),"Train lights stay on in daylight");
                        }
                        RailVehicleDesignProbe.Select(root,true);lights.Invoke(true);
                        foreach(var label in root.transform.Find("Optional vehicle nameplates").GetComponentsInChildren<TMPro.TMP_Text>(true))label.text="Railworks";
                        world.OnStateChangedEvents[Array.IndexOf(world.States,"VehicleTextVisible")].Invoke(true);
                        var panel=root.transform.Find("CabFittings/Cab rear panel");var labelPosition=root.transform.InverseTransformPoint(root.transform.Find("Optional vehicle nameplates").GetComponentInChildren<TMPro.TMP_Text>().transform.position);
                        Check(labelPosition.y>root.transform.Find("CabFittings/Cab floor").localPosition.y+.2f&&labelPosition.z<panel.localPosition.z,"Train nameplate still under chassis");
                        VehicleIntegrationProbe.Capture(root,"detailed-lit-engine",false);
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            var folder=Environment.GetEnvironmentVariable("ECO_ANIMATION_PREVIEW_DIR");if(!string.IsNullOrEmpty(folder))File.WriteAllLines(Path.Combine(folder,"engine-triangle-counts.txt"),counts);
            Debug.Log("RAIL_VEHICLE_63_EXPORTED_OK: adult coaster scale, unchanged coupling length, uniform engine horizontal fitting, native day/night light transitions in both designs and cab-mounted nameplates. "+string.Join("; ",counts));
        }
        static void VerifySeatBay(GameObject root,Transform art,Transform[] chairs)
        {
            var shell=art.Find("PreparedPart_CoasterBody").GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Render_LOD0");
            var points=shell.sharedMesh.vertices.Select(v=>root.transform.InverseTransformPoint(shell.transform.TransformPoint(v))).ToArray();
            Check(points.Where(p=>p.z<-.45f).Max(p=>p.y)>points.Where(p=>p.z>.45f).Max(p=>p.y)+.08f,"Coaster body seat bay faces away from chairs");
            var collision=new GameObject("Coaster seat bay verification");
            try{
                collision.transform.position=shell.transform.position;collision.transform.rotation=shell.transform.rotation;collision.transform.localScale=shell.transform.lossyScale;
                var collider=collision.AddComponent<MeshCollider>();collider.sharedMesh=shell.sharedMesh;Physics.SyncTransforms();
                int checkedVertices=0;
                foreach(var chair in chairs){
                    var mesh=chair.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Render_LOD0");
                    foreach(var v in mesh.sharedMesh.vertices){
                        var p=mesh.transform.TransformPoint(v);var local=root.transform.InverseTransformPoint(p);
                        // Check the lower outer chair shell where it formerly
                        // cut through the seating compartment's side walls.
                        if(local.y<.68f||local.y>.90f||Mathf.Abs(local.x)<.58f)continue;
                        checkedVertices++;
                        if(collider.Raycast(new Ray(p+Vector3.up*2,Vector3.down),out var hit,4))
                            Check(hit.point.y<p.y+.006f,"Coaster chair intersects body wall at "+local);
                    }
                }
                Check(checkedVertices>30,"Coaster wall clearance was not sampled");
                foreach(var plate in art.Cast<Transform>().Where(t=>t.name.StartsWith("Seat mounting pedestal")))
                    Check(plate.localPosition.y+plate.localScale.y*.5f<.60f,"Coaster mounting plate intersects cushion");
                var mounts=root.GetComponent<Mountable>().seats.Skip(1).ToArray();
                for(int i=0;i<chairs.Length;i++){
                    var cushion=chairs[i].Find("Anchors/PassengerSeat").position;
                    var expected=cushion+root.transform.up*(.20f-RailRiderFit.SeatedHipHeight);
                    Check(Vector3.Distance(mounts[i].transform.position,expected)<.003f,"Coaster rider detached from cushion anchor");
                }
                Debug.Log("COASTER_SEAT_BAY_EXPORTED_OK: correct body facing, adult chair wall clearance, deck-level plates and mesh-anchored native riders; "+checkedVertices+" shell vertices checked");
            }finally{UnityEngine.Object.DestroyImmediate(collision);}
        }
    }
}
