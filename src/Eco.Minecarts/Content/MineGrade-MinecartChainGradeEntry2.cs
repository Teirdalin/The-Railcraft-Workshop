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

[NextRamp(typeof(MinecartChainGrade45FormType),1)]
public sealed class MinecartChainGradeEntry2FormType:FormType {
 public override string Name=>"MinecartChainGradeEntry2";
 public override LocString DisplayName=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeEntry2").Name);
 public override LocString DisplayDescription=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeEntry2").Description);
 public override Type GroupType=>typeof(MinecartChainFormGroup);
 public override int MinTier=>1;
 public override int SortOrder=>32;
}
[RotatedVariants(typeof(MinecartChainGradeEntry2270Block),typeof(MinecartChainGradeEntry2Block),typeof(MinecartChainGradeEntry290Block),typeof(MinecartChainGradeEntry2180Block)),IsForm(typeof(MinecartChainGradeEntry2FormType),typeof(MinecartChainItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeEntry2Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeEntry290Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeEntry2180Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeEntry2270Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }

