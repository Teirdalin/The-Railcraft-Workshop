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

[NextRamp(typeof(MinecartTrackGradeCrest2FormType),0)]
public sealed class MinecartTrackGradeCrest1FormType:FormType {
 public override string Name=>"MinecartTrackGradeCrest1";
 public override LocString DisplayName=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeCrest1").Name);
 public override LocString DisplayDescription=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeCrest1").Description);
 public override Type GroupType=>typeof(MinecartTrackFormGroup);
 public override int MinTier=>1;
 public override int SortOrder=>35;
}
[RotatedVariants(typeof(MinecartTrackGradeCrest1270Block),typeof(MinecartTrackGradeCrest1Block),typeof(MinecartTrackGradeCrest190Block),typeof(MinecartTrackGradeCrest1180Block)),IsForm(typeof(MinecartTrackGradeCrest1FormType),typeof(MinecartTrackItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeCrest1Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeCrest190Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeCrest1180Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeCrest1270Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }

