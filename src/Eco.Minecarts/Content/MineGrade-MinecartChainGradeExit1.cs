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

[NextRamp(typeof(MinecartChainGradeExit2FormType),0)]
public sealed class MinecartChainGradeExit1FormType:FormType {
 public override string Name=>"MinecartChainGradeExit1";
 public override LocString DisplayName=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeExit1").Name);
 public override LocString DisplayDescription=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeExit1").Description);
 public override Type GroupType=>typeof(MinecartChainFormGroup);
 public override int MinTier=>1;
 public override int SortOrder=>33;
}
[RotatedVariants(typeof(MinecartChainGradeExit1270Block),typeof(MinecartChainGradeExit1Block),typeof(MinecartChainGradeExit190Block),typeof(MinecartChainGradeExit1180Block)),IsForm(typeof(MinecartChainGradeExit1FormType),typeof(MinecartChainItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeExit1Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeExit190Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeExit1180Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeExit1270Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }

