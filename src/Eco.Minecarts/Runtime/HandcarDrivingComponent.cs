using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, LocDisplayName("Handcar Controls")]
public sealed class HandcarDrivingComponent : WorldObjectComponent
{
    [Interaction(InteractionTrigger.InteractKey,"Operate / leave handcar",requiredEnvVars:new[]{"HandcarPump"},
        interactionDistance:3,priority:60,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void Operate(Player player,InteractionTriggerInfo trigger,InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Manual controls/Operate", this.Parent);
        if(this.Parent.IsDestroyed || !target.ContainsParameter("HandcarPump")
            || !this.Parent.IsAuthorized(player.User,AccessType.FullAccess)
            || Vector3.Distance(player.User.Position,this.Parent.Position)>3) return;
        var mounts=this.Parent.GetComponent<MountComponent>();
        if(mounts.Driver==player) { mounts.TryDismountPlayer(player); return; }
        if(player.MountManager.IsMounted || mounts.Driver!=null || !this.Parent.GetComponent<RailCouplingComponent>().CanBoard) return;
        // Native validation checks calories/exhaustion and grants physics ownership.
        mounts.TryMountTargetSeat(player,0);
        if(mounts.Driver==player) this.Parent.GetComponent<MinecartMotionComponent>().PrepareNativeDriver();
    }
}
