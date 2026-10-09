using System.Numerics;
using Eco.Gameplay.Components;
using Eco.Gameplay.Players;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.Items;

namespace Eco.Minecarts.Runtime;

// This controller owns every manual minecart move. The old saved motion
// component is an interaction/save adapter only for these two vehicle types.
internal sealed class ManualMinecartController(MinecartMotionComponent adapter)
{
    internal static bool Applies(RailVehicleObject vehicle)=>vehicle.Capabilities.HasFlag(RailVehicleCapabilities.ManualHandle);
    private RailVehicleObject Cart=>(RailVehicleObject)adapter.Parent;
    private RailCouplingComponent Coupling=>Cart.GetComponent<RailCouplingComponent>();
    internal ManualCartRailState? State {get;private set;}
    internal Player? Holder {get;private set;}
    private float handleOffset;
    private DateTime lastTick,lastShove;
    private DateTime lastCheckpoint;
    internal string Status {get;private set;}="Stopped; server rail authority";
    internal (VoxelRail Rail,float T,int Facing)? Bound=>State is {} at?(at.Rail,at.Progress,at.Facing):null;
    internal Vector3 Velocity=>State is {} at?Tangent(at)* (float)at.Speed:Vector3.Zero;
    internal bool CanSleep => Holder==null&&!Coupling.AwaitingLoad&&State is {} at&&at.Speed==0
        &&RailMotionSleepClock.RailCanSleep(adapter.Handbrake,at.Rail.Profile.Chain,false,
            Coupling.ConsistGrade(Tangent(at).Y,at.Facing));
    internal void ResetClock()=>lastTick=DateTime.UtcNow;
    internal (VoxelRail Rail,int End)? Next(VoxelRail rail,int end)
    {
        var next=TrackWorld.NeighborForVehicle(rail,end);
        return next is {} found&&!found.Rail.Profile.Tram&&!found.Rail.Profile.Coaster?found:null;
    }
    private Vector3 Tangent(ManualCartRailState at)=>RailGuidance.AxleTangent(at.Rail,at.Progress,Next,Cart.RailSpec.Wheelbase/2);
    private Vector3 Point(ManualCartRailState at)=>RailGuidance.PosePosition(at.Rail,at.Progress,Next,Cart.RailSpec.Wheelbase/2);

