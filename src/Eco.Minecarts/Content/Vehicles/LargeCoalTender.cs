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

[Serialized] public sealed class LargeCoalTenderObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("LargeCoalTender", "Large Coal Tender", 3500, 12000, 32, 3.8f, 2.4f, 2.5f, 30, 0, 0, 6000, 0, Tender: true, Model: "LargeTender", Tier: "Industry");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.None;
    static LargeCoalTenderObject() => AddOccupancy<LargeCoalTenderObject>([]);
}

[LocDescription("Crafted at the Machinist Table using Mechanics.")]
[Serialized, LocDisplayName("Large Coal Tender"), Weight(15000)] public sealed class LargeCoalTenderItem : RailModuleItem<LargeCoalTenderObject> { }

[RequiresSkill(typeof(MechanicsSkill), 4)] public sealed class LargeCoalTenderRecipe : RollingStockRecipe<LargeCoalTenderItem> { }
