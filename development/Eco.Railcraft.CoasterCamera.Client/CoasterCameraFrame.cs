using System.Numerics;

namespace Eco.Railcraft.CoasterCamera.Client;

// Quaternion-only seat transport. World-up/Euler reconstruction would choose
// another heading as the forward vector crosses a vertical loop tangent.
internal sealed class CoasterCameraFrame
{
    private Quaternion localLook=Quaternion.Identity;
    private Quaternion smoothFrame=Quaternion.Identity;
    private Quaternion output=Quaternion.Identity;
    private int steppedFrame = -1;
    internal void Enter(Quaternion frame,Quaternion view)
    {
        frame=Quaternion.Normalize(frame);view=Quaternion.Normalize(view);
        localLook=Quaternion.Normalize(Quaternion.Inverse(frame)*view);
        smoothFrame=frame;output=view;steppedFrame=-1;
    }
    internal void EnterForward(Quaternion frame,int travelSign=1)
        =>Enter(frame,Quaternion.Normalize(frame*(travelSign<0?Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI):Quaternion.Identity)));
    internal Quaternion Desired(Quaternion frame)=>Quaternion.Normalize(frame*localLook);
    internal Quaternion LocalLook=>localLook;
    internal void SetLook(Quaternion look)=>localLook=Quaternion.Normalize(look);
    internal static Vector3 EyePosition(Vector3 centre,Quaternion frame,Vector3 localEye)
        =>centre+Vector3.Transform(localEye,Quaternion.Normalize(frame));
    // Native mount transforms locate the avatar root. Sitting places the
    // eyes above that root; keep them over this seat, slightly behind its hip.
    internal static Vector3 MountEye(Vector3 localMount)
        =>localMount+new Vector3(0,1.18f,-.08f);
    internal Quaternion StepOnce(Quaternion frame,float seconds,int frameNumber)
    {
        if(steppedFrame != frameNumber) { Step(frame,seconds); steppedFrame=frameNumber; }
        // Input arriving between camera callbacks is accepted immediately,
        // without advancing body smoothing multiple times per render frame.
        return Quaternion.Normalize(smoothFrame*localLook);
    }
    internal Quaternion Step(Quaternion frame,float seconds)
    {
        var dt=Math.Clamp(float.IsFinite(seconds)?seconds:0,0,.1f);
        smoothFrame=Quaternion.Normalize(Quaternion.Slerp(smoothFrame,Quaternion.Normalize(frame),1-MathF.Exp(-18*dt)));
        // Smooth the replicated body, not mouse input: a view change must take
        // effect immediately without orbiting or changing the eye position.
        output=Quaternion.Normalize(smoothFrame*localLook);
        return output;
    }
}
