namespace Eco.Mods.TechTree;

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
using Eco.Minecarts.Runtime;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;

[Serialized, LocDisplayName("Mine Train")]
[LocDescription("A compact steam locomotive for Standard Rail. Add burnable fuel and operate the cab's throttle, brake and direction levers, or set a cruising speed on the Autopilot page.")]
[Weight(15000), IconGroup("World Object Minimap")]
[AirPollution(.1f)]
[Ecopedia("Crafted Objects", "Vehicles", createAsSubPage: true)]
public sealed class MineTrainItem : WorldObjectItem<MineTrainObject>, IPersistentData
{
    private object? persistentData;
    [Serialized, SyncToView, NewTooltipChildren(CacheAs.Instance, flags: TTFlags.AllowNonControllerTypeForChildren)]
    public object PersistentData
    {
        get => this.persistentData!;
        set => this.persistentData = value?.GetType() == typeof(object) ? null : value;
    }
}

[RequiresSkill(typeof(BasicEngineeringSkill), 4)]
public sealed class MineTrainRecipe : MinecartRailRecipeFamily
{
    public MineTrainRecipe()
    {
        var recipe = new Recipe();
        recipe.Init("MineTrain", Localizer.DoStr("Mine Train"),
            [new IngredientElement(typeof(IronBarItem), 24, typeof(BasicEngineeringSkill)),
             new IngredientElement("WoodBoard", 12, typeof(BasicEngineeringSkill)),
             new IngredientElement(typeof(IronWheelItem), 4, true),
             new IngredientElement(typeof(CastIronStoveItem), 1, true),
             new IngredientElement(typeof(LubricantItem), 2, true)], [], [new CraftingElement<MineTrainItem>()]);
        this.Configure(recipe, "Mine Train", typeof(MineTrainRecipe), 250, 10);
    }
}

[Serialized]
[RequireComponent(typeof(StandaloneAuthComponent))]
[RequireComponent(typeof(PaintableComponent))]
[RequireComponent(typeof(FuelSupplyComponent))]
[RequireComponent(typeof(FuelConsumptionComponent))]
[RequireComponent(typeof(AirPollutionComponent))]
[RequireComponent(typeof(PublicStorageComponent))]
[RequireComponent(typeof(MovableLinkComponent))]
[RequireComponent(typeof(VehicleComponent))]
[RequireComponent(typeof(CustomTextComponent))]
[RequireComponent(typeof(ModularStockpileComponent))]
[RequireComponent(typeof(MinimapComponent))]
[RequireComponent(typeof(MinecartMotionComponent))]
[RequireComponent(typeof(RailCouplingComponent))]
[RequireComponent(typeof(MineTrainDrivingComponent))]
[RequireComponent(typeof(TrainControllerComponent))]
[RequireComponent(typeof(TrainFareComponent))]
[RequireComponent(typeof(RailConditionComponent))]
public sealed class MineTrainObject : RailVehicleObject, IRepresentsItem
{
    public override Eco.Minecarts.Physics.RailVehicleSpec RailSpec => Eco.Minecarts.Physics.RailVehicleSpec.MineTrain;
    public override float CouplerOffset => .93f;
    public override double RailMassKg => 600;
    public override int DriverPriority => 100;
    public override System.Numerics.Vector3 ContactHalfSize => new(.43f, .78f, .90f);
    static MineTrainObject() => AddOccupancy<MineTrainObject>(new List<BlockOccupancy>());
    private MineTrainObject() { }
    public override TableTextureMode TableTexture => TableTextureMode.Metal;
    public override bool PlacesBlocks => false;
    public override LocString DisplayName => Localizer.DoStr("Mine Train");
    public Type RepresentedItemType => typeof(MineTrainItem);

    protected override void Initialize()
    {
        base.Initialize();
        this.GetComponent<CustomTextComponent>().Initialize(200);
        // Same native fuel/storage/pollution contracts as the installed PoweredCart.
        this.GetComponent<FuelSupplyComponent>().Initialize(2, ["Burnable Fuel"]);
        this.GetComponent<FuelConsumptionComponent>().Initialize(Eco.Minecarts.Physics.RailEconomy.FuelWatts(this.RailSpec));
        this.GetComponent<AirPollutionComponent>().Initialize(.1f);
        this.GetComponent<StockpileComponent>().Initialize(new Vector3i(1, 1, 1));
        this.GetComponent<PublicStorageComponent>().Initialize(4, 250000);
        this.GetComponent<MinimapComponent>().InitAsMovable();
        this.GetComponent<MinimapComponent>().SetCategory(Localizer.DoStr("Vehicles"));
        this.GetComponent<VehicleComponent>().Initialize((float)this.RailSpec.MaximumSpeed, 1.5f, 2);
    }
}
