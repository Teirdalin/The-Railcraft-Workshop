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

[Serialized, MayHaveComponent(typeof(FuelSupplyComponent)), RequireComponent(typeof(TrainControllerComponent)), RequireComponent(typeof(TramRouteComponent))]
[Eco.Gameplay.Skills.RepairRequiresSkill(typeof(global::Eco.Mods.TechTree.IndustrySkill),3)]
public sealed class HeritageTramObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("HeritageTram", "Heritage Tram", 1250, 100, 4, 2.8f, 1.7f, .5f, 20, 9000, 7000, 9000, 2500, PassengerSeats:6, Model:"Tram");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.CableMotor | RailVehicleCapabilities.StationRouting;
    static HeritageTramObject() => AddOccupancy<HeritageTramObject>([]);
}

[Serialized, LocDisplayName("Heritage Tram"), LocDescription("A street tram for carrying passengers between town stops. Draws power through Tram Rail from a mechanical or electrical Tram Cable Drive, with no fuel needed aboard. Crafted at the Electric Machinist Table using Industry."), Weight(15000)]
public sealed class HeritageTramItem : RailModuleItem<HeritageTramObject> { }

[RequiresSkill(typeof(IndustrySkill), 3)] public sealed class HeritageTramRecipe : RollingStockRecipe<HeritageTramItem> { }
