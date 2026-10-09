using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Minecarts.Physics;

namespace Eco.Minecarts.Runtime;

public sealed record RailFuelConfiguration(int Slots,string[] FuelTags,float Pollution=.1f);

internal static class RailFuelSystem
{
    internal static void Initialize(RailVehicleObject vehicle)
    {
        var config=vehicle.FuelConfiguration??throw new InvalidOperationException(vehicle.GetType().Name+" needs a fuel configuration.");
        vehicle.GetComponent<FuelSupplyComponent>().Initialize(config.Slots,config.FuelTags);
        vehicle.GetComponent<FuelConsumptionComponent>().Initialize(RailEconomy.FuelWatts(vehicle.RailSpec));
        vehicle.GetComponent<AirPollutionComponent>().Initialize(config.Pollution);
    }
}
