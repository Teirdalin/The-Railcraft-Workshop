namespace Eco.Mods.TechTree;

using System;
using System.Collections.Generic;
using Eco.Core.Controller;
using Eco.Core.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Systems.NewTooltip;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Utils;

[Serialized]
[LocDisplayName("Minecart")]
[LocDescription("A narrow-gauge iron cart for hauling materials on rails. Crafted at the Wainwright Table using Basic Engineering.")]
[IconGroup("World Object Minimap")]
[Weight(9000)]
[SalvageCost(typeof(IronScrap), 8.0f, typeof(WoodScrap), 2.0f)]
[Ecopedia("Crafted Objects", "Vehicles", createAsSubPage: true)]
public sealed class MinecartItem : WorldObjectItem<MinecartObject>, IPersistentData
{
    private object? persistentData;

    [Serialized]
    [SyncToView]
    [NewTooltipChildren(CacheAs.Instance, flags: TTFlags.AllowNonControllerTypeForChildren)]
    // Eco populates this when a placed vehicle is picked up. A bare System.Object
    // is not valid view data and breaks client deserialization of the item.
    public object PersistentData
    {
        get => this.persistentData!;
        // Also discard the invalid placeholder if it was saved by the first build.
        set => this.persistentData = value?.GetType() == typeof(object) ? null : value;
    }
}

[RequiresSkill(typeof(BasicEngineeringSkill), 2)]
[Ecopedia("Crafted Objects", "Vehicles", subPageName: "Minecart Item")]
public sealed class MinecartRecipe : RecipeFamily
{
    public MinecartRecipe()
    {
        var recipe = new Recipe();
        recipe.Init(
            name: "Minecart",
            displayName: Localizer.DoStr("Minecart"),
            ingredients:
            [
                new IngredientElement(typeof(IronWheelItem), 2, true),
                new IngredientElement(typeof(IronBarItem), 12, typeof(BasicEngineeringSkill)),
                new IngredientElement("WoodBoard", 8, typeof(BasicEngineeringSkill)),
            ],
            garbages: [],
            items: [new CraftingElement<MinecartItem>()]);

        this.Recipes = [recipe];
        this.ExperienceOnCraft = 5;
        this.LaborInCalories = CreateLaborInCaloriesValue(150, typeof(BasicEngineeringSkill));
        this.CraftMinutes = CreateCraftTimeValue(
            beneficiary: typeof(MinecartRecipe),
            start: 5,
            skillType: typeof(BasicEngineeringSkill));
        this.Initialize(Localizer.DoStr("Minecart"), typeof(MinecartRecipe));
        CraftingComponent.AddRecipe(RailRecipeWorkshops.For(typeof(MinecartRecipe)), this);
    }
}

[Serialized]
[RequireComponent(typeof(StandaloneAuthComponent))]
[RequireComponent(typeof(PaintableComponent))]
[RequireComponent(typeof(PublicStorageComponent))]
[RequireComponent(typeof(MovableLinkComponent))]
[RequireComponent(typeof(VehicleComponent))]
[RequireComponent(typeof(CustomTextComponent))]
[RequireComponent(typeof(ModularStockpileComponent))]
[RequireComponent(typeof(MinimapComponent))]
[RequireComponent(typeof(Eco.Minecarts.Runtime.MinecartMotionComponent))]
[RequireComponent(typeof(Eco.Minecarts.Runtime.RailCouplingComponent))]
[RequireComponent(typeof(Eco.Minecarts.Runtime.MinecartRidingComponent))]
[RequireComponent(typeof(Eco.Minecarts.Runtime.RailConditionComponent))]
[Ecopedia("Crafted Objects", "Vehicles", subPageName: "Minecart Item")]
public sealed class MinecartObject : Eco.Minecarts.Runtime.RailVehicleObject, IRepresentsItem
{
    public static Eco.Minecarts.Physics.RailVehicleSpec DefaultSpecification {get;} = new("Minecart", "Minecart", 280, 2500, 12, 1.612f, .82f, .5f, 18, 0, 0, 4800, 0, Pullable: true);
    public override Eco.Minecarts.Runtime.RailVehicleCapabilities Capabilities => Eco.Minecarts.Runtime.RailVehicleCapabilities.ManualHandle;

    static MinecartObject() => WorldObject.AddOccupancy<MinecartObject>(new List<BlockOccupancy>());

    private MinecartObject() { }

    public override TableTextureMode TableTexture => TableTextureMode.Metal;
    public override bool PlacesBlocks => false;
    public override LocString DisplayName => Localizer.DoStr("Minecart");
    public Type RepresentedItemType => typeof(MinecartItem);

    protected override void Initialize()
    {
        base.Initialize();
        this.GetComponent<CustomTextComponent>().Initialize(200);
        this.GetComponent<VehicleComponent>().HumanPowered(0.8f);
        this.GetComponent<StockpileComponent>().Initialize(new Vector3i(2, 1, 2));
        Eco.Minecarts.Physics.RailVehicleBalances.InitializeStorage(this.GetComponent<PublicStorageComponent>(),this.RailSpec);
        this.GetComponent<MinimapComponent>().InitAsMovable();
        this.GetComponent<MinimapComponent>().SetCategory(Localizer.DoStr("Vehicles"));
        this.GetComponent<VehicleComponent>().Initialize(3, 1.1f, 3);
        this.GetComponent<VehicleComponent>().FailDriveMsg =
            Localizer.DoStr(string.Empty);
    }
}
