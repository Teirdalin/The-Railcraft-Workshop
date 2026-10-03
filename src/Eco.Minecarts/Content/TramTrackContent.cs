namespace Eco.Mods.TechTree;

using System;
using Eco.Core.Items;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Items;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Skills;
using Eco.World.Blocks;
using Eco.World.Water;

[Serialized, Constructed, Solid, BlockTier(1)]
public class TramTrackBlock : Block, IRepresentsItem
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}

[Serialized, LocDisplayName("Tram Rail")]
[LocDescription("Street-scale rail for automated trams. Connect a Tram Cable Drive to mechanical power and the rail network. Hammer-select straight, corner, buffer and four-stage slope forms; road and dirt ramps select matching overlays. Compatible standard rails connect directly but use the tram's onboard fallback fuel.")]
[MaxStackSize(20), Weight(1800), ResourcePile, Tag("Constructable"), Tier(1)]
[Ecopedia("Blocks", "Building Materials", createAsSubPage: true)]
public sealed class TramTrackItem : BlockItem<TramTrackBlock>
{
    public override bool CanStickToWalls => false;
    public override Type[] BlockTypes => [typeof(TramTrackStacked1Block), typeof(TramTrackStacked2Block), typeof(TramTrackStacked3Block), typeof(TramTrackStacked4Block)];
}

[RequiresSkill(typeof(BasicEngineeringSkill), 2)]
public sealed class TramTrackRecipe : MinecartRailRecipeFamily
{
    public TramTrackRecipe() => this.Configure(
        MinecartRailRecipes.Make<TramTrackItem>("Tram Rail", 2, 2, 4),
        "Tram Rail", typeof(TramTrackRecipe), 60, 1);
}

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class TramTrackStacked1Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class TramTrackStacked2Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class TramTrackStacked3Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.FullStack)]
public sealed class TramTrackStacked4Block : PickupableBlock, IWaterLoggedBlock { }

public sealed class TramTrackFormGroup : FormGroup
{
    public override string Name => "TramTrack";
    public override LocString DisplayName => Localizer.DoStr("Tram Rail");
    public override LocString DisplayDescription => Localizer.DoStr("Street-scale powered tram rail. Compatible standard rail connects directly; only tram rail conducts cable-drive power.");
    public override int SortOrder => 102;
}

public sealed class TramTrackStraightFormType : FormType
{
    public override string Name => "TramTrackStraight";
    public override LocString DisplayName => Localizer.DoStr("Straight");
    public override LocString DisplayDescription => Localizer.DoStr("One-block straight track.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 1;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(TramTrackStraightBlock), typeof(TramTrackStraight90Block), typeof(TramTrackStraight180Block), typeof(TramTrackStraight270Block))]
[IsForm(typeof(TramTrackStraightFormType), typeof(TramTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackStraightBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackStraight90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackStraight180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackStraight270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}

public sealed class TramTrackBendFormType : FormType
{
    public override string Name => "TramTrackBend";
    public override LocString DisplayName => Localizer.DoStr("Bend");
    public override LocString DisplayDescription => Localizer.DoStr("Compact ninety-degree bend. Use only at low speed.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 2;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(TramTrackBendBlock), typeof(TramTrackBend90Block), typeof(TramTrackBend180Block), typeof(TramTrackBend270Block))]
[IsForm(typeof(TramTrackBendFormType), typeof(TramTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackBendBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackBend90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackBend180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackBend270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}

public sealed class TramTrackStopperFormType : FormType
{
    public override string Name => "TramTrackStopper";
    public override LocString DisplayName => Localizer.DoStr("Stopper");
    public override LocString DisplayDescription => Localizer.DoStr("End-of-line buffer stop.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 3;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(TramTrackStopperBlock), typeof(TramTrackStopper90Block), typeof(TramTrackStopper180Block), typeof(TramTrackStopper270Block))]
[IsForm(typeof(TramTrackStopperFormType), typeof(TramTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackStopperBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackStopper90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackStopper180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackStopper270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}

public sealed class TramTrackCrossingFormType : FormType
{
    public override string Name=>"TramTrackCrossing";
    public override LocString DisplayName=>Localizer.DoStr("Crossing");
    public override LocString DisplayDescription=>Localizer.DoStr("Two independent straight tram paths crossing at street level. Both conduct mechanical network power.");
    public override Type GroupType=>typeof(TramTrackFormGroup);
    public override int SortOrder=>8;
    public override int MinTier=>1;
}
[RotatedVariants(typeof(TramTrackCrossingBlock),typeof(TramTrackCrossing90Block),typeof(TramTrackCrossing180Block),typeof(TramTrackCrossing270Block))]
[IsForm(typeof(TramTrackCrossingFormType),typeof(TramTrackItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class TramTrackCrossingBlock:Block,IRepresentsItem,IWaterLoggedBlock {public Type RepresentedItemType=>typeof(TramTrackItem);}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class TramTrackCrossing90Block:Block,IRepresentsItem,IWaterLoggedBlock {public Type RepresentedItemType=>typeof(TramTrackItem);}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class TramTrackCrossing180Block:Block,IRepresentsItem,IWaterLoggedBlock {public Type RepresentedItemType=>typeof(TramTrackItem);}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class TramTrackCrossing270Block:Block,IRepresentsItem,IWaterLoggedBlock {public Type RepresentedItemType=>typeof(TramTrackItem);}

[NextRamp(typeof(TramTrackSlope2FormType), 0)]
public sealed class TramTrackSlope1FormType : FormType
{
    public override string Name => "TramTrackSlope1";
    public override LocString DisplayName => Localizer.DoStr("Slope 1/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 1 of 4.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 4;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(TramTrackSlope1270Block), typeof(TramTrackSlope1Block), typeof(TramTrackSlope190Block), typeof(TramTrackSlope1180Block))]
[IsForm(typeof(TramTrackSlope1FormType), typeof(TramTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope1Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope190Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope1180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope1270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}

[NextRamp(typeof(TramTrackSlope3FormType), 0)]
public sealed class TramTrackSlope2FormType : FormType
{
    public override string Name => "TramTrackSlope2";
    public override LocString DisplayName => Localizer.DoStr("Slope 2/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 2 of 4.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 5;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(TramTrackSlope2270Block), typeof(TramTrackSlope2Block), typeof(TramTrackSlope290Block), typeof(TramTrackSlope2180Block))]
[IsForm(typeof(TramTrackSlope2FormType), typeof(TramTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope2Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope290Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope2180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope2270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}

[NextRamp(typeof(TramTrackSlope4FormType), 0)]
public sealed class TramTrackSlope3FormType : FormType
{
    public override string Name => "TramTrackSlope3";
    public override LocString DisplayName => Localizer.DoStr("Slope 3/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 3 of 4.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 6;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(TramTrackSlope3270Block), typeof(TramTrackSlope3Block), typeof(TramTrackSlope390Block), typeof(TramTrackSlope3180Block))]
[IsForm(typeof(TramTrackSlope3FormType), typeof(TramTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope3Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope390Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope3180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope3270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}

[NextRamp(typeof(TramTrackSlope1FormType), 1)]
public sealed class TramTrackSlope4FormType : FormType
{
    public override string Name => "TramTrackSlope4";
    public override LocString DisplayName => Localizer.DoStr("Slope 4/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 4 of 4.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 7;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(TramTrackSlope4270Block), typeof(TramTrackSlope4Block), typeof(TramTrackSlope490Block), typeof(TramTrackSlope4180Block))]
[IsForm(typeof(TramTrackSlope4FormType), typeof(TramTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope4Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope490Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope4180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackSlope4270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
