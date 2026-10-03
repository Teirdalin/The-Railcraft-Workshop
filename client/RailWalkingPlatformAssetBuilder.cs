using System;
using UnityEngine;
using KinematicCharacterController;
using UnityEditor.Events;
namespace EcoMinecarts.Editor
{
    public static class RailWalkingPlatformAssetBuilder
    {
        public static void Configure(GameObject vehicle,GameObject deck)
        {
            var body=deck.GetComponent<Rigidbody>();
            if(body==null) body=deck.AddComponent<Rigidbody>();
            body.isKinematic=true; body.useGravity=false;
            body.interpolation=RigidbodyInterpolation.None;
            var mover=deck.GetComponent<PhysicsMover>();
            if(mover==null) mover=deck.AddComponent<PhysicsMover>();
            var follower=deck.GetComponent<RigidbodyMover>();
            if(follower==null) follower=deck.AddComponent<RigidbodyMover>();
            follower.FollowedRigidbody=vehicle.GetComponent<Rigidbody>();
            mover.Rigidbody=body; mover.MoverController=follower;
            var floor=deck.transform.Find("Cab floor");
            var gates=new GameObject[2];
            for(var i=0;i<2;i++)
            {
                var gate=GameObject.CreatePrimitive(PrimitiveType.Cube);
                gate.name=i==0?"Left travel gate":"Right travel gate";
                gate.transform.SetParent(deck.transform,false);
                gate.transform.localPosition=new Vector3((i==0?-1:1)*floor.localScale.x/2,floor.localPosition.y+.485f,floor.localPosition.z);
                gate.transform.localScale=new Vector3(.045f,.90f,floor.localScale.z-.02f);
                gate.GetComponent<Renderer>().sharedMaterial=deck.transform.Find("Cab rear panel").GetComponent<Renderer>().sharedMaterial;
                gate.SetActive(false); gates[i]=gate;
            }
            var world=vehicle.GetComponent<WorldObject>();
            var states=world.States; var changed=world.OnStateChangedEvents;
            var on=world.OnStateEnabledEvents; var off=world.OnStateDisabledEvents;
            var index=Array.IndexOf(states,"CabTravelGatesClosed");
            if(index<0)
            {
                index=states.Length; Array.Resize(ref states,index+1); Array.Resize(ref changed,index+1);
                Array.Resize(ref on,index+1); Array.Resize(ref off,index+1);
                states[index]="CabTravelGatesClosed";
            }
            changed[index]=new ChangedStateEvent(); on[index]=new SetStateEvent(); off[index]=new SetStateEvent();
            foreach(var gate in gates) UnityEventTools.AddPersistentListener(changed[index],gate.SetActive);
            world.States=states; world.OnStateChangedEvents=changed;
            world.OnStateEnabledEvents=on; world.OnStateDisabledEvents=off;
        }
        public static void Verify(GameObject root)
        {
            var deck=root.transform.Find("CabFittings");
            var follower=deck?.GetComponent<RigidbodyMover>();
            var mover=deck?.GetComponent<PhysicsMover>();
            if(follower==null || mover==null || follower.GetType().Assembly.GetName().Name!="Eco.Client"
                || mover.GetType().Assembly.GetName().Name!="ThirdParty"
                || follower.FollowedRigidbody!=root.GetComponent<Rigidbody>()
                || mover.MoverController!=follower || mover.Rigidbody!=deck.GetComponent<Rigidbody>()
                || !mover.Rigidbody.isKinematic || mover.Rigidbody.useGravity)
                throw new Exception("Missing native walking-platform linkage: "+root.name);
            var floor=deck.Find("Cab floor").GetComponent<Collider>();
            if(floor.GetComponentInParent<Rigidbody>(true)!=mover.Rigidbody || floor.isTrigger)
                throw new Exception("Cab floor is not on its native PhysicsMover: "+root.name);
            var world=root.GetComponent<WorldObject>();
            var index=Array.IndexOf(world.States,"CabTravelGatesClosed");
            if(index<0 || world.OnStateChangedEvents[index].GetPersistentEventCount()!=2)
                throw new Exception("Cab travel gates missing native state bindings: "+root.name);
            Debug.Log("ECO_WALKING_PLATFORM_ASSETS_OK: "+root.name+" native RigidbodyMover follows locomotive, PhysicsMover owns solid deck; live walking remains unverified.");
        }
    }
}
