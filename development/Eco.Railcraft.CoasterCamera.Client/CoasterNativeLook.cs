using Eco.CameraControl.Internal;
using UnityEngine;

namespace Eco.Railcraft.CoasterCamera.Client;

// Eco's setter seeds CameraLook; CameraRotation is updated later in its render
// pipeline. Read the accepted input accumulator directly, including when input
// is blocked, rather than taking last frame's rendered rotation as new input.
internal static class CoasterNativeLook
{
    internal static void Seed(PlayerCameraBehaviorFirstPerson behavior, Quaternion look)
    {
        var angles = look.eulerAngles;
        angles.x = Mathf.DeltaAngle(0, angles.x);
        angles.z = 0;
        behavior.SetCameraRotation(angles);
    }

    internal static Quaternion Read(PlayerCameraBehaviorFirstPerson behavior)
        => Quaternion.Euler(behavior.CameraLook);
}
