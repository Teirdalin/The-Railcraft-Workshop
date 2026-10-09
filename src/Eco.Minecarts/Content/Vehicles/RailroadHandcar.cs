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

[Serialized, RequireComponent(typeof(HandcarDrivingComponent))]
public sealed class RailroadHandcarObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("RailroadHandcar", "Railroad Handcar", 180, 100, 4, 1.9f, 1.0f, .5f, 8, 450, 700, 2500, 900, Model: "Handcar");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.HumanMotor | RailVehicleCapabilities.NativeGroundPhysics;
    static RailroadHandcarObject() => AddOccupancy<RailroadHandcarObject>([]);
}

[Serialized, LocDisplayName("Railroad Handcar"), LocDescription("Human-powered rail platform with a pumping lever. Operate from the platform; light loads only. Uses calories, not fuel. Crafted at the Wainwright Table using Basic Engineering."), Weight(10000)]
public sealed class RailroadHandcarItem : RailModuleItem<RailroadHandcarObject> { }

[RequiresSkill(typeof(BasicEngineeringSkill), 2)] public sealed class RailroadHandcarRecipe : RollingStockRecipe<RailroadHandcarItem> { }
