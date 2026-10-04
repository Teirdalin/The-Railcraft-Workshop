using System;
using System.Linq;
using KinematicCharacterController;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    // KCC passengers need a PhysicsMover, not just a collider on the vehicle's
    // dynamic body. These invisible solids follow the body and match the
    // finished roofs/decks without adding a second visible roof.
    public static class RailExteriorPlatformAssetBuilder
    {
        static void Slab(Transform parent,string name,Vector3 center,Vector3 size)
        {
            var node=new GameObject(name);node.transform.SetParent(parent,false);
            node.transform.localPosition=center;
            var box=node.AddComponent<BoxCollider>();box.size=size;
        }

        static void Arch(Transform parent,string name,float baseY,float rise,float width,float length)
        {
            const int count=7;
            var segment=width/count;
            for(var i=0;i<count;i++)
            {
                var x=-width/2+(i+.5f)*segment;
                var height=baseY+rise*(1-4*x*x/(width*width));
                Slab(parent,name+" "+i,new Vector3(x,height-.010f,0),
                    new Vector3(segment+.008f,.020f,length));
            }
        }

        static Transform MovingSurface(GameObject root)
        {
            var old=root.transform.Find("ExteriorPlatforms");
            if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
            var platform=new GameObject("ExteriorPlatforms").transform;
            platform.SetParent(root.transform,false);
            var body=platform.gameObject.AddComponent<Rigidbody>();
            body.isKinematic=true;body.useGravity=false;
            var mover=platform.gameObject.AddComponent<PhysicsMover>();
            var follower=platform.gameObject.AddComponent<RigidbodyMover>();
            follower.FollowedRigidbody=root.GetComponent<Rigidbody>();
            mover.Rigidbody=body;mover.MoverController=follower;
            return platform;
        }

        public static void Apply(GameObject root)
        {
            if(root.GetComponent<Vehicle>()==null)return;
            var cab=root.transform.Find("CabFittings");
            if(cab!=null)
            {
                var oldRoof=cab.Find("Cab roof");
                if(oldRoof==null)throw new Exception("Cab roof missing: "+root.name);
                oldRoof.GetComponent<Collider>().enabled=false;
                var floor=cab.Find("Cab floor");
                var floorTop=floor.localPosition.y+floor.localScale.y/2;
                var oldCenter=oldRoof.localPosition;
                var oldWalkable=cab.Find("Walkable cab roof");
                if(oldWalkable!=null)UnityEngine.Object.DestroyImmediate(oldWalkable.gameObject);
                var parent=new GameObject("Walkable cab roof").transform;
                parent.SetParent(cab,false);
                parent.localPosition=new Vector3(0,0,oldCenter.z);
                Arch(parent,"Cab arch",oldCenter.y-.02f,.13f,oldRoof.localScale.x,oldRoof.localScale.z);
            }

            var tram=root.name=="HeritageTramObject";
            var coach=root.name=="PassengerCarObject"||root.name=="LargePassengerCarObject";
            var handcar=root.name=="RailroadHandcarObject";
            if(!tram&&!coach&&!handcar)
            {
                if(cab!=null)
                    root.GetComponent<Vehicle>().AllVehicleColliders=root.GetComponentsInChildren<Collider>()
                        .Where(c=>c.enabled).ToArray();
                return;
            }
            var platform=MovingSurface(root);
            if(tram)
            {
                var canopy=root.transform.Find("HeritageTram_Visual/Canopy roof");
                var deck=root.transform.Find("HeritageTram_Visual/Passenger deck");
                if(canopy==null||deck==null)throw new Exception("Tram walking geometry missing");
                Arch(platform,"Tram canopy",2.335f,.12f,canopy.localScale.x,canopy.localScale.z);
                Arch(platform,"Tram clerestory",2.59f,.065f,.85f,canopy.localScale.z*.62f);
                Slab(platform,"Tram passenger deck",new Vector3(0,deck.localPosition.y,0),deck.localScale);
                var oldFloor=root.transform.Find("TramFloor");
                if(oldFloor!=null)oldFloor.GetComponent<Collider>().enabled=false;
                foreach(var board in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Running board"))
                    Slab(platform,"Tram running board",board.localPosition,board.localScale);
            }
            else if(coach)
            {
                var roof=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Coach roof");
                var deck=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Deck");
                if(roof==null||deck==null)throw new Exception("Passenger car walking geometry missing: "+root.name);
                Arch(platform,"Passenger roof",roof.localPosition.y,.14f,roof.localScale.x,roof.localScale.z);
                Slab(platform,"Passenger deck",deck.localPosition,deck.localScale);
            }
            else
            {
                var deck=root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Deck");
                if(deck!=null)Slab(platform,"Handcar deck",new Vector3(0,.475f,0),
                    new Vector3(deck.localScale.x,.03f,deck.localScale.z));
            }
            root.GetComponent<Vehicle>().AllVehicleColliders=root.GetComponentsInChildren<Collider>()
                .Where(c=>c.enabled).ToArray();
        }

        public static void Verify(GameObject root)
        {
            if(root.GetComponent<Vehicle>()==null)return;
            var platform=root.transform.Find("ExteriorPlatforms");
            var expected=root.name=="HeritageTramObject"||root.name=="PassengerCarObject"
                ||root.name=="LargePassengerCarObject"||root.name=="RailroadHandcarObject";
            if(expected)
            {
                var mover=platform?.GetComponent<PhysicsMover>();
                var follower=platform?.GetComponent<RigidbodyMover>();
                if(mover==null||follower==null||follower.FollowedRigidbody!=root.GetComponent<Rigidbody>()
                    ||mover.MoverController!=follower||mover.Rigidbody!=platform.GetComponent<Rigidbody>()
                    ||!mover.Rigidbody.isKinematic||mover.Rigidbody.useGravity
                    ||!platform.GetComponentsInChildren<BoxCollider>().Any(c=>c.name.Contains("roof")||c.name.Contains("canopy")||c.name.Contains("deck")))
                    throw new Exception("Solid moving exterior platform missing: "+root.name);
            }
            var cab=root.transform.Find("CabFittings");
            if(cab!=null)
            {
                var roof=cab.Find("Walkable cab roof");
                if(roof==null||!roof.GetComponentsInChildren<BoxCollider>().Any())
                    throw new Exception("Locomotive roof has no walkable arch: "+root.name);
            }
        }
    }
}
