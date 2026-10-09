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
 LocDescription("Electric-powered chain lift. Connect beside chain rails, supply electrical power and choose 0.25x to 5x speed. Power demand scales with speed; insufficient power reduces speed or stops the lift. Use cart handbrakes to prevent rollback. Crafted at the Electric Machinist Table using Industry."), Weight(12000)]
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
        "Electrical Rail Chain Drive"),"Electrical Rail Chain Drive",typeof(ElectricalRailChainDriveRecipe),200,6,typeof(IndustrySkill));
}
