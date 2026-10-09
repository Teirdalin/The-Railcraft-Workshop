using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Players;
using Eco.Minecarts.Physics;
using Eco.Shared.Items;
using Eco.Shared.Networking;
using Eco.Shared.Localization;

namespace Eco.Minecarts.Runtime;

public sealed partial class MinecartMotionComponent
{
    private ManualMinecartController? manualCart;
    internal ManualMinecartController? ManualCart=>Parent is RailVehicleObject cart&&ManualMinecartController.Applies(cart)
        ?manualCart??=new(this):null;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Minecart Motion")] public string ManualMotionStatus {get;private set;}="";
    [SyncToView,Autogen,AutoRPC(AccessType.FullAccess),LocDisplayName("Minecart Diagnostics")] public bool MinecartDiagnosticsEnabled {get;set;}
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Minecart State")] public string ManualMotionDiagnostics {get;private set;}="";
    private DateTime nextManualDiagnostics;
    // Do not take another cart's movement lock while holding the touched cart's
    // lock: coupled-follow ticks acquire them in leader-to-follower order.
    internal void QueueManualShove(Player player,RailCouplingComponent touched)
    {
        ThreadPool.QueueUserWorkItem(_=>{lock(gate){if(!Parent.IsDestroyed&&!motionStopped&&!pickupPending){if(ManualCart is {} manual)manual.ApplyShove(player,touched);else ApplyTrainShove(player,touched);}}});
    }
    internal void PublishManualDiagnostics(ManualCartRailState? state,string status,Player? holder)
    {
        if(ManualMotionStatus!=status){ManualMotionStatus=status;this.Changed(nameof(ManualMotionStatus));}
        if(!MinecartDiagnosticsEnabled||DateTime.UtcNow<nextManualDiagnostics)return;
        nextManualDiagnostics=DateTime.UtcNow.AddMilliseconds(500);
        ManualMotionDiagnostics=$"Server {Parent.Position}; rotation {Parent.Rotation}; rail {state?.Rail.Cell}; progress {state?.Progress:0.000}; speed {state?.Speed:0.000}; acceleration {state?.Acceleration:0.000}; facing {state?.Facing}; sample {state?.Sequence}; holder {holder?.User.Name??"none"}; passengers {Parent.GetComponent<Eco.Gameplay.Components.MountComponent>().MountedPlayers.Count()}; authority server";
        this.Changed(nameof(ManualMotionDiagnostics));
    }
    // Optional client diagnostic hooks submit observed positions only. This RPC
    // never changes authority, movement, a cart transform or a passenger position.
    [RPC] public string CompareMinecartClientPose(Player player,Vector3 root,Vector3 visual,Vector3 collision)
    {
        if(ManualCart==null||!MinecartDiagnosticsEnabled||player==null||!Parent.IsAuthorized(player.User,AccessType.FullAccess))return "Minecart diagnostics disabled or access denied";
        if(!float.IsFinite(root.X)||!float.IsFinite(root.Y)||!float.IsFinite(root.Z))return "Invalid client sample";
        lock(gate)
        {
            var at=ManualCart.State;
            return $"Server={Parent.Position}; client={root}; visual={visual}; collision={collision}; clientError={Vector3.Distance(Parent.Position,root):0.000}; visualRootError={Vector3.Distance(root,visual):0.000}; rail={at?.Rail.Cell}; t={at?.Progress:0.000}; speed={at?.Speed:0.000}; facing={at?.Facing}; sequence={at?.Sequence}; authority=server";
        }
    }
}
