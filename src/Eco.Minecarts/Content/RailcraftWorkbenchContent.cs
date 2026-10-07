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
using Eco.Gameplay.Minimap;
using Eco.Shared.Networking;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.Items;

[Serialized,RequireComponent(typeof(OnOffComponent)),RequireComponent(typeof(PropertyAuthComponent)),
 RequireComponent(typeof(MinimapComponent)),
 RequireComponent(typeof(LinkComponent)),RequireComponent(typeof(CraftingComponent)),
 RequireComponent(typeof(OccupancyRequirementComponent)),RequireComponent(typeof(PluginModulesComponent)),
 RequireComponent(typeof(ForSaleComponent)),Tag("Usable")]
public sealed class RailcraftWorkbenchObject:WorldObject,IRepresentsItem
{
    public Type RepresentedItemType=>typeof(RailcraftWorkbenchItem);
    public override LocString DisplayName=>Localizer.DoStr("Railworks Workbench");
    public override TableTextureMode TableTexture=>TableTextureMode.Wood;
    protected override void Initialize()
    {
        base.Initialize();
        GetComponent<MinimapComponent>().SetCategory(Localizer.DoStr("Crafting"));
    }
    static RailcraftWorkbenchObject()=>AddOccupancy<RailcraftWorkbenchObject>(
        // Match vanilla crafting tables: reserve space without connecting wall meshes.
        Enumerable.Range(0,2).SelectMany(x=>Enumerable.Range(0,2).Select(y=>new BlockOccupancy(new(x,y,0)))).ToList());
}
[Serialized,LocDisplayName("Railworks Workbench"),
 LocDescription("A dedicated workbench for rails, track shapes, switches, supports, stations and mechanical drives. Vehicles and electrical drives are crafted at their profession tables. Connect nearby storage to supply crafting materials."),
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
        Recipes=[MinecartRailRecipes.Make<RailcraftWorkbenchItem>("Railworks Workbench",4,8)];
        ExperienceOnCraft=3;LaborInCalories=CreateLaborInCaloriesValue(120,typeof(BasicEngineeringSkill));
        CraftMinutes=CreateCraftTimeValue(typeof(RailcraftWorkbenchRecipe),3,typeof(BasicEngineeringSkill));
        Initialize(Localizer.DoStr("Railworks Workbench"),typeof(RailcraftWorkbenchRecipe));
        CraftingComponent.AddRecipe(typeof(WainwrightTableObject),this);
    }
}
