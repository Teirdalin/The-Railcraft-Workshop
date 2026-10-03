namespace Eco.Mods.TechTree;
using System;
using Eco.Core.Items;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Items;
using Eco.Shared.SharedTypes;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.World.Blocks;
using Eco.World.Water;
public sealed class MinecartChainBendFormType : FormType
{
    public override string Name => "MinecartChainBend";
    public override LocString DisplayName => Localizer.DoStr("Right Bend");
    public override LocString DisplayDescription => Localizer.DoStr("Hammer-placed chain track. Requires a connected mechanical chain drive.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 11;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainBendBlock), typeof(MinecartChainBend90Block), typeof(MinecartChainBend180Block), typeof(MinecartChainBend270Block))]
[IsForm(typeof(MinecartChainBendFormType), typeof(MinecartChainItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainBendBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainBend90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainBend180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainBend270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
public sealed class MinecartChainBendLeftFormType : FormType
{
    public override string Name => "MinecartChainBendLeft";
    public override LocString DisplayName => Localizer.DoStr("Left Bend");
    public override LocString DisplayDescription => Localizer.DoStr("Left-turning chain track. Requires a connected mechanical chain drive.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 12;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainBendLeftBlock), typeof(MinecartChainBendLeft90Block), typeof(MinecartChainBendLeft180Block), typeof(MinecartChainBendLeft270Block))]
[IsForm(typeof(MinecartChainBendLeftFormType), typeof(MinecartChainItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainBendLeftBlock : Block, IRepresentsItem, IWaterLoggedBlock
{ public Type RepresentedItemType => typeof(MinecartChainItem); }
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainBendLeft90Block : Block, IRepresentsItem, IWaterLoggedBlock
{ public Type RepresentedItemType => typeof(MinecartChainItem); }
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainBendLeft180Block : Block, IRepresentsItem, IWaterLoggedBlock
{ public Type RepresentedItemType => typeof(MinecartChainItem); }
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainBendLeft270Block : Block, IRepresentsItem, IWaterLoggedBlock
{ public Type RepresentedItemType => typeof(MinecartChainItem); }

public sealed class MinecartChainStopperFormType : FormType
{
    public override string Name => "MinecartChainStopper";
    public override LocString DisplayName => Localizer.DoStr("Stopper");
    public override LocString DisplayDescription => Localizer.DoStr("Hammer-placed chain track. Requires a connected mechanical chain drive.");
    public override Type GroupType => typeof(MinecartChainFormGroup);
    public override int SortOrder => 13;
    public override int MinTier => 1;
}
[RotatedVariants(typeof(MinecartChainStopperBlock), typeof(MinecartChainStopper90Block), typeof(MinecartChainStopper180Block), typeof(MinecartChainStopper270Block))]
[IsForm(typeof(MinecartChainStopperFormType), typeof(MinecartChainItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainStopperBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainStopper90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainStopper180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class MinecartChainStopper270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(MinecartChainItem);
}
