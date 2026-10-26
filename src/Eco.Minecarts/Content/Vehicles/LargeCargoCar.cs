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
public sealed class LargeCargoCarObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("LargeCargoCar", "Large Cargo Car", 5000, 20000, 48, 4.4f, 2.8f, 2.5f, 30, 0, 0, 8000, 0, Model: "LargeCargo", Tier: "Industry");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.None;
    static LargeCargoCarObject() => AddOccupancy<LargeCargoCarObject>([]);
}

[LocDescription("A large freight wagon for moving bulk goods between mines, workshops, and settlements. Couple it to a locomotive with enough power for the loaded train. Crafted at the Assembly Line using Mechanics.")]
[Serialized, LocDisplayName("Large Cargo Car"), Weight(15000)] public sealed class LargeCargoCarItem : RailModuleItem<LargeCargoCarObject> { }

[RequiresSkill(typeof(MechanicsSkill), 4)] public sealed class LargeCargoCarRecipe : RollingStockRecipe<LargeCargoCarItem> { }
