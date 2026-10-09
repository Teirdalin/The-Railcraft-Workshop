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

[Serialized] public sealed class PassengerCarObject : RollingStockObject
{
    public static RailVehicleSpec DefaultSpecification {get;} = new("PassengerCar", "Passenger Car", 650, 100, 4, 2.4f, 1.2f, .5f, 30, 0, 0, 2200, 0, PassengerSeats: 4, Model: "Passenger");
    public override RailVehicleSpec RailSpec => RailVehicleBalances.Resolve(DefaultSpecification);
    public override RailVehicleCapabilities Capabilities => RailVehicleCapabilities.None;
    static PassengerCarObject() => AddOccupancy<PassengerCarObject>([]);
}

[LocDescription("Crafted at the Wainwright Table using Basic Engineering.")]
[Serialized, LocDisplayName("Passenger Car"), Weight(15000)] public sealed class PassengerCarItem : RailModuleItem<PassengerCarObject> { }

[RequiresSkill(typeof(BasicEngineeringSkill), 3)] public sealed class PassengerCarRecipe : RollingStockRecipe<PassengerCarItem> { }
