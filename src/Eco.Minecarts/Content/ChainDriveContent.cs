namespace Eco.Mods.TechTree;

using System;
using System.Collections.Concurrent;
using Eco.Core.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Minecarts.Runtime;
using static Eco.Minecarts.Runtime.PoweredRailDrive;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;

[Serialized, LocDisplayName("Rail Chain Drive")]
[LocDescription("Place beside chain track and supply mechanical power. At 1x, requires 2 W per connected block and lifts minecarts at up to 0.5 m/s or coaster carts at up to 1.0 m/s. Set a maximum speed from 0.25x to 3x. The drive automatically slows to available power and recovers toward that maximum when supply returns. Below the power needed for 0.25x it stops. Heavy loads move more slowly. Power loss releases the chain. Use the cart handbrake to prevent rollback. Crafted at the Railworks Workbench using Basic Engineering.")]
[Weight(12000)]
public sealed class MinecartChainDriveItem : WorldObjectItem<MinecartChainDriveObject>, IPersistentData
{
    // The cabinet is floor-mounted. The default world-object placement
    // context accepts side faces and previews it hanging beside raised rails.
    // Use the same downward-only native snap rule as the Railworks Workbench.
    protected override OccupancyContext GetOccupancyContext => new SideAttachedContext(
        DirectionAxisFlags.Down, WorldObject.GetOccupancyInfo(WorldObjectType));
    [Serialized] public object PersistentData { get; set; } = null!;
}

[RequiresSkill(typeof(BasicEngineeringSkill), 3)]
public sealed class MinecartChainDriveRecipe : MinecartRailRecipeFamily
{
    public MinecartChainDriveRecipe() => this.Configure(
        MinecartRailRecipes.Make<MinecartChainDriveItem>("Rail Chain Drive", 12, 8, 1, woodenGears: 2, skillType:typeof(BasicEngineeringSkill)),
        "Rail Chain Drive", typeof(MinecartChainDriveRecipe), 150, 5, skillType:typeof(BasicEngineeringSkill));
}

[Serialized]
[RequireComponent(typeof(OnOffComponent))]
[RequireComponent(typeof(PropertyAuthComponent))]
[RequireComponent(typeof(PowerGridComponent))]
[RequireComponent(typeof(PowerConsumptionComponent))]
[RequireComponent(typeof(ChainDriveSpeedComponent))]
[RequireComponent(typeof(RailPowerConnectionComponent))]
public class MinecartChainDriveObject : WorldObject, IRepresentsItem
{
    public virtual bool Electrical => false;
    public float MaximumSpeedMultiplier => Electrical ? 5 : 3;
    public const float WattsPerBlock = 2;
    // Eco grid cost is a gameplay balance value. Preserve the established
    // lifting simulation budget when reducing that cost, including load sharing.
    private const float LiftSimulationWattsPerBlock = 25;
    private const float CoasterLiftSimulationWattsPerBlock = 100;
    private static readonly PoweredRailRegistry<MinecartChainDriveObject> Drives=new();
    internal static IEnumerable<MinecartChainDriveObject> ActiveDrives => Drives.Values.Where(d=>!d.IsDestroyed);
    private readonly object gate = new();
    private HashSet<RailCell> cells = [];
    private readonly PoweredRailContacts carts=new(1);
    private bool running;
    private float lastDemand = -1;

    private bool OwnsRun => !Drives.Snapshot.Any(d => d.ID < this.ID && !d.IsDestroyed
        && d.GetComponent<RailPowerConnectionComponent>().Connected && d.cells.Overlaps(this.cells));
    // Do not gate recovery on Parent.Enabled: the grid itself disables it when
    // supply falls. Only unrelated faults or an explicit off switch exclude it.
    private float RequestedWatts => this.GetComponent<RailPowerConnectionComponent>().Connected && this.OwnsRun && ReadyExceptPower(this.GetComponent<PowerGridComponent>())
        ? ChainLift.GridDemand(this.cells.Count,this.GetComponent<ChainDriveSpeedComponent>().SpeedMultiplier,MaximumSpeedMultiplier) : 0;

    private float SupportedSpeed()
    {
        var grid=this.GetComponent<PowerGridComponent>().PowerGrid;
        if(grid==null || this.RequestedWatts<=0) return 0;
        var consumers=grid.Components.ToArray();
        var requested=consumers.Where(c=>c.Parent is MinecartChainDriveObject)
            .Sum(c=>((MinecartChainDriveObject)c.Parent).RequestedWatts);
        var other=consumers.Where(c=>c.Parent is not MinecartChainDriveObject && !c.EnergySelfSupply && ReadyExceptPower(c))
            .Sum(c=>Math.Max(0,c.EnergyDemand));
        return ChainLift.SupportedMultiplier(this.GetComponent<ChainDriveSpeedComponent>().SpeedMultiplier,requested,grid.EnergySupply,other,MaximumSpeedMultiplier);
    }

    static MinecartChainDriveObject() => AddOccupancyList(typeof(MinecartChainDriveObject),
        new BlockOccupancy(Vector3i.Zero, typeof(BuildingWorldObjectBlock)));
    protected override void OnDestroy()
    { var footprint=WorldOccupancy?.ToArray();var id=ObjectID;Drives.Remove(this);base.OnDestroy();PoweredRailDrive.RemoveFootprint(footprint,id); }
    public override LocString DisplayName => Localizer.DoStr("Rail Chain Drive");
    public virtual Type RepresentedItemType => typeof(MinecartChainDriveItem);
    private bool HasMechanicalPower
    {
        get
        {
            var grid=this.GetComponent<PowerGridComponent>();
            // Enabled initially defaults true while the native grid registration
            // queue is still pending. Require a real supplied grid as well.
            return grid.Enabled && grid.EnergyDemand>0 && grid.PowerGrid!=null && grid.PowerGrid.EnergySupply>=grid.EnergyDemand;
        }
    }

