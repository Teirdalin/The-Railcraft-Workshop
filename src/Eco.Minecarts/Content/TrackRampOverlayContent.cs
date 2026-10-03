namespace Eco.Mods.TechTree;

using System;
using Eco.Core.Items;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Items;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.World.Blocks;
using Eco.World.Water;

[NextRamp(typeof(MinecartTrackRampTopFormType), 1)]
public sealed class MinecartTrackRampTopFormType : FormType
{
    public override string Name => "MinecartTrackRampTop";
    public override LocString DisplayName => Localizer.DoStr("On Slope 1:1");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 8;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartTrackRampTop270Block), typeof(MinecartTrackRampTopBlock), typeof(MinecartTrackRampTop90Block), typeof(MinecartTrackRampTop180Block))]
// Retired 1:1 shape: preserve serialized blocks for saves, but no hammer form.
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTopBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[NextRamp(typeof(MinecartTrackRampTop2FormType), 0)]
public sealed class MinecartTrackRampTop1FormType : FormType
{
    public override string Name => "MinecartTrackRampTop1";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 1/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 9;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartTrackRampTop1270Block), typeof(MinecartTrackRampTop1Block), typeof(MinecartTrackRampTop190Block), typeof(MinecartTrackRampTop1180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop1Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop190Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop1180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop1270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[NextRamp(typeof(MinecartTrackRampTop3FormType), 0)]
public sealed class MinecartTrackRampTop2FormType : FormType
{
    public override string Name => "MinecartTrackRampTop2";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 2/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 10;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartTrackRampTop2270Block), typeof(MinecartTrackRampTop2Block), typeof(MinecartTrackRampTop290Block), typeof(MinecartTrackRampTop2180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop2Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop290Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop2180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop2270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[NextRamp(typeof(MinecartTrackRampTop4FormType), 0)]
public sealed class MinecartTrackRampTop3FormType : FormType
{
    public override string Name => "MinecartTrackRampTop3";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 3/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 11;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartTrackRampTop3270Block), typeof(MinecartTrackRampTop3Block), typeof(MinecartTrackRampTop390Block), typeof(MinecartTrackRampTop3180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop3Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop390Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop3180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop3270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[NextRamp(typeof(MinecartTrackRampTop1FormType), 1)]
public sealed class MinecartTrackRampTop4FormType : FormType
{
    public override string Name => "MinecartTrackRampTop4";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 4/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(MinecartTrackFormGroup);
    public override int SortOrder => 12;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartTrackRampTop4270Block), typeof(MinecartTrackRampTop4Block), typeof(MinecartTrackRampTop490Block), typeof(MinecartTrackRampTop4180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop4Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop490Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop4180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartTrackRampTop4270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartTrackItem);
}
