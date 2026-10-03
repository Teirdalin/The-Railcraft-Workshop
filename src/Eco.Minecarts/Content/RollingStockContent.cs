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
        this.GetComponent<PublicStorageComponent>().Initialize(spec.Slots, (int)(spec.CargoKg * 1000));
        this.GetComponent<MinimapComponent>().InitAsMovable();
        this.GetComponent<MinimapComponent>().SetCategory(Localizer.DoStr("Vehicles"));
        if (spec.Pullable || spec.HumanPowered) this.GetComponent<VehicleComponent>().HumanPowered(spec.HumanPowered ? 1.2f : .8f);
        this.GetComponent<VehicleComponent>().Initialize((float)spec.MaximumSpeed, 1, spec.Powered ? Math.Max(2,spec.PassengerSeats+1) : spec.Pullable ? 3 : spec.PassengerSeats + 1);
        if (spec.Powered)
        {
            this.GetComponent<FuelSupplyComponent>().Initialize(spec.Length > 3 ? 4 : 2, ["Burnable Fuel"]);
            this.GetComponent<FuelConsumptionComponent>().Initialize(RailEconomy.FuelWatts(spec));
            this.GetComponent<AirPollutionComponent>().Initialize(.1f);
        }
    }
}

[Serialized, RequireComponent(typeof(MinecartRidingComponent))]
public sealed class WoodenMinecartObject : RollingStockObject { static WoodenMinecartObject() => AddOccupancy<WoodenMinecartObject>([]); }
[Serialized, RequireComponent(typeof(FuelSupplyComponent)), RequireComponent(typeof(FuelConsumptionComponent)), RequireComponent(typeof(AirPollutionComponent)), RequireComponent(typeof(TrainControllerComponent)), RequireComponent(typeof(TramRouteComponent))]
public sealed class HeritageTramObject : RollingStockObject { static HeritageTramObject() => AddOccupancy<HeritageTramObject>([]); }
[Serialized, RequireComponent(typeof(HandcarDrivingComponent))]
public sealed class RailroadHandcarObject : RollingStockObject { static RailroadHandcarObject() => AddOccupancy<RailroadHandcarObject>([]); }
[Serialized, RequireComponent(typeof(FuelSupplyComponent)), RequireComponent(typeof(FuelConsumptionComponent)), RequireComponent(typeof(AirPollutionComponent)), RequireComponent(typeof(MineTrainDrivingComponent)), RequireComponent(typeof(TrainControllerComponent))]
public sealed class PassengerLocomotiveObject : RollingStockObject { static PassengerLocomotiveObject() => AddOccupancy<PassengerLocomotiveObject>([]); }
[Serialized, RequireComponent(typeof(FuelSupplyComponent)), RequireComponent(typeof(FuelConsumptionComponent)), RequireComponent(typeof(AirPollutionComponent)), RequireComponent(typeof(MineTrainDrivingComponent)), RequireComponent(typeof(TrainControllerComponent))]
public sealed class FreightLocomotiveObject : RollingStockObject { static FreightLocomotiveObject() => AddOccupancy<FreightLocomotiveObject>([]); }
[Serialized, RequireComponent(typeof(FuelSupplyComponent)), RequireComponent(typeof(FuelConsumptionComponent)), RequireComponent(typeof(AirPollutionComponent)), RequireComponent(typeof(MineTrainDrivingComponent)), RequireComponent(typeof(TrainControllerComponent))]
public sealed class LargeTrainEngineObject : RollingStockObject { static LargeTrainEngineObject() => AddOccupancy<LargeTrainEngineObject>([]); }
[Serialized] public sealed class PassengerCarObject : RollingStockObject { static PassengerCarObject() => AddOccupancy<PassengerCarObject>([]); }
[Serialized] public sealed class CoalTenderObject : RollingStockObject { static CoalTenderObject() => AddOccupancy<CoalTenderObject>([]); }
[Serialized] public sealed class LargeCargoCarObject : RollingStockObject { static LargeCargoCarObject() => AddOccupancy<LargeCargoCarObject>([]); }
[Serialized] public sealed class LargePassengerCarObject : RollingStockObject { static LargePassengerCarObject() => AddOccupancy<LargePassengerCarObject>([]); }
[Serialized] public sealed class LargeCoalTenderObject : RollingStockObject { static LargeCoalTenderObject() => AddOccupancy<LargeCoalTenderObject>([]); }

