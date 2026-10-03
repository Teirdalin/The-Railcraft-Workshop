namespace Eco.Mods.TechTree;

using System;
using Eco.Core.Items;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Items;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.World.Blocks;
using Eco.World.Water;

[NextRamp(typeof(TramTrackRampTopFormType), 1)]
public sealed class TramTrackRampTopFormType : FormType
{
    public override string Name => "TramTrackRampTop";
    public override LocString DisplayName => Localizer.DoStr("On Slope 1:1");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 8;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(TramTrackRampTop270Block), typeof(TramTrackRampTopBlock), typeof(TramTrackRampTop90Block), typeof(TramTrackRampTop180Block))]
// Retired 1:1 shape: preserve serialized blocks for saves, but no hammer form.
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTopBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[NextRamp(typeof(TramTrackRampTop2FormType), 0)]
public sealed class TramTrackRampTop1FormType : FormType
{
    public override string Name => "TramTrackRampTop1";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 1/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 9;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(TramTrackRampTop1270Block), typeof(TramTrackRampTop1Block), typeof(TramTrackRampTop190Block), typeof(TramTrackRampTop1180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop1Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop190Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop1180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop1270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[NextRamp(typeof(TramTrackRampTop3FormType), 0)]
public sealed class TramTrackRampTop2FormType : FormType
{
    public override string Name => "TramTrackRampTop2";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 2/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 10;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(TramTrackRampTop2270Block), typeof(TramTrackRampTop2Block), typeof(TramTrackRampTop290Block), typeof(TramTrackRampTop2180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop2Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop290Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop2180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop2270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[NextRamp(typeof(TramTrackRampTop4FormType), 0)]
public sealed class TramTrackRampTop3FormType : FormType
{
    public override string Name => "TramTrackRampTop3";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 3/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 11;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(TramTrackRampTop3270Block), typeof(TramTrackRampTop3Block), typeof(TramTrackRampTop390Block), typeof(TramTrackRampTop3180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop3Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop390Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop3180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop3270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[NextRamp(typeof(TramTrackRampTop1FormType), 1)]
public sealed class TramTrackRampTop4FormType : FormType
{
    public override string Name => "TramTrackRampTop4";
    public override LocString DisplayName => Localizer.DoStr("On Ramp 4/4");
    public override LocString DisplayDescription => Localizer.DoStr("Place in the cell above a matching ramp, facing the same uphill direction. Track follows its surface without replacing the support.");
    public override Type GroupType => typeof(TramTrackFormGroup);
    public override int SortOrder => 12;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(TramTrackRampTop4270Block), typeof(TramTrackRampTop4Block), typeof(TramTrackRampTop490Block), typeof(TramTrackRampTop4180Block))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop4Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop490Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop4180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class TramTrackRampTop4270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(TramTrackItem);
}
