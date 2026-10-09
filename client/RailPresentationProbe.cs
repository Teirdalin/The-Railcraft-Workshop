using System;
using System.Linq;
using UnityEngine;
namespace EcoMinecarts.Editor
{
    public static class RailPresentationProbe
    {
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
        public static void Verify(GameObject[] prefabs)
        {
            foreach(var name in new[]{"HeritageTramObject","MineTrainObject","PassengerLocomotiveObject","FreightLocomotiveObject","LargeTrainEngineObject","MinecartChainDriveObject","ElectricalRailChainDriveObject","CoasterStationObject"}){
                var root=UnityEngine.Object.Instantiate(prefabs.Single(p=>p.name==name));root.name=name;root.SetActive(true);
                try{
                    if(RailMechanicalAnimationBuilder.Engines.Contains(name)){
                        RailVehicleDesignProbe.Select(root,false);Physics.SyncTransforms();
                        var bar=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Smokebox locking bar");
                        var boss=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Smokebox latch boss");
                        Check(bar.bounds.max.z-boss.bounds.max.z>.005f&&bar.bounds.min.z<boss.bounds.max.z,"Smokebox latch surfaces overlap or locking bar floats: "+name);
                        VehicleIntegrationProbe.Capture(root,"original-latch-clearance",false);
                    }
                    if(name=="HeritageTramObject")foreach(var modern in new[]{false,true}){
                        RailVehicleDesignProbe.Select(root,modern);
                        var host=root.transform.Find("Tram night lamps");var lamps=host.GetComponentsInChildren<Light>(true);var lenses=host.GetComponentsInChildren<MeshRenderer>(true);
                        Check(lamps.Length==2&&lenses.Length==2,"Tram needs two illuminated lamp faces");
                        foreach(var lamp in lamps){
                            var p=root.transform.InverseTransformPoint(lamp.transform.position);
                            Check(Mathf.Abs(p.y-1.38f)<.002f&&Mathf.Abs(Mathf.Abs(p.z)-1.433f)<.003f,"Tram light is not at its authored lamp face: "+p);
                            Check(lamp.type==LightType.Spot&&Mathf.Abs(lamp.spotAngle-71.5f)<.01f&&lamp.transform.forward.z*p.z>0,"Tram headlamp does not point outward");
                        }
                        var world=root.GetComponent<WorldObject>();var evt=world.OnStateChangedEvents[Array.IndexOf(world.States,"TramNightLights")];
                        for(int i=0;i<evt.GetPersistentEventCount();i++)evt.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                        evt.Invoke(true);Check(lamps.All(l=>l.enabled)&&lenses.All(l=>l.enabled&&l.gameObject.activeInHierarchy),"Night state failed to light actual tram lenses");
                        Check(lenses.All(l=>l.sharedMaterial.IsKeywordEnabled("_EMISSION")&&l.sharedMaterial.GetColor("_EmissionColor").maxColorComponent>1),"Tram lens lost its emission keyword after export");
                        VehicleIntegrationProbe.Capture(root,modern?"modern-lamp-night":"original-lamp-night",false,true);
                        var preview=Environment.GetEnvironmentVariable("ECO_ANIMATION_PREVIEW_DIR");
                        if(!string.IsNullOrEmpty(preview)){
                            var texture=new Texture2D(2,2);
                            try{texture.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(preview,name+"-"+(modern?"modern-lamp-night":"original-lamp-night")+".png")));
                                Check(texture.GetPixels32().Count(p=>p.r>220&&p.g>150&&p.b>75)>4,"Tram night lens is enabled but does not visibly emit light");}
                            finally{UnityEngine.Object.DestroyImmediate(texture);}
                        }
                        evt.Invoke(false);Check(lamps.All(l=>!l.enabled)&&lenses.All(l=>!l.enabled),"Tram lamps glow during daytime");
                        VehicleIntegrationProbe.Capture(root,modern?"modern-lamp-day":"original-lamp-day",false);
                    }
                    if(name=="LargeTrainEngineObject"){
                        RailVehicleDesignProbe.Select(root,false);Physics.SyncTransforms();
                        var neck=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Firebox neck");
                        var boiler=root.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Steam boiler");
                        var floor=root.transform.Find("CabFittings/Cab floor").GetComponent<BoxCollider>();
                        Check(neck.gameObject.activeInHierarchy&&neck.bounds.max.z>=boiler.bounds.min.z&&neck.bounds.min.z<=floor.bounds.max.z,"Large engine firebox fails to bridge boiler and cab");
                        VehicleIntegrationProbe.Capture(root,"original-firebox",false);
                    }
                    if(name.Contains("Drive")){
                        var backing=root.transform.Find("Drive dark interior");
                        Check(Mathf.Abs(backing.localPosition.z+.22f)<.001f&&Mathf.Abs(backing.localScale.x-.30f)<.001f,"Drive backing is outside housing or oversized");
                        Check(backing.GetComponent<Collider>()==null,"Drive interior changes interaction collision");
                        root.transform.rotation=Quaternion.Euler(0,180,0);VehicleIntegrationProbe.Capture(root,"inset-backing",false);
                    }
                    if(name=="CoasterStationObject"){
                        var geometry=root.transform.Find("CoasterStation");var lettering=root.transform.Find("Station lettering");
                        Check(root.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.enabled).All(r=>r.name=="Meshy station"||r.name=="Continuous timber platform"||r.transform.IsChildOf(geometry)||r.transform.IsChildOf(lettering)),"Station still renders obsolete cabinet fittings");
                        var rails=geometry.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name=="Continuous rail").ToArray();
                        Check(rails.Length==3&&rails.All(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sharedMaterial!=null&&r.bounds.size.z>.99f),"Functional station rails are hidden or incomplete");
                        var platform=root.transform.Find("Continuous timber platform");
                        Check(Mathf.Abs(platform.localScale.y-.25f)<.001f&&Mathf.Abs(platform.localPosition.y+platform.localScale.y/2+.25f)<.001f,"Station requires a solid deck at the existing boarding height");
                        var mesh=root.transform.Find("Meshy station").GetComponent<MeshFilter>().sharedMesh;var v=mesh.vertices;var t=mesh.triangles;
                        Check(!Enumerable.Range(0,t.Length/3).Any(i=>v[t[i*3]].y<=-.24f&&v[t[i*3+1]].y<=-.24f&&v[t[i*3+2]].y<=-.24f),"Perforated imported station deck remains over solid deck");
                        var body=t.Distinct().Select(i=>v[i]).Where(p=>p.y<.62f).ToArray();
                        Check(body.All(p=>p.x>=.02f)&&body.Max(p=>p.x)-body.Min(p=>p.x)>.35f,"Station cabinet lacks width or still contains decorative rails");
                        Check(body.Where(p=>p.y<.30f).All(p=>p.z>=-.435f&&p.z<=.115f),"Cabinet foot still flares into a fin");
                        Check(body.Where(p=>p.y<-.17f).All(p=>p.z>=-.381f),"Cabinet base still projects past its door frame");
                        var world=root.GetComponent<WorldObject>();var textEvent=world.OnStringStateChanged[Array.IndexOf(world.StringStates,"StationSignText")];
                        Check(textEvent.GetPersistentEventCount()==2,"Station sign lacks two native text listeners");
                        for(int i=0;i<2;i++)textEvent.SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                        textEvent.Invoke("Pine Grove");var labels=lettering.GetComponentsInChildren<TMPro.TextMeshPro>();
                        Check(labels.Length==2&&labels.All(label=>label.text=="Pine Grove"&&!label.richText&&label.gameObject.activeInHierarchy&&label.fontSharedMaterial.GetColor("_Color").maxColorComponent<.1f),"Station sign did not display native replicated dark plain text");
                        foreach(var label in labels){label.ForceMeshUpdate();Check(label.textInfo.characterCount==10&&label.textInfo.meshInfo.Any(info=>info.vertexCount>0),"Station caption produced no glyph geometry");}
                        var sign=root.transform.Find("Meshy station").gameObject.AddComponent<MeshCollider>();sign.sharedMesh=mesh;
                        Physics.SyncTransforms();
                        foreach(var label in labels){
                            var outward=-label.transform.forward;
                            Check(sign.Raycast(new Ray(label.transform.position+outward*.02f,-outward),out var hit,.04f),"Station text does not sit over the sign surface");
                            var clearance=Vector3.Dot(label.transform.position-hit.point,outward);
                            Check(clearance>.001f&&clearance<.008f,"Station text is buried or floats too far above the sign face: "+clearance);
                        }
                        UnityEngine.Object.DestroyImmediate(sign);
                        Debug.Log("STATION_CAPTION_CLEARANCE_EXPORTED_OK: front and rear lettering are 1-8 mm above the actual exported sign mesh.");
                        VehicleIntegrationProbe.Capture(root,"solid-platform",false);
                        root.transform.rotation=Quaternion.Euler(0,180,0);VehicleIntegrationProbe.Capture(root,"station-controls-caption",false);
                        textEvent.Invoke("");Check(labels.All(label=>label.text==""),"Cleared station text persists visually");
                        Debug.Log("STATION_SIGN_EXPORTED_OK: restored complete native track, widened cabinet without flared feet, two-sided plain caption, native change callbacks and clearing.");
                    }
                }finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            Debug.Log("PRESENTATION_REPAIRS_EXPORTED_OK: face-mounted tram spots and night lenses in both designs; inset drive backing; solid station deck without fins or guide rails; large-engine firebox overlap; four original train latch surfaces separated. Live Eco acceptance remains pending.");
        }
    }
}
