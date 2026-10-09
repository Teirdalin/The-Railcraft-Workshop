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
public sealed class LargeTrainEngineObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("LargeTrainEngine", "Large Train Engine", 7000, 1000, 8, 4.8f, 2.6f, 2.5f, 30, 250000, 70000, 40000, 100000, Model: "LargeEngine", Tier: "Industry");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.FueledMotor | RailVehicleCapabilities.StationRouting;
    public override RailFuelConfiguration FuelConfiguration=>new(4,["Burnable Fuel"]);
    static LargeTrainEngineObject() => AddOccupancy<LargeTrainEngineObject>([]);
}

[LocDescription("Crafted at the Machinist Table using Mechanics.")]
[Serialized, LocDisplayName("Large Train Engine"), Weight(15000)] public sealed class LargeTrainEngineItem : RailModuleItem<LargeTrainEngineObject> { }

[RequiresSkill(typeof(MechanicsSkill), 5)] public sealed class LargeTrainEngineRecipe : RollingStockRecipe<LargeTrainEngineItem> { }
