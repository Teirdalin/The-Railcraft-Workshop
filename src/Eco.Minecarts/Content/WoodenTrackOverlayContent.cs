namespace Eco.Mods.TechTree;

using System;
using Eco.Core.Items;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Items;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.World.Blocks;
using Eco.World.Water;

[NextRamp(typeof(WoodenTrackRampTopFormType), 1)]
public sealed class WoodenTrackRampTopFormType : FormType
{
    public override string Name => "WoodenTrackRampTop";
    public override LocString DisplayName => Localizer.DoStr("On Slope 1:1");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 8;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(WoodenTrackRampTop270Block), typeof(WoodenTrackRampTopBlock), typeof(WoodenTrackRampTop90Block), typeof(WoodenTrackRampTop180Block))]
// Retired 1:1 shape: preserve serialized blocks for saves, but no hammer form.
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTopBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[NextRamp(typeof(WoodenTrackRampTop2FormType), 0)]
public sealed class WoodenTrackRampTop1FormType : FormType
{
    public override string Name => "WoodenTrackRampTop1";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 1/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 9;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(WoodenTrackRampTop1270Block), typeof(WoodenTrackRampTop1Block), typeof(WoodenTrackRampTop190Block), typeof(WoodenTrackRampTop1180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop1Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop190Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop1180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop1270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[NextRamp(typeof(WoodenTrackRampTop3FormType), 0)]
public sealed class WoodenTrackRampTop2FormType : FormType
{
    public override string Name => "WoodenTrackRampTop2";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 2/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 10;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(WoodenTrackRampTop2270Block), typeof(WoodenTrackRampTop2Block), typeof(WoodenTrackRampTop290Block), typeof(WoodenTrackRampTop2180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop2Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop290Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop2180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop2270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[NextRamp(typeof(WoodenTrackRampTop4FormType), 0)]
public sealed class WoodenTrackRampTop3FormType : FormType
{
    public override string Name => "WoodenTrackRampTop3";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 3/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 11;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(WoodenTrackRampTop3270Block), typeof(WoodenTrackRampTop3Block), typeof(WoodenTrackRampTop390Block), typeof(WoodenTrackRampTop3180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop3Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop390Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop3180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop3270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[NextRamp(typeof(WoodenTrackRampTop1FormType), 1)]
public sealed class WoodenTrackRampTop4FormType : FormType
{
    public override string Name => "WoodenTrackRampTop4";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 4/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 12;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(WoodenTrackRampTop4270Block), typeof(WoodenTrackRampTop4Block), typeof(WoodenTrackRampTop490Block), typeof(WoodenTrackRampTop4180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop4Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop490Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop4180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackRampTop4270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
