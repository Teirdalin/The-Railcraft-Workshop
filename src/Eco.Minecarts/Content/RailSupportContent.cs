namespace Eco.Mods.TechTree;
using Eco.Core.Items;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Skills;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;
using Eco.World.Blocks;
using Eco.World.Water;
[Serialized,Constructed,Solid,BlockTier(1)] public class RailSupportWoodBlock:Block,IRepresentsItem {public Type RepresentedItemType=>typeof(RailSupportWoodItem);}
[Serialized,LocDisplayName("Wood Rail Support"),LocDescription("Hammer-built support: base, middle and rail-gripping top. Wood support reach: 6 rail blocks. Stack a grounded column beneath its top."),MaxStackSize(20),Weight(1800),ResourcePile,Tag("Constructable"),Tier(1),Ecopedia("Blocks","Building Materials",createAsSubPage:true)] public sealed class RailSupportWoodItem:BlockItem<RailSupportWoodBlock> {public override bool CanStickToWalls=>false;public override Type[] BlockTypes=>[typeof(RailSupportWoodStacked1Block),typeof(RailSupportWoodStacked2Block),typeof(RailSupportWoodStacked3Block),typeof(RailSupportWoodStacked4Block)];}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class RailSupportWoodStacked1Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class RailSupportWoodStacked2Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class RailSupportWoodStacked3Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.FullStack)] public sealed class RailSupportWoodStacked4Block:PickupableBlock,IWaterLoggedBlock{}
public sealed class RailSupportWoodGroup:FormGroup {public override string Name=>"RailSupportWoodGroup";public override LocString DisplayName=>Localizer.DoStr("Wood Rail Support");public override LocString DisplayDescription=>Localizer.DoStr("Base, column and rail saddle.");public override int SortOrder=>120;}
public sealed class RailSupportWoodBaseFormType:FormType {public override string Name=>"RailSupportWoodBase";public override LocString DisplayName=>Localizer.DoStr("Base");public override LocString DisplayDescription=>Localizer.DoStr("Base support section. Rotate with the hammer. The top follows ordinary rail slope height and direction automatically.");public override Type GroupType=>typeof(RailSupportWoodGroup);public override int MinTier=>1;}
[RotatedVariants(typeof(RailSupportWoodBaseBlock),typeof(RailSupportWoodBaseR90Block),typeof(RailSupportWoodBaseR180Block),typeof(RailSupportWoodBaseR270Block)),IsForm(typeof(RailSupportWoodBaseFormType),typeof(RailSupportWoodItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodBaseBlock:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodBaseR90Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodBaseR180Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodBaseR270Block:RailSupportWoodBlock,IWaterLoggedBlock{}
public sealed class RailSupportWoodMiddleFormType:FormType {public override string Name=>"RailSupportWoodMiddle";public override LocString DisplayName=>Localizer.DoStr("Middle");public override LocString DisplayDescription=>Localizer.DoStr("Middle support section. Rotate with the hammer. The top follows ordinary rail slope height and direction automatically.");public override Type GroupType=>typeof(RailSupportWoodGroup);public override int MinTier=>1;}
[RotatedVariants(typeof(RailSupportWoodMiddleBlock),typeof(RailSupportWoodMiddleR90Block),typeof(RailSupportWoodMiddleR180Block),typeof(RailSupportWoodMiddleR270Block)),IsForm(typeof(RailSupportWoodMiddleFormType),typeof(RailSupportWoodItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodMiddleBlock:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodMiddleR90Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodMiddleR180Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodMiddleR270Block:RailSupportWoodBlock,IWaterLoggedBlock{}
public sealed class RailSupportWoodTopFormType:FormType {public override string Name=>"RailSupportWoodTop";public override LocString DisplayName=>Localizer.DoStr("Top");public override LocString DisplayDescription=>Localizer.DoStr("Top support section. Rotate with the hammer. The top follows ordinary rail slope height and direction automatically.");public override Type GroupType=>typeof(RailSupportWoodGroup);public override int MinTier=>1;}
[RotatedVariants(typeof(RailSupportWoodTopBlock),typeof(RailSupportWoodTopR90Block),typeof(RailSupportWoodTopR180Block),typeof(RailSupportWoodTopR270Block)),IsForm(typeof(RailSupportWoodTopFormType),typeof(RailSupportWoodItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopBlock:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopR90Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopR180Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopR270Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope1Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope1R90Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope1R180Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope1R270Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope2Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope2R90Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope2R180Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope2R270Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope3Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope3R90Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope3R180Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope3R270Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope4Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope4R90Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope4R180Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportWoodTopSlope4R270Block:RailSupportWoodBlock,IWaterLoggedBlock{}
[RequiresSkill(typeof(LoggingSkill),1)] public sealed class RailSupportWoodRecipe:RecipeFamily {public RailSupportWoodRecipe(){var r=new Recipe();r.Init("RailSupportWood",Localizer.DoStr("Wood Rail Support"),[new IngredientElement("Wood",1,typeof(LoggingSkill))],[],[new CraftingElement<RailSupportWoodItem>(1)]);Recipes=[r];ExperienceOnCraft=1;LaborInCalories=CreateLaborInCaloriesValue(40,typeof(LoggingSkill));CraftMinutes=CreateCraftTimeValue(typeof(RailSupportWoodRecipe),1,typeof(LoggingSkill));Initialize(Localizer.DoStr("Wood Rail Support"),typeof(RailSupportWoodRecipe));CraftingComponent.AddRecipe(typeof(RailcraftWorkbenchObject),this);}}
[Serialized,Constructed,Solid,BlockTier(1)] public class RailSupportIronBlock:Block,IRepresentsItem {public Type RepresentedItemType=>typeof(RailSupportIronItem);}
[Serialized,LocDisplayName("Iron Rail Support"),LocDescription("Hammer-built support: base, middle and rail-gripping top. Iron support reach: 12 rail blocks. Stack a grounded column beneath its top."),MaxStackSize(20),Weight(1800),ResourcePile,Tag("Constructable"),Tier(1),Ecopedia("Blocks","Building Materials",createAsSubPage:true)] public sealed class RailSupportIronItem:BlockItem<RailSupportIronBlock> {public override bool CanStickToWalls=>false;public override Type[] BlockTypes=>[typeof(RailSupportIronStacked1Block),typeof(RailSupportIronStacked2Block),typeof(RailSupportIronStacked3Block),typeof(RailSupportIronStacked4Block)];}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class RailSupportIronStacked1Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class RailSupportIronStacked2Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class RailSupportIronStacked3Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.FullStack)] public sealed class RailSupportIronStacked4Block:PickupableBlock,IWaterLoggedBlock{}
public sealed class RailSupportIronGroup:FormGroup {public override string Name=>"RailSupportIronGroup";public override LocString DisplayName=>Localizer.DoStr("Iron Rail Support");public override LocString DisplayDescription=>Localizer.DoStr("Base, column and rail saddle.");public override int SortOrder=>120;}
public sealed class RailSupportIronBaseFormType:FormType {public override string Name=>"RailSupportIronBase";public override LocString DisplayName=>Localizer.DoStr("Base");public override LocString DisplayDescription=>Localizer.DoStr("Base support section. Rotate with the hammer. The top follows ordinary rail slope height and direction automatically.");public override Type GroupType=>typeof(RailSupportIronGroup);public override int MinTier=>1;}
[RotatedVariants(typeof(RailSupportIronBaseBlock),typeof(RailSupportIronBaseR90Block),typeof(RailSupportIronBaseR180Block),typeof(RailSupportIronBaseR270Block)),IsForm(typeof(RailSupportIronBaseFormType),typeof(RailSupportIronItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronBaseBlock:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronBaseR90Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronBaseR180Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronBaseR270Block:RailSupportIronBlock,IWaterLoggedBlock{}
public sealed class RailSupportIronMiddleFormType:FormType {public override string Name=>"RailSupportIronMiddle";public override LocString DisplayName=>Localizer.DoStr("Middle");public override LocString DisplayDescription=>Localizer.DoStr("Middle support section. Rotate with the hammer. The top follows ordinary rail slope height and direction automatically.");public override Type GroupType=>typeof(RailSupportIronGroup);public override int MinTier=>1;}
[RotatedVariants(typeof(RailSupportIronMiddleBlock),typeof(RailSupportIronMiddleR90Block),typeof(RailSupportIronMiddleR180Block),typeof(RailSupportIronMiddleR270Block)),IsForm(typeof(RailSupportIronMiddleFormType),typeof(RailSupportIronItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronMiddleBlock:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronMiddleR90Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronMiddleR180Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronMiddleR270Block:RailSupportIronBlock,IWaterLoggedBlock{}
public sealed class RailSupportIronTopFormType:FormType {public override string Name=>"RailSupportIronTop";public override LocString DisplayName=>Localizer.DoStr("Top");public override LocString DisplayDescription=>Localizer.DoStr("Top support section. Rotate with the hammer. The top follows ordinary rail slope height and direction automatically.");public override Type GroupType=>typeof(RailSupportIronGroup);public override int MinTier=>1;}
[RotatedVariants(typeof(RailSupportIronTopBlock),typeof(RailSupportIronTopR90Block),typeof(RailSupportIronTopR180Block),typeof(RailSupportIronTopR270Block)),IsForm(typeof(RailSupportIronTopFormType),typeof(RailSupportIronItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopBlock:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopR90Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopR180Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopR270Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope1Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope1R90Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope1R180Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope1R270Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope2Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope2R90Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope2R180Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope2R270Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope3Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope3R90Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope3R180Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope3R270Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope4Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope4R90Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope4R180Block:RailSupportIronBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportIronTopSlope4R270Block:RailSupportIronBlock,IWaterLoggedBlock{}
[RequiresSkill(typeof(BasicEngineeringSkill),1)] public sealed class RailSupportIronRecipe:RecipeFamily {public RailSupportIronRecipe(){var r=new Recipe();r.Init("RailSupportIron",Localizer.DoStr("Iron Rail Support"),[new IngredientElement(typeof(IronBarItem),1,typeof(BasicEngineeringSkill))],[],[new CraftingElement<RailSupportIronItem>(4)]);Recipes=[r];ExperienceOnCraft=1;LaborInCalories=CreateLaborInCaloriesValue(40,typeof(BasicEngineeringSkill));CraftMinutes=CreateCraftTimeValue(typeof(RailSupportIronRecipe),1,typeof(BasicEngineeringSkill));Initialize(Localizer.DoStr("Iron Rail Support"),typeof(RailSupportIronRecipe));CraftingComponent.AddRecipe(typeof(RailcraftWorkbenchObject),this);}}
[Serialized,Constructed,Solid,BlockTier(1)] public class RailSupportSteelBlock:Block,IRepresentsItem {public Type RepresentedItemType=>typeof(RailSupportSteelItem);}
[Serialized,LocDisplayName("Steel Rail Support"),LocDescription("Hammer-built support: base, middle and rail-gripping top. Steel support reach: 18 rail blocks. Stack a grounded column beneath its top."),MaxStackSize(20),Weight(1800),ResourcePile,Tag("Constructable"),Tier(1),Ecopedia("Blocks","Building Materials",createAsSubPage:true)] public sealed class RailSupportSteelItem:BlockItem<RailSupportSteelBlock> {public override bool CanStickToWalls=>false;public override Type[] BlockTypes=>[typeof(RailSupportSteelStacked1Block),typeof(RailSupportSteelStacked2Block),typeof(RailSupportSteelStacked3Block),typeof(RailSupportSteelStacked4Block)];}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class RailSupportSteelStacked1Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class RailSupportSteelStacked2Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.PartialStack)] public sealed class RailSupportSteelStacked3Block:PickupableBlock,IWaterLoggedBlock{}
[Serialized,Solid,Tag("Constructable"),Tag(BlockTags.FullStack)] public sealed class RailSupportSteelStacked4Block:PickupableBlock,IWaterLoggedBlock{}
public sealed class RailSupportSteelGroup:FormGroup {public override string Name=>"RailSupportSteelGroup";public override LocString DisplayName=>Localizer.DoStr("Steel Rail Support");public override LocString DisplayDescription=>Localizer.DoStr("Base, column and rail saddle.");public override int SortOrder=>120;}
public sealed class RailSupportSteelBaseFormType:FormType {public override string Name=>"RailSupportSteelBase";public override LocString DisplayName=>Localizer.DoStr("Base");public override LocString DisplayDescription=>Localizer.DoStr("Base support section. Rotate with the hammer. The top follows ordinary rail slope height and direction automatically.");public override Type GroupType=>typeof(RailSupportSteelGroup);public override int MinTier=>1;}
[RotatedVariants(typeof(RailSupportSteelBaseBlock),typeof(RailSupportSteelBaseR90Block),typeof(RailSupportSteelBaseR180Block),typeof(RailSupportSteelBaseR270Block)),IsForm(typeof(RailSupportSteelBaseFormType),typeof(RailSupportSteelItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelBaseBlock:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelBaseR90Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelBaseR180Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelBaseR270Block:RailSupportSteelBlock,IWaterLoggedBlock{}
public sealed class RailSupportSteelMiddleFormType:FormType {public override string Name=>"RailSupportSteelMiddle";public override LocString DisplayName=>Localizer.DoStr("Middle");public override LocString DisplayDescription=>Localizer.DoStr("Middle support section. Rotate with the hammer. The top follows ordinary rail slope height and direction automatically.");public override Type GroupType=>typeof(RailSupportSteelGroup);public override int MinTier=>1;}
[RotatedVariants(typeof(RailSupportSteelMiddleBlock),typeof(RailSupportSteelMiddleR90Block),typeof(RailSupportSteelMiddleR180Block),typeof(RailSupportSteelMiddleR270Block)),IsForm(typeof(RailSupportSteelMiddleFormType),typeof(RailSupportSteelItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelMiddleBlock:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelMiddleR90Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelMiddleR180Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelMiddleR270Block:RailSupportSteelBlock,IWaterLoggedBlock{}
public sealed class RailSupportSteelTopFormType:FormType {public override string Name=>"RailSupportSteelTop";public override LocString DisplayName=>Localizer.DoStr("Top");public override LocString DisplayDescription=>Localizer.DoStr("Top support section. Rotate with the hammer. The top follows ordinary rail slope height and direction automatically.");public override Type GroupType=>typeof(RailSupportSteelGroup);public override int MinTier=>1;}
[RotatedVariants(typeof(RailSupportSteelTopBlock),typeof(RailSupportSteelTopR90Block),typeof(RailSupportSteelTopR180Block),typeof(RailSupportSteelTopR270Block)),IsForm(typeof(RailSupportSteelTopFormType),typeof(RailSupportSteelItem))]
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopBlock:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopR90Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopR180Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopR270Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope1Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope1R90Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope1R180Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope1R270Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope2Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope2R90Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope2R180Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope2R270Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope3Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope3R90Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope3R180Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope3R270Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope4Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope4R90Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope4R180Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[Serialized,Constructed,Solid,BlockTier(1),Tag("Constructable")] public sealed class RailSupportSteelTopSlope4R270Block:RailSupportSteelBlock,IWaterLoggedBlock{}
[RequiresSkill(typeof(BasicEngineeringSkill),1)] public sealed class RailSupportSteelRecipe:RecipeFamily {public RailSupportSteelRecipe(){var r=new Recipe();r.Init("RailSupportSteel",Localizer.DoStr("Steel Rail Support"),[new IngredientElement(typeof(SteelBarItem),1,typeof(BasicEngineeringSkill))],[],[new CraftingElement<RailSupportSteelItem>(4)]);Recipes=[r];ExperienceOnCraft=1;LaborInCalories=CreateLaborInCaloriesValue(40,typeof(BasicEngineeringSkill));CraftMinutes=CreateCraftTimeValue(typeof(RailSupportSteelRecipe),1,typeof(BasicEngineeringSkill));Initialize(Localizer.DoStr("Steel Rail Support"),typeof(RailSupportSteelRecipe));CraftingComponent.AddRecipe(typeof(RailcraftWorkbenchObject),this);}}
