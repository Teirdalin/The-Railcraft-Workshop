namespace Eco.Mods.TechTree;

using System;
using Eco.Core.Items;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Skills;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;
using Eco.World.Blocks;
using Eco.World.Water;

[Serialized, Constructed, Solid, BlockTier(1)]
public class MinecartChainBlock : Block, IRepresentsItem
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}

[Serialized, LocDisplayName("Powered Chain Rail")]
[LocDescription("Hammer-placed chain-lift track. Connect to a Rail Chain Drive beside the track. Each connected block requires 2 W of mechanical power. Uphill direction follows the slope. Load and available power limit lifting speed.")]
[MaxStackSize(20), Weight(2200), ResourcePile, Tag("Constructable"), Tier(1)]
[Ecopedia("Blocks", "Building Materials", createAsSubPage: true)]
public sealed class MinecartChainItem : BlockItem<MinecartChainBlock>
{
    public override bool CanStickToWalls => false;
    public override Type[] BlockTypes => [typeof(MinecartChainStacked1Block), typeof(MinecartChainStacked2Block), typeof(MinecartChainStacked3Block), typeof(MinecartChainStacked4Block)];
}

[RequiresSkill(typeof(BasicEngineeringSkill), 2)]
public sealed class MinecartChainRecipe : MinecartRailRecipeFamily
{
    public MinecartChainRecipe() => this.Configure(
        MinecartRailRecipes.Make<MinecartChainItem>("Powered Chain Rail", 4, 0, 4, hewnLogs: 2, woodenGears: 1, fixedMaterials: true),
        "Powered Chain Rail", typeof(MinecartChainRecipe), 100, 2);
}

