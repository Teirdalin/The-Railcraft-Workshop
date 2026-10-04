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
public class MinecartTrackBlock : Block, IRepresentsItem
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}

[Serialized, LocDisplayName("Standard Rail")]
[LocDescription("Shared steel-wheeled rail family for minecarts, handcars and locomotives. Carry it and use a hammer to select shapes. Long rolling stock requires broad-radius turns, not a separate gauge. Slope stages 1–4 form a one-block rise; matching road or dirt ramps automatically select overlays. Coaster carts use Roller Coaster Rail instead.")]
[MaxStackSize(20), Weight(1800), ResourcePile, Tag("Constructable"), Tier(1)]
[Ecopedia("Blocks", "Building Materials", createAsSubPage: true)]
public sealed class MinecartTrackItem : BlockItem<MinecartTrackBlock>
{
    public override bool CanStickToWalls => false;
    public override Type[] BlockTypes => [typeof(MinecartTrackStacked1Block), typeof(MinecartTrackStacked2Block), typeof(MinecartTrackStacked3Block), typeof(MinecartTrackStacked4Block)];
}

[RequiresSkill(typeof(BasicEngineeringSkill), 2)]
public sealed class MinecartTrackRecipe : MinecartRailRecipeFamily
{
    public MinecartTrackRecipe() => this.Configure(
        MinecartRailRecipes.Make<MinecartTrackItem>("Standard Rail", 3, 0, 4, hewnLogs: 2, fixedMaterials: true),
        "Standard Rail", typeof(MinecartTrackRecipe), 80, 1.25f);
}

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class MinecartTrackStacked1Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class MinecartTrackStacked2Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class MinecartTrackStacked3Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.FullStack)]
public sealed class MinecartTrackStacked4Block : PickupableBlock, IWaterLoggedBlock { }

public sealed class MinecartTrackFormGroup : FormGroup
{
    public override string Name => "MinecartTrack";
    public override LocString DisplayName => Localizer.DoStr("Standard Rail");
    public override LocString DisplayDescription => Localizer.DoStr("Shared rail shapes for minecarts and trains; broad-radius turns are separate crafted pieces.");
    public override int SortOrder => 100;
}

public sealed class MinecartTrackStraightFormType : FormType
{
    public override string Name => "MinecartTrackStraight";
    public override LocString DisplayName => Localizer.DoStr("Straight");
    public override LocString DisplayDescription => Localizer.DoStr("One-block straight track.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 1;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(MinecartTrackStraightBlock), typeof(MinecartTrackStraight90Block), typeof(MinecartTrackStraight180Block), typeof(MinecartTrackStraight270Block))]
[IsForm(typeof(MinecartTrackStraightFormType), typeof(MinecartTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackStraightBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackStraight90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackStraight180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackStraight270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}

public sealed class MinecartTrackBendFormType : FormType
{
    public override string Name => "MinecartTrackBend";
    public override LocString DisplayName => Localizer.DoStr("Bend");
    public override LocString DisplayDescription => Localizer.DoStr("Compact ninety-degree bend. Use only at low speed.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 2;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(MinecartTrackBendBlock), typeof(MinecartTrackBend90Block), typeof(MinecartTrackBend180Block), typeof(MinecartTrackBend270Block))]
[IsForm(typeof(MinecartTrackBendFormType), typeof(MinecartTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackBendBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackBend90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackBend180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackBend270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}

public sealed class MinecartTrackStopperFormType : FormType
{
    public override string Name => "MinecartTrackStopper";
    public override LocString DisplayName => Localizer.DoStr("Stopper");
    public override LocString DisplayDescription => Localizer.DoStr("End-of-line buffer stop.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 3;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(MinecartTrackStopperBlock), typeof(MinecartTrackStopper90Block), typeof(MinecartTrackStopper180Block), typeof(MinecartTrackStopper270Block))]
[IsForm(typeof(MinecartTrackStopperFormType), typeof(MinecartTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackStopperBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackStopper90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackStopper180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackStopper270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}

[NextRamp(typeof(MinecartTrackSlope2FormType), 0)]
public sealed class MinecartTrackSlope1FormType : FormType
{
    public override string Name => "MinecartTrackSlope1";
    public override LocString DisplayName => Localizer.DoStr("Slope 1/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 1 of 4.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 4;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(MinecartTrackSlope1270Block), typeof(MinecartTrackSlope1Block), typeof(MinecartTrackSlope190Block), typeof(MinecartTrackSlope1180Block))]
[IsForm(typeof(MinecartTrackSlope1FormType), typeof(MinecartTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope1Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope190Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope1180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope1270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}

[NextRamp(typeof(MinecartTrackSlope3FormType), 0)]
public sealed class MinecartTrackSlope2FormType : FormType
{
    public override string Name => "MinecartTrackSlope2";
    public override LocString DisplayName => Localizer.DoStr("Slope 2/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 2 of 4.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 5;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(MinecartTrackSlope2270Block), typeof(MinecartTrackSlope2Block), typeof(MinecartTrackSlope290Block), typeof(MinecartTrackSlope2180Block))]
[IsForm(typeof(MinecartTrackSlope2FormType), typeof(MinecartTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope2Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope290Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope2180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope2270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}

[NextRamp(typeof(MinecartTrackSlope4FormType), 0)]
public sealed class MinecartTrackSlope3FormType : FormType
{
    public override string Name => "MinecartTrackSlope3";
    public override LocString DisplayName => Localizer.DoStr("Slope 3/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 3 of 4.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 6;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(MinecartTrackSlope3270Block), typeof(MinecartTrackSlope3Block), typeof(MinecartTrackSlope390Block), typeof(MinecartTrackSlope3180Block))]
[IsForm(typeof(MinecartTrackSlope3FormType), typeof(MinecartTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope3Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope390Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope3180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope3270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}

[NextRamp(typeof(MinecartTrackSlope1FormType), 1)]
public sealed class MinecartTrackSlope4FormType : FormType
{
    public override string Name => "MinecartTrackSlope4";
    public override LocString DisplayName => Localizer.DoStr("Slope 4/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 4 of 4.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 7;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(MinecartTrackSlope4270Block), typeof(MinecartTrackSlope4Block), typeof(MinecartTrackSlope490Block), typeof(MinecartTrackSlope4180Block))]
[IsForm(typeof(MinecartTrackSlope4FormType), typeof(MinecartTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope4Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope490Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope4180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackSlope4270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
