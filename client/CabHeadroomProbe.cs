using System;
using System.Linq;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    public static class CabHeadroomProbe
    {
        public static void Verify(GameObject[] prefabs)
        {
            var count=0;
            foreach(var prefab in prefabs)
            {
                var roof=prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="CabRoof"||t.name=="Cab roof"||t.name=="Coach roof");
                if(roof==null)continue;
                count++;
                var underside=roof.GetComponent<Renderer>().bounds.min.y;
                var mount=prefab.GetComponent<Mountable>();
                var seats=mount.seats.Skip(1);
                foreach(var seat in seats)
                    if(underside-seat.transform.position.y<(prefab.transform.Find("CabFittings")!=null?1.95f:2f))throw new Exception("Insufficient native seat-origin/roof clearance: "+prefab.name);
                var posts=prefab.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="RoofPost"||t.name=="Cab window post"||t.name=="Window pillar");
                foreach(var post in posts)
                    if(Mathf.Abs(post.GetComponent<Renderer>().bounds.max.y-underside)>.002f)throw new Exception("Roof support does not meet roof: "+prefab.name);
                if(roof.GetComponent<Renderer>().bounds.max.y-prefab.transform.position.y>prefab.GetComponent<Vehicle>().size.y)
                    throw new Exception("Roof exceeds registered vehicle height: "+prefab.name);
            }
            if(count<5)throw new Exception("Missing enclosed train models in headroom audit");
            Debug.Log("ECO_CAB_HEADROOM_OK: "+count+" enclosed vehicles, supported roofs, 1.95m operator/2m coach seated-origin clearance and updated vehicle heights.");
        }
    }
}
