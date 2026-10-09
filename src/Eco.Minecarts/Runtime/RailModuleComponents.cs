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
        if (this.Parent.IsDestroyed
            || Vector3.Distance(player.User.Position, this.Parent.Position) > this.Vehicle.CouplerOffset + 3) return;
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

[Serialized, NoIcon, AutogenClass, LocDisplayName("Rail Vehicle Condition")]
public sealed class RailConditionComponent : WorldObjectComponent, IPersistentData
{
    internal static (Type Material,int Quantity) RepairCost(Eco.Minecarts.Physics.RailVehicleSpec spec)=>spec.Key=="RollerCoasterCart"
        ?(typeof(LubricantItem),1):(spec.Tier=="Wood"?typeof(WoodPulpItem):spec.Tier=="Iron"?typeof(IronBarItem):typeof(SteelBarItem),2);
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Maintenance Cost")]
    public string MaintenanceCost
    {
        get
        {
            var cost=RepairCost(((RailVehicleObject)Parent).RailSpec);
            return $"{cost.Quantity} {Eco.Gameplay.Items.Item.Get(cost.Material).DisplayName} restores 25% condition";
        }
    }
    [Serialized, SyncToView, Autogen, PropReadOnly, LocDisplayName("Condition (%)")] public float ConditionPercent { get; private set; } = 100;
    public object PersistentData
    {
        get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailConditionComponent.PersistentData.get", this.Parent); return new RailConditionData { Percent = this.ConditionPercent }; }
        set { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailConditionComponent.PersistentData.set", this.Parent); var percent = (value as RailConditionData)?.Percent ?? 100; this.ConditionPercent = float.IsFinite(percent) ? Math.Clamp(percent,0,100) : 100; }
    }
    private Vector3 lastPosition;
    private DateTime lastTime;
    private readonly object gate = new();
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Attachment access and condition/Tick", this.Parent);
        base.Tick();
        var now = DateTime.UtcNow;
        if (this.lastTime != default && Vector3.DistanceSquared(this.Parent.Position, this.lastPosition) > .0001f)
        {
            this.ConditionPercent = Math.Max(0, this.ConditionPercent - (float)(Math.Min(5, (now - this.lastTime).TotalSeconds)
                / (((RailVehicleObject)this.Parent).RailSpec.DurabilityHours * 3600) * 100));
            this.Changed(nameof(ConditionPercent)); this.Parent.SetDirty();
        }
        this.lastTime = now; this.lastPosition = this.Parent.Position;
        if (this.ConditionPercent <= 0) this.Parent.GetComponent<MinecartMotionComponent>().Handbrake = true;
    }
    [RPC, Autogen, UITypeName("BigButton")] public void Repair(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Attachment access and condition/Repair", this.Parent);
        if (!this.Parent.IsAuthorized(player.User, AccessType.FullAccess) || Vector3.Distance(player.User.Position, this.Parent.Position) > 5) return;
        lock (this.gate)
        {
            if (this.ConditionPercent >= 100) return;
            var spec = ((RailVehicleObject)this.Parent).RailSpec;
            var cost = RepairCost(spec);
            if (player.User.Inventory.TryRemoveItems(cost.Material, cost.Quantity, player.User).Success)
            { this.ConditionPercent = Math.Min(100, this.ConditionPercent + 25); this.Changed(nameof(ConditionPercent)); this.Parent.SetDirty(); }
        }
    }
    // Older open views may still send this RPC; discard edits rather than throwing.
    [RPC] public void SetConditionPercent(Player player, float value) {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Attachment access and condition/SetConditionPercent", this.Parent); }
    [RPC, Autogen, UITypeName("BigButton")] public void Rerail(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Passengers/Attachment access and condition/Rerail", this.Parent);
        if (this.Parent.IsAuthorized(player.User,AccessType.FullAccess) && Vector3.Distance(player.User.Position,this.Parent.Position)<=5)
            this.Parent.GetComponent<MinecartMotionComponent>().RecoverOnRail();
    }
}

internal static class TenderSupply
{
    public static void Refill(RailCouplingComponent coupling)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Cargo/Tender fuel transfer/Refill", coupling.Parent);
        var group = coupling.Group();
        foreach (var engine in group.Where(x => x.Vehicle.RailSpec.Powered && !x.Vehicle.RailSpec.Tram).OrderBy(x => x.Parent.ID))
        {
            var fuel = engine.Parent.GetComponent<FuelSupplyComponent>();
            if (fuel == null || fuel.Energy + fuel.EnergyInSupply > Eco.Minecarts.Physics.RailEconomy.FuelWatts(engine.Vehicle.RailSpec) * 60) continue;
            foreach (var tender in group.Where(x => x.Vehicle.RailSpec.Tender).OrderBy(x => x.Parent.ID))
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
