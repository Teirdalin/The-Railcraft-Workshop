using System.Diagnostics;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Core.PropertyHandling;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Mods.TechTree;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, LocDisplayName("Passenger Seating")]
public sealed class RailPassengerComponent : WorldObjectComponent
{
    private RailVehicleObject Vehicle => (RailVehicleObject)this.Parent;
    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Attachment access and condition/PostInitialize", this.Parent);
        base.PostInitialize();
        if (this.Vehicle.RailSpec.PassengerSeats == 0) return;
        this.Parent.GetComponent<MountComponent>().MountValidation.Add((seat,player) =>
        {
            if(seat<=0 || seat>this.Vehicle.RailSpec.PassengerSeats) return Eco.Core.Utils.Result.Fail(LocString.Empty);
            var fares=this.Parent.GetComponent<TrainFareComponent>();
            if(fares.HasAccess(player,this.Vehicle)) return Eco.Core.Utils.Result.Succeeded;
            fares.RequestBoard(player,this.Vehicle,seat);
            return Eco.Core.Utils.Result.Fail(LocString.Empty);
        });
    }
    [Interaction(InteractionTrigger.InteractKey, "Ride / leave passenger car", requiredEnvVars: new[] { "RailPassengerSeat" },
        interactionDistance: 4, priority: 60, authRequired: AccessType.None, flags: InteractionFlags.BlocksOtherInteraction)]
    public void Board(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Attachment access and condition/Board", this.Parent);
        if (player == null || this.Parent.IsDestroyed
            || !RailWorldDistance.Within(player.User.Position, this.Parent.Position, this.Vehicle.CouplerOffset + 3)) return;
        var mounts = this.Parent.GetComponent<MountComponent>();
        if (mounts.MountedPlayers.Contains(player)) { mounts.TryDismountPlayer(player); return; }
        if (player.MountManager.IsMounted || !target.TryGetParameter("RailPassengerSeat", out var value)
            || !int.TryParse(value?.ToString(), out var seat) || seat < 1 || seat > this.Vehicle.RailSpec.PassengerSeats) return;
        this.Parent.GetComponent<TrainFareComponent>().RequestBoard(player,this.Vehicle,seat);
    }
}

[Serialized]
public sealed class RailConditionData
{
    [Serialized] public float Percent { get; set; } = 100;
}

[Serialized, NoIcon, AutogenClass, LocDisplayName("Rail Recovery")]
public sealed class RailConditionComponent : WorldObjectComponent, IPersistentData
{
    // Retained serialized percentage/data for old worlds and carried vehicles.
    // After first initialization, native Parts is the only health authority.
    private float conditionPercent=100;
    private PartsComponent? parts;
    private bool migrateCondition;
    private Vector3 lastPosition;
    private long lastTimestamp;
    private readonly object gate=new();
    [Serialized] public float ConditionPercent
    {
        get=>parts?.Parts!=null ? (parts.AllPartsWorking ? parts.MinDurabilityPercent()*100 : 0) : Volatile.Read(ref conditionPercent);
        private set=>Volatile.Write(ref conditionPercent,float.IsFinite(value)?Math.Clamp(value,0,100):100);
    }
    public object PersistentData
    {
        get {using var profile=RailProfile.Measure("Persistence/Component snapshots/RailConditionComponent.PersistentData.get",Parent);lock(gate)return new RailConditionData{Percent=ConditionPercent};}
        set {using var profile=RailProfile.Measure("Persistence/Component snapshots/RailConditionComponent.PersistentData.set",Parent);lock(gate){ConditionPercent=(value as RailConditionData)?.Percent??100;lastTimestamp=0;}}
    }
    internal void ConfigureParts()
    {
        parts=Parent.GetComponent<PartsComponent>();
        migrateCondition=parts.PersistentData is PartsComponent.PartsInventoryData{FirstPlacement:true};
        parts.Config(()=>Localizer.DoStr("Wear follows rail use and vehicle durability settings, scaled by the server vehicle maintenance rate."),
            RailVehicleMaintenance.RequiredParts(((RailVehicleObject)Parent).RailSpec));
        // Native VehicleComponent also charges occupied vehicles. One rail
        // adapter covers occupied and unattended stock without double billing.
        Parent.GetComponent<VehicleComponent>().ReducePartDurabilityPerHourOfUse=0;
    }
    public override void PostInitialize()
    {
        base.PostInitialize();
        if(migrateCondition && parts?.Parts!=null)
        {
            foreach(var stack in parts.Parts.NonEmptyStacks)
                if(stack.Item is PartItem part)part.SetDurability(part.OriginalMaxDurability*conditionPercent/100);
            // Refresh native working flags/status without applying decay or
            // changing the migrated condition through a server multiplier.
            parts.ConsumeDurabilityNow(null!,0);
            migrateCondition=false;
            Parent.SetDirty();
        }
        if(parts!=null && RailVehicleMaintenance.MigrateTrainParts(((RailVehicleObject)Parent).RailSpec,parts))
            Parent.SetDirty();
    }
    internal bool RepairRequired
    {
        get
        {
            var current=parts;
            if(current?.Parts==null)return conditionPercent<=0;
            if(current.AllPartsWorking)return false;
            if(Eco.Gameplay.Players.DifficultySettingsConfig.Advanced.BrokenPartsWillDisableVehicles)return true;
            // Native AllPartsPresent can be overwritten by a later healthy
            // slot. Missing any required part must still prevent rail motion.
            foreach(var required in current.PartsRequired)
            {
                var count=0;
                foreach(var stack in current.Parts.NonEmptyStacks)
                    if(stack.Item.Name==required.TypeName)count+=stack.Quantity;
                if(count<required.Quantity)return true;
            }
            return false;
        }
    }
    internal double SpeedFactor=>parts?.AllPartsWorking==false?.5:1;
    public override void Tick()
    {
        using var profile=RailProfile.Measure("Passengers/Attachment access and condition/Tick",Parent);
        base.Tick();
        var wear=0d;User? actor=null;
        lock(gate)
        {
            var now=Stopwatch.GetTimestamp();var position=Parent.Position;
            var driver=Parent.GetComponent<VehicleComponent>().Driver;
            if(lastTimestamp!=0 && parts?.Parts!=null && (driver!=null || RailWorldDistance.Squared(position,lastPosition)>.0001f))
            {
                var seconds=Math.Clamp(Stopwatch.GetElapsedTime(lastTimestamp,now).TotalSeconds,0,5);
                var rate=Eco.Gameplay.Systems.BalanceConfig.Obj?.VehicleMaintenanceDecayPercentage??10;
                wear=seconds/(((RailVehicleObject)Parent).RailSpec.DurabilityHours*3600)*100*Math.Max(0,rate)/10d;
                actor=driver?.User??Parent.Creator;
            }
            lastTimestamp=now;lastPosition=position;
        }
        if(wear>0)
        {
            // Native consumption publishes status callbacks. Keep those outside
            // the clock/snapshot gate, as with the existing motion lock order.
            parts!.ConsumeDurabilityAccumulated(actor!,wear);
            // Native small additions do not dirty the owner until their batch
            // flush. Persist queued wear even for an occupied stationary car.
            Parent.SetDirty();
        }
        // Native inventory replacement/bounty repairs take effect immediately.
        // Avoid condition/motion gate inversion when waking the brake.
        if(RepairRequired)Parent.GetComponent<MinecartMotionComponent>().Handbrake=true;
    }
    private bool CanMaintain(Player player)=>player!=null && !Parent.IsDestroyed
        && Parent.IsAuthorized(player.User,AccessType.FullAccess) && RailWorldDistance.Within(player.User.Position,Parent.Position,5);
    // Already-open old views may still send Repair. Open the current native
    // object UI; never consume bars/pulp or mutate durability through this RPC.
    [RPC] public void Repair(Player player){if(CanMaintain(player))Parent.OpenUI(player);}
    [RPC] public void SetConditionPercent(Player player,float value) { }
    [RPC, Autogen, UITypeName("BigButton")] public void Rerail(Player player)
    {if(CanMaintain(player))Parent.GetComponent<MinecartMotionComponent>().RecoverOnRail();}
}