public abstract class RailModuleItem<T> : WorldObjectItem<T>, IPersistentData where T : WorldObject
{
    [Serialized, SyncToView, NewTooltipChildren(CacheAs.Instance, flags: TTFlags.AllowNonControllerTypeForChildren)]
    public object PersistentData { get; set; } = null!;
}
[Serialized, LocDisplayName("Wooden Minecart"), Weight(10000)] public sealed class WoodenMinecartItem : RailModuleItem<WoodenMinecartObject> { }
[Serialized, LocDisplayName("Heritage Tram"), LocDescription("Automated city tram. Runs from a mechanically powered Tram Rail network, with a small onboard burner for compatible standard rail. Set its Text for front and rear destination boards; configure its route in the Tram Route page."), Weight(15000)]
public sealed class HeritageTramItem : RailModuleItem<HeritageTramObject> { }
[Serialized, LocDisplayName("Railroad Handcar"), LocDescription("Human-powered rail platform with a pumping lever. Operate from the platform; light loads only. Uses calories, not fuel."), Weight(10000)]
public sealed class RailroadHandcarItem : RailModuleItem<RailroadHandcarObject> { }
[Serialized, LocDisplayName("Passenger Locomotive"), Weight(15000)] public sealed class PassengerLocomotiveItem : RailModuleItem<PassengerLocomotiveObject> { }
[Serialized, LocDisplayName("Heavy-Haul Locomotive"), Weight(15000)] public sealed class FreightLocomotiveItem : RailModuleItem<FreightLocomotiveObject> { }
[Serialized, LocDisplayName("Passenger Car"), Weight(15000)] public sealed class PassengerCarItem : RailModuleItem<PassengerCarObject> { }
[Serialized, LocDisplayName("Coal Tender"), Weight(15000)] public sealed class CoalTenderItem : RailModuleItem<CoalTenderObject> { }
[Serialized, LocDisplayName("Large Train Engine"), Weight(15000)] public sealed class LargeTrainEngineItem : RailModuleItem<LargeTrainEngineObject> { }
[Serialized, LocDisplayName("Large Cargo Car"), Weight(15000)] public sealed class LargeCargoCarItem : RailModuleItem<LargeCargoCarObject> { }
[Serialized, LocDisplayName("Large Passenger Car"), Weight(15000)] public sealed class LargePassengerCarItem : RailModuleItem<LargePassengerCarObject> { }
[Serialized, LocDisplayName("Large Coal Tender"), Weight(15000)] public sealed class LargeCoalTenderItem : RailModuleItem<LargeCoalTenderObject> { }

public abstract class RollingStockRecipe<T> : RecipeFamily where T : Item, new()
{
    protected RollingStockRecipe()
    {
        var spec = RailVehicleSpec.Find(typeof(T).Name.Replace("Item", ""));
        var wood = spec.Tier == "Wood";
        var steel = spec.Tier is "Steel" or "Industry";
        var skill = wood ? typeof(LoggingSkill) : steel ? typeof(MechanicsSkill) : typeof(BasicEngineeringSkill);
        var recipe = new Recipe();
        var cost=RailEconomy.BuildCost(spec.Key);
        // Full-size stock uses structural lumber; compact vehicles retain boards.
        var ingredients = new List<IngredientElement> { new(spec.Tier == "Industry" ? "Lumber" : "WoodBoard", cost.Timber, skill) };
        if (wood) ingredients.Add(new IngredientElement("HewnLog", cost.HewnLogs, skill));
        else ingredients.Add(new IngredientElement(steel ? typeof(SteelBarItem) : typeof(IronBarItem), cost.Metal, skill));
        if (cost.Stoves>0) ingredients.Add(new IngredientElement(typeof(CastIronStoveItem), cost.Stoves, true));
        recipe.Init(spec.Key, Localizer.DoStr(spec.Name), ingredients, [], [new CraftingElement<T>()]);
        this.Recipes = [recipe]; this.ExperienceOnCraft = 4;
        this.LaborInCalories = CreateLaborInCaloriesValue(cost.Labor, skill);
        this.CraftMinutes = CreateCraftTimeValue(this.GetType(), cost.Minutes, skill);
        this.Initialize(Localizer.DoStr(spec.Name), this.GetType());
        CraftingComponent.AddRecipe(typeof(RailcraftWorkbenchObject), this);
    }
}
[RequiresSkill(typeof(LoggingSkill), 1)] public sealed class WoodenMinecartRecipe : RollingStockRecipe<WoodenMinecartItem> { }
[RequiresSkill(typeof(BasicEngineeringSkill), 3)] public sealed class HeritageTramRecipe : RollingStockRecipe<HeritageTramItem> { }
[RequiresSkill(typeof(BasicEngineeringSkill), 2)] public sealed class RailroadHandcarRecipe : RollingStockRecipe<RailroadHandcarItem> { }
[RequiresSkill(typeof(MechanicsSkill), 3)] public sealed class PassengerLocomotiveRecipe : RollingStockRecipe<PassengerLocomotiveItem> { }
[RequiresSkill(typeof(MechanicsSkill), 3)] public sealed class FreightLocomotiveRecipe : RollingStockRecipe<FreightLocomotiveItem> { }
[RequiresSkill(typeof(BasicEngineeringSkill), 3)] public sealed class PassengerCarRecipe : RollingStockRecipe<PassengerCarItem> { }
[RequiresSkill(typeof(BasicEngineeringSkill), 3)] public sealed class CoalTenderRecipe : RollingStockRecipe<CoalTenderItem> { }
[RequiresSkill(typeof(MechanicsSkill), 5)] public sealed class LargeTrainEngineRecipe : RollingStockRecipe<LargeTrainEngineItem> { }
[RequiresSkill(typeof(MechanicsSkill), 4)] public sealed class LargeCargoCarRecipe : RollingStockRecipe<LargeCargoCarItem> { }
[RequiresSkill(typeof(MechanicsSkill), 4)] public sealed class LargePassengerCarRecipe : RollingStockRecipe<LargePassengerCarItem> { }
[RequiresSkill(typeof(MechanicsSkill), 4)] public sealed class LargeCoalTenderRecipe : RollingStockRecipe<LargeCoalTenderItem> { }
