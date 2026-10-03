namespace Eco.Mods.TechTree;
using Eco.Core.Items;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Modules;
using Eco.Shared.Networking;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

[Serialized,RequireComponent(typeof(OnOffComponent)),RequireComponent(typeof(PropertyAuthComponent)),
 RequireComponent(typeof(LinkComponent)),RequireComponent(typeof(CraftingComponent)),
 RequireComponent(typeof(OccupancyRequirementComponent)),RequireComponent(typeof(PluginModulesComponent)),
 RequireComponent(typeof(ForSaleComponent)),Tag("Usable")]
public sealed class RailcraftWorkbenchObject:WorldObject,IRepresentsItem
{
    public Type RepresentedItemType=>typeof(RailcraftWorkbenchItem);
    public override LocString DisplayName=>Localizer.DoStr("Railcraft Workbench");
    static RailcraftWorkbenchObject()=>AddOccupancy<RailcraftWorkbenchObject>(
        Enumerable.Range(0,2).SelectMany(x=>Enumerable.Range(0,2).Select(y=>new BlockOccupancy(new(x,y,0),typeof(BuildingWorldObjectBlock)))).ToList());
}
[Serialized,LocDisplayName("Railcraft Workbench"),
 LocDescription("A dedicated workbench for rails, supports, minecarts, trains and roller coaster equipment. Connect nearby storage to supply crafting materials."),
 Weight(4000),Ecopedia("Work Stations","Craft Tables",createAsSubPage:true),
 AllowPluginModules(ItemTypes=new[]{typeof(BasicEngineeringUpgradeItem),typeof(BasicUpgradeItem),typeof(AdvancedUpgradeItem),typeof(ModernUpgradeItem)})]
public sealed class RailcraftWorkbenchItem:WorldObjectItem<RailcraftWorkbenchObject>,IPersistentData
{
    protected override OccupancyContext GetOccupancyContext=>new SideAttachedContext(
        Eco.Shared.Math.DirectionAxisFlags.Down,WorldObject.GetOccupancyInfo(WorldObjectType));
    [Serialized,SyncToView] public object PersistentData {get;set;}=null!;
}
[RequiresSkill(typeof(BasicEngineeringSkill),1)]
public sealed class RailcraftWorkbenchRecipe:RecipeFamily
{
    public RailcraftWorkbenchRecipe()
    {
        Recipes=[MinecartRailRecipes.Make<RailcraftWorkbenchItem>("Railcraft Workbench",4,8)];
        ExperienceOnCraft=3;LaborInCalories=CreateLaborInCaloriesValue(120,typeof(BasicEngineeringSkill));
        CraftMinutes=CreateCraftTimeValue(typeof(RailcraftWorkbenchRecipe),3,typeof(BasicEngineeringSkill));
        Initialize(Localizer.DoStr("Railcraft Workbench"),typeof(RailcraftWorkbenchRecipe));
        CraftingComponent.AddRecipe(typeof(WainwrightTableObject),this);
    }
}
