namespace Eco.Mods.TechTree;
using Eco.Core.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Minecarts.Runtime;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

[Serialized,LocDisplayName("Minecart Dumping Rail"),Weight(12000),LocDescription("A railway unloading point that tips cargo from vehicles set up for automatic dumping. Link output storage nearby; cargo stays aboard when there is no suitable space. Crafted at the Railworks Workbench.")]
public sealed class MinecartDumpRailItem:WorldObjectItem<MinecartDumpRailObject>{}
[Serialized,RequireComponent(typeof(StandaloneAuthComponent)),RequireComponent(typeof(LinkComponent)),RequireComponent(typeof(InOutLinkedInventoriesComponent)),RequireComponent(typeof(MinecartDumpRailComponent))]
public sealed class MinecartDumpRailObject:WorldObject,IRepresentsItem
{
    static MinecartDumpRailObject()=>AddOccupancyList(typeof(MinecartDumpRailObject),new BlockOccupancy(Eco.Shared.Math.Vector3i.Zero,typeof(BuildingWorldObjectBlock)));
    public Type RepresentedItemType=>typeof(MinecartDumpRailItem);
    public override LocString DisplayName=>Localizer.DoStr("Minecart Dumping Rail");
    protected override void Initialize(){base.Initialize();GetComponent<LinkComponent>().Initialize(12);}
}
[RequiresSkill(typeof(BasicEngineeringSkill),2)]
public sealed class MinecartDumpRailRecipe:MinecartRailRecipeFamily
{
    public MinecartDumpRailRecipe()=>Configure(MinecartRailRecipes.Make<MinecartDumpRailItem>("Minecart Dumping Rail",6,0,hewnLogs:4),"Minecart Dumping Rail",typeof(MinecartDumpRailRecipe),120,1.5f);
}
