using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

/// <summary>Server poses for rail-only stock; native physics remains available for hand-operated carts.</summary>
internal sealed class RailVehicleNetEntity : NetPhysicsEntity
{
    private readonly RailVehicleObject? cart;
    public RailVehicleNetEntity(string type, INetObject owner) : base(type, owner)
    {
        this.cart = owner as RailVehicleObject;
        this.guided = this.cart?.ServerOnlyPhysics == true;
    }
    private readonly object ownershipGate = new();
    private bool guided;
    private Eco.Minecarts.Physics.ManualCartRailState? railSample;
    private bool handoffPending;
    private float guidedKeyframeTime;
    private double clockOrigin = Eco.Shared.Time.TimeUtil.Seconds;
    private float clockBase;
    private float? lastNativePacketTime;

    private void CaptureGuidedPose()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/CaptureGuidedPose", this.cart);
        if (this.cart == null) return;
        // A guided WorldObject moves on the server without SyncPositionAndRotation's
        // forced client snap. NetPhysicsEntity sends its own cached transform, so
        // refresh that cache for distant viewers before publishing the keyframe.
        this.Position = this.cart.Position;
        this.Rotation = this.cart.Rotation;
        this.railSample = this.cart.GetComponent<MinecartMotionComponent>()?.ManualCart?.State;
    }

    public void SetGuided(bool value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/SetGuided", this.cart);
        lock (this.ownershipGate)
        {
            value |= this.cart?.ServerOnlyPhysics == true;
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
                this.CaptureGuidedPose();
                this.guidedKeyframeTime = this.clockBase + (float)(Eco.Shared.Time.TimeUtil.Seconds - this.clockOrigin);
                this.MarkPoseUpdated();
            }
        }
    }

    internal void PublishGuidedPose(System.Numerics.Vector3 velocity)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/PublishGuidedPose", this.cart);
        lock (this.ownershipGate)
        {
            if (this.guided && this.Controller != null)
                this.SetPhysicsController(null!, () => false);
            this.CaptureGuidedPose();
            this.Velocity = velocity;
            this.guidedKeyframeTime = this.clockBase + (float)(Eco.Shared.Time.TimeUtil.Seconds - this.clockOrigin);
            this.MarkPoseUpdated();
            // Continuous movement uses only the interpolated physics stream.
            // SyncPositionAndRotation forces an immediate client snap to the
            // current server pose, ahead of the buffered interpolation timeline.
            // Repeating that snap makes the next buffered pose jump backwards.
            // Keep absolute sync at explicit recovery and ownership handoffs.
        }
    }

    internal void ReleaseGuidedPose(System.Numerics.Vector3 velocity)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/ReleaseGuidedPose", this.cart);
        lock (this.ownershipGate)
        {
            this.guided = this.cart?.ServerOnlyPhysics == true;
            this.SetPhysicsController(null!);
            // SetPhysicsController(null) may clear velocity. Restore it AFTER
            // ownership changes, and retain the guided clock until a native packet.
            this.CaptureGuidedPose();
            this.Velocity = velocity;
            this.guidedKeyframeTime = this.clockBase + (float)(Eco.Shared.Time.TimeUtil.Seconds - this.clockOrigin);
            this.handoffPending = true;
            this.MarkPoseUpdated();
        }
    }

    public override void SendUpdate(BSONObject data, INetObjectViewer viewer)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/SendUpdate", this.cart);
        lock (this.ownershipGate)
        {
            this.SendPose(data, viewer, initial: false);
            if (this.guided || this.handoffPending) data["time"] = this.guidedKeyframeTime;
        }
    }

    public override void SendInitialState(BSONObject data, INetObjectViewer viewer)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/SendInitialState", this.cart);
        lock (this.ownershipGate)
        {
            this.SendPose(data, viewer, initial: true);
            if (this.guided || this.handoffPending) data["time"] = this.guidedKeyframeTime;
        }
    }

    private void SendPose(BSONObject data, INetObjectViewer viewer, bool initial)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/SendPose", this.cart);
        if (!this.guided && this.cart?.ServerOnlyPhysics != true)
        {
            if (initial) base.SendInitialState(data, viewer);
            else base.SendUpdate(data, viewer);
            return;
        }
        // Native SyncPhysics.ReceiveUpdate makes the body dynamic whenever
        // TryGetVector3("v") succeeds, regardless of the prefab's kinematic flag.
        // Omit the field entirely: encoded null decodes to a BSON null VALUE,
        // which still passes TryGetValue. Keep every other native payload field.
        var packet = BSONObject.New;
        try
        {
            if (initial) base.SendInitialState(packet, viewer);
            else base.SendUpdate(packet, viewer);
            foreach (var entry in packet.ToArray())
            {
                if (entry.Key is "v" or "controller") continue;
                data[entry.Key] = entry.Value;
                packet[entry.Key] = null; // Transfer pooled-value ownership.
            }
        }
        finally { packet.Recycle(); }
        data["pos"] = this.Position; data["rot"] = this.Rotation;
        if (this.railSample is {} at)
        {
            data["railProgress"]=at.Progress; data["railSpeed"]=(float)at.Speed;
            data["railFacing"]=at.Facing; data["railSequence"]=at.Sequence;
        }
    }

    public override bool IsRelevant(INetObjectViewer viewer)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/IsRelevant", this.cart);
        lock (this.ownershipGate)
            return !this.guided ? base.IsRelevant(viewer) : viewer is IWorldObserver observer
                // A guided car is rendered for as long as its track chunk is
                // visible. SimulationViewDistance can be shorter; cutting pose
                // updates there leaves a still-rendered car frozen in midair
                // while the track below remains visible.
                && this.DistanceSquared(observer) <= observer.ChunkViewDistance.NotVisibleSq;
    }

    public override bool IsNotRelevant(INetObjectViewer viewer)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/IsNotRelevant", this.cart);
        lock (this.ownershipGate)
            return !this.guided ? base.IsNotRelevant(viewer) : viewer is IWorldObserver observer
                && this.DistanceSquared(observer) > observer.ChunkViewDistance.NotVisibleSq;
    }

    public override void ReceiveUpdate(BSONObject data)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/ReceiveUpdate", this.cart);
        System.Numerics.Vector3 velocity;
        double packetSeconds;
        lock (this.ownershipGate)
        {
            // Rail-only stock never accepts a client rigidbody as its driver.
            // Reject initial/late collision packets, including during placement.
            if (this.guided || this.cart?.ServerOnlyPhysics == true) return;
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

    private float DistanceSquared(IWorldObserver observer) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/DistanceSquared", this.cart); return Eco.Shared.Voxel.World.WrappedDistanceSq(
        new Eco.Shared.Math.Vector2(observer.Position.X, observer.Position.Z),
        new Eco.Shared.Math.Vector2(this.Position.X, this.Position.Z)); }
}
