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

[Serialized, RequireComponent(typeof(MinecartRidingComponent))]
public sealed class WoodenMinecartObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("WoodenMinecart", "Wooden Minecart", 80, 400, 8, 1.612f, .82f, .5f, 12, 0, 0, 1300, 0, Pullable: true, DurabilityHours: 40, Model: "Wood", Tier: "Wood");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.ManualHandle;
    public override System.Numerics.Vector3 ContactHalfSize => new(.40f,.40f,.80f);
    static WoodenMinecartObject() => AddOccupancy<WoodenMinecartObject>([]);
}

[LocDescription("Crafted at the Wainwright Table using Basic Engineering.")]
[Serialized, LocDisplayName("Wooden Minecart"), Weight(10000)] public sealed class WoodenMinecartItem : RailModuleItem<WoodenMinecartObject> { }

[RequiresSkill(typeof(BasicEngineeringSkill), 1)] public sealed class WoodenMinecartRecipe : RollingStockRecipe<WoodenMinecartItem> { }
