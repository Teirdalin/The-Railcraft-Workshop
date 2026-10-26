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

[Serialized, LocDisplayName("Electrical Tram Cable Drive"),
 LocDescription("An electric drive that supplies traction to trams through connected Tram Rail. Connect it to an electrical grid to power the line without carrying fuel aboard each tram. Crafted at the Electric Machinist Table using Industry."), Weight(12000)]
public sealed class ElectricalTramCableDriveItem : WorldObjectItem<ElectricalTramCableDriveObject>
{
    protected override OccupancyContext GetOccupancyContext => new SideAttachedContext(
        DirectionAxisFlags.Down, WorldObject.GetOccupancyInfo(WorldObjectType));
}

[Serialized]
public sealed class ElectricalTramCableDriveObject : TramCableDriveObject
{
    static ElectricalTramCableDriveObject() => AddOccupancyList(typeof(ElectricalTramCableDriveObject),
        new BlockOccupancy(Vector3i.Zero,typeof(BuildingWorldObjectBlock)));
    public override bool Electrical => true;
    public override Type RepresentedItemType => typeof(ElectricalTramCableDriveItem);
    public override LocString DisplayName => Localizer.DoStr("Electrical Tram Cable Drive");
}

[RequiresSkill(typeof(IndustrySkill),3)]
public sealed class ElectricalTramCableDriveRecipe : MinecartRailRecipeFamily
{
    public ElectricalTramCableDriveRecipe() => Configure(ElectricalDriveRecipes.Make<ElectricalTramCableDriveItem,TramCableDriveItem>(
        "Electrical Tram Cable Drive"),"Electrical Tram Cable Drive",typeof(ElectricalTramCableDriveRecipe),200,3,typeof(IndustrySkill));
}

internal static class ElectricalDriveRecipes
{
    internal static Recipe Make<T,TDrive>(string name) where T:Item,new() where TDrive:Item,new()
    {
        var recipe=new Recipe();
        recipe.Init(name,Localizer.DoStr(name),[
            new IngredientElement(typeof(TDrive),1,true),
            new IngredientElement(typeof(ElectricMotorItem),1,true),
            new IngredientElement(typeof(SteelBarItem),3,typeof(IndustrySkill)),
            new IngredientElement(typeof(BasicCircuitItem),2,typeof(IndustrySkill))],[],[new CraftingElement<T>()]);
        return recipe;
    }
}
