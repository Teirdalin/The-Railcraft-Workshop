using System;
using System.Linq;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    public static class TramAssetProbe
    {
        public static void Verify(GameObject[] prefabs)
        {
            var tram=prefabs.Single(p=>p.name=="HeritageTramObject");
            var vehicle=tram.GetComponent<global::Vehicle>();
            var mounts=tram.GetComponent<Mountable>();
            if(tram.GetComponentsInChildren<WheelCollider>(true).Any(w=>w.enabled)||tram.GetComponent<Rigidbody>().useGravity
                ||!tram.GetComponent<Rigidbody>().isKinematic||tram.GetComponent<SyncPhysics>().distanceToIgnorePhysics!=0
                ||tram.GetComponent<SyncPhysics>().AutomaticChunkDepenetrationEnabled||tram.GetComponent<SyncPhysics>().SyncVelocity)
                throw new Exception("Passenger-only tram still applies native suspension or gravity against guided poses");
            if(vehicle.LicensePlate==null||vehicle.LicensePlate.font==null||vehicle.LicensePlate.fontSharedMaterial==null
                ||vehicle.ExtraLicensePlates.Length!=1||vehicle.ExtraLicensePlates[0]==null
                ||vehicle.ExtraLicensePlates[0].font==null||vehicle.ExtraLicensePlates[0].fontSharedMaterial==null
                ||mounts.seats.Length!=7||tram.transform.Find("HeritageTram_Visual/Canopy roof")==null
                ||tram.transform.Find("HeritageTram_Visual/Automatic cab housing")==null
                ||tram.transform.Find("TramFloor")==null||tram.transform.Find("SteamExhaust")!=null)
                throw new Exception("Heritage tram lacks destination boards, six passenger positions, streetcar body, floor or driverless layout.");
            if(tram.GetComponentsInChildren<SpecificInteractable>(true).Count(t=>t.interactionTargetName=="RailPassengerSeat")!=6)
                throw new Exception("Heritage tram passenger interactions do not match the six seats/standing positions.");
            foreach(var name in new[]{"TramStopObject","TramCableDriveObject"})
                if(prefabs.Single(p=>p.name==name).GetComponentsInChildren<Collider>(true).Length==0)
                    throw new Exception(name+" has no placement or interaction collider.");
            var drive=prefabs.Single(p=>p.name=="TramCableDriveObject");
            var animation=drive.GetComponent<Animation>();
            var world=drive.GetComponent<WorldObject>();
            var index=Array.IndexOf(world.States,"CableRunning");
            if(animation==null||animation.clip==null||index<0||drive.transform.Find("CableDrumPivot/Cable drum")==null)
                throw new Exception("Tram cable drive lacks its rotating powered drum state.");
            world.OnStateChangedEvents[index].Invoke(true);
            if(!animation.enabled)throw new Exception("Tram cable drive does not animate when powered.");
            world.OnStateChangedEvents[index].Invoke(false);
            if(animation.enabled)throw new Exception("Tram cable drive does not stop its animation.");
            Debug.Log("ECO_TRAM_ASSETS_OK: destination signs, passenger cabin, compact stop, centered cable drive and powered animation.");
        }
    }
}
