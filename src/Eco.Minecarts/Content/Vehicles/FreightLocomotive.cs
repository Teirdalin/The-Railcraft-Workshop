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
public sealed class FreightLocomotiveObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("FreightLocomotive", "Heavy-Haul Locomotive", 2200, 400, 6, 3.0f, 1.6f, 1.0f, 30, 75000, 18000, 13000, 25000, Model: "Freight", Tier: "Steel");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.FueledMotor | RailVehicleCapabilities.StationRouting;
    public override RailFuelConfiguration FuelConfiguration=>new(2,["Burnable Fuel"]);
    static FreightLocomotiveObject() => AddOccupancy<FreightLocomotiveObject>([]);
}

[LocDescription("Crafted at the Machinist Table using Mechanics.")]
[Serialized, LocDisplayName("Heavy-Haul Locomotive"), Weight(15000)] public sealed class FreightLocomotiveItem : RailModuleItem<FreightLocomotiveObject> { }

[RequiresSkill(typeof(MechanicsSkill), 3)] public sealed class FreightLocomotiveRecipe : RollingStockRecipe<FreightLocomotiveItem> { }
