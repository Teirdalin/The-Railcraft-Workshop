namespace Eco.Mods.TechTree;

using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Minecarts.Runtime;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

[Serialized,LocDisplayName("Railroad Crossing Gate"),Weight(15000)]
[LocDescription("An automatic road crossing barrier with flashing red warning lights. Place the post beside the road, with its arm reaching across the road and the railway behind the lights. Adjust the arm from 1 to 10 blocks after placement. The gate lowers for approaching trains and stays down until every carriage clears.")]
public sealed class RailCrossingGateItem:WorldObjectItem<RailCrossingGateObject>{}

[Serialized,RequireComponent(typeof(PropertyAuthComponent)),RequireComponent(typeof(RailCrossingComponent))]
public sealed class RailCrossingGateObject:WorldObject,IRepresentsItem
{
    static RailCrossingGateObject()=>AddOccupancyList(typeof(RailCrossingGateObject),new BlockOccupancy(Eco.Shared.Math.Vector3i.Zero,typeof(BuildingWorldObjectBlock)));
    public Type RepresentedItemType=>typeof(RailCrossingGateItem);
    public override LocString DisplayName=>Localizer.DoStr("Railroad Crossing Gate");
}

[RequiresSkill(typeof(BasicEngineeringSkill),3)]
public sealed class RailCrossingGateRecipe:MinecartRailRecipeFamily
{
    public RailCrossingGateRecipe()=>Configure(MinecartRailRecipes.Make<RailCrossingGateItem>("Railroad Crossing Gate",12,8,skillType:typeof(BasicEngineeringSkill)),"Railroad Crossing Gate",typeof(RailCrossingGateRecipe),120,2,skillType:typeof(BasicEngineeringSkill));
}
