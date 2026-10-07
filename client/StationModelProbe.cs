using System;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    public static class StationModelProbe
    {
        public static void Verify(GameObject[] prefabs)
        {
            var station=Object.Instantiate(prefabs.Single(p=>p.name=="TrainStationObject"));
            try
            {
                station.SetActive(true); station.transform.position=new Vector3(1000,1000,1000);
                Physics.SyncTransforms();
                var colliders=station.GetComponentsInChildren<Collider>().Where(c=>!c.isTrigger&&c.enabled).ToArray();
                bool Hit(Vector3 point,int side)
                {
                    var ray=new Ray(station.transform.TransformPoint(point+Vector3.forward*side),station.transform.TransformDirection(Vector3.back*side));
                    return colliders.Any(c=>c.Raycast(ray,out _,2));
                }
                foreach(var side in new[]{-1,1})
                foreach(var point in new[]{new Vector3(-.32f,1.50f,0),new Vector3(.32f,1.50f,0),new Vector3(0,1.62f,0),new Vector3(.41f,.06f,0),new Vector3(.20f,.50f,0)})
                    if(!Hit(point,side)) throw new Exception("Station visible surface lacks collision/interaction: "+point+" side "+side);
                if(Hit(new Vector3(.34f,.90f,0),1)) throw new Exception("Station collision fills empty space beside the post");
                Debug.Log("ECO_STATION_MODEL_OK: sign corners, post top, platform edges and cabinet targetable from both sides; empty side space clear.");
            }
            finally { Object.DestroyImmediate(station); }
        }
    }
}