    protected override void Initialize()
    {
        base.Initialize();
        PoweredRailDrive.Initialize(this,Electrical);
        Drives.Register(this);
    }

    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/Tick");
        base.Tick();
        lock (this.gate)
        {
            this.cells = this.GetComponent<RailPowerConnectionComponent>().Cells;
            var ownsRun = this.OwnsRun;
            var supported=this.SupportedSpeed();
            var demand = supported>0 ? ChainLift.GridDemand(this.cells.Count,supported,MaximumSpeedMultiplier) : 0;
            if (demand != this.lastDemand)
            {
                this.GetComponent<PowerConsumptionComponent>().OverridePowerConsumption(demand);
                this.lastDemand = demand;
            }
            this.running = ownsRun && this.cells.Count != 0 && this.Enabled && this.HasMechanicalPower;
            this.GetComponent<ChainDriveSpeedComponent>().SetEffectiveSpeed(this.running ? supported : 0);
            this.SetAnimatedState("ChainRunning", this.running);
        }
    }

    internal static double PowerFor(RailCell cell, int cartId) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Vehicle propulsion lookup/PowerFor"); return LiftFor(cell,cartId).Watts; }
    // Inspection must not call LiftFor: that registers a cart and divides the
    // available traction between consumers. Match its first-drive ownership.
    internal static Dictionary<RailCell, bool> ReadPowerIndicators()
    {
        var result = new Dictionary<RailCell, bool>();
        foreach (var drive in Drives.Snapshot)
        {
            lock (drive.gate)
            {
                if (drive.IsDestroyed) continue;
                var powered = drive.GetComponent<RailPowerConnectionComponent>().Connected && drive.running && drive.Enabled && drive.HasMechanicalPower
                    && drive.GetComponent<ChainDriveSpeedComponent>().EffectiveSpeedMultiplier > 0;
                foreach (var cell in drive.cells) result.TryAdd(cell, powered);
            }
        }
        return result;
    }
    internal static (double Watts,float Multiplier) LiftFor(RailCell cell, int cartId) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Vehicle propulsion lookup/LiftFor"); return LiftFor(cell,cartId,false); }
    // A consist is one consumer per drive, even if several of its cars contact
    // the same powered run. Retain contact until the last car clears the chain.
    internal static (double Watts,float Multiplier) LiftForConsist(IEnumerable<RailCell> cells,int leaderId)
        => LiftBudgetForConsist(cells,leaderId,false);
    internal static (double Watts,float Multiplier) CoasterLiftForConsist(IEnumerable<RailCell> cells,int leaderId)
        => LiftBudgetForConsist(cells,leaderId,true);
    private static (double Watts,float Multiplier) LiftBudgetForConsist(IEnumerable<RailCell> cells,int leaderId,bool coaster)
    {
        var remaining=new HashSet<RailCell>(cells);double total=0;var speed=float.PositiveInfinity;
        foreach(var drive in Drives.Snapshot)
        {
            if(drive.IsDestroyed)continue;
            lock(drive.gate)
            {
                if(!remaining.Overlaps(drive.cells))continue;
                remaining.ExceptWith(drive.cells); // Same first-drive ownership as LiftFor.
                var multiplier=drive.GetComponent<ChainDriveSpeedComponent>().EffectiveSpeedMultiplier;
                if(!drive.GetComponent<RailPowerConnectionComponent>().Connected||!drive.running||!drive.Enabled||!drive.HasMechanicalPower||multiplier<=0)continue;
                var now=DateTime.UtcNow;
                drive.carts.Touch(leaderId,now);
                total+=drive.cells.Count*(coaster?CoasterLiftSimulationWattsPerBlock:LiftSimulationWattsPerBlock)*multiplier/drive.carts.Count;
                speed=Math.Min(speed,multiplier);
            }
            if(remaining.Count==0)break;
        }
        return (total,float.IsFinite(speed)?speed:1);
    }
    internal static float PoweredSpeedFor(RailCell cell)
    {
        foreach(var drive in Drives.Snapshot)
        {
            if(drive.IsDestroyed)continue;
            lock(drive.gate)
            {
                if(!drive.cells.Contains(cell))continue;
                return drive.GetComponent<RailPowerConnectionComponent>().Connected&&drive.running&&drive.Enabled&&drive.HasMechanicalPower
                    ?drive.GetComponent<ChainDriveSpeedComponent>().EffectiveSpeedMultiplier:0;
            }
        }
        return 0;
    }
    internal static (double Watts,float Multiplier) LiftFor(RailCell cell, int cartId, bool coaster)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Vehicle propulsion lookup/LiftFor");
        // One drive owns a run; parallel drives cannot accidentally multiply free traction.
        foreach (var drive in Drives.Snapshot)
        {
            if (drive.IsDestroyed) continue;
            lock (drive.gate)
            {
                if (!drive.cells.Contains(cell)) continue;
                var multiplier=drive.GetComponent<ChainDriveSpeedComponent>().EffectiveSpeedMultiplier;
                if (!drive.GetComponent<RailPowerConnectionComponent>().Connected || !drive.running || !drive.Enabled || !drive.HasMechanicalPower) return (0,multiplier);
                var now = DateTime.UtcNow;
                drive.carts.Touch(cartId,now);
                var wattsPerBlock=coaster ? CoasterLiftSimulationWattsPerBlock : LiftSimulationWattsPerBlock;
                return (drive.cells.Count * wattsPerBlock * multiplier / drive.carts.Count,multiplier);
            }
        }
        return (0,1);
    }
}
