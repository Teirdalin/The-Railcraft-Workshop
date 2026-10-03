namespace Eco.Mods.TechTree;

using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Runtime;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;

[Serialized, RequireComponent(typeof(PropertyAuthComponent)), RequireComponent(typeof(IndustrialRailComponent))]
public abstract class IndustrialRailObject : WorldObject, IRepresentsItem
{
    public IndustrialTrackDefinition Definition => IndustrialTrackDefinition.Find(GetType().Name.Replace("Object", ""));
    public Type RepresentedItemType => GetType().Assembly.GetType("Eco.Mods.TechTree." + Definition.Key + "Item")!;
    public override LocString DisplayName => Localizer.DoStr(Definition.Name + " (3 wide)");
    protected static void Occupy<T>(string key) where T : IndustrialRailObject
    {
        var d = IndustrialTrackDefinition.Find(key);
        var cells = d.Bend ? Enumerable.Range(-3, 7).SelectMany(x => Enumerable.Range(-3, 7).Select(z => new Vector3i(x,0,z)))
            : Enumerable.Range(-1,3).Select(x => new Vector3i(x,0,0));
        // Reserve headroom wherever a raised quarter-slope crosses the next cell.
        var occupied=cells.Select(c => new BlockOccupancy(c,typeof(BuildingWorldObjectBlock))).ToList();
        if(d.Shape is "Slope3" or "Slope4") occupied.AddRange(Enumerable.Range(-1,3).Select(x => new BlockOccupancy(new Vector3i(x,1,0),typeof(BuildingWorldObjectBlock))));
        AddOccupancy<T>(occupied);
    }
}
public abstract class IndustrialRailRecipe<T> : MinecartRailRecipeFamily where T : Item,new()
{
    protected IndustrialRailRecipe()
    {
        var d=IndustrialTrackDefinition.Find(typeof(T).Name.Replace("Item", ""));
        // Three running lanes: charge the same approximate half-bar per lane
        // metre as standard rail. A stop is a passive structural bumper.
        var bars=d.Bend?30:3;
        var boards=d.Bend?60:d.Shape=="Stopper"?8:6;
        var gears=d.Chain&&d.Shape!="Stopper"?1:0;
        Configure(MinecartRailRecipes.Make<T>(d.Name,bars,boards,2,woodenGears:gears),
            d.Name,GetType(),d.Bend ? 600 : 150,d.Bend ? 12 : 4);
    }
}
[Serialized, LocDisplayName("Industrial Straight (3 wide)")]
public sealed class IndustrialTrackStraightItem : WorldObjectItem<IndustrialTrackStraightObject> { }
[Serialized] public sealed class IndustrialTrackStraightObject : IndustrialRailObject
{ static IndustrialTrackStraightObject() => Occupy<IndustrialTrackStraightObject>("IndustrialTrackStraight"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackStraightRecipe : IndustrialRailRecipe<IndustrialTrackStraightItem> { }
[Serialized, LocDisplayName("Industrial Right Bend (3 wide)")]
public sealed class IndustrialTrackBendItem : WorldObjectItem<IndustrialTrackBendObject> { }
[Serialized] public sealed class IndustrialTrackBendObject : IndustrialRailObject
{ static IndustrialTrackBendObject() => Occupy<IndustrialTrackBendObject>("IndustrialTrackBend"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackBendRecipe : IndustrialRailRecipe<IndustrialTrackBendItem> { }
[Serialized, LocDisplayName("Industrial Left Bend (3 wide)")]
public sealed class IndustrialTrackBendLeftItem : WorldObjectItem<IndustrialTrackBendLeftObject> { }
[Serialized] public sealed class IndustrialTrackBendLeftObject : IndustrialRailObject
{ static IndustrialTrackBendLeftObject() => Occupy<IndustrialTrackBendLeftObject>("IndustrialTrackBendLeft"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackBendLeftRecipe : IndustrialRailRecipe<IndustrialTrackBendLeftItem> { }
[Serialized, LocDisplayName("Industrial Stopper (3 wide)")]
public sealed class IndustrialTrackStopperItem : WorldObjectItem<IndustrialTrackStopperObject> { }
[Serialized] public sealed class IndustrialTrackStopperObject : IndustrialRailObject
{ static IndustrialTrackStopperObject() => Occupy<IndustrialTrackStopperObject>("IndustrialTrackStopper"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackStopperRecipe : IndustrialRailRecipe<IndustrialTrackStopperItem> { }
[Serialized, LocDisplayName("Industrial Slope 1 (3 wide)")]
public sealed class IndustrialTrackSlope1Item : WorldObjectItem<IndustrialTrackSlope1Object> { }
[Serialized] public sealed class IndustrialTrackSlope1Object : IndustrialRailObject
{ static IndustrialTrackSlope1Object() => Occupy<IndustrialTrackSlope1Object>("IndustrialTrackSlope1"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackSlope1Recipe : IndustrialRailRecipe<IndustrialTrackSlope1Item> { }
[Serialized, LocDisplayName("Industrial Slope 2 (3 wide)")]
public sealed class IndustrialTrackSlope2Item : WorldObjectItem<IndustrialTrackSlope2Object> { }
[Serialized] public sealed class IndustrialTrackSlope2Object : IndustrialRailObject
{ static IndustrialTrackSlope2Object() => Occupy<IndustrialTrackSlope2Object>("IndustrialTrackSlope2"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackSlope2Recipe : IndustrialRailRecipe<IndustrialTrackSlope2Item> { }
[Serialized, LocDisplayName("Industrial Slope 3 (3 wide)")]
public sealed class IndustrialTrackSlope3Item : WorldObjectItem<IndustrialTrackSlope3Object> { }
[Serialized] public sealed class IndustrialTrackSlope3Object : IndustrialRailObject
{ static IndustrialTrackSlope3Object() => Occupy<IndustrialTrackSlope3Object>("IndustrialTrackSlope3"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackSlope3Recipe : IndustrialRailRecipe<IndustrialTrackSlope3Item> { }
[Serialized, LocDisplayName("Industrial Slope 4 (3 wide)")]
public sealed class IndustrialTrackSlope4Item : WorldObjectItem<IndustrialTrackSlope4Object> { }
[Serialized] public sealed class IndustrialTrackSlope4Object : IndustrialRailObject
{ static IndustrialTrackSlope4Object() => Occupy<IndustrialTrackSlope4Object>("IndustrialTrackSlope4"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackSlope4Recipe : IndustrialRailRecipe<IndustrialTrackSlope4Item> { }
[Serialized, LocDisplayName("Industrial Ramp Top 1 (3 wide)")]
public sealed class IndustrialTrackRampTop1Item : WorldObjectItem<IndustrialTrackRampTop1Object> { }
[Serialized] public sealed class IndustrialTrackRampTop1Object : IndustrialRailObject
{ static IndustrialTrackRampTop1Object() => Occupy<IndustrialTrackRampTop1Object>("IndustrialTrackRampTop1"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackRampTop1Recipe : IndustrialRailRecipe<IndustrialTrackRampTop1Item> { }
[Serialized, LocDisplayName("Industrial Ramp Top 2 (3 wide)")]
public sealed class IndustrialTrackRampTop2Item : WorldObjectItem<IndustrialTrackRampTop2Object> { }
[Serialized] public sealed class IndustrialTrackRampTop2Object : IndustrialRailObject
{ static IndustrialTrackRampTop2Object() => Occupy<IndustrialTrackRampTop2Object>("IndustrialTrackRampTop2"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackRampTop2Recipe : IndustrialRailRecipe<IndustrialTrackRampTop2Item> { }
[Serialized, LocDisplayName("Industrial Ramp Top 3 (3 wide)")]
public sealed class IndustrialTrackRampTop3Item : WorldObjectItem<IndustrialTrackRampTop3Object> { }
[Serialized] public sealed class IndustrialTrackRampTop3Object : IndustrialRailObject
{ static IndustrialTrackRampTop3Object() => Occupy<IndustrialTrackRampTop3Object>("IndustrialTrackRampTop3"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackRampTop3Recipe : IndustrialRailRecipe<IndustrialTrackRampTop3Item> { }
[Serialized, LocDisplayName("Industrial Ramp Top 4 (3 wide)")]
public sealed class IndustrialTrackRampTop4Item : WorldObjectItem<IndustrialTrackRampTop4Object> { }
[Serialized] public sealed class IndustrialTrackRampTop4Object : IndustrialRailObject
{ static IndustrialTrackRampTop4Object() => Occupy<IndustrialTrackRampTop4Object>("IndustrialTrackRampTop4"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialTrackRampTop4Recipe : IndustrialRailRecipe<IndustrialTrackRampTop4Item> { }
[Serialized, LocDisplayName("Industrial Chain Straight (3 wide)")]
public sealed class IndustrialChainStraightItem : WorldObjectItem<IndustrialChainStraightObject> { }
[Serialized] public sealed class IndustrialChainStraightObject : IndustrialRailObject
{ static IndustrialChainStraightObject() => Occupy<IndustrialChainStraightObject>("IndustrialChainStraight"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainStraightRecipe : IndustrialRailRecipe<IndustrialChainStraightItem> { }
[Serialized, LocDisplayName("Industrial Chain Right Bend (3 wide)")]
public sealed class IndustrialChainBendItem : WorldObjectItem<IndustrialChainBendObject> { }
[Serialized] public sealed class IndustrialChainBendObject : IndustrialRailObject
{ static IndustrialChainBendObject() => Occupy<IndustrialChainBendObject>("IndustrialChainBend"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainBendRecipe : IndustrialRailRecipe<IndustrialChainBendItem> { }
[Serialized, LocDisplayName("Industrial Chain Left Bend (3 wide)")]
public sealed class IndustrialChainBendLeftItem : WorldObjectItem<IndustrialChainBendLeftObject> { }
[Serialized] public sealed class IndustrialChainBendLeftObject : IndustrialRailObject
{ static IndustrialChainBendLeftObject() => Occupy<IndustrialChainBendLeftObject>("IndustrialChainBendLeft"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainBendLeftRecipe : IndustrialRailRecipe<IndustrialChainBendLeftItem> { }
[Serialized, LocDisplayName("Industrial Chain Stopper (3 wide)")]
public sealed class IndustrialChainStopperItem : WorldObjectItem<IndustrialChainStopperObject> { }
[Serialized] public sealed class IndustrialChainStopperObject : IndustrialRailObject
{ static IndustrialChainStopperObject() => Occupy<IndustrialChainStopperObject>("IndustrialChainStopper"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainStopperRecipe : IndustrialRailRecipe<IndustrialChainStopperItem> { }
[Serialized, LocDisplayName("Industrial Chain Slope 1 (3 wide)")]
public sealed class IndustrialChainSlope1Item : WorldObjectItem<IndustrialChainSlope1Object> { }
[Serialized] public sealed class IndustrialChainSlope1Object : IndustrialRailObject
{ static IndustrialChainSlope1Object() => Occupy<IndustrialChainSlope1Object>("IndustrialChainSlope1"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainSlope1Recipe : IndustrialRailRecipe<IndustrialChainSlope1Item> { }
[Serialized, LocDisplayName("Industrial Chain Slope 2 (3 wide)")]
public sealed class IndustrialChainSlope2Item : WorldObjectItem<IndustrialChainSlope2Object> { }
[Serialized] public sealed class IndustrialChainSlope2Object : IndustrialRailObject
{ static IndustrialChainSlope2Object() => Occupy<IndustrialChainSlope2Object>("IndustrialChainSlope2"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainSlope2Recipe : IndustrialRailRecipe<IndustrialChainSlope2Item> { }
[Serialized, LocDisplayName("Industrial Chain Slope 3 (3 wide)")]
public sealed class IndustrialChainSlope3Item : WorldObjectItem<IndustrialChainSlope3Object> { }
[Serialized] public sealed class IndustrialChainSlope3Object : IndustrialRailObject
{ static IndustrialChainSlope3Object() => Occupy<IndustrialChainSlope3Object>("IndustrialChainSlope3"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainSlope3Recipe : IndustrialRailRecipe<IndustrialChainSlope3Item> { }
[Serialized, LocDisplayName("Industrial Chain Slope 4 (3 wide)")]
public sealed class IndustrialChainSlope4Item : WorldObjectItem<IndustrialChainSlope4Object> { }
[Serialized] public sealed class IndustrialChainSlope4Object : IndustrialRailObject
{ static IndustrialChainSlope4Object() => Occupy<IndustrialChainSlope4Object>("IndustrialChainSlope4"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainSlope4Recipe : IndustrialRailRecipe<IndustrialChainSlope4Item> { }
[Serialized, LocDisplayName("Industrial Chain Ramp Top 1 (3 wide)")]
public sealed class IndustrialChainRampTop1Item : WorldObjectItem<IndustrialChainRampTop1Object> { }
[Serialized] public sealed class IndustrialChainRampTop1Object : IndustrialRailObject
{ static IndustrialChainRampTop1Object() => Occupy<IndustrialChainRampTop1Object>("IndustrialChainRampTop1"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainRampTop1Recipe : IndustrialRailRecipe<IndustrialChainRampTop1Item> { }
[Serialized, LocDisplayName("Industrial Chain Ramp Top 2 (3 wide)")]
public sealed class IndustrialChainRampTop2Item : WorldObjectItem<IndustrialChainRampTop2Object> { }
[Serialized] public sealed class IndustrialChainRampTop2Object : IndustrialRailObject
{ static IndustrialChainRampTop2Object() => Occupy<IndustrialChainRampTop2Object>("IndustrialChainRampTop2"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainRampTop2Recipe : IndustrialRailRecipe<IndustrialChainRampTop2Item> { }
[Serialized, LocDisplayName("Industrial Chain Ramp Top 3 (3 wide)")]
public sealed class IndustrialChainRampTop3Item : WorldObjectItem<IndustrialChainRampTop3Object> { }
[Serialized] public sealed class IndustrialChainRampTop3Object : IndustrialRailObject
{ static IndustrialChainRampTop3Object() => Occupy<IndustrialChainRampTop3Object>("IndustrialChainRampTop3"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainRampTop3Recipe : IndustrialRailRecipe<IndustrialChainRampTop3Item> { }
[Serialized, LocDisplayName("Industrial Chain Ramp Top 4 (3 wide)")]
public sealed class IndustrialChainRampTop4Item : WorldObjectItem<IndustrialChainRampTop4Object> { }
[Serialized] public sealed class IndustrialChainRampTop4Object : IndustrialRailObject
{ static IndustrialChainRampTop4Object() => Occupy<IndustrialChainRampTop4Object>("IndustrialChainRampTop4"); }
[RequiresSkill(typeof(BasicEngineeringSkill),3)] public sealed class IndustrialChainRampTop4Recipe : IndustrialRailRecipe<IndustrialChainRampTop4Item> { }
