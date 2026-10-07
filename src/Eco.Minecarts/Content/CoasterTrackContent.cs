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
 LocDescription("Hammer-build flat rails, sharp corners, four-part slopes, two-part steep slopes and 45Â° grades with smooth bottom and crest transitions. Building a numbered slope in a line selects its next part and changes height after the last part. Complete loops, banks and large bends are crafted separately at the Railworks Workbench. Chain rails require an active Rail Chain Drive."),
 MaxStackSize(20),Weight(1800),ResourcePile,Tag("Constructable"),Tier(1),Ecopedia("Blocks","Building Materials",createAsSubPage:true)]
public sealed class CoasterTrackItem:BlockItem<CoasterTrackBlock>
{
    public override bool CanStickToWalls=>false;
    public override Type[] BlockTypes=>[typeof(CoasterTrackStacked1Block),typeof(CoasterTrackStacked2Block),typeof(CoasterTrackStacked3Block),typeof(CoasterTrackStacked4Block)];
}
[RequiresSkill(typeof(IndustrySkill),3)]
public sealed class CoasterTrackRecipe:MinecartRailRecipeFamily
{public CoasterTrackRecipe()=>Configure(MinecartRailRecipes.Make<CoasterTrackItem>("CoasterTrack",3,4,4,fixedMaterials:true, skillType:typeof(IndustrySkill),metalType:typeof(SteelBarItem)),"Roller Coaster Rail",typeof(CoasterTrackRecipe),120,2.5f, skillType:typeof(IndustrySkill));}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class CoasterTrackStacked1Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class CoasterTrackStacked2Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class CoasterTrackStacked3Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.FullStack)] public sealed class CoasterTrackStacked4Block:PickupableBlock,IWaterLoggedBlock{}
public static class CoasterTrackNames
{
    // The same unpowered mesh may be entered from either end. Keep both
    // keys for native NextRamp order and old plans, but show one shape family.
    public static string? BlueprintShapeFamily(string key)
    {
        if(!key.StartsWith("CoasterTrack",StringComparison.Ordinal)||key.StartsWith("CoasterTrackChain",StringComparison.Ordinal))return null;
        var section=CoasterTerrainPath.All.FirstOrDefault(p=>p.Key==key);
        if(section==null)return null;
        var source=section.SourceKey;var phase=section.Section;
        if(source is "CoasterSlopeUp" or "CoasterSlopeDown")
            return $"Gentle Slope - Height Slice {(source.EndsWith("Down")?5-phase:phase)}/4";
        if(source is "CoasterSteepUp" or "CoasterSteepDown")
            return $"Steep Slope - Height Slice {(source.EndsWith("Down")?3-phase:phase)}/2";
        if(source is "CoasterGradeUp" or "CoasterGradeDown")return "45° Slope";
        if(source is "CoasterGradeUpEntry" or "CoasterGradeDownExit")
            return $"45° Bottom Transition - {(source.Contains("Down")?3-phase:phase)}/2";
        if(source is "CoasterGradeUpExit" or "CoasterGradeDownEntry")
            return $"45° Crest Transition - {(source.Contains("Down")?3-phase:phase)}/2";
        return source switch {
            "CoasterGradeCompactUpEntry" or "CoasterGradeCompactDownExit"=>"Single-Block Bottom Transition",
            "CoasterGradeCompactUpExit" or "CoasterGradeCompactDownEntry"=>"Single-Block Crest Transition",
            _=>null
        };
    }
    public static bool IsAvailable(string key)=>!key.StartsWith("CoasterTrackChain",StringComparison.Ordinal)||!key.Contains("Down",StringComparison.Ordinal);
    public static string Group(string source)
    {
        if(source=="CoasterChainBrake")return "Braking Chain - Station Approach";
        if(source.Contains("Compact",StringComparison.Ordinal))
        {
            var shape=source[(source.IndexOf("Compact",StringComparison.Ordinal)+7)..] switch
            {"UpEntry"=>"Flat to Uphill", "UpExit"=>"Uphill to Flat", "DownEntry"=>"Flat to Downhill", "DownExit"=>"Downhill to Flat", "Crest"=>"Crest", "Dip"=>"Dip", _=>source};
            return (source.Contains("Chain",StringComparison.Ordinal)?"Chain-Lift ":"")+"Single-Block "+shape;
        }
        if(source.Contains("Grade",StringComparison.Ordinal))
        {
            var up=source.Contains("GradeUp",StringComparison.Ordinal);
            var grade=$"{(source.Contains("Chain",StringComparison.Ordinal)?"Chain-Lift ":"")}45Â° {(up?"Uphill":"Downhill")}";
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
        if(p.SourceKey=="CoasterChainBrake")return "High-friction station approach rail. Gradually slows fast coasters toward the connected chain drive's speed, then pulls them forward like normal chain rail. Braking works without power; connect a drive for onward movement. Use a long enough approach for the incoming speed.";
        if(p.SourceKey.Contains("Compact",StringComparison.Ordinal))
            return "One-block curve with sockets matching 45Â° grades. Crest joins uphill to downhill; dip joins downhill to uphill. Flat transitions change height by one block and are tighter than the two-block transitions. Rotate with the hammer; keep the full cart envelope clear.";
        if(p.SourceKey.Contains("Grade",StringComparison.Ordinal))
            return p.Count==1?"45Â° coaster grade: rises or falls one block per cell. Use the matching eased transitions to join flat track. Building in a line retains the grade and changes height each cell."
                :"Two-cell smooth pitch transition between flat rail and a 45Â° coaster grade. Build parts 1 and 2 in a line; the next shape and block height advance automatically. A downhill entry begins one block below the preceding flat rail.";
        return p.Count==1?"Modular coaster rail. Match the rail ends and rotate with the hammer.":$"Building in a line advances through parts 1 to {p.Count} automatically, then changes elevation by one block. Reverse the order of travel to ride downhill.";
    }
}
