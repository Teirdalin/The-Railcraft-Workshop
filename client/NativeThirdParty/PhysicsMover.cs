using UnityEngine;
namespace KinematicCharacterController
{
    // Fields/type identity verified against installed ThirdParty.dll metadata.
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsMover : MonoBehaviour
    {
        public Rigidbody Rigidbody;
        public BaseMoverController MoverController;
    }
}
