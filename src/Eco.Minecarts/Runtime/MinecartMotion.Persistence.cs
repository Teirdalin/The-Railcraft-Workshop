using System.Numerics;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Track;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

// Detached snapshot captured by Eco's normal save pipeline, including autosaves.
// WorldObject already persists the pose and couplings; never relocate on resume.
[Serialized]
public sealed class SavedCoasterMotion
{
    [Serialized] public bool Guided { get; set; }
    [Serialized] public int X { get; set; }
    [Serialized] public int Y { get; set; }
    [Serialized] public int Z { get; set; }
    [Serialized] public float Parameter { get; set; }
    [Serialized] public int Facing { get; set; } = 1;
    [Serialized] public float VelocityX { get; set; }
    [Serialized] public float VelocityY { get; set; }
    [Serialized] public float VelocityZ { get; set; }
    [Serialized] public bool InFlight { get; set; }
    [Serialized] public bool FreeFollower { get; set; }
    [Serialized] public bool QueueFeed { get; set; }
    [Serialized] public int FlightFacing { get; set; } = 1;
    [Serialized] public float TrajectoryX { get; set; }
    [Serialized] public float TrajectoryY { get; set; }
    [Serialized] public float TrajectoryZ { get; set; }
    [Serialized] public float OffsetX { get; set; }
    [Serialized] public float OffsetY { get; set; }
    [Serialized] public float OffsetZ { get; set; }
    [Serialized] public float OffsetW { get; set; } = 1;
    [Serialized] public double LandingElapsed { get; set; } = .45;
    [Serialized] public float PivotX { get; set; }
    [Serialized] public float PivotY { get; set; }
    [Serialized] public float PivotZ { get; set; }
    internal Vector3 Velocity => new(VelocityX, VelocityY, VelocityZ);
    internal Vector3 Trajectory => new(TrajectoryX, TrajectoryY, TrajectoryZ);
    internal Quaternion Offset => new(OffsetX, OffsetY, OffsetZ, OffsetW);
    internal Vector3 Pivot => new(PivotX, PivotY, PivotZ);
    internal bool Valid => float.IsFinite(Velocity.LengthSquared()) && Velocity.LengthSquared() < 10000
        && float.IsFinite(Trajectory.LengthSquared()) && float.IsFinite(Offset.LengthSquared())
        && Offset.LengthSquared() > .01f && float.IsFinite(Pivot.LengthSquared()) && Pivot.LengthSquared() < 100
        && float.IsFinite(Parameter) && Parameter is >= 0 and <= 1 && double.IsFinite(LandingElapsed);
}

public sealed partial class MinecartMotionComponent
{
    private SavedCoasterMotion? loadedMomentum;
    private bool resumeMomentumPending;
    [Serialized, ThreadSafe] private SavedCoasterMotion? SavedMomentum
    {
        get
        {
            lock (gate)
            {
                // A save during initialization must retain the pending snapshot.
                if (resumeMomentumPending) return loadedMomentum;
                if (Parent is not RailVehicleObject vehicle || !vehicle.RailSpec.Coaster) return null;
                var velocity = inFlight ? flightVelocity : freeFollowerPose ? freeFollowerTrajectory
                    : current is { } rail ? rail.Profile.Tangent(t) * (float)state.Speed : nativeVelocity;
                return new() { Guided = current != null, X = current?.Cell.X ?? 0, Y = current?.Cell.Y ?? 0,
                    Z = current?.Cell.Z ?? 0, Parameter = t, Facing = facing,
                    VelocityX = velocity.X, VelocityY = velocity.Y, VelocityZ = velocity.Z,
                    InFlight = inFlight, FreeFollower = freeFollowerPose, QueueFeed = coasterQueueFeed, FlightFacing = flightFacingSign,
                    TrajectoryX = freeFollowerTrajectory.X, TrajectoryY = freeFollowerTrajectory.Y, TrajectoryZ = freeFollowerTrajectory.Z,
                    OffsetX = landingRotationOffset.X, OffsetY = landingRotationOffset.Y,
                    OffsetZ = landingRotationOffset.Z, OffsetW = landingRotationOffset.W, LandingElapsed = landingRotationElapsed,
                    PivotX = landingPivot.X, PivotY = landingPivot.Y, PivotZ = landingPivot.Z };
            }
        }
        set { lock (gate) { loadedMomentum = value; resumeMomentumPending = true; } }
    }

    private bool ResumeConsistMomentum(RailCouplingComponent coupling)
    {
        if (!WorldObjectManager.Init.Initialized || !coupling.LoadMembersReady || coupling.IsFollower) return false;
        var members = coupling.Group().OrderBy(c => c.Parent.ID)
            .Select(c => c.Parent.GetComponent<MinecartMotionComponent>()).ToArray();
        var acquired = new List<object>();
        try
        {
            // Never wait on another motion callback while holding our own gate.
            foreach (var motion in members)
            {
                if (!Monitor.TryEnter(motion.gate)) return false;
                acquired.Add(motion.gate);
            }
            foreach (var motion in members) motion.ResumeMomentum();
            coupling.CompleteLoad();
            return true;
        }
        finally { foreach (var memberGate in acquired.AsEnumerable().Reverse()) Monitor.Exit(memberGate); }
    }

    private void ResumeMomentum()
    {
        if (!resumeMomentumPending) return;
        var saved = loadedMomentum;
        if (saved?.Valid == true)
        {
            current = saved.Guided ? TrackWorld.Read(new(saved.X, saved.Y, saved.Z)) : null;
            if (current is { Profile.Coaster: false }) current = null;
            t = saved.Parameter; facing = saved.Facing < 0 ? -1 : 1;
            nativeVelocity = flightVelocity = saved.Velocity;
            freeFollowerTrajectory = saved.Trajectory;
            flightFacingSign = saved.FlightFacing < 0 ? -1 : 1;
            inFlight = saved.InFlight || saved.Guided && current == null && !Derailed;
            freeFollowerPose = saved.FreeFollower;
            coasterQueueFeed = saved.QueueFeed; queueCheckedCell = null;
            state = new(0, current is { } rail ? Vector3.Dot(saved.Velocity, rail.Profile.Tangent(t)) : 0, 0);
            landingRotationOffset = Quaternion.Normalize(saved.Offset);
            landingRotationElapsed = Math.Clamp(saved.LandingElapsed, 0, .45);
            landingPivot = saved.Pivot;
            // Reject a stale cursor after an administrative move, keeping the
            // saved world pose rather than snapping back to the old rail.
            if (current is { } bound)
            {
                var forward = RailTangent(bound, t, NextRail) * facing;
                var expected = LandingPosition(RailPosition(bound, t, NextRail), bound, t, forward, Parent.Rotation);
                if (Vector3.DistanceSquared(expected, Parent.Position) > .65f * .65f)
                { current = null; inFlight = !Derailed; state = new(0, 0, 0); }
            }
        }
        placementSnapPending = false;
        lastTick = lastFreeFollowerAt = DateTime.UtcNow;
        loadedMomentum = null; resumeMomentumPending = false;
        RailVehicle.SetRailGuidance(true);
        RailVehicle.PublishRailPose(inFlight ? flightVelocity : freeFollowerPose ? freeFollowerTrajectory : CurrentRailVelocity);
        WakeMotion();
    }
}
