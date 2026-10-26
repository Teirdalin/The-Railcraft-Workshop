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

[NextRamp(typeof(MinecartChainGradeDip2FormType),0)]
public sealed class MinecartChainGradeDip1FormType:FormType {
 public override string Name=>"MinecartChainGradeDip1";
 public override LocString DisplayName=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeDip1").Name);
 public override LocString DisplayDescription=>Localizer.DoStr(Eco.Minecarts.Track.MineRailGradePath.Find("GradeDip1").Description);
 public override Type GroupType=>typeof(MinecartChainFormGroup);
 public override int MinTier=>1;
 public override int SortOrder=>37;
}
[RotatedVariants(typeof(MinecartChainGradeDip1270Block),typeof(MinecartChainGradeDip1Block),typeof(MinecartChainGradeDip190Block),typeof(MinecartChainGradeDip1180Block)),IsForm(typeof(MinecartChainGradeDip1FormType),typeof(MinecartChainItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeDip1Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeDip190Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeDip1180Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")]
public sealed class MinecartChainGradeDip1270Block:Block,IRepresentsItem,IWaterLoggedBlock { public Type RepresentedItemType=>typeof(MinecartChainItem); }

