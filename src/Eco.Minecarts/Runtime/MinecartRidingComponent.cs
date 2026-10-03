using System.Numerics;
using Eco.Core.Controller;
using Eco.Core.Utils;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Mods.TechTree;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, LocDisplayName("Bucket Passenger")]
public sealed class MinecartRidingComponent : WorldObjectComponent
{
    private volatile bool boarding;
    private Player? approvedPlayer;
    private int approvedSeat;
    private MountComponent Mounts => this.Parent.GetComponent<MountComponent>();
    private Inventory Storage => this.Parent.GetComponent<PublicStorageComponent>().Inventory;
    public bool StorageLocked => this.boarding || this.Mounts.MountedPlayers.Any(player => player != this.Mounts.Driver && player.MountManager.Mount == this.Mounts);
    public double PassengerMassKg => this.StorageLocked ? 80 : 0;

    public override void PostInitialize()
    {
        base.PostInitialize();
        this.Storage.AddInvRestriction(new PassengerStorageRestriction(this));
        // All native mount RPCs must also pass this, not only our Shift+E action.
        this.Mounts.MountValidation.Add((seat, player) =>
            (seat == 0 && this.Parent.IsAuthorized(player.User, AccessType.FullAccess))
            || (this.boarding && this.approvedPlayer == player && seat == this.approvedSeat && seat is 1 or 2 && this.Storage.IsEmpty)
                ? Result.Succeeded : Result.Fail(Localizer.DoStr(string.Empty)));
        this.Mounts.PlayerMountedEvent += this.OnMounted;
        this.Mounts.PlayerDismountedEvent += this.OnDismounted;
    }

    [Interaction(InteractionTrigger.InteractKey, "Ride / leave empty minecart", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "MinecartStorage" }, interactionDistance: 3, priority: 50,
        authRequired: AccessType.ConsumerAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void Ride(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (!target.ContainsParameter("MinecartStorage") || this.Parent.IsDestroyed || Vector3.Distance(player.User.Position, this.Parent.Position) > 3) return;
        if (this.Mounts.MountedPlayers.Contains(player)) { this.Mounts.TryDismountPlayer(player); return; }
        if (!this.Parent.IsAuthorized(player.User, AccessType.ConsumerAccess)) return;
        if (player.MountManager.IsMounted) { return; }
        // The same inventory lock is taken by item-transfer transactions. Boarding
        // and insertion cannot both win the empty-bucket check.
        using (new InventoryLock(new[] { this.Storage }))
        {
            if (this.StorageLocked) { return; }
            if (!this.Storage.IsEmpty) { return; }
            this.boarding = true;
            this.approvedPlayer = player;
            this.approvedSeat = Eco.Minecarts.Track.RailGuidance.SelectPassengerSeat(
                player.User.Rotation.RotateVector(Vector3.UnitZ), this.Parent.Rotation.RotateVector(Vector3.UnitZ),
                player.User.Position - this.Parent.Position);
            try
            {
                this.Parent.CloseUIForAll(true);
                this.Mounts.TryMountTargetSeat(player, this.approvedSeat);
            }
            finally { this.approvedPlayer = null; this.approvedSeat = 0; this.boarding = false; }
        }
    }

    private void OnMounted()
    {
        if (this.StorageLocked) this.Parent.CloseUIForAll(true);
    }
    private void OnDismounted() { this.boarding = false; this.approvedPlayer = null; this.approvedSeat = 0; }

    public override void Tick()
    {
        // Motion owns rail physics; off-rail gravity belongs to the native
        // client. A passenger must not repeatedly revoke its physics controller.
    }

    public override void Destroy()
    {
        this.Mounts.PlayerMountedEvent -= this.OnMounted;
        this.Mounts.PlayerDismountedEvent -= this.OnDismounted;
        this.boarding = false;
        this.approvedPlayer = null;
        base.Destroy();
    }
}

public sealed class PassengerStorageRestriction(MinecartRidingComponent riding) : InventoryRestriction
{
    public override LocString Message => Localizer.DoStr(string.Empty);
    public override int MaxAccepted(Item item) => riding.StorageLocked ? 0 : int.MaxValue;
    public override int MaxAccepted(Item item, int quantity) => this.MaxAccepted(item);
    public override int MaxAccepted(RestrictionCheckData check, Item item) => this.MaxAccepted(item);
    public override int MaxAccepted(RestrictionCheckData check, Item item, int quantity) => this.MaxAccepted(item);
    public override int MaxPickup(RestrictionCheckData check, Item item, int totalMoved) => this.MaxAccepted(item);
}
