namespace Eco.Mods.TechTree;
using Eco.Core.Items;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Skills;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;
using Eco.World.Blocks;
using Eco.World.Water;

[Serialized,Constructed,Solid,BlockTier(1)]
public class CoasterTrackBlock:Block,IRepresentsItem
{public Type RepresentedItemType=>typeof(CoasterTrackItem);}
// These small native construction blocks expose the endpoints of a complete
// placed shape to Eco's hammer. They are not extra free rail segments and are
// removed with their owning shape.
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class CoasterTrackSnapEndBlock:Block,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class CoasterTrackSnapEndR90Block:Block,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class CoasterTrackSnapEndR180Block:Block,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class CoasterTrackSnapEndR270Block:Block,IWaterLoggedBlock{}
[Serialized,LocDisplayName("Roller Coaster Rail"),
 LocDescription("Hammer-build flat rails, sharp corners, four-part slopes, two-part steep slopes and 45° grades with smooth bottom and crest transitions. Building a numbered slope in a line selects its next part and changes height after the last part. Complete loops, banks and large bends are crafted separately at the Railcraft Workbench. Chain rails require an active Rail Chain Drive."),
 MaxStackSize(20),Weight(1800),ResourcePile,Tag("Constructable"),Tier(1),Ecopedia("Blocks","Building Materials",createAsSubPage:true)]
public sealed class CoasterTrackItem:BlockItem<CoasterTrackBlock>
{
    public override bool CanStickToWalls=>false;
    public override Type[] BlockTypes=>[typeof(CoasterTrackStacked1Block),typeof(CoasterTrackStacked2Block),typeof(CoasterTrackStacked3Block),typeof(CoasterTrackStacked4Block)];
}
[RequiresSkill(typeof(BasicEngineeringSkill),3)]
public sealed class CoasterTrackRecipe:MinecartRailRecipeFamily
{public CoasterTrackRecipe()=>Configure(MinecartRailRecipes.Make<CoasterTrackItem>("CoasterTrack",2,4,4),"Roller Coaster Rail",typeof(CoasterTrackRecipe),100,2);}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class CoasterTrackStacked1Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class CoasterTrackStacked2Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class CoasterTrackStacked3Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.FullStack)] public sealed class CoasterTrackStacked4Block:PickupableBlock,IWaterLoggedBlock{}
public static class CoasterTrackNames
{
    public static string Group(string source)
    {
        if(source.Contains("Grade",StringComparison.Ordinal))
        {
            var up=source.Contains("GradeUp",StringComparison.Ordinal);
            var grade=$"{(source.Contains("Chain",StringComparison.Ordinal)?"Chain-Lift ":"")}45° {(up?"Uphill":"Downhill")}";
            return source.EndsWith("Entry",StringComparison.Ordinal)?grade+(up?" - Bottom Transition":" - Crest Transition")
                :source.EndsWith("Exit",StringComparison.Ordinal)?grade+(up?" - Crest Transition":" - Bottom Transition"):grade;
        }
        return source=="CoasterStraight"?"Straight":CoasterNames.Name(source).Replace("Roller Coaster Rail - ","").Replace("Coaster Chain-Lift Rail - ","Chain Lift - ");
    }
    public static string Name(string key)
    {
        var p=CoasterTerrainPath.Find(key);
        return p.Count==1?Group(p.SourceKey):$"{Group(p.SourceKey)} - {p.Section}/{p.Count}";
    }
    public static string Description(string key)
    {
        var p=CoasterTerrainPath.Find(key);
        if(p.SourceKey.Contains("Grade",StringComparison.Ordinal))
            return p.Count==1?"45° coaster grade: rises or falls one block per cell. Use the matching eased transitions to join flat track. Building in a line retains the grade and changes height each cell."
                :"Two-cell smooth pitch transition between flat rail and a 45° coaster grade. Build parts 1 and 2 in a line; the next shape and block height advance automatically. A downhill entry begins one block below the preceding flat rail.";
        return p.Count==1?"Modular coaster rail. Match the rail ends and rotate with the hammer.":$"Building in a line advances through parts 1 to {p.Count} automatically, then changes elevation by one block. Reverse the order of travel to ride downhill.";
    }
}
