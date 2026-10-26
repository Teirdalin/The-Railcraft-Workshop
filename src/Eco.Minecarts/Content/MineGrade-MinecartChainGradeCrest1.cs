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

[NextRamp(typeof(MinecartChainGradeCrest2FormType),0)]
public sealed class MinecartChainGradeCrest1FormType:FormType {
 public override string Name=>"MinecartChainGradeCrest1";
 public override LocString DisplayName=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeCrest1").Name);
 public override LocString DisplayDescription=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeCrest1").Description);
 public override Type GroupType=>typeof(MinecartChainFormGroup);
 public override int MinTier=>1;
 public override int SortOrder=>35;
}
[RotatedVariants(typeof(MinecartChainGradeCrest1270Block),typeof(MinecartChainGradeCrest1Block),typeof(MinecartChainGradeCrest190Block),typeof(MinecartChainGradeCrest1180Block)),IsForm(typeof(MinecartChainGradeCrest1FormType),typeof(MinecartChainItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeCrest1Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeCrest190Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeCrest1180Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeCrest1270Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }

