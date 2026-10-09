using Eco.Gameplay.Components.Storage;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;

namespace Eco.Minecarts.Runtime;

internal readonly record struct RailDriveCommand(double Force,bool Brake);
internal readonly record struct RailDriverContext(MinecartMotionComponent Motion,VoxelRail Rail,float Progress,
    int Facing,double Speed,double Seconds,double Grade);
internal interface IRailForceDriver { RailDriveCommand Request(in RailDriverContext context); }

// These adapters request force/brake commands. Only the movement component
// applies the resulting rail coordinate and world pose.
internal sealed class RailMotorDriver(RailVehicleObject vehicle) : IRailForceDriver
{
    private readonly IRailForceDriver input=new RailCommandDriver(vehicle);
    public RailDriveCommand Request(in RailDriverContext context)
    {
        var request=input.Request(context);
        if(vehicle.Capabilities.HasFlag(RailVehicleCapabilities.CableMotor))
        {
            var supply=context.Rail.Profile.Tram?TramCableDriveObject.PowerFor(context.Rail.Cell,vehicle.ID):0;
            return TramPowerRules.UsesCable(context.Rail.Profile.Tram,supply)?request with{Force=request.Force*supply}:new(0,true);
        }
        if(vehicle.Capabilities.HasFlag(RailVehicleCapabilities.FueledMotor)&&request.Force!=0)
        {
            var fuel=vehicle.GetComponent<FuelSupplyComponent>();
            var joules=(float)(RailEconomy.FuelWatts(vehicle.RailSpec)*context.Seconds*context.Motion.ServerThrottle);
            if(fuel==null||fuel.ConsumeAsMuchAsPossible(joules)+.001f<joules)
            {
                vehicle.GetComponent<TrainControllerComponent>()?.ReportSafety("Out of fuel");
                vehicle.PublishCabControls(0,true,context.Motion.ServerDirection);
                return new(0,true);
            }
            if(vehicle.GetComponent<RailConditionComponent>()?.ConditionPercent<=0)return new(0,true);
        }
        return request;
    }
}

internal sealed class RailCommandDriver(RailVehicleObject vehicle) : IRailForceDriver
{
    public RailDriveCommand Request(in RailDriverContext context)
    {
        if(vehicle.GetComponent<TrainControllerComponent>() is {} autopilot)
        {
            var command=autopilot.Control(context.Rail,context.Progress,context.Facing,context.Speed,context.Seconds);
            vehicle.PublishCabControls(command.Brake?0:context.Motion.ServerThrottle,command.Brake||autopilot.ServiceBrake>.001,context.Motion.ServerDirection);
            return new(command.Force,command.Brake);
        }
        var motion=context.Motion;
        return new(motion.ServerDriving?vehicle.GetComponent<RailCouplingComponent>().Performance
            .DriveForce(context.Speed,context.Grade*motion.ServerDirection*context.Facing)
            *motion.ServerThrottle*motion.ServerDirection*context.Facing:0,motion.ServerDriving&&motion.CommandBrake);
    }
}
