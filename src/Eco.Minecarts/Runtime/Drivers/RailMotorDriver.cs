using Eco.Gameplay.Components.Storage;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;

namespace Eco.Minecarts.Runtime;

internal readonly record struct RailDriveCommand(double Force,bool Brake,double Effort=0);
internal readonly record struct RailDriverContext(MinecartMotionComponent Motion,VoxelRail Rail,float Progress,
    int Facing,double Speed,double Seconds,double Grade);
internal interface IRailForceDriver { RailDriveCommand Request(in RailDriverContext context); }

// These adapters request force/brake commands. Only the movement component
// applies the resulting rail coordinate and world pose.
internal sealed class RailMotorDriver(RailVehicleObject vehicle) : IRailForceDriver
{
    private readonly IRailForceDriver input=new RailCommandDriver(vehicle);
    private long nextPollutionRetry;
    private bool PollutionAllowed(double seconds)
    {
        var now=Environment.TickCount64;
        if(now>=nextPollutionRetry && RailFuelSystem.TryPollute(vehicle,seconds))return true;
        // Native denial retains the queued pollution. Retry it without adding
        // fuel/emissions, and avoid repeating owner notifications per substep.
        if(now>=nextPollutionRetry)nextPollutionRetry=now+1000;
        vehicle.GetComponent<TrainControllerComponent>()?.ReportSafety("Air pollution prohibited");
        return false;
    }
    public RailDriveCommand Request(in RailDriverContext context)
    {
        // Publish one final decision. Fuel/condition failures must not first
        // release the visual lever through the command adapter every substep.
        var result=Resolve(context);
        vehicle.DriveSafetyBrake=result.Brake||vehicle.GetComponent<TrainControllerComponent>()?.ServiceBrake>.001;
        vehicle.PublishCabControls(result.Brake?0:context.Motion.ServerThrottle,vehicle.DriveSafetyBrake,context.Motion.ServerDirection);
        return result;
    }
    private RailDriveCommand Resolve(in RailDriverContext context)
    {
        if(vehicle.GetComponent<RailCouplingComponent>().RepairRequired)
        {
            vehicle.FuelDemandWatts=0;
            vehicle.GetComponent<TrainControllerComponent>()?.ReportRepairRequired(context.Rail,context.Progress,context.Facing);
            return new(0,true);
        }
        var request=input.Request(context);
        if(vehicle.Capabilities.HasFlag(RailVehicleCapabilities.CableMotor))
        {
            var supply=context.Rail.Profile.Tram?TramCableDriveObject.PowerFor(context.Rail.Cell,vehicle.ID):0;
            return TramPowerRules.UsesCable(context.Rail.Profile.Tram,supply)?request with{Force=request.Force*supply}:new(0,true);
        }
        if(vehicle.Capabilities.HasFlag(RailVehicleCapabilities.FueledMotor))
        {
            vehicle.FuelDemandWatts = 0;
            if (request.Force == 0) return request;
            if(vehicle.GetComponent<RailConditionComponent>()?.RepairRequired==true)return new(0,true);
            if(!PollutionAllowed(0))return new(0,true);
            var fuel=vehicle.GetComponent<FuelSupplyComponent>();
            var demand=RailEconomy.HaulFuelWatts(vehicle.RailSpec,vehicle.GetComponent<RailCouplingComponent>().Performance.Mass,request.Effort);
            var joules=(float)(demand*context.Seconds);
            var consumed=fuel?.ConsumeAsMuchAsPossible(joules)??0;
            if(consumed>0 && joules>0 && !PollutionAllowed(context.Seconds*Math.Min(1,consumed/joules)))return new(0,true);
            if(fuel==null||consumed<=0||consumed+.001f<joules)
            {
                vehicle.GetComponent<TrainControllerComponent>()?.ReportSafety("Out of fuel");
                return new(0,true);
            }
            vehicle.FuelDemandWatts = demand;
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
            return new(command.Force,command.Brake,autopilot.DriveEffort);
        }
        var motion=context.Motion;
        return new(motion.ServerDriving?vehicle.GetComponent<RailCouplingComponent>().Performance
            .DriveForce(context.Speed,context.Grade*motion.ServerDirection*context.Facing)
            *motion.ServerThrottle*motion.ServerDirection*context.Facing:0,motion.ServerDriving&&motion.CommandBrake,
            motion.ServerDriving?motion.ServerThrottle:0);
    }
}
