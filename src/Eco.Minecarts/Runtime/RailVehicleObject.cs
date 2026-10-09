using System.Numerics;
using Eco.Gameplay.Objects;

namespace Eco.Minecarts.Runtime;

// Shared network ownership and rail-pose contract for carts and locomotives.
// Existing concrete saved object names remain unchanged.
[RequireComponent(typeof(RailVehicleAccessComponent))]
[RequireComponent(typeof(RailVehicleTextComponent))]
public abstract class RailVehicleObject : PhysicsWorldObject
{
    private readonly object animationGate=new();
    private Guid dumpOwner;
    private float previousVisualSpeed;
    private long previousVisualTime;
    internal void SetRestraints(bool closed)
    {
        if(Capabilities.HasFlag(RailVehicleCapabilities.Coaster))
            SetAnimatedState("RailRestraintPose",closed?"Secured":"Boarding");
    }
    internal void PublishCabControls(double throttle,bool brake,int direction)
    {
        if(!Capabilities.HasFlag(RailVehicleCapabilities.FueledMotor))return;
        SetAnimatedState("RailThrottlePose","Throttle"+Math.Clamp((int)Math.Round(throttle*10),0,10));
        SetAnimatedState("RailBrakePose",brake?"BrakeOn":"BrakeOff");
        SetAnimatedState("RailReverserPose",direction<0?"Reverse":"Forward");
    }
    internal bool TryBeginDump(Guid owner)
    {lock(animationGate){if(dumpOwner!=Guid.Empty)return false;dumpOwner=owner;return true;}}
    internal void PublishDump(Guid owner,float degrees,int side,bool returning)
    {
        lock(animationGate)
        {
            if(dumpOwner!=owner)return;
            SetAnimatedState("RailDumpPose",$"Dump{(side<0?"Left":"Right")}{Math.Clamp((int)MathF.Round(degrees/2.5f),0,28):D2}");
            SetAnimatedState("RailDumpState",degrees<=0?"Idle":returning?"ReturningFromDump":"Dumping");
        }
    }
    internal void EndDump(Guid owner)
    {lock(animationGate){if(dumpOwner!=owner)return;PublishDump(owner,0,1,false);dumpOwner=Guid.Empty;}}
    public virtual Eco.Minecarts.Physics.RailVehicleSpec RailSpec => Eco.Minecarts.Physics.RailVehicleSpec.Minecart;
    public virtual float CouplerOffset => .806f;
    public virtual double RailMassKg => this.RailSpec.EmptyKg;
    public virtual int DriverPriority => 0;
    public virtual Vector3 ContactHalfSize => new(.40f, .40f, .80f);
    public virtual RailVehicleCapabilities Capabilities => RailVehicleCapabilities.ManualHandle;
    public virtual RailFuelConfiguration? FuelConfiguration => null;
    public virtual RailVehiclePoints ConnectionPoints => RailVehiclePoints.Standard(CouplerOffset);
    internal bool ServerOnlyPhysics => !Capabilities.HasFlag(RailVehicleCapabilities.NativeGroundPhysics);
    protected override void CreateEntity() => this.netEntity = new RailVehicleNetEntity(GetType().Name,this);
    internal void SetRailGuidance(bool active)
    { SetAnimatedState("RailGuidedPhysics",active||ServerOnlyPhysics); ((RailVehicleNetEntity)netEntity).SetGuided(active); }
    internal void PublishRailPose(Vector3 velocity)
    { PublishWheelMotion(velocity); ((RailVehicleNetEntity)netEntity).PublishGuidedPose(velocity); }
    // Visual motion has no WheelCollider dependency and never applies forces.
    // Use each car's own orientation, rather than its leader's authored rail sign.
    internal void PublishWheelMotion(Vector3 velocity)
    {
        var speed=Vector3.Dot(velocity,Rotation.RotateVector(Vector3.UnitZ));
        if(!float.IsFinite(speed))speed=0;
        if(Math.Abs(speed)>=.05f)SetRestraints(true);
        // Enable before selecting playback direction, including the first
        // reverse start. Stopping disables evaluation without losing phase.
        SetAnimatedState("RailWheelsMoving",Math.Abs(speed)>=.001f);
        if(Math.Abs(speed)>.001f)
            SetAnimatedState("RailWheelDirection",speed<0?"RollReverse":"RollForward");
        SetAnimatedState("RailWheelSpeed",Math.Abs(speed)<.001f?0:Math.Abs(speed));
        if(RailSpec.HumanPowered)
        {
            var pumping=GetComponent<Eco.Gameplay.Components.MountComponent>().Driver!=null&&Math.Abs(speed)>.05f;
            SetAnimatedState("RailPumpPose",pumping?"PumpOn":"PumpOff");
            SetAnimatedState("HandcarPumping",pumping);
        }
        var now=Environment.TickCount64;
        var acceleration=previousVisualTime==0||now-previousVisualTime<20?0:(Math.Abs(speed)-previousVisualSpeed)*1000/(now-previousVisualTime);
        SetAnimatedState("RailMotionState",Math.Abs(speed)<.001f?"Idle":acceleration>.1f?"Accelerating":acceleration<-.1f?"Decelerating":speed<0?"MovingReverse":"MovingForward");
        previousVisualSpeed=Math.Abs(speed);previousVisualTime=now;
    }
    internal void ReleaseRailPose(Vector3 velocity)
    { PublishWheelMotion(velocity); SetAnimatedState("RailGuidedPhysics",ServerOnlyPhysics); ((RailVehicleNetEntity)netEntity).ReleaseGuidedPose(velocity); }
}
