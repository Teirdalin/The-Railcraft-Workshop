namespace Eco.Mods.TechTree;

using Eco.Core.Controller;
using Eco.Core.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Systems.NewTooltip;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Runtime;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;

[Serialized, RequireComponent(typeof(FuelSupplyComponent)), RequireComponent(typeof(FuelConsumptionComponent)), RequireComponent(typeof(AirPollutionComponent)), RequireComponent(typeof(MineTrainDrivingComponent)), RequireComponent(typeof(TrainControllerComponent))]
public sealed class PassengerLocomotiveObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("PassengerLocomotive", "Passenger Locomotive", 1100, 150, 4, 2.6f, 1.25f, .5f, 30, 38000, 5000, 8500, 5000, Model: "Express", Tier: "Steel");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.FueledMotor | RailVehicleCapabilities.StationRouting;
    public override RailFuelConfiguration FuelConfiguration=>new(2,["Burnable Fuel"]);
    static PassengerLocomotiveObject() => AddOccupancy<PassengerLocomotiveObject>([]);
}

[LocDescription("Crafted at the Machinist Table using Mechanics.")]
[Serialized, LocDisplayName("Passenger Locomotive"), Weight(15000)] public sealed class PassengerLocomotiveItem : RailModuleItem<PassengerLocomotiveObject> { }

[RequiresSkill(typeof(MechanicsSkill), 3)] public sealed class PassengerLocomotiveRecipe : RollingStockRecipe<PassengerLocomotiveItem> { }
