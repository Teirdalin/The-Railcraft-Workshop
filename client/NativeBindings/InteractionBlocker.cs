using System.Collections.Generic;
using UnityEngine;
namespace Eco.Client
{
    // Required by native VehicleBase.AddRider/RemoveRider, not a mod runtime.
    public class InteractionBlocker : MonoBehaviour
    {
        [SerializeField] public List<Collider> ignoredColliders = new List<Collider>();
    }
}
