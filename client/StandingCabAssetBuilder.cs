using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EcoMinecarts.Editor
{
    // Serialized native components only. Seat zero is never offered by the
    // server; passenger spots cannot activate Eco's client-side driving solver.
    public static class StandingCabAssetBuilder
    {
        public static void Build(GameObject root, Transform model, float floor, float z, float width, float depth,
            Material wood, Material iron, Material paint)
        {
            // The compact Mine Train needs a narrow footplate; larger engines
            // retain the generous walkable cab shared with their wider bodies.
            width = Mathf.Max(root.name=="MineTrainObject" ? 1.05f : 1.65f,width);
            depth = Mathf.Max(1.25f,depth);
            foreach(var name in new[]{"CabFloor","CabRear","CabSide","RoofPost","CabRoof","DriverBench","Backrest",
                "ControlConsole","ThrottleLever","ThrottleGrip","PressureGauge","Cab side","Cab window post","Cab roof",
                "Bench","Cab floor","Control desk","Pressure gauge","Regulator lever"})
                foreach(var node in model.Cast<Transform>().Where(t=>t.name==name).ToArray()) Object.DestroyImmediate(node.gameObject);
            var old=root.transform.Find("CabInteraction"); if(old!=null)Object.DestroyImmediate(old.gameObject);
            var fittings=new GameObject("CabFittings").transform; fittings.SetParent(root.transform,false);
            var front=z+depth/2;
            var rear=z-depth/2;
            Box(fittings,"Cab floor",new Vector3(0,floor-.035f,z),new Vector3(width,.07f,depth),wood);
            Box(fittings,"Cab rear panel",new Vector3(0,floor+.35f,rear),new Vector3(width-.065f,.7f,.045f),paint);
            // Side doors remain clear for walking in. Corner posts support a roof
            // with 2.08 m of genuine standing clearance above the collision deck.
            foreach(var x in new[]{-width/2,width/2})
            {
                foreach(var end in new[]{rear,front})
                    Box(fittings,"Cab window post",new Vector3(x,floor+1.04f,end),new Vector3(.045f,2.08f,.045f),iron);
                Box(fittings,"Boarding step",new Vector3(x+Mathf.Sign(x)*.10f,floor-.20f,z),new Vector3(.25f,.06f,Mathf.Min(.45f,depth)),iron);
            }
            Box(fittings,"Cab roof",new Vector3(0,floor+2.12f,z),new Vector3(width+.16f,.08f,depth+.12f),paint);
            var console=Box(fittings,"Cab console",new Vector3(0,floor+1.18f,front-.025f),new Vector3(width-.18f,.16f,.10f),paint,"MineTrainCab");
            foreach(var side in new[]{-1,1})
                Box(fittings,"Console support",new Vector3(side*(width/2-.10f),floor+.55f,front-.025f),new Vector3(.045f,1.10f,.08f),iron);
            // Four separated, visible handles. Their small solid hit targets do
            // not fill the walkable cab or hide the console/chair/handrail.
            Lever(fittings,"Throttle",-width*.32f,floor,front,wood,iron,"RailCabThrottle",.12f);
            Lever(fittings,"Brake",-width*.11f,floor,front,paint,iron,"RailCabBrake",.13f);
            Lever(fittings,"Reverser",width*.11f,floor,front,iron,iron,"RailCabReverse",.10f);
            Lever(fittings,"Route selector",width*.32f,floor,front,wood,iron,"RailCabSwitch",.13f);
            // One operator position. Reuse the existing chair's .64 m seated
            // hip offset, moving the cushion and mount together. Live animation
            // alignment remains a connected-client acceptance check.
            var chairX=0f;
            var chairZ=front-.97f;
            Box(fittings,"Standing handrail",new Vector3(-.35f,floor+.96f,front+.02f),new Vector3(.24f,.04f,.055f),iron,"RailCabStand");
            // Suspend the boarding/control handrail from the console underside;
            // the previous short bracket ended in mid-air below the panel.
            foreach(var x in new[]{-.47f,-.23f})
                Box(fittings,"Handrail bracket",new Vector3(x,floor+1.025f,front+.015f),new Vector3(.035f,.17f,.04f),iron);
            Box(fittings,"Operator cushion",new Vector3(chairX,floor+.70f,chairZ),new Vector3(.44f,.08f,.30f),wood,"RailCabSeat");
            Box(fittings,"Operator backrest",new Vector3(chairX,floor+.94f,chairZ-.155f),new Vector3(.44f,.40f,.045f),wood,"RailCabSeat");
            foreach(var x in new[]{chairX-.15f,chairX+.15f})
                foreach(var dz in new[]{-.10f,.11f})
                    Box(fittings,"Chair leg",new Vector3(x,floor+.33f,chairZ+dz),new Vector3(.045f,.66f,.045f),iron);
            var chair=Spot(fittings,"CabOperatorSpot",new Vector3(chairX,floor+.10f,chairZ),3,width,z,floor);
            var mounts=root.GetComponent<Mountable>();
            mounts.seats=new[]{mounts.seats[0],chair};
            // Reserved origin stays valid for native serialization, never mountable.
            mounts.seats[0].transform.localPosition=new Vector3(0,floor,z);
            RailWalkingPlatformAssetBuilder.Configure(root,fittings.gameObject);
            root.GetComponent<MoveThroughSounds>().OverlapCheck=console;
            var vehicle=root.GetComponent<Vehicle>();
            vehicle.size=new Vector3(Mathf.Max(vehicle.size.x,width+.45f),floor+2.2f,Mathf.Max(vehicle.size.z,2*Mathf.Abs(rear)+.05f));
            vehicle.AllVehicleColliders=root.GetComponentsInChildren<Collider>();
        }
        static MountSpot Spot(Transform parent,string name,Vector3 pos,int state,float width,float z,float floor)
        {
            var node=new GameObject(name);node.transform.SetParent(parent,false);node.transform.localPosition=pos;
            var seat=node.AddComponent<MountSpot>(); seat.setAsParent=true;seat.lockRotation=false;
            seat.overrideAvatarState=(Eco.Animation.AnimationStateManager.AvatarState)state;
            seat.InteractionFromInside=InteractionFromVehicle.Allowed;
            // Releasing the chair steps into the cab aisle, not onto the track.
            // Explicit Leave Train uses a separate server-relative outside exit.
            var deck=parent.Find("Cab floor");
            var aisle=new Vector3(0,floor+.02f,z+deck.localScale.z/2-.52f);
            seat.exitPosition=Anchor(parent,name+"Exit",aisle);
            seat.alternativeExitPosition=Anchor(parent,name+"OtherExit",aisle);
            return seat;
        }
        static Transform Anchor(Transform parent,string name,Vector3 pos)
        {var n=new GameObject(name).transform;n.SetParent(parent,false);n.localPosition=pos;return n;}
        static void Lever(Transform p,string name,float x,float floor,float front,Material grip,Material iron,string target,float span)
        {
            Box(p,name+" shaft",new Vector3(x,floor+1.37f,front-.065f),new Vector3(.032f,.23f,.032f),iron,target);
            Box(p,name+" grip",new Vector3(x,floor+1.495f,front-.065f),new Vector3(span,.06f,.065f),grip,target);
        }
        static BoxCollider Box(Transform parent,string name,Vector3 pos,Vector3 size,Material material,string target=null)
        {
            var n=GameObject.CreatePrimitive(PrimitiveType.Cube);n.name=name;n.transform.SetParent(parent,false);
            n.transform.localPosition=pos;n.transform.localScale=size;n.GetComponent<Renderer>().sharedMaterial=material;
            if(target!=null)n.AddComponent<SpecificInteractable>().interactionTargetName=target;
            return n.GetComponent<BoxCollider>();
        }
        public static void Verify(GameObject prefab,bool originalDesign=false)
        {
            bool prepared=!originalDesign&&prefab.transform.Find("PreparedVehicleArt")!=null;
            RailRiderInteractionAssetBuilder.Verify(prefab, explicitExit:true);
            RailWalkingPlatformAssetBuilder.Verify(prefab);
            var mount=prefab.GetComponent<Mountable>();
            if(mount==null || mount.seats.Length!=2 || (int)mount.seats[1].overrideAvatarState!=3)
                throw new Exception("Cab needs reserved driver and ONE seated operator: "+prefab.name);
            foreach(var seat in mount.seats.Skip(1))
                if(!seat.setAsParent || seat.lockRotation || seat.InteractionFromInside!=InteractionFromVehicle.Allowed || seat.exitPosition==null)
                    throw new Exception("Cab attachment/inside interaction missing: "+prefab.name);
            var fittings=prefab.transform.Find("CabFittings");
            if(fittings==null || fittings.Find("Cab floor").GetComponent<BoxCollider>()==null || prefab.transform.Find("CabInteraction")!=null)
                throw new Exception("Cab has no walkable deck or retains blocking box: "+prefab.name);
            var deck=fittings.Find("Cab floor");
            var panel=fittings.Find("Cab console");
            var rail=fittings.Find("Standing handrail");
            var visual=originalDesign?prefab.transform.Find("LegacyDesignGeometry/CabFittings"):fittings;
            var brackets=visual.Cast<Transform>().Where(t=>t.name.StartsWith("Handrail bracket")).ToArray();
            if(brackets.Length!=2 || brackets.Any(t=>
                t.localPosition.y+t.localScale.y/2 < panel.localPosition.y-panel.localScale.y/2 ||
                t.localPosition.y-t.localScale.y/2 > rail.localPosition.y+rail.localScale.y/2))
                throw new Exception("Cab handrail bracket must connect the handle to the console: "+prefab.name);
            var minimumWidth=prefab.name=="MineTrainObject" ? 1.05f : 1.65f;
            if(deck.localScale.z<1.25f-.001f || deck.localScale.x<minimumWidth-.001f)
                throw new Exception("Cab depth/width regression: "+prefab.name);
            foreach(var key in new[]{"MineTrainCab","RailCabStand","RailCabSeat","RailCabThrottle","RailCabBrake","RailCabReverse","RailCabSwitch"})
                if(!fittings.GetComponentsInChildren<SpecificInteractable>().Any(t=>t.interactionTargetName==key))
                    throw new Exception("Missing standing cab control: "+key);
            // Check clearance at the intended standing body's center, not just
            // that the old blocker was renamed or replaced by another solid box.
            var cushion=visual.Find("Operator cushion");
            if(Mathf.Abs(cushion.GetComponentInChildren<Renderer>(true).bounds.max.y-mount.seats[1].transform.position.y-RailRiderFit.SeatedHipHeight+(prepared?0:RailRiderFit.RiderLift))>.002f)
                throw new Exception("Operator cushion does not match seated hip datum: "+prefab.name);
            var bodyCenter=mount.seats[1].transform.position+Vector3.up*1.05f;
            // Placement volumes are removed by Eco; disabled/trigger colliders are not solid cab obstacles.
            foreach(var c in prefab.GetComponentsInChildren<BoxCollider>().Where(c=>c.enabled&&!c.isTrigger&&c.GetComponent<ColliderPlacementOptions>()?.RemoveColliderAfterPlacement!=true))
            {
                var local=c.transform.InverseTransformPoint(bodyCenter)-c.center;
                if(Mathf.Abs(local.x)<c.size.x/2 && Mathf.Abs(local.y)<c.size.y/2 && Mathf.Abs(local.z)<c.size.z/2)
                    throw new Exception("Standing cab body overlaps solid collider: "+prefab.name+"/"+c.name);
            }
            var copy=Object.Instantiate(prefab);
            try
            {
                copy.SetActive(true);if(copy.GetComponent<Animator>()!=null)RailVehicleDesignProbe.Select(copy,!originalDesign);copy.GetComponent<Rigidbody>().isKinematic=true;
                // Match Eco placement before testing the occupied cab and entry paths.
                foreach(var placement in copy.GetComponentsInChildren<ColliderPlacementOptions>(true))
                    if(placement.RemoveColliderAfterPlacement)foreach(var collider in placement.GetComponents<Collider>())Object.DestroyImmediate(collider);
                Physics.SyncTransforms();
                var activeDeck=copy.transform.Find("CabFittings");
                if(activeDeck.Find("Cab floor").GetComponent<Collider>().attachedRigidbody!=activeDeck.GetComponent<Rigidbody>())
                    throw new Exception("Live cab collider does not belong to walking platform: "+prefab.name);
                var world=copy.GetComponent<WorldObject>();
                var gateIndex=Array.IndexOf(world.States,"CabTravelGatesClosed");
                for(int i=0;i<world.OnStateChangedEvents[gateIndex].GetPersistentEventCount();i++)world.OnStateChangedEvents[gateIndex].SetPersistentListenerState(i,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
                world.OnStateChangedEvents[gateIndex].Invoke(true); Physics.SyncTransforms();
                foreach(var side in new[]{"Left travel gate","Right travel gate"})
                    if(!activeDeck.Find(side).gameObject.activeSelf || activeDeck.Find(side).GetComponent<Collider>().attachedRigidbody!=activeDeck.GetComponent<Rigidbody>())
                        throw new Exception("Moving cab gate not active/on platform: "+prefab.name);
                world.OnStateChangedEvents[gateIndex].Invoke(false); Physics.SyncTransforms();
                var colliders=copy.GetComponentsInChildren<Collider>();
                var standing=copy.GetComponent<Mountable>().seats[1].transform.position;
                // Eco's installed Player capsule is 1.85m tall, radius .25m.
                // Inflate horizontally to .28m for practical entry clearance.
                // Upper torso/head clearance above the supporting chair back.
                // The cushion and backrest intentionally contact the seated body.
                var solids=Physics.OverlapCapsule(standing+Vector3.up*1.30f,standing+Vector3.up*1.57f,.25f)
                    .Where(c=>c.transform.IsChildOf(copy.transform) && !c.isTrigger).ToArray();
                if(solids.Length!=0) throw new Exception("Standing player capsule intersects "+prefab.name+"/"+solids[0].name);
                foreach(var side in new[]{-1,1})
                {
                    var aisle=standing+Vector3.forward*.45f;
                    var entry=aisle+Vector3.right*side*2;
                    foreach(var doorHit in Physics.CapsuleCastAll(entry+Vector3.up*.28f,entry+Vector3.up*1.57f,.28f,Vector3.left*side,2))
                        if(doorHit.collider.transform.IsChildOf(copy.transform) && !doorHit.collider.isTrigger)
                            throw new Exception("Cab entry obstructed on side "+side+": "+prefab.name+"/"+doorHit.collider.name);
                }
                foreach(var seat in copy.GetComponent<Mountable>().seats.Skip(1))
                foreach(var name in new[]{"Throttle grip","Brake grip","Reverser grip","Route selector grip","Cab console","Standing handrail"})
                {
                    var target=activeDeck.GetComponentsInChildren<SpecificInteractable>().Single(t=>t.name==name);
                    var from=seat.transform.position+Vector3.up*1.65f;
                    var toward=target.transform.position-from;
                    var downPitch=Mathf.Atan2(-toward.y,new Vector2(toward.x,toward.z).magnitude)*Mathf.Rad2Deg;
                    if(downPitch>45f) throw new Exception("Control needs excessive downward camera pitch: "+prefab.name+"/"+name);
                    var ray=new Ray(from,toward.normalized);var closest=float.PositiveInfinity;Collider hit=null;
                    foreach(var c in colliders)
                        if(c.Raycast(ray,out var contact,toward.magnitude+.2f) && contact.distance<closest){closest=contact.distance;hit=c;}
                    if(hit==null || hit.GetComponent<SpecificInteractable>()?.interactionTargetName!=target.interactionTargetName)
                        throw new Exception("Cab control obscured from "+seat.name+": "+prefab.name+"/"+name+" hit "+hit?.name);
                }
            }
            finally { Object.DestroyImmediate(copy); }
            Debug.Log("ECO_STANDING_CAB_OK: "+prefab.name+" one usable operator chair, cushion aligned to sitting pose, clear door aisle, reachable controls within 45 degrees, reserved native driver.");
        }
    }
}
