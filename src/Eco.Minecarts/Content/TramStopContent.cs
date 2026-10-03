namespace Eco.Mods.TechTree;

using Eco.Core.Items;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Minecarts.Runtime;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;

[Serialized,LocDisplayName("Tram Stop"),LocDescription("Compact street-side boarding stop. Name it and set its dwell time and served lines. Trams brake automatically and repeat their configured stop route."),Weight(6000)]
public sealed class TramStopItem : WorldObjectItem<TramStopObject> { }

[RequiresSkill(typeof(BasicEngineeringSkill),3)]
public sealed class TramStopRecipe : MinecartRailRecipeFamily
{ public TramStopRecipe()=>Configure(MinecartRailRecipes.Make<TramStopItem>("Tram Stop",2,4),"Tram Stop",typeof(TramStopRecipe),60,2); }

[Serialized,RequireComponent(typeof(PropertyAuthComponent)),RequireComponent(typeof(TrainStationComponent))]
[RequireComponent(typeof(TramStopComponent)),RequireComponent(typeof(RailAutomationComponent))]
public sealed class TramStopObject : WorldObject,IRepresentsItem
{
    static TramStopObject()=>AddOccupancyList(typeof(TramStopObject),new BlockOccupancy(Vector3i.Zero,typeof(BuildingWorldObjectBlock)));
    public Type RepresentedItemType=>typeof(TramStopItem);
    public override LocString DisplayName=>Localizer.DoStr("Tram Stop");
    protected override void OnDestroy()
    {
        var cells=WorldOccupancy?.ToArray();var id=ObjectID;base.OnDestroy();
        if(cells!=null)foreach(var cell in cells)
            if(Eco.World.World.GetBlock(cell) is WorldObjectBlock block && block.WorldObjectHandle.Id==id)
                Eco.World.World.DeleteBlock(cell);
    }
}
