using UnityEngine;
using Eco.Animation;

// Serialized fields verified against the installed Eco.Client IL2CPP metadata.
// IK targets remain empty; Sitting uses the game's normal avatar animation.
public class MountSpot : SubscribableBehavior
{
    public RootMotion.FinalIK.InteractionTarget[] IKTargets = new RootMotion.FinalIK.InteractionTarget[0];
    public Transform exitPosition;
    public Transform alternativeExitPosition;
    public float thirdPersonCameraOffset;
    public bool setAsParent = true;
    public Transform cameraTarget;
    public AnimationStateManager.AvatarState overrideAvatarState = AnimationStateManager.AvatarState.Sitting;
    public CustomAnimsetOverride mountedAnimSetOverride;
    public CustomAnimsetOverride mountedAnimSetOverrideFemale;
    public Collider occupiedCollider;
    public bool lockRotation = true;
    public bool AlignWithPlayerSide;
    public InteractionFromVehicle InteractionFromInside = InteractionFromVehicle.None;
    [SerializeField] public bool ApplyHandsGripOnMount = false;
}
public enum InteractionFromVehicle { None, Allowed, AutomaticallyToggled }
