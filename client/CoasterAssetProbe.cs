using System;
using System.Linq;
using UnityEngine;
namespace EcoMinecarts.Editor
{
    public static class CoasterAssetProbe
    {
        static void VerifyRailMeshes(GameObject section,CoasterAssetBuilder.Path path)
        {
            var geometry=section.transform.Find(path.Key);
            var rails=geometry==null?new MeshFilter[0]:geometry.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Continuous rail").ToArray();
            if(rails.Length!=3)throw new Exception("Whole-section rails must use the modular swept profile: "+path.Key);
            foreach(var filter in rails)
            {
                var mesh=filter.sharedMesh;var renderer=filter.GetComponent<MeshRenderer>();
                if(mesh==null||mesh.vertexCount!=path.Points.Length*8+8||mesh.subMeshCount!=1
                    ||mesh.GetIndexCount(0)!=(path.Points.Length-1)*24+12)
                    throw new Exception("Exported continuous rail geometry is missing or incomplete: "+path.Key);
                if(renderer==null||!renderer.enabled||!filter.gameObject.activeSelf||renderer.sharedMaterial==null)
                    throw new Exception("Exported continuous rail cannot render: "+path.Key);
            }
        }
        public static void Verify(GameObject[] prefabs)
        {
            foreach(var path in RailExpansionAssetBuilder.ReadCatalog().Coasters.Where(p=>p.Key!="CoasterStraight"))
            {
                var section=prefabs.Single(p=>p.name==path.Key+"Object");
                if(section.GetComponentsInChildren<Collider>().Length<8)throw new Exception("Incomplete whole-section collision: "+path.Key);
                if(!MinecartIconBuilder.IsBuilding(path.Key))throw new Exception("Coaster section icon must use construction backdrop");
                var swept=section.transform.Find(path.Key);
                VerifyRailMeshes(section,path);
                var expected=-path.Points[0].Z-.5f;
                if(Mathf.Abs(swept.localPosition.z-expected)>.001f||Mathf.Abs(path.Points[0].Z+swept.localPosition.z+.5f)>.001f)
                    throw new Exception("Whole-section entry pivot must be at its rail-end build cell: "+path.Key);
            }
            if(prefabs.Any(p=>p.name=="CoasterStraightObject"))throw new Exception("Duplicate whole straight rail exported");
            var cart=prefabs.Single(p=>p.name=="RollerCoasterCartObject");
            if(cart.transform.Find("RollerCoasterCart_Visual/Coach roof")!=null||cart.GetComponent<Mountable>().seats.Length!=3
                ||cart.GetComponentsInChildren<Transform>().Count(t=>t.name=="Upstop roller")!=4
                ||cart.GetComponent<RCCCarControllerV2>().engineTorque!=0)throw new Exception("Coaster cart captive wheels/seats/passive motion incorrect");
            if(RailRiderFit.Verify(cart)!=2)
                throw new Exception("Coaster requires two cushion-aligned passenger seats.");
            if(cart.GetComponentsInChildren<SpecificInteractable>(true).Count(t=>t.interactionTargetName=="CoasterShove")!=2
                ||cart.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="Shove bar")!=2)
                throw new Exception("Coaster needs two visible, side-accessible shove targets.");
            var station=prefabs.Single(p=>p.name=="CoasterStationObject");
            if(station.GetComponentsInChildren<Collider>().Any(c=>c.GetComponent<SpecificInteractable>()?.interactionTargetName!="CoasterLoadingStation"))
                throw new Exception("Every station surface must expose cart loading to the held item.");
            VerifyRailMeshes(station,new CoasterAssetBuilder.Path{Key="CoasterStation",Points=new CoasterAssetBuilder.Point[65]});
            if(station.transform.Find("Loading platform")==null||station.transform.Find("Station control cabinet")==null)throw new Exception("Coaster station loading/control models missing");
            var junctions=station.GetComponentsInChildren<Transform>().Where(t=>t.name=="Station rail junction box").ToArray();
            if(junctions.Length!=2||junctions.Any(t=>Mathf.Abs(t.localPosition.x+1)>.0001f)
                ||Mathf.Abs(junctions[0].localPosition.z+junctions[1].localPosition.z)>.0001f)
                throw new Exception("Coaster station needs matching rail junction boxes on both ends");
            var deck=station.transform.Find("Loading platform");var cabinet=station.transform.Find("Station control cabinet");
            if(Mathf.Abs(deck.localPosition.y-deck.localScale.y*.5f+.5f)>.0001f
                ||Mathf.Abs(deck.localPosition.y+deck.localScale.y*.5f+.25f)>.0001f)
                throw new Exception("Station platform must meet the floor without changing its boarding height");
            // Running-rail collider spans deliberately overlap their seams by
            // 5 mm. The visible deck and protected voxel footprint are 1 m.
            if(Mathf.Abs(deck.localScale.z-1)>.0001f||station.GetComponentsInChildren<BoxCollider>().Any(c=>Mathf.Abs(c.transform.localPosition.z)+c.size.z*Mathf.Abs(c.transform.localScale.z)/2>.506f))
                throw new Exception("Coaster station must occupy one block of track length");
            if(Mathf.Abs(deck.localPosition.x)>.0001f||station.GetComponentsInChildren<BoxCollider>().Where(c=>c.name=="RailCollision").Any(c=>Mathf.Abs(c.transform.localPosition.x+1)>.0001f))throw new Exception("Station placement anchor must be on platform; rail collision must be offset by one cell");
            if(Mathf.Abs(cabinet.localPosition.y-cabinet.localScale.y*.5f-(deck.localPosition.y+deck.localScale.y*.5f))>.0001f
                ||station.transform.Find("Station sign post")==null
                ||cart.GetComponentsInChildren<Transform>().Count(t=>t.name=="Restraint pivot upright")!=2)throw new Exception("Floating station fittings or unsupported cart restraints");
            Debug.Log("ECO_COASTER_ASSETS_OK: complete specialty sections use the shared swept profile and entry pivot; station surfaces expose cart loading.");
        }
    }
}
