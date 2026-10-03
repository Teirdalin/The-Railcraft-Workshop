using System;
using System.Linq;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    // Editor-only configuration of native WheelColliders. No custom player plugin.
    public static class RailWheelSuspension
    {
        public static void Configure(GameObject root,float emptyKg,float ratedKg)
        {
            var wheels=root.GetComponentsInChildren<WheelCollider>(true);
            const float travel=.06f;
            var spring=ratedKg*9.81f/(wheels.Length*travel*.25f);
            var damper=1.6f*Mathf.Sqrt(spring*ratedKg/wheels.Length);
            foreach(var wheel in wheels)
            {
                wheel.suspensionDistance=travel;
                wheel.suspensionSpring=new JointSpring{spring=spring,damper=damper,targetPosition=.5f};
                // At empty load the visual rail-contact plane stays at y=0.
                // Extra load compresses within the existing 3cm guide tolerance.
                wheel.center=Vector3.up*(travel*.5f-emptyKg*9.81f/(wheels.Length*spring));
                wheel.sprungMass=emptyKg/wheels.Length;
            }
        }
        public static void Verify(GameObject[] prefabs)
        {
            var specs=RailExpansionAssetBuilder.ReadCatalog().Vehicles.Where(s=>s.Powered)
                .Select(s=>(Name:s.Key+"Object",Empty:s.EmptyKg,Rated:s.EmptyKg+s.CargoKg+500))
                .Concat(new[]{(Name:"MineTrainObject",Empty:600f,Rated:1350f)});
            foreach(var spec in specs)
            {
                var prefab=prefabs.Single(p=>p.name==spec.Name);
                var wheels=prefab.GetComponentsInChildren<WheelCollider>(true);
                foreach(var wheel in wheels)
                {
                    var s=wheel.suspensionSpring;
                    var compression=spec.Rated*9.81f/(wheels.Length*s.spring);
                    if(compression>=wheel.suspensionDistance*(1-s.targetPosition))throw new Exception("Overloaded native suspension: "+spec.Name);
                    var emptyExtension=wheel.suspensionDistance*(1-s.targetPosition)-spec.Empty*9.81f/(wheels.Length*s.spring);
                    if(Mathf.Abs(wheel.center.y-emptyExtension)>.0001f)throw new Exception("Native wheel equilibrium disagrees with rail plane: "+spec.Name);
                    if((spec.Rated-spec.Empty)*9.81f/(wheels.Length*s.spring)>.03f)throw new Exception("Payload suspension exceeds guide tolerance: "+spec.Name);
                }
            }
            Debug.Log("ECO_POWERED_SUSPENSION_OK: all four engine masses, rated payload support and empty/load rail-height equilibrium.");
        }
        public static GameObject CreateSettlingRig(Transform origin,float mass,Vector3 position)
        {
            var source=origin.GetComponentsInChildren<WheelCollider>(true);
            var rig=new GameObject("Suspension test rig");
                rig.transform.position=position;
                var body=rig.AddComponent<Rigidbody>();body.constraints=RigidbodyConstraints.FreezeRotation|RigidbodyConstraints.FreezePositionX|RigidbodyConstraints.FreezePositionZ;
                body.sleepThreshold=0;body.mass=mass;
                foreach(var original in source)
                {
                    var node=new GameObject("Test wheel");node.transform.SetParent(rig.transform,false);
                    node.transform.localPosition=origin.InverseTransformPoint(original.transform.position);
                    var wheel=node.AddComponent<WheelCollider>();wheel.radius=original.radius;wheel.center=original.center;
                    wheel.suspensionDistance=original.suspensionDistance;wheel.suspensionSpring=original.suspensionSpring;
                    wheel.mass=original.mass;wheel.forceAppPointDistance=original.forceAppPointDistance;
                    wheel.sprungMass=mass/source.Length;
                }
            return rig;
        }
    }
}
