namespace Eco.Mods.TechTree;
using Eco.Core.Items;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Players;
using Eco.World.Blocks;
using Eco.Minecarts.Runtime;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

[Serialized] [Eco.Gameplay.Skills.RepairRequiresSkill(typeof(global::Eco.Mods.TechTree.IndustrySkill),3)]
public sealed class RollerCoasterCartObject:RollingStockObject
{
    public static Eco.Minecarts.Physics.RailVehicleSpec DefaultSpecification {get;} = new("RollerCoasterCart", "Roller Coaster Cart", 300, 0, 1, 1.8f, .8f, .5f, 40, 0, 0, 7000, 0, PassengerSeats: 2, Model: "Coaster");
    public override Eco.Minecarts.Runtime.RailVehicleCapabilities Capabilities => Eco.Minecarts.Runtime.RailVehicleCapabilities.Coaster;
    public override Eco.Minecarts.Physics.RailVehicleSpec RailSpec => Eco.Minecarts.Physics.RailVehicleBalances.Resolve(DefaultSpecification);
static RollerCoasterCartObject()=>AddOccupancy<RollerCoasterCartObject>([]);}
[Serialized,LocDisplayName("Roller Coaster Cart"),LocDescription("A two-seat cart with guide wheels that hold it to Roller Coaster Rail. Gravity carries it downhill, while powered chain rails lift it for the next descent. Crafted at the Electric Machinist Table using Industry."),Weight(12000)]
public sealed class RollerCoasterCartItem:RailModuleItem<RollerCoasterCartObject>
{
    // The station's explicit placement validates track and clearance. Empty
    // occupancy prevents the native floor test rejecting its own rail surface.
    protected override OccupancyContext GetOccupancyContext=>new PositionsRequirementContext([]);
    public override Task<bool> CanPlaceObject(Player player,System.Numerics.Vector3 pos,Eco.Shared.Math.Quaternion rotation)
    {
        if(TrackWorld.Capture(pos,rotation.RotateVector(System.Numerics.Vector3.UnitZ),.5f,1.05f,coaster:true)!=null)
            return Task.FromResult(true);
        return Task.FromResult(new SideAttachedContext(Eco.Shared.Math.DirectionAxisFlags.None,WorldObject.GetOccupancyInfo(WorldObjectType))
            .CanPlaceObject(player,this,pos,rotation));
    }
    [Eco.Gameplay.Interactions.Interactors.Interaction(Eco.Shared.SharedTypes.InteractionTrigger.RightClick,
        "Place cart on station",requiredEnvVars:new[]{"CoasterLoadingStation"},interactionDistance:5,priority:100,
        flags:Eco.Shared.SharedTypes.InteractionFlags.BlocksOtherInteraction)]
    public async Task ApplyToStation(Player player,Eco.Shared.SharedTypes.InteractionTriggerInfo trigger,Eco.Shared.SharedTypes.InteractionTarget target)
    {
        if(target.NetObj is CoasterStationObject station)
            await station.GetComponent<CoasterStationComponent>().PlaceCart(player,this);
    }
}
[RequiresSkill(typeof(IndustrySkill),3)]
public sealed class RollerCoasterCartRecipe:MinecartRailRecipeFamily
{
    public RollerCoasterCartRecipe()
    {
        // Native steel plates cost three bars each: nine bars of body metal
        // replace fourteen, while the existing fabric and mechanisms remain.
        var recipe=MinecartRailRecipes.Make<RollerCoasterCartItem>("RollerCoasterCart",3,8,fabric:4,
            skillType:typeof(IndustrySkill),metalType:typeof(SteelPlateItem),steelGears:1,lubricant:1);
        Configure(recipe,"Roller Coaster Cart",typeof(RollerCoasterCartRecipe),200,4,skillType:typeof(IndustrySkill));
    }
}
