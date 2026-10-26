namespace Eco.Mods.TechTree;
using System;
using Eco.Core.Items;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;
using Eco.World.Blocks;
using Eco.World.Water;

[NextRamp(typeof(MinecartTrackGradeDip2FormType),0)]
public sealed class MinecartTrackGradeDip1FormType:FormType {
 public override string Name=>"MinecartTrackGradeDip1";
 public override LocString DisplayName=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeDip1").Name);
 public override LocString DisplayDescription=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeDip1").Description);
 public override Type GroupType=>typeof(MinecartTrackFormGroup);
 public override int MinTier=>1;
 public override int SortOrder=>37;
}
[RotatedVariants(typeof(MinecartTrackGradeDip1270Block),typeof(MinecartTrackGradeDip1Block),typeof(MinecartTrackGradeDip190Block),typeof(MinecartTrackGradeDip1180Block)),IsForm(typeof(MinecartTrackGradeDip1FormType),typeof(MinecartTrackItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeDip1Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeDip190Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeDip1180Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeDip1270Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }

