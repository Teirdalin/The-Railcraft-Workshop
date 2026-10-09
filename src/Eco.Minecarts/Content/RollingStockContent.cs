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
using Eco.Minecarts.Physics;
using Eco.Minecarts.Runtime;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;

[Serialized]
[RequireComponent(typeof(StandaloneAuthComponent)), RequireComponent(typeof(PaintableComponent))]
[RequireComponent(typeof(PublicStorageComponent)), RequireComponent(typeof(MovableLinkComponent))]
[RequireComponent(typeof(VehicleComponent)), RequireComponent(typeof(CustomTextComponent))]
[RequireComponent(typeof(ModularStockpileComponent)), RequireComponent(typeof(MinimapComponent))]
[RequireComponent(typeof(MinecartMotionComponent)), RequireComponent(typeof(RailCouplingComponent))]
[RequireComponent(typeof(RailPassengerComponent)), RequireComponent(typeof(RailConditionComponent))]
[RequireComponent(typeof(TrainFareComponent))]
public abstract class RollingStockObject : RailVehicleObject, IRepresentsItem
{
    public override RailVehicleSpec RailSpec => RailVehicleSpec.Find(this.GetType().Name.Replace("Object", ""));
    public override float CouplerOffset => this.RailSpec.Length / 2;
    public override double RailMassKg => this.RailSpec.EmptyKg;
    public override int DriverPriority => this.RailSpec.Powered ? (int)this.RailSpec.TractionN : 0;
    public override System.Numerics.Vector3 ContactHalfSize => new(this.RailSpec.BodyWidth / 2, .78f, this.CouplerOffset - .03f);
    public override TableTextureMode TableTexture => this.RailSpec.Tier == "Wood" ? TableTextureMode.Wood : TableTextureMode.Metal;
    public override bool PlacesBlocks => false;
    public override LocString DisplayName => Localizer.DoStr(this.RailSpec.Name);
    public Type RepresentedItemType => this.GetType().Assembly.GetType("Eco.Mods.TechTree." + this.RailSpec.Key + "Item")!;
    protected override void Initialize()
    {
        base.Initialize();
        var spec = this.RailSpec;
        this.GetComponent<CustomTextComponent>().Initialize(200);
        this.GetComponent<StockpileComponent>().Initialize(new Vector3i(2, 1, spec.Length > 3 ? 4 : 2));
        RailVehicleBalances.InitializeStorage(this.GetComponent<PublicStorageComponent>(),spec);
        this.GetComponent<MinimapComponent>().InitAsMovable();
        this.GetComponent<MinimapComponent>().SetCategory(Localizer.DoStr("Vehicles"));
        if (spec.Pullable || spec.HumanPowered) this.GetComponent<VehicleComponent>().HumanPowered(spec.HumanPowered ? 1.2f : .8f);
        this.GetComponent<VehicleComponent>().Initialize((float)spec.MaximumSpeed, 1, spec.Powered ? Math.Max(2,spec.PassengerSeats+1) : spec.Pullable ? 3 : spec.PassengerSeats + 1);
        if (Capabilities.HasFlag(RailVehicleCapabilities.FueledMotor))
        {
            RailFuelSystem.Initialize(this);
        }
        else if (spec.Tram && this.GetComponent<FuelSupplyComponent>() is { } legacyFuel)
        {
            // Old saves retain this optional component and its inventory. Eco
            // does not serialize fuelTags, so it must be configured before
            // InitializeComponents even though tram propulsion never uses it.
            legacyFuel.Initialize(spec.Length > 3 ? 4 : 2, ["Burnable Fuel"]);
        }
    }
}

public abstract class RailModuleItem<T> : WorldObjectItem<T>, IPersistentData where T : WorldObject
{
    [Serialized, SyncToView, NewTooltipChildren(CacheAs.Instance, flags: TTFlags.AllowNonControllerTypeForChildren)]
    public object PersistentData { get; set; } = null!;
}
public abstract class RollingStockRecipe<T> : RecipeFamily where T : Item, new()
{
    protected RollingStockRecipe()
    {
        var spec = RailVehicleSpec.Find(typeof(T).Name.Replace("Item", ""));
        var wood = spec.Tier == "Wood";
        var steel = spec.Tier is "Steel" or "Industry";
        var skill = RailRecipeWorkshops.VehicleSkill(this.GetType());
        var recipe = new Recipe();
        var cost=RailEconomy.BuildCost(spec.Key);
        // Full-size stock uses structural lumber; compact vehicles retain boards.
        var ingredients = new List<IngredientElement> { new(spec.Tier == "Industry" ? "Lumber" : "WoodBoard", cost.Timber, skill) };
        if (wood) ingredients.Add(new IngredientElement("HewnLog", cost.HewnLogs, skill));
        else ingredients.Add(new IngredientElement(steel ? typeof(SteelBarItem) : typeof(IronBarItem), cost.Metal, skill));
        if (cost.Stoves>0) ingredients.Add(new IngredientElement(typeof(CastIronStoveItem), cost.Stoves, true));
        if (cost.Fabric>0) ingredients.Add(new IngredientElement("Fabric", cost.Fabric, skill));
        recipe.Init(spec.Key, Localizer.DoStr(spec.Name), ingredients, [], [new CraftingElement<T>()]);
        this.Recipes = [recipe]; this.ExperienceOnCraft = 4;
        this.LaborInCalories = CreateLaborInCaloriesValue(cost.Labor, skill);
        this.CraftMinutes = CreateCraftTimeValue(this.GetType(), cost.Minutes, skill);
        this.Initialize(Localizer.DoStr(spec.Name), this.GetType());
        CraftingComponent.AddRecipe(RailRecipeWorkshops.For(this.GetType()), this);
    }
}