public sealed class MinecartChainFormGroup : FormGroup
{
    public override string Name => "MinecartChain";
    public override LocString DisplayName => Localizer.DoStr("Chain Lift");
    public override LocString DisplayDescription => Localizer.DoStr("Mechanically powered lift track. 2 W per connected block.");
    public override int SortOrder => 101;
}

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class MinecartChainStacked1Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class MinecartChainStacked2Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class MinecartChainStacked3Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.FullStack)]
public sealed class MinecartChainStacked4Block : PickupableBlock, IWaterLoggedBlock { }
public sealed class MinecartChainStraightFormType : FormType
{
    public override string Name => "MinecartChainStraight";
    public override LocString DisplayName => Localizer.DoStr("Chain Straight");
    public override LocString DisplayDescription => Localizer.DoStr("Connect to a mechanical chain drive. Costs 2 W per block.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 1;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainStraightBlock), typeof(MinecartChainStraight90Block), typeof(MinecartChainStraight180Block), typeof(MinecartChainStraight270Block))]
[IsForm(typeof(MinecartChainStraightFormType), typeof(MinecartChainItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainStraightBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainStraight90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainStraight180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainStraight270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}

[NextRamp(typeof(MinecartChainSlope2FormType), 0)]
public sealed class MinecartChainSlope1FormType : FormType
{
    public override string Name => "MinecartChainSlope1";
    public override LocString DisplayName => Localizer.DoStr("Chain Slope 1");
    public override LocString DisplayDescription => Localizer.DoStr("Connect to a mechanical chain drive. Costs 2 W per block.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 2;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainSlope1270Block), typeof(MinecartChainSlope1Block), typeof(MinecartChainSlope190Block), typeof(MinecartChainSlope1180Block))]
[IsForm(typeof(MinecartChainSlope1FormType), typeof(MinecartChainItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope1Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope190Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope1180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope1270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}

[NextRamp(typeof(MinecartChainSlope3FormType), 0)]
public sealed class MinecartChainSlope2FormType : FormType
{
    public override string Name => "MinecartChainSlope2";
    public override LocString DisplayName => Localizer.DoStr("Chain Slope 2");
    public override LocString DisplayDescription => Localizer.DoStr("Connect to a mechanical chain drive. Costs 2 W per block.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 3;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainSlope2270Block), typeof(MinecartChainSlope2Block), typeof(MinecartChainSlope290Block), typeof(MinecartChainSlope2180Block))]
[IsForm(typeof(MinecartChainSlope2FormType), typeof(MinecartChainItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope2Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope290Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope2180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope2270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}

[NextRamp(typeof(MinecartChainSlope4FormType), 0)]
public sealed class MinecartChainSlope3FormType : FormType
{
    public override string Name => "MinecartChainSlope3";
    public override LocString DisplayName => Localizer.DoStr("Chain Slope 3");
    public override LocString DisplayDescription => Localizer.DoStr("Connect to a mechanical chain drive. Costs 2 W per block.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 4;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainSlope3270Block), typeof(MinecartChainSlope3Block), typeof(MinecartChainSlope390Block), typeof(MinecartChainSlope3180Block))]
[IsForm(typeof(MinecartChainSlope3FormType), typeof(MinecartChainItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope3Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope390Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope3180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope3270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}

[NextRamp(typeof(MinecartChainSlope1FormType), 1)]
public sealed class MinecartChainSlope4FormType : FormType
{
    public override string Name => "MinecartChainSlope4";
    public override LocString DisplayName => Localizer.DoStr("Chain Slope 4");
    public override LocString DisplayDescription => Localizer.DoStr("Connect to a mechanical chain drive. Costs 2 W per block.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 5;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainSlope4270Block), typeof(MinecartChainSlope4Block), typeof(MinecartChainSlope490Block), typeof(MinecartChainSlope4180Block))]
[IsForm(typeof(MinecartChainSlope4FormType), typeof(MinecartChainItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope4Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope490Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope4180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainSlope4270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}

[NextRamp(typeof(MinecartChainRampTopFormType), 1)]
public sealed class MinecartChainRampTopFormType : FormType
{
    public override string Name => "MinecartChainRampTop";
    public override LocString DisplayName => Localizer.DoStr("Chain Ramp Top ");
    public override LocString DisplayDescription => Localizer.DoStr("Place above a matching ramp facing uphill. Connect to a mechanical chain drive. Costs 2 W per block.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 6;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainRampTop270Block), typeof(MinecartChainRampTopBlock), typeof(MinecartChainRampTop90Block), typeof(MinecartChainRampTop180Block))]
// Retired 1:1 shape: preserve serialized blocks for saves, but no hammer form.
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTopBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}

[NextRamp(typeof(MinecartChainRampTop2FormType), 0)]
public sealed class MinecartChainRampTop1FormType : FormType
{
    public override string Name => "MinecartChainRampTop1";
    public override LocString DisplayName => Localizer.DoStr("Chain Ramp Top 1");
    public override LocString DisplayDescription => Localizer.DoStr("Place above a matching ramp facing uphill. Connect to a mechanical chain drive. Costs 2 W per block.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 7;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainRampTop1270Block), typeof(MinecartChainRampTop1Block), typeof(MinecartChainRampTop190Block), typeof(MinecartChainRampTop1180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop1Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop190Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop1180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop1270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}

[NextRamp(typeof(MinecartChainRampTop3FormType), 0)]
public sealed class MinecartChainRampTop2FormType : FormType
{
    public override string Name => "MinecartChainRampTop2";
    public override LocString DisplayName => Localizer.DoStr("Chain Ramp Top 2");
    public override LocString DisplayDescription => Localizer.DoStr("Place above a matching ramp facing uphill. Connect to a mechanical chain drive. Costs 2 W per block.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 8;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainRampTop2270Block), typeof(MinecartChainRampTop2Block), typeof(MinecartChainRampTop290Block), typeof(MinecartChainRampTop2180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop2Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop290Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop2180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop2270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}

[NextRamp(typeof(MinecartChainRampTop4FormType), 0)]
public sealed class MinecartChainRampTop3FormType : FormType
{
    public override string Name => "MinecartChainRampTop3";
    public override LocString DisplayName => Localizer.DoStr("Chain Ramp Top 3");
    public override LocString DisplayDescription => Localizer.DoStr("Place above a matching ramp facing uphill. Connect to a mechanical chain drive. Costs 2 W per block.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 9;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainRampTop3270Block), typeof(MinecartChainRampTop3Block), typeof(MinecartChainRampTop390Block), typeof(MinecartChainRampTop3180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop3Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop390Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop3180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop3270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}

[NextRamp(typeof(MinecartChainRampTop1FormType), 1)]
public sealed class MinecartChainRampTop4FormType : FormType
{
    public override string Name => "MinecartChainRampTop4";
    public override LocString DisplayName => Localizer.DoStr("Chain Ramp Top 4");
    public override LocString DisplayDescription => Localizer.DoStr("Place above a matching ramp facing uphill. Connect to a mechanical chain drive. Costs 2 W per block.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 10;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainRampTop4270Block), typeof(MinecartChainRampTop4Block), typeof(MinecartChainRampTop490Block), typeof(MinecartChainRampTop4180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop4Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop490Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop4180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainRampTop4270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
