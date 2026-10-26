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

[NextRamp(typeof(MinecartChainStraightFormType),1)]
public sealed class MinecartChainGradeExit2FormType:FormType {
 public override string Name=>"MinecartChainGradeExit2";
 public override LocString DisplayName=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeExit2").Name);
 public override LocString DisplayDescription=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeExit2").Description);
 public override Type GroupType=>typeof(MinecartChainFormGroup);
 public override int MinTier=>1;
 public override int SortOrder=>34;
}
[RotatedVariants(typeof(MinecartChainGradeExit2270Block),typeof(MinecartChainGradeExit2Block),typeof(MinecartChainGradeExit290Block),typeof(MinecartChainGradeExit2180Block)),IsForm(typeof(MinecartChainGradeExit2FormType),typeof(MinecartChainItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeExit2Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeExit290Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeExit2180Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeExit2270Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }

