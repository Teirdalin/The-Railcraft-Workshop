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

[Serialized] [Eco.Gameplay.Skills.RepairRequiresSkill(typeof(global::Eco.Mods.TechTree.MechanicsSkill),4)]
public sealed class LargePassengerCarObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("LargePassengerCar", "Large Passenger Car", 4500, 500, 8, 4.6f, 2.8f, 2.5f, 30, 0, 0, 6500, 0, PassengerSeats: 8, Model: "LargePassenger", Tier: "Industry");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.None;
    static LargePassengerCarObject() => AddOccupancy<LargePassengerCarObject>([]);
}

[LocDescription("A spacious railway carriage for carrying more travelers on longer trains. Couple it to a locomotive and provide broad curves along the route. Crafted at the Assembly Line using Mechanics.")]
[Serialized, LocDisplayName("Large Passenger Car"), Weight(15000)] public sealed class LargePassengerCarItem : RailModuleItem<LargePassengerCarObject> { }

[RequiresSkill(typeof(MechanicsSkill), 4)] public sealed class LargePassengerCarRecipe : RollingStockRecipe<LargePassengerCarItem> { }
