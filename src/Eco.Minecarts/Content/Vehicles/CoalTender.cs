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

[Serialized] public sealed class CoalTenderObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("CoalTender", "Coal Tender", 480, 3000, 12, 2.0f, 1.0f, .5f, 30, 0, 0, 1800, 0, Tender: true, Model: "Tender");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.None;
    static CoalTenderObject() => AddOccupancy<CoalTenderObject>([]);
}

[LocDescription("Crafted at the Wainwright Table using Basic Engineering.")]
[Serialized, LocDisplayName("Coal Tender"), Weight(15000)] public sealed class CoalTenderItem : RailModuleItem<CoalTenderObject> { }

[RequiresSkill(typeof(BasicEngineeringSkill), 3)] public sealed class CoalTenderRecipe : RollingStockRecipe<CoalTenderItem> { }
