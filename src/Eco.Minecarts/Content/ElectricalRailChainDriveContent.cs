namespace Eco.Mods.TechTree;

using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;

[Serialized, LocDisplayName("Electrical Rail Chain Drive"),
 LocDescription("An electric motor for lifting carts along connected chain rails. Connect electrical power and choose a lifting speed; the chain slows or stops when power is short. Cart handbrakes prevent rollback. Crafted at the Electric Machinist Table using Industry."), Weight(12000)]
public sealed class ElectricalRailChainDriveItem : WorldObjectItem<ElectricalRailChainDriveObject>, IPersistentData
{
    protected override OccupancyContext GetOccupancyContext => new SideAttachedContext(
        DirectionAxisFlags.Down, WorldObject.GetOccupancyInfo(WorldObjectType));
    [Serialized] public object PersistentData {get;set;} = null!;
}

[Serialized]
public sealed class ElectricalRailChainDriveObject : MinecartChainDriveObject
{
    static ElectricalRailChainDriveObject() => AddOccupancyList(typeof(ElectricalRailChainDriveObject),
        new BlockOccupancy(Vector3i.Zero,typeof(BuildingWorldObjectBlock)));
    public override bool Electrical => true;
    public override Type RepresentedItemType => typeof(ElectricalRailChainDriveItem);
    public override LocString DisplayName => Localizer.DoStr("Electrical Rail Chain Drive");
}

[RequiresSkill(typeof(IndustrySkill),3)]
public sealed class ElectricalRailChainDriveRecipe : MinecartRailRecipeFamily
{
    public ElectricalRailChainDriveRecipe() => Configure(ElectricalDriveRecipes.Make<ElectricalRailChainDriveItem,MinecartChainDriveItem>(
        "Electrical Rail Chain Drive"),"Electrical Rail Chain Drive",typeof(ElectricalRailChainDriveRecipe),200,3,typeof(IndustrySkill));
}
