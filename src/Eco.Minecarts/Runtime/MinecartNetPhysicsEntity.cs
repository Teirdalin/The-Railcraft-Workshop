using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

/// <summary>Native client physics off-track/while pulled; authoritative server guidance on rails.</summary>
internal sealed class MinecartNetPhysicsEntity : NetPhysicsEntity
{
    private readonly RailVehicleObject? cart;
    public MinecartNetPhysicsEntity(string type, INetObject owner) : base(type, owner)
        => this.cart = owner as RailVehicleObject;
    private readonly object ownershipGate = new();
    private bool guided;
    private bool handoffPending;
    private float guidedKeyframeTime;
    private double clockOrigin = Eco.Shared.Time.TimeUtil.Seconds;
    private float clockBase;
    private float? lastNativePacketTime;

    public void SetGuided(bool value)
    {
        lock (this.ownershipGate)
        {
            // VehicleComponent assigns the mounted player as Controller even
            // when our guided flag was already true. Revoke that reassignment:
            // otherwise IsUpdated suppresses updates to the driver while
            // ReceiveUpdate rejects their local motion, splitting the worlds.
            if (value && this.Controller != null)
                this.SetPhysicsController(null!, () => false);
            if (this.guided == value) return;
            this.guided = value;
            if (value)
            {
                this.SetPhysicsController(null!, () => false);
                this.guidedKeyframeTime = this.clockBase + (float)(Eco.Shared.Time.TimeUtil.Seconds - this.clockOrigin);
                this.MarkPoseUpdated();
            }
        }
    }

    internal void PublishGuidedPose(System.Numerics.Vector3 velocity)
    {
        lock (this.ownershipGate)
        {
            if (this.guided && this.Controller != null)
                this.SetPhysicsController(null!, () => false);
            this.Velocity = velocity;
            this.guidedKeyframeTime = this.clockBase + (float)(Eco.Shared.Time.TimeUtil.Seconds - this.clockOrigin);
            this.MarkPoseUpdated();
        }
    }

    internal void ReleaseGuidedPose(System.Numerics.Vector3 velocity)
    {
        lock (this.ownershipGate)
        {
            this.guided = false;
            this.SetPhysicsController(null!);
            // SetPhysicsController(null) may clear velocity. Restore it AFTER
            // ownership changes, and retain the guided clock until a native packet.
            this.Velocity = velocity;
            this.guidedKeyframeTime = this.clockBase + (float)(Eco.Shared.Time.TimeUtil.Seconds - this.clockOrigin);
            this.handoffPending = true;
            this.MarkPoseUpdated();
        }
    }

    public override void SendUpdate(BSONObject data, INetObjectViewer viewer)
    {
        lock (this.ownershipGate)
        {
            base.SendUpdate(data, viewer);
            if (this.guided || this.handoffPending) data["time"] = this.guidedKeyframeTime;
        }
    }

    public override void SendInitialState(BSONObject data, INetObjectViewer viewer)
    {
        lock (this.ownershipGate)
        {
            base.SendInitialState(data, viewer);
            if (this.guided || this.handoffPending) data["time"] = this.guidedKeyframeTime;
        }
    }

    public override bool IsRelevant(INetObjectViewer viewer)
    {
        lock (this.ownershipGate)
            return !this.guided ? base.IsRelevant(viewer) : viewer is IWorldObserver observer
                && this.DistanceSquared(observer) <= observer.SimulationViewDistance.NotVisibleSq;
    }

    public override bool IsNotRelevant(INetObjectViewer viewer)
    {
        lock (this.ownershipGate)
            return !this.guided ? base.IsNotRelevant(viewer) : viewer is IWorldObserver observer
                && this.DistanceSquared(observer) > observer.SimulationViewDistance.NotVisibleSq;
    }

    public override void ReceiveUpdate(BSONObject data)
    {
        System.Numerics.Vector3 velocity;
        double packetSeconds;
        lock (this.ownershipGate)
        {
            if (this.guided) return;
            var lastReceived = this.LastReceivedUpdateTime;
            base.ReceiveUpdate(data);
            if (this.LastReceivedUpdateTime <= lastReceived || !data.TryGetFloatValue("time", out var nativeTime)) return;
            // Continue the accepted client's keyframe clock across ownership handoff.
            // Subtract doubles before conversion: wall-clock Seconds is too large for float precision.
            this.clockBase = nativeTime;
            packetSeconds = lastNativePacketTime is {} previous && nativeTime>previous ? nativeTime-previous : .05;
            lastNativePacketTime = nativeTime;
            this.handoffPending = false;
            this.clockOrigin = Eco.Shared.Time.TimeUtil.Seconds;
            velocity = this.Velocity;
        }
        // Outside ownershipGate: motion ticks take their component gate first,
        // then ownershipGate. Calling back inside it would invert that order.
        this.cart?.GetComponent<MinecartMotionComponent>()?.OnNativePhysicsPose(velocity,packetSeconds);
    }

    private float DistanceSquared(IWorldObserver observer) => Eco.Shared.Voxel.World.WrappedDistanceSq(
        new Eco.Shared.Math.Vector2(observer.Position.X, observer.Position.Z),
        new Eco.Shared.Math.Vector2(this.Position.X, this.Position.Z));
}
