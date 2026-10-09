using Eco.Minecarts.Track;

namespace Eco.Minecarts.Physics;

// The manual cart has one rail coordinate and signed speed. World position is
// derived from this coordinate; received client transforms are never an input.
public readonly record struct ManualCartRailState(VoxelRail Rail, float Progress,
    int Facing, double Speed = 0, double Acceleration = 0, long Sequence = 0);

public static class ManualCartRailMotion
{
    public static ManualCartRailState Step(ManualCartRailState state, MinecartInput input,
        MinecartTuning tuning, double seconds, double speedLimit,
        Func<VoxelRail,int,(VoxelRail Rail,int End)?> neighbor,
        Func<VoxelRail,float,double,double>? limitTravel = null)
    {
        if(!double.IsFinite(seconds)||seconds<=0)return state;
        var dt=Math.Min(seconds,.02);
        var integrated=RailPhysics.Integrate(new(0,state.Speed,0),input,
            tuning with {DerailLateralAcceleration=double.PositiveInfinity},dt);
        var speed=Math.Clamp(integrated.State.Speed,-speedLimit,speedLimit);
        var travel=speed*dt;
        if(limitTravel!=null)
        {
            var permitted=limitTravel(state.Rail,state.Progress,travel);
            if(Math.Abs(permitted-travel)>.000001)speed=0;
            travel=permitted;
        }
        var next=RailPathCursor.Travel(state.Rail,state.Progress,travel,neighbor);
        return new(next.Rail,next.Progress,state.Facing*next.Orientation,
            next.Remaining!=0?0:speed*next.Orientation,integrated.Acceleration,state.Sequence+1);
    }

    public static double HandleForce(double mass,double grade,double speed,
        double walkingSpeed,double longitudinalError)
    {
        if(!double.IsFinite(walkingSpeed)||!double.IsFinite(longitudinalError))return 0;
        var target=Math.Clamp(walkingSpeed+longitudinalError*2,-1.5,1.5);
        // Walking changes a force command; it never writes a cart/player pose.
        var force=mass*(Math.Clamp((target-speed)*4,-2,2)+9.80665*grade);
        return Math.Clamp(force,-4500,4500);
    }
}
