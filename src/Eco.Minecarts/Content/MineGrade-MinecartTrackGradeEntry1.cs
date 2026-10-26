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

[NextRamp(typeof(MinecartTrackGradeEntry2FormType),0)]
public sealed class MinecartTrackGradeEntry1FormType:FormType {
 public override string Name=>"MinecartTrackGradeEntry1";
 public override LocString DisplayName=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeEntry1").Name);
 public override LocString DisplayDescription=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeEntry1").Description);
 public override Type GroupType=>typeof(MinecartTrackFormGroup);
 public override int MinTier=>1;
 public override int SortOrder=>31;
}
[RotatedVariants(typeof(MinecartTrackGradeEntry1270Block),typeof(MinecartTrackGradeEntry1Block),typeof(MinecartTrackGradeEntry190Block),typeof(MinecartTrackGradeEntry1180Block)),IsForm(typeof(MinecartTrackGradeEntry1FormType),typeof(MinecartTrackItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeEntry1Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeEntry190Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeEntry1180Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartTrackGradeEntry1270Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartTrackItem); }

