namespace Eco.Mods.TechTree;

using System.Numerics;
using Eco.Core.Controller;
using Eco.Core.Items;
using Eco.Core.PropertyHandling;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Players;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Systems.EnvVars;
using Eco.Minecarts.Runtime;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;

[Serialized, LocDisplayName("Standard Rail - Wide Turn (3x3)"), Weight(12000)]
public sealed class WideRailTurnItem : WorldObjectItem<WideRailTurnObject> { }
[Serialized, RequireComponent(typeof(PropertyAuthComponent))]
public sealed class WideRailTurnObject : WorldObject, IRepresentsItem
{
    static WideRailTurnObject() => AddOccupancy<WideRailTurnObject>(Enumerable.Range(-1, 3).SelectMany(x => Enumerable.Range(-1, 3)
        .Select(z => new BlockOccupancy(new Eco.Shared.Math.Vector3i(x, 0, z), typeof(BuildingWorldObjectBlock)))).ToList());
    public Type RepresentedItemType => typeof(WideRailTurnItem);
    public override LocString DisplayName => Localizer.DoStr("Standard Rail - Wide Turn (3x3)");
    protected override void Initialize() { base.Initialize(); RailInfrastructure.Register(this); }
    protected override void OnDestroy()
    {
        var cells=WorldOccupancy?.ToArray();var id=ObjectID;
        RailInfrastructure.Remove(this);base.OnDestroy();
        if(cells!=null)foreach(var cell in cells)
            if(Eco.World.World.GetBlock(cell) is WorldObjectBlock block && block.WorldObjectHandle.Id==id)
                Eco.World.World.DeleteBlock(cell);
    }
}
[Serialized,LocDisplayName("Tram Rail - Wide Turn (3x3)"),Weight(12000)]
public sealed class TramWideRailTurnItem : WorldObjectItem<TramWideRailTurnObject> { }
[Serialized,RequireComponent(typeof(PropertyAuthComponent))]
public sealed class TramWideRailTurnObject : WorldObject,IRepresentsItem
{
    static TramWideRailTurnObject()=>AddOccupancy<TramWideRailTurnObject>(Enumerable.Range(-1,3).SelectMany(x=>Enumerable.Range(-1,3)
        .Select(z=>new BlockOccupancy(new Eco.Shared.Math.Vector3i(x,0,z),typeof(BuildingWorldObjectBlock)))).ToList());
    public Type RepresentedItemType=>typeof(TramWideRailTurnItem);
    public override LocString DisplayName=>Localizer.DoStr("Tram Rail - Wide Turn (3x3)");
    protected override void Initialize(){base.Initialize();RailInfrastructure.Register(this);}
    protected override void OnDestroy()
    {
        var cells=WorldOccupancy?.ToArray();var id=ObjectID;
        RailInfrastructure.Remove(this);base.OnDestroy();
        if(cells!=null)foreach(var cell in cells)
            if(Eco.World.World.GetBlock(cell) is WorldObjectBlock block && block.WorldObjectHandle.Id==id)
                Eco.World.World.DeleteBlock(cell);
    }
}
[RequiresSkill(typeof(BasicEngineeringSkill),3)]
public sealed class TramWideRailTurnRecipe:MinecartRailRecipeFamily
{public TramWideRailTurnRecipe()=>Configure(MinecartRailRecipes.Make<TramWideRailTurnItem>("Tram Rail - Wide Turn (3x3)",4,6,2),"Tram Rail - Wide Turn (3x3)",typeof(TramWideRailTurnRecipe),120,3);}
[RequiresSkill(typeof(BasicEngineeringSkill), 3)]
public sealed class WideRailTurnRecipe : MinecartRailRecipeFamily
{ public WideRailTurnRecipe() => this.Configure(MinecartRailRecipes.Make<WideRailTurnItem>("Standard Rail - Wide Turn (3x3)", 4, 0, 2, hewnLogs: 6), "Standard Rail - Wide Turn (3x3)", typeof(WideRailTurnRecipe), 120, 3); }

