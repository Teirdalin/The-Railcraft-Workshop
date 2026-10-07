namespace Eco.Mods.TechTree;

using System;
using Eco.Core.Items;
using Eco.Gameplay.Blocks;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Components;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Skills;
using Eco.World.Blocks;
using Eco.World.Water;

[Serialized, Constructed, Solid, BlockTier(1)]
public class WoodenTrackBlock : Block, IRepresentsItem
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}

[Serialized, LocDisplayName("Wooden Rail")]
[LocDescription("Early wooden rail tier with the same gauge as Standard Rail. Supports up to 900 kg per vehicle; overload breaks the rail. Carry it and use a hammer for shapes. Best paired with the lightweight wooden cart.")]
[MaxStackSize(20), Weight(1800), ResourcePile, Tag("Constructable"), Tier(1)]
[Ecopedia("Blocks", "Building Materials", createAsSubPage: true)]
public sealed class WoodenTrackItem : BlockItem<WoodenTrackBlock>
{
    public override bool CanStickToWalls => false;
    public override Type[] BlockTypes => [typeof(WoodenTrackStacked1Block), typeof(WoodenTrackStacked2Block), typeof(WoodenTrackStacked3Block), typeof(WoodenTrackStacked4Block)];
}

[RequiresSkill(typeof(LoggingSkill), 1)]
public sealed class WoodenTrackRecipe : RecipeFamily
{
    public WoodenTrackRecipe()
    {
        var recipe = new Recipe();
        recipe.Init("WoodenTrack", Localizer.DoStr("Wooden Rail"),
            [new IngredientElement("Wood", 4, true)], [],
            [new CraftingElement<WoodenTrackItem>(4)]);
        this.Recipes = [recipe]; this.ExperienceOnCraft = 1;
        this.LaborInCalories = CreateLaborInCaloriesValue(80, typeof(LoggingSkill));
        this.CraftMinutes = CreateCraftTimeValue(typeof(WoodenTrackRecipe), 1, typeof(LoggingSkill));
        this.Initialize(Localizer.DoStr("Wooden Rail"), typeof(WoodenTrackRecipe));
        CraftingComponent.AddRecipe(typeof(RailcraftWorkbenchObject), this);
    }
}

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class WoodenTrackStacked1Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class WoodenTrackStacked2Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.PartialStack)]
public sealed class WoodenTrackStacked3Block : PickupableBlock, IWaterLoggedBlock { }

[Serialized, Solid, Tag("Constructable"), Tag(BlockTags.FullStack)]
public sealed class WoodenTrackStacked4Block : PickupableBlock, IWaterLoggedBlock { }

public sealed class WoodenTrackFormGroup : FormGroup
{
    public override string Name => "WoodenTrack";
    public override LocString DisplayName => Localizer.DoStr("Wooden Rail");
    public override LocString DisplayDescription => Localizer.DoStr("Track shapes for minecart construction.");
    public override int SortOrder => 100;
}

