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

[NextRamp(typeof(MinecartTrackGradeExit2FormType),0)]
public sealed class MinecartTrackGradeExit1FormType:FormType {
 public override string Name=>"MinecartTrackGradeExit1";
 public override LocString DisplayName=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeExit1").Name);
 public override LocString DisplayDescription=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeExit1").Description);
 public override Type GroupType=>typeof(MinecartTrackFormGroup);
 public override int MinTier=>1;
 public override int SortOrder=>33;
}
[RotatedVariants(typeof(MinecartTrackGradeExit1270Block),typeof(MinecartTrackGradeExit1Block),typeof(MinecartTrackGradeExit190Block),typeof(MinecartTrackGradeExit1180Block)),IsForm(typeof(MinecartTrackGradeExit1FormType),typeof(MinecartTrackItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeExit1Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeExit190Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeExit1180Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeExit1270Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }

