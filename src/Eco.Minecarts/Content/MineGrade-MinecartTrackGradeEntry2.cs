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

[NextRamp(typeof(MinecartTrackGrade45FormType),1)]
public sealed class MinecartTrackGradeEntry2FormType:FormType {
 public override string Name=>"MinecartTrackGradeEntry2";
 public override LocString DisplayName=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeEntry2").Name);
 public override LocString DisplayDescription=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeEntry2").Description);
 public override Type GroupType=>typeof(MinecartTrackFormGroup);
 public override int MinTier=>1;
 public override int SortOrder=>32;
}
[RotatedVariants(typeof(MinecartTrackGradeEntry2270Block),typeof(MinecartTrackGradeEntry2Block),typeof(MinecartTrackGradeEntry290Block),typeof(MinecartTrackGradeEntry2180Block)),IsForm(typeof(MinecartTrackGradeEntry2FormType),typeof(MinecartTrackItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeEntry2Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeEntry290Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeEntry2180Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeEntry2270Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }

