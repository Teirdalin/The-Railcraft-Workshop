using System;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    public static class RailSwitchAssetProbe
    {
        public static void Verify(GameObject[] prefabs)
        {
            foreach(var definition in RailExpansionAssetBuilder.ReadCatalog().Switches)
            {
                var prefab=prefabs.Single(p=>p.name==definition.Key+"Object");
                var copy=Object.Instantiate(prefab);copy.transform.position=new Vector3(200,20,200);copy.SetActive(true);
                try
                {
                    var world=copy.GetComponent<WorldObject>();
                    var stand=copy.transform.Find("Switch stand base").GetComponent<Renderer>().bounds;
                    var support=copy.transform.Find("Switch mounting sleeper").GetComponent<Renderer>().bounds;
                    if(Mathf.Abs(stand.min.y-support.max.y)>.001f || support.min.x>stand.min.x || support.max.x<stand.max.x
                        || support.min.z>stand.min.z || support.max.z<stand.max.z
                        || Mathf.Abs(support.min.y-(copy.transform.position.y-.495f))>.001f)
                        throw new Exception("Floating or unsupported switch stand: "+definition.Key);
                    if(!copy.GetComponentsInChildren<SpecificInteractable>(true).Any(x=>x.interactionTargetName=="RailSwitch"))throw new Exception("Missing switch lever target: "+definition.Key);
                    foreach(var route in definition.Paths)
                    {
                        foreach(var r in new[]{-1,0,1})world.OnStateChangedEvents[Array.IndexOf(world.States,"Route"+(r<0?"Left":r>0?"Right":"Forward"))].Invoke(r==route.Route);
                        var selected=copy.GetComponentsInChildren<Transform>(true).Where(x=>x.name.StartsWith("Selected")).ToArray();
                        if(selected.Count(x=>x.gameObject.activeSelf)!=1 || !copy.transform.Find("Selected"+(route.Route<0?"Left":route.Route>0?"Right":"Forward")).gameObject.activeSelf)throw new Exception("Route blade state mismatch: "+definition.Key);
                        Physics.SyncTransforms();
                        foreach(var point in route.Points.Where((p,i)=>i%8==0))
                            if(!Physics.RaycastAll(copy.transform.TransformPoint(point.Value)+Vector3.up,Vector3.down,2).Any(h=>h.collider.transform.IsChildOf(copy.transform)))throw new Exception("Gap in switch deck collision: "+definition.Key);
                    }
                }
                finally {Object.DestroyImmediate(copy);}
            }
            Debug.Log("ECO_SWITCH_ASSETS_OK: all exported turnout prefabs, route blades, lever targets and continuous deck raycasts.");
        }
    }
}