    internal void Initialize()
    {
        Cart.SetRailGuidance(true);
        Cart.GetComponent<MountComponent>().MountValidation.Add((seat,player)=>seat==0
            ?Eco.Core.Utils.Result.Fail(Eco.Shared.Localization.Localizer.DoStr("Use the handle to walk the cart; Shift+E boards the bucket."))
            :Eco.Core.Utils.Result.Succeeded);
        Bind(false);Publish();lastTick=DateTime.UtcNow;
    }
    internal bool Bind(bool placement)
    {
        if(State is {} at&&TrackWorld.Contains(at.Rail))return true;
        State=null;
        var captured=TrackWorld.Capture(Cart.Position,Cart.Rotation.RotateVector(Vector3.UnitZ),
            placement?.45f:.3f,placement?1.1f:.4f);
        if(captured is not {} found||found.Rail.Profile.Coaster||found.Rail.Profile.Tram)return false;
        var facing=Vector3.Dot(Cart.Rotation.RotateVector(Vector3.UnitZ),found.Rail.Profile.Tangent(found.T))<0?-1:1;
        State=new(found.Rail,found.T,facing);adapter.Derailed=false;Publish();return true;
    }
    internal void Tick(bool placement)
    {
        var now=DateTime.UtcNow;var elapsed=lastTick==default?0:Math.Clamp((now-lastTick).TotalSeconds,0,.25);lastTick=now;
        Cart.SetRailGuidance(true);
        if(Coupling.AwaitingLoad)
        {
            if(Coupling.LoadMembersReady&&!Coupling.IsFollower&&Bind(placement)&&State is {} restored
                &&Coupling.FollowTrain(restored.Rail,restored.Progress,restored.Facing,Vector3.Zero,restoring:true))Coupling.CompleteLoad();
            return;
        }
        if(Coupling.IsFollower){ReleaseHandle();return;}
        if(!Bind(placement)){ReleaseHandle();Status="No supporting rail; stopped";Publish();return;}
        if(Holder is {} held&&(!held.User.IsOnline||held.MountManager.IsMounted
            ||!Cart.IsAuthorized(held.User,AccessType.FullAccess)
            ||Vector3.DistanceSquared(held.User.Position,Cart.Position)>9))ReleaseHandle();
        var mass=Coupling.Performance.Mass;
        var steps=Math.Max(1,(int)Math.Ceiling(elapsed/.02));
        for(var i=0;i<steps&&elapsed>0;i++)
        {
            var at=State!.Value;
            if(!RailInfrastructure.Supports(at.Rail,mass)
                ||RailSwitchComponent.At(at.Rail.Cell) is {} points&&!points.AdmitTrailing(at.Rail,Coupling))
            {Stop();Status="Track unavailable or switch occupied";break;}
            var tangent=Tangent(at);var grade=Coupling.ConsistGrade(tangent.Y,at.Facing);
            var chain=Coupling.ChainTraction(at.Rail,at.Facing,at.Speed,grade);
            var drive=chain.Force;
            if(Holder is {} player)
            {
                var error=Vector3.Dot(player.User.Position-Point(at),tangent)-handleOffset;
                var walk=Vector3.Dot(player.BaseVelocity,tangent);
                if(!float.IsFinite(walk)||Math.Abs(walk)>6)walk=0;
                drive+=ManualCartRailMotion.HandleForce(mass,grade,at.Speed,walk,error);
            }
            var tuning=new MinecartTuning(EmptyMassKg:Cart.RailSpec.EmptyKg,MaximumHandbrakeForceN:Coupling.Performance.BrakingForce,
                DerailLateralAcceleration:double.PositiveInfinity);
            State=ManualCartRailMotion.Step(at,new(mass-Cart.RailSpec.EmptyKg,drive,adapter.Handbrake?1:0,grade,at.Rail.Profile.Curvature),
                tuning,elapsed/steps,Holder!=null?1.5:Cart.RailSpec.MaximumSpeed,Next,
                (rail,t,travel)=>Coupling.LimitTrainTravel(rail,t,RailGuidance.LimitBufferTravel(rail,t,travel,Next,Cart.CouplerOffset+.194f)));
            MinecartDumpRailComponent.TryUnload(State.Value.Rail.Cell,Cart);
        }
        Publish();
        if(State is {} state)Coupling.FollowTrain(state.Rail,state.Progress,state.Facing,Velocity);
        if((now-lastCheckpoint).TotalSeconds>=1){Cart.SetDirty();lastCheckpoint=now;}
    }
    internal void Accept(VoxelRail rail,float progress,int facing,Vector3 velocity)
    {
        ReleaseHandle();State=new(rail,progress,facing,Vector3.Dot(velocity,rail.Profile.Tangent(progress)),0,(State?.Sequence??0)+1);Publish();
    }
    internal void Stop(){ReleaseHandle();if(State is {} at)State=at with{Speed=0,Acceleration=0,Sequence=at.Sequence+1};Publish();}
    internal void ReleaseHandle(){Holder=null;handleOffset=0;}
    internal bool ToggleHandle(Player player,int end)
    {
        if(Holder==player){ReleaseHandle();return true;}
        if(Holder!=null||player.MountManager.IsMounted||!Coupling.CanBoard||!Coupling.EndAvailable(end)||!Bind(true)
            ||RailCouplingComponent.LiveVehicles.Any(c=>c!=Coupling&&c.Parent.GetComponent<MinecartMotionComponent>()?.ManualCart?.Holder==player))return false;
        // A held cart is the consist's driver, without mounting a dynamic body.
        Holder=player;handleOffset=Vector3.Dot(player.User.Position-Point(State!.Value),Tangent(State.Value));
        adapter.Handbrake=false;lastTick=DateTime.UtcNow;return true;
    }
    internal void Shove(Player player)
    {
        var leader=Coupling.Leader().Parent.GetComponent<MinecartMotionComponent>();
        leader.QueueManualShove(player,Coupling);
    }
    internal void ApplyShove(Player player,RailCouplingComponent touched)
    {
        if(player==null||Holder!=null||!Bind(false)||player.MountManager.IsMounted
            ||Vector3.Distance(player.User.Position,touched.Parent.Position)>3
            ||Coupling.FacingRelativeTo(touched)==0||Coupling.AwaitingLoad
            ||Coupling.Group().Any(c=>!c.Parent.IsAuthorized(player.User,AccessType.FullAccess)))return;
        Nudge(touched,player.User.Position);
    }
    internal void Nudge(RailCouplingComponent touched,Vector3 playerPosition)
    {
        if(Holder!=null||!Bind(false)||Coupling.FacingRelativeTo(touched)==0)return;
        var now=DateTime.UtcNow;if((now-lastShove).TotalSeconds<.8)return;
        var touchedCart=touched.Parent;
        var direction=RailGuidance.SelectShoveDirection(playerPosition,touchedCart.Position,touchedCart.Rotation.RotateVector(Vector3.UnitZ))
            *Coupling.FacingRelativeTo(touched)*State!.Value.Facing;
        lastShove=now;adapter.Handbrake=false;
        State=State.Value with {Speed=Math.Clamp(State.Value.Speed+direction*180/Coupling.Performance.Mass,-12,12)};
        Publish();
    }
    internal bool CanRestore(VoxelRail rail,float progress)
    {
        foreach(var direction in new[]{-1,1})
        {
            var at=rail;var remaining=Cart.RailSpec.Wheelbase/2;
            var distance=progress*at.Profile.Length;var sign=direction;
            for(var crossed=0;crossed<16;crossed++)
            {
                var available=sign>0?at.Profile.Length-distance:distance;
                if(remaining<=available+.001f)break;
                remaining-=available;
                if(Next(at,sign>0?1:0) is not {} next)return false;
                at=next.Rail;sign=next.End==0?1:-1;distance=sign>0?0:at.Profile.Length;
            }
            if(at.Profile.Coaster||at.Profile.Tram||!RailInfrastructure.Supports(at,Coupling.Load.Mass))return false;
        }
        return true;
    }
    internal void Publish()
    {
        if(State is {} at)
        {
            Cart.Position=Point(at);Cart.Rotation=Eco.Shared.Math.Quaternion.LookRotation(Tangent(at)*at.Facing);
            Status=$"{(Holder!=null?"Walking handle":Coupling.IsFollower?"Coupled follower":Math.Abs(at.Speed)<.001?"Stopped":"Coasting")}; server rail authority";
        }
        Cart.SetRailGuidance(true);Cart.PublishRailPose(Velocity);
        var sound=CartSound.AtSpeed(Velocity.Length());
        Cart.SetAnimatedState("RailVolume",sound.Volume);Cart.SetAnimatedState("RailPitch",sound.Pitch);
        Cart.SetAnimatedState("CornerVolume",CartSound.CornerVolume(Velocity.Length(),State?.Rail.Profile.Curvature??0));
        var brake=CartSound.Braking(Velocity.Length(),adapter.EffectiveHandbrake?1:0);Cart.SetAnimatedState("BrakeVolume",brake.Volume);
        var chainSpeed=State is {} chain&&chain.Rail.Profile.Chain?MinecartChainDriveObject.PoweredSpeedFor(chain.Rail.Cell):0;
        Cart.SetAnimatedState("ChainLiftPitch",chainSpeed>0?chainSpeed:1);
        Cart.SetAnimatedState("ChainLiftRunning",chainSpeed>0&&Velocity.Length()>.03f&&!adapter.EffectiveHandbrake);
        adapter.PublishManualDiagnostics(State,Status,Holder);
    }
}
