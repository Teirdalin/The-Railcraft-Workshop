using Eco.Core.Controller;
using Eco.Core.Utils;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, LocDisplayName("Rail Pickup")]
public sealed class RailInfrastructurePickupComponent : WorldObjectComponent
{
    internal Result CanPickup()
    {
        if (Parent.IsDestroyed) return Result.FailLocStr("This rail has already been picked up.");
        var position = Parent.Position3i;
        // These flat track objects share the ordinary rail height. Reuse the
        // route interlock's live vehicle/body clearance and world-seam handling.
        var rail = new VoxelRail(new(position.X, position.Y, position.Z), new("Straight", 0, false));
        var radius = Parent.GetComponent<RailSwitchComponent>()?.Definition.Radius ?? 2.5f;
        return RailCouplingComponent.OccupiesSwitch(rail, radius)
            ? Result.FailLocStr("Move all rail vehicles clear of this track before picking it up.")
            : Result.Succeeded;
    }

    public override InventoryMoveResult TryPickup(Player player, InventoryChangeSet invChanges, Inventory targetInventory, bool force)
    {
        // Check at native transaction preparation, after any contract or other
        // component confirmations. This component needs no extra confirmation.
        if (!force)
        {
            var allowed = CanPickup();
            if (!allowed) return allowed;
        }
        return base.TryPickup(player, invChanges, targetInventory, force);
    }
}
