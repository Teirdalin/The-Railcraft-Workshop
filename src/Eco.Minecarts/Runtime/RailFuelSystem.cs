using Eco.Core.Utils;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Minecarts.Physics;

namespace Eco.Minecarts.Runtime;

public sealed record RailFuelConfiguration(int Slots,string[] FuelTags,float Pollution=.1f);

internal static class RailFuelSystem
{
    internal static Result FlushQueuedPollution(RailVehicleObject vehicle)
    {
        if(vehicle.GetComponent<AirPollutionComponent>() is not {} pollution)return Result.Succeeded;
        Result result;
        lock(pollution.PollutionThrottle)
        {
            // Pickup packs selected component data into an item, then destroys
            // the object. Native pending emissions are not part of that item.
            // Use the native action so attribution, climate and law denial stay
            // authoritative; a denial retains the queue and rejects pickup.
            if(pollution.QueuedPollution<=0)return Result.Succeeded;
            pollution.PollutionThrottle.SetToTriggerNextTick();
            result=pollution.TryPollute(0,0);
        }
        // Pickup can still fail its inventory, contract or construction checks.
        // Persist a completed flush even when the engine remains in the world.
        if(result.Success)vehicle.SetDirty();
        return result;
    }

    // Native VehicleComponent.Operating requires a seated driver. Guided
    // autopilot and standing-cab operation burn on the motion clock instead.
    // Keep the existing rate, climate scaling, queue, attribution and laws.
    internal static bool TryPollute(RailVehicleObject vehicle,double seconds)
    {
        if(vehicle.Operating || vehicle.GetComponent<AirPollutionComponent>() is not {} pollution)return true;
        var time=(float)seconds;
        return pollution.TryPollute(pollution.PollutionTonsPerHour*vehicle.OperatingEfficiency*time/3600,time).Success;
    }

    internal static void Initialize(RailVehicleObject vehicle)
    {
        var config=vehicle.FuelConfiguration??throw new InvalidOperationException(vehicle.GetType().Name+" needs a fuel configuration.");
        vehicle.GetComponent<FuelSupplyComponent>().Initialize(config.Slots,config.FuelTags);
        // Guided driving consumes fuel once on the authoritative motion clock.
        // The native component otherwise also charges mounted vehicles per world
        // tick, doubling consumption. Current demand is exposed on TrainController.
        vehicle.GetComponent<FuelConsumptionComponent>().Initialize(0);
        vehicle.FuelDemandWatts = 0;
        vehicle.GetComponent<AirPollutionComponent>().Initialize(config.Pollution);
    }
}
