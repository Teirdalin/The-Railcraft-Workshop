using UnityEngine;
using RootMotion.FinalIK;

// Exact native cart hook and hand binding fields from SmallWoodCartObject.
public class MountSpotPulled : MountSpot
{
    public InteractionTarget lHand, rHand;
    public float leftLimit = 80, rightLimit = 95, transitionAngle = 10;
    public float firstPersonControlsRevertAngle = 110, handsWeightChangeSpeed = 5;
    public Transform CenterOfMassWithoutOccupancy, CenterOfMassOnOccupancy;
    public Collider[] CollidersToEnableOnOccupancy = new Collider[0];
    public GameObject WheelCollider;
    [SerializeField] private GameObject enviromentPlayerBlocker;
}