public sealed class WoodenTrackStraightFormType : FormType
{
    public override string Name => "WoodenTrackStraight";
    public override LocString DisplayName => Localizer.DoStr("Straight");
    public override LocString DisplayDescription => Localizer.DoStr("One-block straight track.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 1;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(WoodenTrackStraightBlock), typeof(WoodenTrackStraight90Block), typeof(WoodenTrackStraight180Block), typeof(WoodenTrackStraight270Block))]
[IsForm(typeof(WoodenTrackStraightFormType), typeof(WoodenTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackStraightBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackStraight90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackStraight180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackStraight270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}

public sealed class WoodenTrackBendFormType : FormType
{
    public override string Name => "WoodenTrackBend";
    public override LocString DisplayName => Localizer.DoStr("Bend");
    public override LocString DisplayDescription => Localizer.DoStr("Compact ninety-degree bend. Use only at low speed.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 2;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(WoodenTrackBendBlock), typeof(WoodenTrackBend90Block), typeof(WoodenTrackBend180Block), typeof(WoodenTrackBend270Block))]
[IsForm(typeof(WoodenTrackBendFormType), typeof(WoodenTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackBendBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackBend90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackBend180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackBend270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}

public sealed class WoodenTrackStopperFormType : FormType
{
    public override string Name => "WoodenTrackStopper";
    public override LocString DisplayName => Localizer.DoStr("Stopper");
    public override LocString DisplayDescription => Localizer.DoStr("End-of-line buffer stop.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 3;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(WoodenTrackStopperBlock), typeof(WoodenTrackStopper90Block), typeof(WoodenTrackStopper180Block), typeof(WoodenTrackStopper270Block))]
[IsForm(typeof(WoodenTrackStopperFormType), typeof(WoodenTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackStopperBlock : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackStopper90Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackStopper180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackStopper270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}

[NextRamp(typeof(WoodenTrackSlope2FormType), 0)]
public sealed class WoodenTrackSlope1FormType : FormType
{
    public override string Name => "WoodenTrackSlope1";
    public override LocString DisplayName => Localizer.DoStr("Slope 1/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 1 of 4.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 4;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(WoodenTrackSlope1270Block), typeof(WoodenTrackSlope1Block), typeof(WoodenTrackSlope190Block), typeof(WoodenTrackSlope1180Block))]
[IsForm(typeof(WoodenTrackSlope1FormType), typeof(WoodenTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope1Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope190Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope1180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope1270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}

[NextRamp(typeof(WoodenTrackSlope3FormType), 0)]
public sealed class WoodenTrackSlope2FormType : FormType
{
    public override string Name => "WoodenTrackSlope2";
    public override LocString DisplayName => Localizer.DoStr("Slope 2/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 2 of 4.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 5;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(WoodenTrackSlope2270Block), typeof(WoodenTrackSlope2Block), typeof(WoodenTrackSlope290Block), typeof(WoodenTrackSlope2180Block))]
[IsForm(typeof(WoodenTrackSlope2FormType), typeof(WoodenTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope2Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope290Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope2180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope2270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}

[NextRamp(typeof(WoodenTrackSlope4FormType), 0)]
public sealed class WoodenTrackSlope3FormType : FormType
{
    public override string Name => "WoodenTrackSlope3";
    public override LocString DisplayName => Localizer.DoStr("Slope 3/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 3 of 4.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 6;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(WoodenTrackSlope3270Block), typeof(WoodenTrackSlope3Block), typeof(WoodenTrackSlope390Block), typeof(WoodenTrackSlope3180Block))]
[IsForm(typeof(WoodenTrackSlope3FormType), typeof(WoodenTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope3Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope390Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope3180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope3270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}

[NextRamp(typeof(WoodenTrackSlope1FormType), 1)]
public sealed class WoodenTrackSlope4FormType : FormType
{
    public override string Name => "WoodenTrackSlope4";
    public override LocString DisplayName => Localizer.DoStr("Slope 4/4");
    public override LocString DisplayDescription => Localizer.DoStr("Quarter-rise slope section 4 of 4.");
    public override Type GroupType => typeof(WoodenTrackFormGroup);
    public override int SortOrder => 7;
    public override int MinTier => 1;
}

[RotatedVariants(typeof(WoodenTrackSlope4270Block), typeof(WoodenTrackSlope4Block), typeof(WoodenTrackSlope490Block), typeof(WoodenTrackSlope4180Block))]
[IsForm(typeof(WoodenTrackSlope4FormType), typeof(WoodenTrackItem))]
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope4Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope490Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope4180Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
[Serialized, Constructed, Solid, BlockTier(1), Tag("Constructable")]
public sealed class WoodenTrackSlope4270Block : Block, IRepresentsItem, IWaterLoggedBlock
{
    public Type RepresentedItemType => typeof(WoodenTrackItem);
}