internal static class TenderSupply
{
    public static void Refill(RailCouplingComponent coupling)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Cargo/Tender fuel transfer/Refill", coupling.Parent);
        var group = coupling.Group();
        // Most once-per-second polls have nothing to transfer. Inspect live
        // reserves and the existing cached consist before allocating ordered
        // engine/tender enumerations. Recheck reserves below before moving fuel.
        var needsFuel = false;
        foreach (var member in group)
        {
            var spec = member.Vehicle.RailSpec;
            if (!spec.Powered || spec.Tram) continue;
            var supply = member.Parent.GetComponent<FuelSupplyComponent>();
            if (supply != null && !(supply.Energy + supply.EnergyInSupply > Eco.Minecarts.Physics.RailEconomy.FuelWatts(spec) * 60))
            { needsFuel = true; break; }
        }
        if (!needsFuel) return;
        var hasSupplier = false;
        foreach (var member in group)
            if (Eco.Minecarts.Physics.RailVehicleBalances.SuppliesLocomotiveFuel(member.Vehicle.RailSpec))
            { hasSupplier = true; break; }
        if (!hasSupplier) return;
        foreach (var engine in group.Where(x => x.Vehicle.RailSpec.Powered && !x.Vehicle.RailSpec.Tram).OrderBy(x => x.Parent.ID))
        {
            var fuel = engine.Parent.GetComponent<FuelSupplyComponent>();
            if (fuel == null || fuel.Energy + fuel.EnergyInSupply > Eco.Minecarts.Physics.RailEconomy.FuelWatts(engine.Vehicle.RailSpec) * 60) continue;
            foreach (var tender in group.Where(x => Eco.Minecarts.Physics.RailVehicleBalances.SuppliesLocomotiveFuel(x.Vehicle.RailSpec)).OrderBy(x => x.Parent.ID))
            {
                var source = tender.Parent.GetComponent<PublicStorageComponent>().Inventory;
                foreach (var stack in source.GroupedStacks.ToArray())
                {
                    if (stack.Item.Fuel <= 0 || !fuel.Inventory.AcceptsItem(stack.Item)) continue;
                    // Native transaction locks both inventories and honors their restrictions.
                    source.MoveAllItems(stack.Item, Math.Min(stack.Quantity, 4), fuel.Inventory, true);
                    if (fuel.Inventory.NonEmptyStacks.Sum(x => x.Quantity) >= 4) break;
                }
                if (fuel.Inventory.NonEmptyStacks.Sum(x => x.Quantity) >= 4) break;
            }
        }
    }
}
