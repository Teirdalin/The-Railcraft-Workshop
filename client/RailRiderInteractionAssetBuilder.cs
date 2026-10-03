using System;
using System.Linq;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    // Native VehicleBase.AddRider calls InteractionBlocker.Block even for
    // passengers. Exempt explicit targets from that collider-layer filter,
    // without removing structural colliders or granting driver authority.
    public static class RailRiderInteractionAssetBuilder
    {
        public static void Configure(GameObject root, bool explicitExit = false)
        {
            var blocker = root.GetComponent<Eco.Client.InteractionBlocker>();
            if(blocker == null) throw new Exception("Rail vehicle lacks native interaction blocker: " + root.name);
            blocker.ignoredColliders = root.GetComponentsInChildren<Collider>(true)
                .Where(c => c.GetComponent<SpecificInteractable>() != null).Distinct().ToList();
            // Native Mountable.LateUpdate consumes E globally when true.
            // Cabs instead offer targeted handrail/chair exits and a UI exit.
            root.GetComponent<Mountable>().handleDismount = !explicitExit;
        }
        public static void Verify(GameObject root, bool explicitExit = false)
        {
            var blocker = root.GetComponent<Eco.Client.InteractionBlocker>();
            if(blocker == null || blocker.ignoredColliders == null)
                throw new Exception("Rail rider interaction policy missing: " + root.name);
            foreach(var collider in root.GetComponentsInChildren<Collider>(true))
                if(blocker.ignoredColliders.Contains(collider) != (collider.GetComponent<SpecificInteractable>() != null))
                    throw new Exception("Mounted target exemption mismatch: " + root.name + "/" + collider.name);
            if(root.GetComponent<Mountable>().handleDismount == explicitExit)
                throw new Exception("Cab E/dismount policy mismatch: " + root.name);
        }
    }
}
