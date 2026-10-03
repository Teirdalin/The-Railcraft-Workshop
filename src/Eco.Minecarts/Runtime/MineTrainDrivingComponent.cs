using System.Numerics;
using Eco.Core.Controller;
using Eco.Shared.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;
using Eco.Shared.Networking;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, AutogenClass, CreateComponentTabLoc("Locomotive Status",true), LocDisplayName("Locomotive Status")]
public sealed class MineTrainDrivingComponent : WorldObjectComponent
{
    private int switchStep;
    private int switchDriver;
    private Player? cabOperator;
    private bool mountValidationInstalled;

    public override void PostInitialize()
    {
        base.PostInitialize();
        if (mountValidationInstalled) return;
        mountValidationInstalled = true;
        // Seat zero is deliberately reserved and inaccessible: native driver
        // mounting would grant client physics authority. The sole operator is a passenger.
        Parent.GetComponent<MountComponent>().MountValidation.Add((seat, player) =>
            player != null && seat == 1 && Parent.IsAuthorized(player.User, AccessType.FullAccess)
                ? Eco.Core.Utils.Result.Succeeded : Eco.Core.Utils.Result.Fail(LocString.Empty));
        Parent.GetComponent<MountComponent>().PlayerDismountedEvent += OnCabDismount;
    }
    private void OnCabDismount()
    {
        if(cabOperator!=null && !Parent.GetComponent<MountComponent>().MountedPlayers.Contains(cabOperator))
            ReleaseOperator("operator dismounted");
    }
    private void ReleaseOperator(string reason)
    {
        cabOperator=null; switchStep=0;
        Parent.GetComponent<TrainControllerComponent>()?.HandoffToAutopilot();
        this.Changed(nameof(Throttle)); this.Changed(nameof(DriveMode)); this.Changed(nameof(SwitchCommand));
    }
    public override void Destroy()
    {
        if(mountValidationInstalled) Parent.GetComponent<MountComponent>().PlayerDismountedEvent -= OnCabDismount;
        cabOperator = null;
        base.Destroy();
    }
    [SyncToView,Autogen,PropReadOnly] public string Throttle => $"{Parent.GetComponent<MinecartMotionComponent>().ServerThrottle * 100:0}%";
    [SyncToView,Autogen,PropReadOnly] public string Direction => Parent.GetComponent<MinecartMotionComponent>().ServerDirection < 0 ? "Reverse" : "Forward";
    [SyncToView,Autogen,PropReadOnly] public string Brakes => Parent.GetComponent<MinecartMotionComponent>().Handbrake ? "Applied" : "Released";
    [SyncToView] public string DriveMode => Parent.GetComponent<TrainControllerComponent>().Autopilot ? "Autopilot" : "Manual";
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Next Switch")] public string SwitchCommand=>switchStep==0?"Follow track":(switchStep<0?"Left":"Right")+" requested";
    private void SelectSwitchDirection(Player player,int direction)
    {
        if(!CanDrive(player)) return;
        switchStep=direction; switchDriver=player.ID; this.Changed(nameof(SwitchCommand));
    }
    public void SwitchLeft(Player player)=>SelectSwitchDirection(player,-1);
    public void SwitchForward(Player player)=>CancelSwitchCommand(player);
    public void SwitchRight(Player player)=>SelectSwitchDirection(player,1);
    public void CancelSwitchCommand(Player player)
    {
        if(!CanDrive(player)) return;
        switchStep=0; switchDriver=player.ID; this.Changed(nameof(SwitchCommand));
    }
    public override void Tick()
    {
        base.Tick();
        this.Changed(nameof(Throttle)); this.Changed(nameof(Direction)); this.Changed(nameof(Brakes)); this.Changed(nameof(DriveMode));
        var mounts=Parent.GetComponent<MountComponent>();
        // Recover old saves with an occupant in the former driver seat.
        if(mounts.Driver is {} oldDriver) mounts.TryDismountPlayer(oldDriver);
        if(cabOperator!=null && !HasStandingOperator)
        {
            ReleaseOperator("operator offline, permission revoked, or control spot released");
        }
        var driver=cabOperator;
        if(driver==null || driver.ID!=switchDriver) {if(switchStep!=0){switchStep=0;this.Changed(nameof(SwitchCommand));} return;}
        if(switchStep==0) return;
        var vehicle=(RailVehicleObject)Parent;
        if(TrackWorld.Capture(Parent.Position,Parent.Rotation.RotateVector(Vector3.UnitZ),.8f,1,vehicle.RailSpec.Industrial) is not {} at) return;
        var forward=Parent.Rotation.RotateVector(Vector3.UnitZ);
        var velocity=Parent.GetComponent<MinecartMotionComponent>().CurrentRailVelocity;
        if(velocity.LengthSquared()>.04f) forward=Vector3.Normalize(velocity);
        var exit=Vector3.Dot(forward,at.Rail.Profile.Tangent(at.T))>=0?1:0;
        if(RailSwitchComponent.AheadStep(at.Rail,exit,switchStep) is {} command
            && command.Switch.Select(driver,command.Route,true))
        {
            switchStep=0; this.Changed(nameof(SwitchCommand));
        }
    }
    [Interaction(InteractionTrigger.InteractKey, "Change manual / autopilot mode", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "MineTrainCab" }, interactionDistance: 3, priority: 60,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ToggleAutodrive(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (!target.ContainsParameter("MineTrainCab")) return;
        ToggleAutodrive(player);
    }
    [Interaction(InteractionTrigger.InteractKey, "Change manual / autopilot mode", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "MineTrainBoiler" }, interactionDistance: 3, priority: 60,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ToggleAutodriveFromEngine(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (!target.ContainsParameter("MineTrainBoiler")) return;
        ToggleAutodrive(player);
    }
    private void ToggleAutodrive(Player player)
    {
        if (player == null || this.Parent.IsDestroyed
            || Vector3.Distance(player.User.Position, this.Parent.Position) > 3) return;
        var controller = this.Parent.GetComponent<TrainControllerComponent>();
        controller.SetAutopilot(player, !controller.Autopilot);
    }

    [Interaction(InteractionTrigger.InteractKey, "Inspect locomotive", requiredEnvVars: new[] { "MineTrainCab" },
        interactionDistance: 3, priority: 50, authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void Drive(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (this.Parent.IsDestroyed || !target.ContainsParameter("MineTrainCab")
            || !this.Parent.IsAuthorized(player.User, AccessType.FullAccess)
            || Vector3.Distance(player.User.Position, this.Parent.Position) > 3) return;
        Parent.OpenUI(player);
    }

    internal bool HasStandingOperator => cabOperator != null && !Parent.GetComponent<TrainControllerComponent>().Autopilot
        && CanOccupyCab(cabOperator) && Parent.GetComponent<MountComponent>().MountedPlayers.Contains(cabOperator);
    internal int OperatorId => cabOperator?.ID ?? 0;
    private bool CanOccupyCab(Player player)
    {
        if(player==null || Parent.IsDestroyed || !player.User.IsOnline
            || !Parent.IsAuthorized(player.User,AccessType.FullAccess)) return false;
        var mounts=Parent.GetComponent<MountComponent>();
        if(mounts.MountedPlayers.Contains(player)) return mounts.Driver!=player;
        if(player.MountManager.IsMounted) return false;
        var r=Parent.Rotation;
        var local=Vector3.Transform(player.User.Position-Parent.Position,Quaternion.Inverse(new Quaternion(r.x,r.y,r.z,r.w)));
        var spec=((RailVehicleObject)Parent).RailSpec;
        return Physics.StandingCab.Contains(spec,local);
    }
    private bool CanDrive(Player player)
    {
        if(!CanOccupyCab(player) || (HasStandingOperator && cabOperator!=player)) return false;
        var mounts=Parent.GetComponent<MountComponent>();
        if(!mounts.MountedPlayers.Contains(player))
        {
            if(Parent.GetComponent<RailCouplingComponent>()?.CanBoard==false) return false;
            // Native passenger parenting follows the rendered vehicle transform.
            // MountSeat is exact; TryMountTargetSeat can silently choose a different seat.
            mounts.MountSeat(1,player);
            if(!mounts.MountedPlayers.Contains(player) || mounts.Driver==player) return false;
        }
        cabOperator=player;
        var auto=Parent.GetComponent<TrainControllerComponent>();
        auto.TakeManualControl();
        return !auto.Autopilot;
    }
    public void ReleaseControls(Player player)
    {
        if(player==null || Parent.IsDestroyed) return;
        var mounts=Parent.GetComponent<MountComponent>();
        if(!mounts.MountedPlayers.Contains(player)) return;
        if(cabOperator==player) ReleaseOperator("operator stepped onto moving cab deck");
        else Parent.GetComponent<TrainControllerComponent>().HandoffToAutopilot();
        mounts.TryDismountPlayer(player);
        if(!player.MountManager.IsMounted)
        {
            var cab=Physics.StandingCab.Layout(((RailVehicleObject)Parent).RailSpec);
            player.SetRelativePosAndRot(Parent.ID,new Vector3(0,cab.Floor+.02f,cab.Z+cab.Depth/2-.52f),Vector3.UnitZ);
        }
    }
    public bool TakeControls(Player player) => CanDrive(player);
    public void LeaveTrain(Player player)
    {
        if(player==null || Parent.IsDestroyed) return;
        var mounts=Parent.GetComponent<MountComponent>();
        var attached=mounts.MountedPlayers.Contains(player);
        if(!attached && !CanOccupyCab(player)) return;
        if(cabOperator==player) ReleaseOperator("operator deliberately exited train");
        if(attached) mounts.TryDismountPlayer(player);
        if(player.MountManager.IsMounted) return;
        var cab=Physics.StandingCab.Layout(((RailVehicleObject)Parent).RailSpec);
        player.SetRelativePosAndRot(Parent.ID,new Vector3(cab.Width/2+.60f,.05f,cab.Z),Vector3.UnitZ);
    }
    // Exit must remain possible for passengers and after authorization revocation.
    // Only this player's membership is inspected; never dismount another rider.
    public void LeaveCab(Player player)
    {
        LeaveTrain(player);
    }
    [Interaction(InteractionTrigger.RightClick,"Leave cab",modifier:InteractionModifier.Shift,
        requiredEnvVars:new[]{"MineTrainCab"},interactionDistance:3,priority:90,
        flags:InteractionFlags.BlocksOtherInteraction)]
    public void ExitCab(Player p,InteractionTriggerInfo t,InteractionTarget target)
    { if(target.ContainsParameter("MineTrainCab")) LeaveTrain(p); }
    [Interaction(InteractionTrigger.InteractKey,"Take / leave controls (stay aboard)",requiredEnvVars:new[]{"RailCabStand"},
        interactionDistance:3,priority:70,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void Stand(Player player,InteractionTriggerInfo trigger,InteractionTarget target) => BoardCab(player,target,"RailCabStand",1,AccessType.FullAccess);
    [Interaction(InteractionTrigger.InteractKey,"Take / leave controls (stay aboard)",requiredEnvVars:new[]{"RailCabSeat"},
        interactionDistance:3,priority:70,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void Sit(Player player,InteractionTriggerInfo trigger,InteractionTarget target) => BoardCab(player,target,"RailCabSeat",1,AccessType.FullAccess);
    private void BoardCab(Player player,InteractionTarget target,string key,int seat,AccessType access)
    {
        if(player==null || Parent.IsDestroyed || !target.ContainsParameter(key) || !Parent.IsAuthorized(player.User,access)
            || Vector3.Distance(player.User.Position,Parent.Position)>3) return;
        var mounts=Parent.GetComponent<MountComponent>();
        if(mounts.MountedPlayers.Contains(player)){ReleaseControls(player);return;}
        if(player.MountManager.IsMounted || Parent.GetComponent<RailCouplingComponent>()?.CanBoard==false) return;
        mounts.MountSeat(seat,player);
        if(mounts.MountedPlayers.Contains(player)) CanDrive(player);
    }
    [Interaction(InteractionTrigger.LeftClick,"Increase throttle",requiredEnvVars:new[]{"RailCabThrottle"},interactionDistance:3,priority:70,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void ThrottleUp(Player p,InteractionTriggerInfo t,InteractionTarget target){if(target.ContainsParameter("RailCabThrottle"))IncreaseThrottle(p);}
    [Interaction(InteractionTrigger.RightClick,"Decrease throttle",requiredEnvVars:new[]{"RailCabThrottle"},interactionDistance:3,priority:70,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void ThrottleDown(Player p,InteractionTriggerInfo t,InteractionTarget target){if(target.ContainsParameter("RailCabThrottle"))DecreaseThrottle(p);}
    [Interaction(InteractionTrigger.InteractKey,"Apply brakes",requiredEnvVars:new[]{"RailCabBrake"},interactionDistance:3,priority:70,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void BrakeLever(Player p,InteractionTriggerInfo t,InteractionTarget target){if(target.ContainsParameter("RailCabBrake"))Brake(p);}
    [Interaction(InteractionTrigger.InteractKey,"Release brakes / coast",modifier:InteractionModifier.Shift,requiredEnvVars:new[]{"RailCabBrake"},interactionDistance:3,priority:80,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void CoastLever(Player p,InteractionTriggerInfo t,InteractionTarget target){if(target.ContainsParameter("RailCabBrake"))Coast(p);}
    [Interaction(InteractionTrigger.InteractKey,"Reverse (stop first)",requiredEnvVars:new[]{"RailCabReverse"},interactionDistance:3,priority:70,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void ReverseLever(Player p,InteractionTriggerInfo t,InteractionTarget target){if(target.ContainsParameter("RailCabReverse"))Reverse(p);}
    [Interaction(InteractionTrigger.LeftClick,"Switch route left one step",requiredEnvVars:new[]{"RailCabSwitch"},interactionDistance:3,priority:70,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void RouteLeft(Player p,InteractionTriggerInfo t,InteractionTarget target){if(target.ContainsParameter("RailCabSwitch"))SwitchLeft(p);}
    [Interaction(InteractionTrigger.RightClick,"Switch route right one step",requiredEnvVars:new[]{"RailCabSwitch"},interactionDistance:3,priority:70,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void RouteRight(Player p,InteractionTriggerInfo t,InteractionTarget target){if(target.ContainsParameter("RailCabSwitch"))SwitchRight(p);}
    [Interaction(InteractionTrigger.InteractKey,"Cancel pending switch request",modifier:InteractionModifier.Shift,requiredEnvVars:new[]{"RailCabSwitch"},interactionDistance:3,priority:80,authRequired:AccessType.FullAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void CancelRoute(Player p,InteractionTriggerInfo t,InteractionTarget target){if(target.ContainsParameter("RailCabSwitch"))CancelSwitchCommand(p);}

    public void IncreaseThrottle(Player player) { if (!CanDrive(player)) return; var m=Parent.GetComponent<MinecartMotionComponent>(); m.SetServerThrottle(Math.Min(1,m.ServerThrottle+.2),m.ServerDirection); this.Changed(nameof(Throttle)); this.Changed(nameof(DriveMode)); }
    public void DecreaseThrottle(Player player) { if (!CanDrive(player)) return; var m=Parent.GetComponent<MinecartMotionComponent>(); m.SetServerThrottle(Math.Max(0,m.ServerThrottle-.2),m.ServerDirection); this.Changed(nameof(Throttle)); }
    public void Coast(Player player) { if (!CanDrive(player)) return; var m=Parent.GetComponent<MinecartMotionComponent>(); m.SetDriveCommands(0,m.ServerDirection,false); this.Changed(nameof(Throttle)); }
    public void Brake(Player player) { if (!CanDrive(player)) return; var m=Parent.GetComponent<MinecartMotionComponent>(); m.SetDriveCommands(0,m.ServerDirection,true); this.Changed(nameof(Throttle)); }
    public void Reverse(Player player)
    {
        if (!CanDrive(player)) return;
        var m=Parent.GetComponent<MinecartMotionComponent>();
        if (Math.Abs(m.CurrentRailVelocity.Length())>.08) { Brake(player); return; }
        m.SetDriveCommands(m.ServerThrottle,-m.ServerDirection,m.CommandBrake); this.Changed(nameof(DriveMode));
    }

    [Interaction(InteractionTrigger.RightClick, "Put %SelectedNonTool% into fuel supply",
        requiredEnvVars: new[] { "MineTrainBoiler", "SelectedNonTool", "CanPutIntoFuelTank" },
        interactionDistance: 3, priority: 50, authRequired: AccessType.ConsumerAccess,
        flags: InteractionFlags.BlocksOtherInteraction, MinCaloriesRequired = 0)]
    public void PutFuel(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (player == null || this.Parent.IsDestroyed || !target.ContainsParameter("MineTrainBoiler")
            || !this.Parent.IsAuthorized(player.User, AccessType.ConsumerAccess)
            || Vector3.Distance(player.User.Position, this.Parent.Position) > 3) return;
        var fuel = this.Parent.GetComponent<FuelSupplyComponent>();
        if (fuel != null && fuel.CanPutIntoFuelTank(player.User)) fuel.PutItem(player, trigger, target);
    }

    [Interaction(InteractionTrigger.InteractKey, "Open boiler and supplies", requiredEnvVars: new[] { "MineTrainBoiler" },
        interactionDistance: 3, priority: 50, authRequired: AccessType.ConsumerAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void OpenBoiler(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (target.ContainsParameter("MineTrainBoiler") && !this.Parent.IsDestroyed
            && this.Parent.IsAuthorized(player.User, AccessType.ConsumerAccess)
            && Vector3.Distance(player.User.Position, this.Parent.Position) <= 3) this.Parent.OpenUI(player);
    }
}
