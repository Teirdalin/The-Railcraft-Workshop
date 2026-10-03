namespace Eco.Mods.TechTree;

using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Runtime;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

[Serialized,RequireComponent(typeof(PropertyAuthComponent)),RequireComponent(typeof(RailSwitchComponent))]
public abstract class RailSwitchObject : WorldObject,IRepresentsItem
{
    public RailSwitchDefinition Definition=>RailSwitchDefinition.Find(GetType().Name.Replace("Object",""));
    public override LocString DisplayName=>Localizer.DoStr(Definition.Name);
    public Type RepresentedItemType=>GetType().Assembly.GetType("Eco.Mods.TechTree."+Definition.Key+"Item")!;
    protected override void OnDestroy()
    {
        var cells=WorldOccupancy?.ToArray();var id=ObjectID;base.OnDestroy();
        if(cells!=null)foreach(var cell in cells)if(Eco.World.World.GetBlock(cell) is WorldObjectBlock block && block.WorldObjectHandle.Id==id)Eco.World.World.DeleteBlock(cell);
    }
    protected static void Occupy<T>(string key) where T:RailSwitchObject
    {
        var half=RailSwitchDefinition.Find(key).Footprint/2;
        AddOccupancy<T>(Enumerable.Range(-half,half*2+1).SelectMany(x=>Enumerable.Range(-half,half*2+1).Select(z=>new BlockOccupancy(new Eco.Shared.Math.Vector3i(x,0,z),typeof(BuildingWorldObjectBlock)))).ToList());
    }
}
public abstract class RailSwitchRecipe<T> : MinecartRailRecipeFamily where T:Item,new()
{
    protected RailSwitchRecipe() {var d=RailSwitchDefinition.Find(typeof(T).Name.Replace("Item","")); Configure(MinecartRailRecipes.Make<T>(d.Name,2*d.Footprint,d.Tram||d.Industrial?2*d.Footprint:0,2,hewnLogs:d.Tram||d.Industrial?0:2*d.Footprint,woodenGears:2),d.Name,GetType(),60*d.Footprint,2*d.Footprint);}
}
[Serialized,LocDisplayName("Standard Rail Left Switch")] public sealed class RailSwitchLeftItem:WorldObjectItem<RailSwitchLeftObject>{}
[Serialized] public sealed class RailSwitchLeftObject:RailSwitchObject {static RailSwitchLeftObject()=>Occupy<RailSwitchLeftObject>("RailSwitchLeft");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class RailSwitchLeftRecipe:RailSwitchRecipe<RailSwitchLeftItem>{}
[Serialized,LocDisplayName("Standard Rail Right Switch")] public sealed class RailSwitchRightItem:WorldObjectItem<RailSwitchRightObject>{}
[Serialized] public sealed class RailSwitchRightObject:RailSwitchObject {static RailSwitchRightObject()=>Occupy<RailSwitchRightObject>("RailSwitchRight");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class RailSwitchRightRecipe:RailSwitchRecipe<RailSwitchRightItem>{}
[Serialized,LocDisplayName("Standard Rail Three-Way Switch")] public sealed class RailSwitchThreeWayItem:WorldObjectItem<RailSwitchThreeWayObject>{}
[Serialized] public sealed class RailSwitchThreeWayObject:RailSwitchObject {static RailSwitchThreeWayObject()=>Occupy<RailSwitchThreeWayObject>("RailSwitchThreeWay");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class RailSwitchThreeWayRecipe:RailSwitchRecipe<RailSwitchThreeWayItem>{}
[Serialized,LocDisplayName("Standard Rail Wide-Turn Left Switch")] public sealed class WideRailSwitchLeftItem:WorldObjectItem<WideRailSwitchLeftObject>{}
[Serialized] public sealed class WideRailSwitchLeftObject:RailSwitchObject {static WideRailSwitchLeftObject()=>Occupy<WideRailSwitchLeftObject>("WideRailSwitchLeft");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class WideRailSwitchLeftRecipe:RailSwitchRecipe<WideRailSwitchLeftItem>{}
[Serialized,LocDisplayName("Standard Rail Wide-Turn Right Switch")] public sealed class WideRailSwitchRightItem:WorldObjectItem<WideRailSwitchRightObject>{}
[Serialized] public sealed class WideRailSwitchRightObject:RailSwitchObject {static WideRailSwitchRightObject()=>Occupy<WideRailSwitchRightObject>("WideRailSwitchRight");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class WideRailSwitchRightRecipe:RailSwitchRecipe<WideRailSwitchRightItem>{}
[Serialized,LocDisplayName("Standard Rail Wide-Turn Three-Way Switch")] public sealed class WideRailSwitchThreeWayItem:WorldObjectItem<WideRailSwitchThreeWayObject>{}
[Serialized] public sealed class WideRailSwitchThreeWayObject:RailSwitchObject {static WideRailSwitchThreeWayObject()=>Occupy<WideRailSwitchThreeWayObject>("WideRailSwitchThreeWay");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class WideRailSwitchThreeWayRecipe:RailSwitchRecipe<WideRailSwitchThreeWayItem>{}
[Serialized,LocDisplayName("Tram Rail Left Switch")] public sealed class TramRailSwitchLeftItem:WorldObjectItem<TramRailSwitchLeftObject>{}
[Serialized] public sealed class TramRailSwitchLeftObject:RailSwitchObject {static TramRailSwitchLeftObject()=>Occupy<TramRailSwitchLeftObject>("TramRailSwitchLeft");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class TramRailSwitchLeftRecipe:RailSwitchRecipe<TramRailSwitchLeftItem>{}
[Serialized,LocDisplayName("Tram Rail Right Switch")] public sealed class TramRailSwitchRightItem:WorldObjectItem<TramRailSwitchRightObject>{}
[Serialized] public sealed class TramRailSwitchRightObject:RailSwitchObject {static TramRailSwitchRightObject()=>Occupy<TramRailSwitchRightObject>("TramRailSwitchRight");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class TramRailSwitchRightRecipe:RailSwitchRecipe<TramRailSwitchRightItem>{}
[Serialized,LocDisplayName("Tram Rail Three-Way Switch")] public sealed class TramRailSwitchThreeWayItem:WorldObjectItem<TramRailSwitchThreeWayObject>{}
[Serialized] public sealed class TramRailSwitchThreeWayObject:RailSwitchObject {static TramRailSwitchThreeWayObject()=>Occupy<TramRailSwitchThreeWayObject>("TramRailSwitchThreeWay");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class TramRailSwitchThreeWayRecipe:RailSwitchRecipe<TramRailSwitchThreeWayItem>{}
[Serialized,LocDisplayName("Tram Rail Wide-Turn Left Switch")] public sealed class TramWideRailSwitchLeftItem:WorldObjectItem<TramWideRailSwitchLeftObject>{}
[Serialized] public sealed class TramWideRailSwitchLeftObject:RailSwitchObject {static TramWideRailSwitchLeftObject()=>Occupy<TramWideRailSwitchLeftObject>("TramWideRailSwitchLeft");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class TramWideRailSwitchLeftRecipe:RailSwitchRecipe<TramWideRailSwitchLeftItem>{}
[Serialized,LocDisplayName("Tram Rail Wide-Turn Right Switch")] public sealed class TramWideRailSwitchRightItem:WorldObjectItem<TramWideRailSwitchRightObject>{}
[Serialized] public sealed class TramWideRailSwitchRightObject:RailSwitchObject {static TramWideRailSwitchRightObject()=>Occupy<TramWideRailSwitchRightObject>("TramWideRailSwitchRight");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class TramWideRailSwitchRightRecipe:RailSwitchRecipe<TramWideRailSwitchRightItem>{}
[Serialized,LocDisplayName("Tram Rail Wide-Turn Three-Way Switch")] public sealed class TramWideRailSwitchThreeWayItem:WorldObjectItem<TramWideRailSwitchThreeWayObject>{}
[Serialized] public sealed class TramWideRailSwitchThreeWayObject:RailSwitchObject {static TramWideRailSwitchThreeWayObject()=>Occupy<TramWideRailSwitchThreeWayObject>("TramWideRailSwitchThreeWay");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class TramWideRailSwitchThreeWayRecipe:RailSwitchRecipe<TramWideRailSwitchThreeWayItem>{}
#if INDUSTRIAL_TRACKS
[Serialized,LocDisplayName("Industrial Railway Left Switch")] public sealed class IndustrialRailSwitchLeftItem:WorldObjectItem<IndustrialRailSwitchLeftObject>{}
[Serialized] public sealed class IndustrialRailSwitchLeftObject:RailSwitchObject {static IndustrialRailSwitchLeftObject()=>Occupy<IndustrialRailSwitchLeftObject>("IndustrialRailSwitchLeft");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialRailSwitchLeftRecipe:RailSwitchRecipe<IndustrialRailSwitchLeftItem>{}
[Serialized,LocDisplayName("Industrial Railway Right Switch")] public sealed class IndustrialRailSwitchRightItem:WorldObjectItem<IndustrialRailSwitchRightObject>{}
[Serialized] public sealed class IndustrialRailSwitchRightObject:RailSwitchObject {static IndustrialRailSwitchRightObject()=>Occupy<IndustrialRailSwitchRightObject>("IndustrialRailSwitchRight");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialRailSwitchRightRecipe:RailSwitchRecipe<IndustrialRailSwitchRightItem>{}
[Serialized,LocDisplayName("Industrial Railway Three-Way Switch")] public sealed class IndustrialRailSwitchThreeWayItem:WorldObjectItem<IndustrialRailSwitchThreeWayObject>{}
[Serialized] public sealed class IndustrialRailSwitchThreeWayObject:RailSwitchObject {static IndustrialRailSwitchThreeWayObject()=>Occupy<IndustrialRailSwitchThreeWayObject>("IndustrialRailSwitchThreeWay");}
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialRailSwitchThreeWayRecipe:RailSwitchRecipe<IndustrialRailSwitchThreeWayItem>{}
#endif