[Serialized, LocDisplayName("Train Station"), Weight(10000)]
public sealed class TrainStationItem : WorldObjectItem<TrainStationObject> { }
[Serialized, RequireComponent(typeof(PropertyAuthComponent)), RequireComponent(typeof(TrainStationComponent)), RequireComponent(typeof(RailAutomationComponent))]
public sealed class TrainStationObject : WorldObject, IRepresentsItem
{
    static TrainStationObject() => AddOccupancyList(typeof(TrainStationObject), new BlockOccupancy(Eco.Shared.Math.Vector3i.Zero, typeof(BuildingWorldObjectBlock)));
    public Type RepresentedItemType => typeof(TrainStationItem);
    public override LocString DisplayName => Localizer.DoStr("Train Station");
}
[RequiresSkill(typeof(BasicEngineeringSkill), 3)]
public sealed class TrainStationRecipe : MinecartRailRecipeFamily
{ public TrainStationRecipe() => this.Configure(MinecartRailRecipes.Make<TrainStationItem>("Train Station", 8, 12), "Train Station", typeof(TrainStationRecipe), 100, 4); }

[Serialized, LocDisplayName("Broken Wooden Rail"), Weight(1000)]
public sealed class BrokenWoodenTrackItem : WorldObjectItem<BrokenWoodenTrackObject> { }
[Serialized, RequireComponent(typeof(PropertyAuthComponent)), RequireComponent(typeof(BrokenRailComponent))]
public sealed class BrokenWoodenTrackObject : WorldObject, IRepresentsItem
{
    static BrokenWoodenTrackObject() => AddOccupancyList(typeof(BrokenWoodenTrackObject), new BlockOccupancy(Eco.Shared.Math.Vector3i.Zero, typeof(BuildingWorldObjectBlock)));
    public Type RepresentedItemType => typeof(BrokenWoodenTrackItem);
    public override LocString DisplayName => Localizer.DoStr("Broken Wooden Rail");
    protected override void OnDestroy()
    {
        // Keep the original occupancy cells and verify our own placeholder is
        // gone after native destruction. Never delete a replacement block.
        var cells=this.WorldOccupancy?.ToArray();
        var id=this.ObjectID;
        base.OnDestroy();
        if(cells==null) return;
        foreach(var cell in cells)
            if(Eco.World.World.GetBlock(cell) is WorldObjectBlock block
                && block.WorldObjectHandle.Id==id)
                Eco.World.World.DeleteBlock(cell);
    }
}
[Serialized, NoIcon, LocDisplayName("Broken Rail")]
public sealed class BrokenRailComponent : WorldObjectComponent, IHasEnvVars, IPickupConfirmationComponent
{
    public Eco.Core.Utils.Result CanPickup() => Eco.Core.Utils.Result.Fail(LocString.Empty);
    private readonly object gate = new();
    [Notify, EnvVar] public bool HoldingRailAxe(User user) => user.Inventory.Toolbar.SelectedItem is AxeItem;
    [Interaction(InteractionTrigger.LeftClick, "Cut into wood pulp", requiredEnvVars: new[] { "HoldingRailAxe" }, interactionDistance: 3,
        priority: 100, authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction, AnimationDriven = true)]
    public void Chop(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        lock (this.gate)
        {
            if (this.Parent.IsDestroyed || !this.HoldingRailAxe(player.User) || !this.Parent.IsAuthorized(player.User, AccessType.FullAccess)
                || Vector3.Distance(player.User.Position, this.Parent.Position) > 3) return;
            var axe=(AxeItem)player.User.Inventory.Toolbar.SelectedItem;
            using(var pack=new GameActionPack())
            {
                pack.AddToInventory(player.User.Inventory, Item.Get<WoodPulpItem>(), 4, player.User);
                pack.UseTool(axe.CreateMultiblockContext(player, false, this.Parent.Position3i));
                pack.AddPostEffect(()=>WorldObjectManager.DestroyPermanently(this.Parent));
                pack.TryPerform(player.User);
            }
        }
    }
}
