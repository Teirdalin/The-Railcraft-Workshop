using System.Numerics;
using Eco.Core.Items;
using Eco.Core.Controller;
using Eco.Core.PropertyHandling;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.EnvVars;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;
using Eco.Shared.Items;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, LocDisplayName("Rail Motion")]
public sealed class MinecartMotionComponent : WorldObjectComponent, IHasEnvVars, IPickupConfirmationComponent
{
    [Serialized] public bool Handbrake { get; set; }
    // These are train-owned commands, not the lifetime of an occupied seat.
    [Serialized] private bool serverDriving;
    private bool coasterApproachBraking;
    [Serialized] private double serverThrottle;
    [Serialized] private int serverDirection = 1;
    [Serialized] private bool commandsInitialized;
    [Serialized] public bool CommandBrake { get; internal set; } = true;
    internal bool ServerDriving => this.serverDriving;
    internal double ServerThrottle => this.serverThrottle;
    internal int ServerDirection => this.serverDirection;
    internal Vector3 CurrentRailVelocity=>this.current is {} rail?rail.Profile.Tangent(this.t)*(float)this.state.Speed:this.nativeVelocity;
    [Serialized] public bool Derailed { get; set; }
    private readonly object gate = new();
    // Tracks constrain the cart. Curve overload must not disable automated routes.
    private static readonly MinecartTuning CartTuning = new(DerailLateralAcceleration: double.PositiveInfinity);
    private static readonly MinecartTuning TrainTuning = new(EmptyMassKg: 600, DerailLateralAcceleration: double.PositiveInfinity);
    private RailVehicleObject RailVehicle => (RailVehicleObject)this.Parent;
    private MinecartTuning Tuning => new(EmptyMassKg: this.RailVehicle.RailSpec.EmptyKg,
        AerodynamicDrag: .65 + (this.Parent.GetComponent<RailCouplingComponent>()?.Performance.Length ?? 1) * .15,
        StaticResistanceN: this.RailVehicle.RailSpec.Coaster ? 15 : 95 + (this.Parent.GetComponent<RailCouplingComponent>()?.Performance.Cars.Count ?? 1) * 22,
        MaximumHandbrakeForceN: this.Parent.GetComponent<RailCouplingComponent>()?.Performance.BrakingForce ?? this.RailVehicle.RailSpec.BrakeN,
        DerailLateralAcceleration: double.PositiveInfinity);
    private double guidedSpeed;
    internal (VoxelRail Rail, int End)? NextRail(VoxelRail rail, int end)
    {
        var control = this.Parent.GetComponent<RailCouplingComponent>()?.Leader().Parent.GetComponent<TrainControllerComponent>();
        return control?.Active == true ? control.Next(rail, end) : TrackWorld.NeighborForVehicle(rail, end);
    }
    private Vector3 RailPosition(VoxelRail rail, float parameter, Func<VoxelRail, int, (VoxelRail Rail, int End)?> neighbor) =>
        RailGuidance.PosePosition(rail, parameter, neighbor, this.RailVehicle.RailSpec.Wheelbase / 2);
    private Vector3 RailTangent(VoxelRail rail, float parameter, Func<VoxelRail, int, (VoxelRail Rail, int End)?> neighbor) =>
        RailGuidance.AxleTangent(rail, parameter, neighbor, this.RailVehicle.RailSpec.Wheelbase / 2);

    internal bool CheckRail(VoxelRail rail, double speed)
    {
        var coupling = this.Parent.GetComponent<RailCouplingComponent>();
        var performance = coupling.Performance;
        if(RailSwitchComponent.At(rail.Cell) is {} points && !points.AdmitTrailing(rail,coupling))
        { this.state=this.state with {Speed=0}; this.nativeVelocity=Vector3.Zero; return false; }
        var supported = RailInfrastructure.Supports(rail, coupling.Load.Mass);
        if (supported
            && rail.Profile.Industrial == this.RailVehicle.RailSpec.Industrial
            && rail.Profile.Coaster == this.RailVehicle.RailSpec.Coaster
            && rail.Profile.Radius + .001 >= this.RailVehicle.RailSpec.MinimumRadius)
        {
            // A car following the locomotive inherits its guided speed. Never let
            // a follower independently break the whole consist at a bend.
            if (coupling.IsFollower) return true;
            // The driver and autopilot use the same curve governor. A late tick,
            // downhill acceleration or a curve under the front axle can still
            // cross the nominal limit before Control has a chance to brake.
            // Correct that speed here instead of derailing and uncoupling.
            if (this.serverDriving && this.RailVehicle.RailSpec.Powered
                && performance.ExceedsCurveLimit(speed, rail.Profile.Curvature, false))
            {
                var limit = performance.CurveSpeed(rail.Profile.Radius);
                this.state = this.state with { Speed = Math.CopySign(Math.Min(Math.Abs(this.state.Speed), limit), this.state.Speed) };
                this.Parent.GetComponent<TrainControllerComponent>()?.ReportSafety("Slowing for curve");
                return true;
            }
            // Compact carts retain rail guidance when held OR freely coasting.
            // Walking-speed corner forces must not detach flanged minecarts.
            if (!performance.ExceedsCurveLimit(speed, rail.Profile.Curvature,
                this.RailVehicle.RailSpec.Pullable || this.RailVehicle.RailSpec.Coaster)) return true;
        }
        Eco.Shared.Logging.Log.WriteWarningLineLoc($"RAIL_GUIDANCE_LOST: object={Parent.ID}; rail={rail.Cell}; speed={speed:0.00}; supported={supported}; industrial={rail.Profile.Industrial}; coaster={rail.Profile.Coaster}; radius={rail.Profile.Radius:0.00}");
        this.Derailed = true;
        this.StopServerDriver();
        this.current = null; this.pulledRail = null; this.Handbrake = true;
        this.nativeWasPulling = false;
        var velocity = this.Parent.Rotation.RotateVector(Vector3.UnitZ) * (float)speed;
        coupling.BreakConnections();
        var controller = this.Parent.GetComponent<TrainControllerComponent>();
        if (controller != null) controller.Autopilot = false;
        var mounts = this.Parent.GetComponent<MountComponent>();
        if (mounts.Driver is { } driver) mounts.TryDismountPlayer(driver);
        this.ReleaseToGroundPhysics();
        this.inFlight = true; this.flightVelocity = velocity; this.departedRail = rail;
        this.RailVehicle.SetRailGuidance(true);
        this.RailVehicle.PublishRailPose(velocity);
        return false;
    }
    private bool CheckFootprint(VoxelRail rail, float parameter, double speed)
    {
        if (!this.CheckRail(rail,speed)) return false;
        foreach (var direction in new[]{-1,1})
        {
            var axle = RailGuidance.Advance(rail,parameter,direction*this.RailVehicle.RailSpec.Wheelbase/2,this.NextRail);
            if (axle.Rail != rail && !this.CheckRail(axle.Rail,speed)) return false;
        }
        return true;
    }
    private Vector3 nativePosition;
    private Vector3 nativeVelocity;
    private DateTime nativeSampleTime;
    private bool nativeWasPulling;
    private bool nativeGroundPhysics;
    private DateTime lastTick;
    private VoxelRail? current;
    internal RailCell? BoundRailCell=>this.current?.Cell;
    internal Eco.Shared.Math.Quaternion RailRotation(VoxelRail rail,float parameter,Vector3 forward)
    {
        if(!rail.Profile.Coaster)return Eco.Shared.Math.Quaternion.LookRotation(forward);
        var up=rail.Profile.Up(parameter);
        var z=Vector3.Normalize(forward); var x=Vector3.Normalize(Vector3.Cross(up,z)); var y=Vector3.Cross(z,x);
        var q=Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1));
        return new(q.X,q.Y,q.Z,q.W);
    }
    private float t;
    private int facing = 1;
    private MinecartState state;
    private Vector3 groundVelocity;
    private bool pickupPending;
    private Timer? motionTimer;
    private DateTime nextMotionRetry;
    internal void RecoverMotionError(Exception error)
    {
        lock(this.gate)
        {
            if(this.motionStopped || Parent.IsDestroyed) return;
            this.serverDriving=false; this.serverThrottle=0; this.CommandBrake=true; this.Handbrake=true;
            this.state=this.state with {Speed=0,Distance=0}; this.lastTick=DateTime.UtcNow;
            this.nextMotionRetry=DateTime.UtcNow.AddSeconds(1);
            Parent.GetComponent<TrainControllerComponent>()?.ReportSafety("Temporarily stopped; retrying");
            Eco.Shared.Logging.Log.WriteErrorLineLoc($"RAIL_TRAIN_MOTION_ERROR: object={Parent.ID}; retry in 1s; {error}");
        }
    }
    private Vector3 captureOffset;
    private bool motionStopped;
    private bool placementSnapPending;
    private VoxelRail? pulledRail;
    private float pulledT;
    private bool nativeVelocityAvailable;
    private DateTime lastShoveAt;
    private Vector3 lastPublishedRailVelocity;
    private DateTime nextPulledCornerCorrection;
    private VoxelRail? departedRail;
    private Vector3 departurePoint;
    private Vector3 departureDirection;
    private bool inFlight;
    private bool parkedOffRail;
    private Vector3 flightVelocity;
    private bool chainLiftEngaged;

    [Interaction(InteractionTrigger.InteractKey, "Shove minecart", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "MinecartHandle" }, interactionDistance: 2.5f, priority: 50,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void Shove(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        lock (this.gate)
        {
            var now = DateTime.UtcNow;
            var mounts = this.Parent.GetComponent<MountComponent>();
            if (this.motionStopped || this.pickupPending || this.Parent.IsDestroyed || mounts.IsMounted
                || this.Parent.GetComponent<RailCouplingComponent>().AwaitingLoad
                || (now - this.lastShoveAt).TotalSeconds < .8
                || Vector3.Distance(player.User.Position, this.Parent.Position) > 3
                || !target.ContainsParameter("MinecartHandle")
                || !this.Parent.IsAuthorized(player.User, AccessType.FullAccess)) return;
            // A shove immediately after letting go must not lose its impulse to
            // the next tick's native-to-server momentum handoff.
            if (this.nativeWasPulling)
            {
                this.nativeWasPulling = false;
                this.ReleaseRailMotion();
                this.nativeVelocityAvailable = false;
            }
            if (!this.TryBindRail()) return;
            var tangent = this.RailTangent(this.current!.Value, this.t, (r, end) => this.NextRail(r, end));
            var alignment = Vector3.Dot(player.User.Rotation.RotateVector(Vector3.UnitZ), tangent);
            if (Math.Abs(alignment) < .2) return; // Looking across the track is ambiguous.
            this.lastShoveAt = now;
            this.Handbrake = false;
            var mass = this.Parent.GetComponent<RailCouplingComponent>().Performance.Mass;
            this.state = this.state with { Speed = Math.Clamp(this.state.Speed + Math.Sign(alignment) * 180 / mass, -12, 12) };
        }
    }

    [Interaction(InteractionTrigger.InteractKey, "Shove coaster cart", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "CoasterShove" }, interactionDistance: 2.5f, priority: 50,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ShoveCoaster(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if(!this.RailVehicle.RailSpec.Coaster || player==null || player.MountManager.IsMounted
            || !target.ContainsParameter("CoasterShove") || !this.Parent.IsAuthorized(player.User,AccessType.FullAccess)) return;
        var leader=this.Parent.GetComponent<RailCouplingComponent>().Leader().Parent.GetComponent<MinecartMotionComponent>();
        leader.ApplyCoasterShove(player,this.Parent.Position);
    }
    private void ApplyCoasterShove(Player player,Vector3 touchedCartPosition)
    {
        lock(this.gate)
        {
            var now=DateTime.UtcNow;
            if(!this.RailVehicle.RailSpec.Coaster || this.motionStopped || this.pickupPending || this.Parent.IsDestroyed || this.Derailed
                || this.Parent.GetComponent<RailCouplingComponent>().AwaitingLoad
                || this.Parent.GetComponent<RailConditionComponent>()?.ConditionPercent<=0
                || (now-this.lastShoveAt).TotalSeconds<.8
                || Vector3.Distance(player.User.Position,touchedCartPosition)>3
                || !this.Parent.IsAuthorized(player.User,AccessType.FullAccess)
                || Math.Abs(this.state.Speed)>.35 || !this.TryBindRail()) return;
            var tangent=this.RailTangent(this.current!.Value,this.t,(r,end)=>this.NextRail(r,end));
            var alignment=Vector3.Dot(player.User.Rotation.RotateVector(Vector3.UnitZ),tangent);
            if(Math.Abs(alignment)<.2) { player.InfoBoxLoc($"Look along the track in the direction you want to shove."); return; }
            var mass=Math.Max(1,this.Parent.GetComponent<RailCouplingComponent>().Performance.Mass);
            this.lastShoveAt=now;
            this.Handbrake=false;
            this.CommandBrake=false;
            this.state=this.state with { Speed=Math.Sign(alignment)*Math.Clamp(350/mass,.45,1.6) };
            this.Parent.SetDirty();
        }
    }

    private double nativePacketSeconds = .05;
    internal void OnNativePhysicsPose(Vector3 velocity, double packetSeconds = .05)
    {
        lock (this.gate)
        {
            if (this.Parent.GetComponent<MountComponent>().Driver == null) return;
            this.nativeVelocity = velocity;
            this.nativePacketSeconds = double.IsFinite(packetSeconds) ? Math.Clamp(packetSeconds,.005,.3) : .05;
            this.nativeSampleTime = DateTime.UtcNow;
            this.nativeVelocityAvailable = true;
            this.nativeWasPulling = true;
            if (!this.GuidePulledRail()) this.Parent.GetComponent<RailCouplingComponent>()?.FollowFree(velocity);
        }
    }

    public override void OnCreate()
    {
        base.OnCreate();
        // Placement is the only route allowed a wider snap envelope. Do not
        // apply this to loaded/moved carts, which may intentionally be off-rail.
        this.placementSnapPending = true;
    }

    internal bool SnapPlacedCart()
    {
        lock (this.gate)
        {
            var nearest = TrackWorld.Capture(this.Parent.Position,
                this.Parent.Rotation.RotateVector(Vector3.UnitZ), .45f, .6f, this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster);
            if (nearest == null) return false;
            this.AlignToRail(nearest.Value.Rail, nearest.Value.T);
            return true;
        }
    }

    internal void GuideNativePullPose()
    {
        lock (this.gate)
        {
            if (this.motionStopped || this.Parent.IsDestroyed || this.pickupPending
                || this.Parent.GetComponent<MountComponent>().Driver == null) return;
            this.GuidePulledRail();
        }
    }

    internal bool GuidePulledRail()
    {
        lock (this.gate)
        {
            if (this.Derailed) return false;
            if (this.pulledRail is { } old && !TrackWorld.Contains(old)) this.pulledRail = null;
            if (this.pulledRail == null)
            {
                var capture = TrackWorld.Capture(this.Parent.Position, this.Parent.Rotation.RotateVector(Vector3.UnitZ), .65f, .75f, this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster)
                    ?? TrackWorld.UnderCart(this.Parent.Position, this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster);
                if (capture == null) return false;
                this.pulledRail = capture.Value.Rail;
                this.pulledT = capture.Value.T;
            }
            // Cache topology for the projection search. Only the existing rail
            // and its connected neighbours are eligible, never a nearby branch.
            var links = new Dictionary<(VoxelRail, int), (VoxelRail Rail, int End)?>();
            (VoxelRail Rail, int End)? Neighbor(VoxelRail r, int end)
            {
                if (!links.TryGetValue((r, end), out var next)) links[(r, end)] = next = this.NextRail(r, end);
                return next;
            }
            var candidates = new HashSet<VoxelRail> { this.pulledRail.Value };
            foreach (var end in new[] { 0, 1 })
                if (Neighbor(this.pulledRail.Value, end) is { } next) candidates.Add(next.Rail);
            var bestDistance = float.MaxValue;
            var bestRail = this.pulledRail.Value;
            var bestT = this.pulledT;
            foreach (var candidate in candidates)
            {
                // Native gravity/suspension must not cancel the player's uphill
                // progress. Derive held travel in XZ, then restore rail height.
                var projected = RailGuidance.ProjectPose(candidate, this.Parent.Position, Neighbor, horizontalOnly: true, halfWheelbase: this.RailVehicle.RailSpec.Wheelbase / 2);
                if (projected.Distance >= bestDistance) continue;
                bestDistance = projected.Distance; bestRail = candidate; bestT = projected.T;
            }
            // Once acquired, pushing cannot break the rail constraint. A native
            // collider impulse is not an instruction to detach from the track.
            var retained = this.pulledRail.Value;
            var travel = (double)(bestT - this.pulledT) * retained.Profile.Length;
            if (bestRail != retained)
                foreach (var end in new[] { 0, 1 })
                    if (Neighbor(retained, end) is { } next && next.Rail == bestRail)
                    {
                        var toEnd = end == 0 ? this.pulledT * retained.Profile.Length : (1 - this.pulledT) * retained.Profile.Length;
                        var intoNext = next.End == 0 ? bestT * bestRail.Profile.Length : (1 - bestT) * bestRail.Profile.Length;
                        travel = (end == 0 ? -1 : 1) * (toEnd + intoNext);
                        break;
                    }
            var limited = RailGuidance.LimitBufferTravel(retained, this.pulledT, travel, Neighbor, this.RailVehicle.CouplerOffset + .194f);
            var constrainedSpeed = this.nativeVelocity.Length();
            if (this.RailVehicle.RailSpec.Powered || this.RailVehicle.RailSpec.HumanPowered)
            {
                var seconds = this.nativePacketSeconds;
                var train = this.Parent.GetComponent<RailCouplingComponent>().Performance;
                // The mounted driver is simulated by native wheel physics. Applying
                // the autonomous integrator's acceleration again rolls each accepted
                // packet backwards, repeatedly force-teleporting the owning client.
                // Retain the safety speed ceiling, with a packet-sized deadband;
                // track, buffer and vehicle-contact constraints still apply below.
                var limit = train.SpeedLimit;
                if (this.Parent.GetComponent<RailConditionComponent>()?.ConditionPercent <= 0) limit = 0;
                limited = RailGuidance.LimitNativeTravel(limited, limit, seconds);
                // Position correction distance includes suspension/projection and
                // network tolerance. It is not the train's physical speed.
                this.guidedSpeed = Math.Abs(Vector3.Dot(this.nativeVelocity,this.RailTangent(bestRail,bestT,Neighbor)));
                constrainedSpeed = (float)this.guidedSpeed;
            }
            limited = this.LimitVehicleTravel(retained, this.pulledT, limited);
            if (this.Parent.GetComponent<RailConditionComponent>()?.ConditionPercent <= 0) limited = 0;
            if (Math.Abs(limited - travel) > .00001)
            {
                (bestRail, bestT) = RailGuidance.Advance(retained, this.pulledT, limited, Neighbor);
                var along=this.RailTangent(bestRail,bestT,Neighbor);
                this.nativeVelocity = Math.Abs(limited)<.00001 ? Vector3.Zero
                    : along*(float)Math.CopySign(Math.Min(constrainedSpeed,Math.Abs(limited)/this.nativePacketSeconds),Vector3.Dot(this.nativeVelocity,along));
            }
            this.pulledRail = bestRail; this.pulledT = bestT;
            if (!this.CheckFootprint(bestRail, bestT, constrainedSpeed)) return false;
            this.AlignToRail(bestRail, bestT, pulling: true);
            this.Parent.GetComponent<RailCouplingComponent>()?.FollowTrain(bestRail, bestT,
                Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ), bestRail.Profile.Tangent(bestT)) < 0 ? -1 : 1, this.nativeVelocity);
            this.Derailed = false;
            return true;
        }
    }

    internal bool ReleaseRailMotion()
    {
        lock (this.gate)
        {
            // Manual release parks the cart. Preserve velocity for a short
            // physical braking interval instead of an instantaneous stop.
            // Shove explicitly releases this brake before adding its impulse.
            this.Handbrake = true;
            if (this.pulledRail is { } rail && TrackWorld.Contains(rail))
            {
                this.current = rail; this.t = this.pulledT;
                var tangent = this.RailTangent(rail, this.t, (r, end) => this.NextRail(r, end));
                this.facing = Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ), tangent) < 0 ? -1 : 1;
                this.state = new(0, RailGuidance.ReleaseSpeed(this.nativeVelocity, tangent), 0);
                this.captureOffset = Vector3.Zero; this.nativeGroundPhysics = false;
                ((RailVehicleObject)this.Parent).SetRailGuidance(true);
                this.pulledRail = null;
                return true;
            }
            this.pulledRail = null;
            if (!this.TryBindRail()) return false;
            this.state = this.state with { Speed = RailGuidance.ReleaseSpeed(this.nativeVelocity,
                this.RailTangent(this.current!.Value, this.t, (r, end) => this.NextRail(r, end))) };
            return true;
        }
    }

    internal void ResetCoupledMotion()
    {
        lock (this.gate)
        {
            this.Handbrake = true; this.state = default; this.nativeVelocity = Vector3.Zero;
            this.nativeWasPulling = false; this.nativeVelocityAvailable = false;
            this.groundVelocity = Vector3.Zero; this.inFlight = false;
            ((RailVehicleObject)this.Parent).SetRailGuidance(true);
            ((RailVehicleObject)this.Parent).PublishRailPose(Vector3.Zero);
        }
    }

    internal void AcceptCoupledPose(VoxelRail rail, float parameter, int orientation, Vector3 velocity)
    {
        lock (this.gate)
        {
            if (this.Derailed || !this.CheckFootprint(rail, parameter, velocity.Length())) return;
            this.current = rail; this.t = parameter; this.facing = orientation;
            this.placementSnapPending = false;
            this.captureOffset = Vector3.Zero; this.nativeWasPulling = false; this.inFlight = false;
            this.state = new(0, Vector3.Dot(velocity, rail.Profile.Tangent(parameter)), 0);
            var point = this.RailPosition(rail, parameter, (r, e) => this.NextRail(r, e));
            var tangent = this.RailTangent(rail, parameter, (r, e) => this.NextRail(r, e));
            ((RailVehicleObject)this.Parent).SetRailGuidance(true);
            this.Parent.Position = point;
            this.Parent.Rotation = this.RailRotation(rail,parameter,tangent * orientation);
            ((RailVehicleObject)this.Parent).PublishRailPose(velocity);
            this.Sound(velocity.Length());
        }
    }

    internal void AcceptCoupledFreePose(Vector3 position, Vector3 forward, Vector3 velocity)
    {
        lock (this.gate)
        {
            this.current = null; this.pulledRail = null; this.nativeWasPulling = false;
            this.Parent.Position = position;
            this.Parent.Rotation = Eco.Shared.Math.Quaternion.LookRotation(forward);
            ((RailVehicleObject)this.Parent).SetRailGuidance(true);
            ((RailVehicleObject)this.Parent).PublishRailPose(velocity);
            this.Sound(0);
        }
    }

    internal bool CanRestoreCoupledPose(VoxelRail rail, float parameter)
    {
        if (this.Derailed) return false;
        bool Compatible(VoxelRail at) => at.Profile.Industrial == this.RailVehicle.RailSpec.Industrial
            && at.Profile.Coaster == this.RailVehicle.RailSpec.Coaster
            && at.Profile.Radius + .001 >= this.RailVehicle.RailSpec.MinimumRadius
            && RailInfrastructure.Supports(at, this.Parent.GetComponent<RailCouplingComponent>().Load.Mass);
        return Compatible(rail) && new[] {-1, 1}.All(direction =>
            Compatible(RailGuidance.Advance(rail, parameter, direction * this.RailVehicle.RailSpec.Wheelbase / 2, this.NextRail).Rail));
    }

    private double LimitVehicleTravel(VoxelRail rail, float parameter, double travel)
    {
        var coupling = this.Parent.GetComponent<RailCouplingComponent>();
        if (coupling == null || Math.Abs(travel) < .000001) return travel;
        return coupling.LimitTrainTravel(rail, parameter, travel);
    }

    private void AlignToRail(VoxelRail rail, float railT, bool pulling = false)
    {
        var tangent = this.RailTangent(rail, railT, (r, end) => this.NextRail(r, end));
        var forward = this.Parent.Rotation.RotateVector(Vector3.UnitZ);
        if (Vector3.Dot(forward, tangent) < 0) tangent = -tangent;
        var point = this.RailPosition(rail, railT, (r, end) => this.NextRail(r, end));
        var distance = Vector3.Distance(this.Parent.Position, point);
        var alignment = Vector3.Dot(forward, tangent);
        var frameAligned=!rail.Profile.Coaster||Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitY),rail.Profile.Up(railT))>.99995f;
        // While a player is driving, RCC owns the native body. A full pose snap
        // on every network packet makes the client solver and rail solver fight,
        // which is visible as violent jitter through curves. Keep a deadband and
        // blend only meaningful drift; reserve hard snaps for genuine loss of
        // rail contact. Autodrive/placement retain authoritative alignment.
        if (pulling)
        {
            if (distance < .075f && alignment > .985f) return;
            if (distance < .75f && alignment > .55f)
            {
                var corner = rail.Profile.Curvature > .001f || alignment < .95f;
                var blend = corner ? Math.Min(.65f, .18f / Math.Max(distance,.001f)) : distance > .30f ? .38f : .16f;
                this.Parent.Position = Vector3.Lerp(this.Parent.Position, point, blend);
                var blendedForward = Vector3.Lerp(forward, tangent, blend);
                if (blendedForward.LengthSquared() > .0001f)
                    this.Parent.Rotation = Eco.Shared.Math.Quaternion.LookRotation(Vector3.Normalize(blendedForward));
                // The native owner does not receive ordinary MarkPoseUpdated
                // snapshots. Send a bounded correction at most ten times per
                // second while cornering, so its local cart stays on the rail
                // without the full-pose snap on every 20 Hz guidance tick.
                if (corner && (distance > .10f || alignment < .95f) && DateTime.UtcNow >= this.nextPulledCornerCorrection)
                {
                    this.Parent.SyncPositionAndRotation();
                    this.nextPulledCornerCorrection = DateTime.UtcNow.AddMilliseconds(100);
                }
                ((RailVehicleObject)this.Parent).MarkPoseUpdated();
                return;
            }
        }
        var tolerance = pulling ? .03f : .008f;
        if (distance < tolerance && alignment > (pulling ? .999f : .99995f) && frameAligned) return;
        this.Parent.Position = point;
        this.Parent.Rotation = this.RailRotation(rail,railT,tangent);
        this.Parent.SyncPositionAndRotation();
        ((RailVehicleObject)this.Parent).MarkPoseUpdated();
    }

    public override void PostInitialize()
    {
        base.PostInitialize();
        // A locomotive placed on bare ground has no rail to capture. Keep its
        // parked pose authoritative from the first client frame; otherwise a
        // WheelCollider/terrain contact can launch the local Rigidbody while
        // the server continues to hold the original placement position.
        if (this.RailVehicle.RailSpec.Powered && this.Parent.GetComponent<MountComponent>().Driver == null)
            this.RailVehicle.SetRailGuidance(true);
        // Eco's WorldObjectManager waits 1–5 seconds between ordinary ticks.
        // Only this component needs 20 Hz motion; do not retime the whole world.
        this.motionTimer = new Timer(_ =>
        {
            try { if (this.Parent.Initialized && !this.Parent.IsDestroyed) this.Tick(); }
            catch (Exception error)
            {
                this.RecoverMotionError(error);
            }
        }, null, 50, 50);
    }

    public override void Destroy()
    {
        lock (this.gate)
        {
            this.motionStopped = true;
            this.motionTimer?.Dispose();
            this.motionTimer = null;
        }
        base.Destroy();
    }

    [Notify, EnvVar]
    public bool IsHandcart(User user) => ((RailVehicleObject)this.Parent).RailSpec.Pullable;

    internal void PrepareNativeDriver()
    {
        lock (this.gate)
        {
            this.Handbrake = false;
            this.Derailed = false;
            this.pulledRail = this.current;
            this.pulledT = this.t;
            this.GuidePulledRail();
            this.current = null;
            this.state = default;
            this.nativeVelocityAvailable = false;
            this.nativeVelocity = Vector3.Zero;
            ((RailVehicleObject)this.Parent).SetRailGuidance(false);
        }
    }

    internal void PrepareServerDriver()
    {
        lock (this.gate)
        {
            this.EnsureDriveCommands();
            this.serverDriving = true;
            this.current ??= this.TryBindRail() ? this.current : null;
            this.RailVehicle.SetRailGuidance(true);
        }
    }

    internal void EnsureDriveCommands()
    {
        lock(this.gate)
        {
            if(this.commandsInitialized) return;
            var legacy=Parent.GetComponent<TrainControllerComponent>();
            this.serverThrottle=legacy?.Autopilot==true?1:0;
            this.serverDirection=legacy?.Reverse==true?-1:1;
            this.CommandBrake=legacy?.Autopilot!=true;
            this.commandsInitialized=true;
            Parent.SetDirty();
        }
    }

    internal void SetDriveCommands(double throttle,int direction,bool brake)
    {
        lock(this.gate)
        {
            this.EnsureDriveCommands();
            this.serverDriving=true;
            this.serverThrottle=double.IsFinite(throttle)?Math.Clamp(throttle,0,1):0;
            this.serverDirection=direction<0?-1:1;
            this.CommandBrake=brake;
            this.Handbrake=brake;
            Parent.SetDirty();
        }
    }

    internal void StopServerDriver()
    {
        // Explicit emergency/reset path only; never called for an operator exit.
        lock (this.gate) { this.serverDriving = false; this.serverThrottle = 0; this.CommandBrake=true; this.Handbrake = true; Parent.SetDirty(); }
    }

    internal void SetServerThrottle(double value, int direction)
    {
        this.SetDriveCommands(value,direction,value<=.001);
    }

    [Interaction(InteractionTrigger.InteractKey, "Pull", requiredEnvVars: new[] { "MinecartHandle", "IsHandcart" }, interactionDistance: 2.5f, priority: 20, authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    // Fallback for client builds that fail to forward a SpecificInteractable
    // parameter. It is explicitly hidden on the bucket, whose E action remains
    // storage. Handle targeting still wins through its higher-priority route.
    [Interaction(InteractionTrigger.InteractKey, "Pull", requiredEnvVars: new[] { "IsHandcart" }, interactionDistance: 2.5f, priority: 5,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction,
        DisallowedEnvVars = new string[] { "MinecartStorage" })]
    public void Grab(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (!((RailVehicleObject)this.Parent).RailSpec.Pullable || target.ContainsParameter("MinecartStorage") || !this.Parent.IsAuthorized(player.User, AccessType.FullAccess) || Vector3.Distance(player.User.Position, this.Parent.Position) > 3) return;
        lock (this.gate)
        {
            var mounts = this.Parent.GetComponent<MountComponent>();
            if (mounts.Driver == player) { mounts.TryDismountPlayer(player); return; }
            if (player.MountManager.IsMounted) { return; }
            if (mounts.Driver != null) { return; }
            if (this.Parent.GetComponent<RailCouplingComponent>()?.CanBoard == false) return;
            if (this.pickupPending) return;
            var localPlayer = Vector3.Transform(player.User.Position - this.Parent.Position, Quaternion.Inverse(this.Rotation));
            var side = this.ResolveGripSide(target, localPlayer);
            // The symmetric cart has one native driver mount. Turn it toward the
            // selected end before mounting so either modeled grip can be taken.
            if (side < 0)
            {
                var rotation = Quaternion.Normalize(this.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI));
                this.Parent.Rotation = new Eco.Shared.Math.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
                this.Parent.SyncPositionAndRotation();
            }
            this.Handbrake = false;
            this.Derailed = false;
            this.pulledRail = this.current;
            this.pulledT = this.t;
            this.GuidePulledRail();
            this.current = null;
            this.state = default;
            this.nativeVelocityAvailable = false;
            this.nativeVelocity = Vector3.Zero;
            ((RailVehicleObject)this.Parent).SetRailGuidance(false);
            this.Parent.GetComponent<VehicleComponent>().MountInteraction(player, trigger, target);
            // VehicleComponent and MountSpotPulled own client input, attachment,
            // hand IK and physics ownership. No player SetPosition loop.
        }
    }

    private int ResolveGripSide(InteractionTarget target, Vector3 localPlayer)
    {
        if (target.TryGetParameter("MinecartHandle", out var value) && int.TryParse(value?.ToString(), out var requested) && requested != 0)
            return Math.Sign(requested);
        return localPlayer.Z < 0 ? -1 : 1;
    }

    // SpecificInteractable child colliders can bypass the client's generic
    // world-object pickup affordance. Route explicitly to Eco's real hammer RPC,
    // retaining cargo/persistent-data, permissions, laws and inventory checks.
    [Notify, EnvVar]
    public bool HoldingHammer(User user) => user.Inventory.Toolbar.SelectedItem is HammerItem;

    // Generic pickup RPCs may not bypass the explicit hammer-hit interaction.
    public Eco.Core.Utils.Result CanPickup()
    {
        lock(this.gate) return this.pickupPending ? Eco.Core.Utils.Result.Succeeded : Eco.Core.Utils.Result.Fail(LocString.Empty);
    }

    [Interaction(InteractionTrigger.LeftClick, "Dismantle minecart (hammer)", requiredEnvVars: new[] { "HoldingHammer" }, interactionDistance: 5, priority: 40, authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction, AnimationDriven = true)]
    public async Task PickUpWithHammer(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (this.Parent.IsDestroyed || !this.Parent.IsAuthorized(player.User, AccessType.FullAccess) || Vector3.Distance(player.User.Position, this.Parent.Position) > 5) return;
        if (player.User.Inventory.Toolbar.SelectedItem is not HammerItem hammer) { return; }
        if (this.Parent.GetComponent<RailCouplingComponent>()?.Linked == true) return;
        // Explicit hammer pickup also releases this player's own seat. Never
        // silently eject another player's passenger or driver.
        var mounts = this.Parent.GetComponent<MountComponent>();
        if (mounts.MountedPlayers.Contains(player)) mounts.TryDismountPlayer(player);
        if (mounts.MountedPlayers.Any()) { return; }
        lock (this.gate)
        {
            if (this.pickupPending) return;
            this.pickupPending = true;
            this.groundVelocity = Vector3.Zero;
            this.state = this.state with { Speed = 0 };
        }
        try { await hammer.PickupWorldObject(player, this.Parent); }
        finally { lock (this.gate) this.pickupPending = false; }
    }

    private bool CanQuickTransfer(Player player, InteractionTarget target) => player != null && !this.Parent.IsDestroyed
        && target.ContainsParameter("MinecartStorage")
        && this.Parent.IsAuthorized(player.User, AccessType.ConsumerAccess)
        && Vector3.Distance(player.User.Position, this.Parent.Position) <= 3
        && this.Parent.GetComponent<MinecartRidingComponent>()?.StorageLocked != true;

    // SpecificInteractable bucket targets need explicit forwarding to Eco's
    // stockpile actions. The native methods retain inventory laws/restrictions.
    [Interaction(InteractionTrigger.RightClick, "Put %SelectedNonTool%", requiredEnvVars: new[] { "MinecartStorage", "SelectedNonTool", "CanPut" },
        interactionDistance: 3, priority: 35, authRequired: AccessType.ConsumerAccess, flags: InteractionFlags.BlocksOtherInteraction, MinCaloriesRequired = 0)]
    public void PutCargo(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (!CanQuickTransfer(player, target)) return;
        var storage = this.Parent.GetComponent<PublicStorageComponent>();
        if (storage.CanPut(player.User)) storage.PutItem(player, trigger, target);
    }

    [Interaction(InteractionTrigger.LeftClick, "Take %CanTake%", requiredEnvVars: new[] { "MinecartStorage", "CanTake" },
        interactionDistance: 3, priority: 35, authRequired: AccessType.ConsumerAccess, flags: InteractionFlags.BlocksOtherInteraction,
        MinCaloriesRequired = 0, DisallowedEnvVars = new[] { "HoldingHammer" })]
    public void TakeCargo(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (!CanQuickTransfer(player, target) || HoldingHammer(player.User)) return;
        var storage = this.Parent.GetComponent<PublicStorageComponent>();
        if (storage.CanTake(player.User) != null) storage.TakeItem(player, trigger, target);
    }

    [Interaction(InteractionTrigger.InteractKey, "Open minecart inventory", requiredEnvVars: new[] { "MinecartStorage" }, interactionDistance: 2.5f, priority: 20, authRequired: AccessType.ConsumerAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void OpenInventory(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (!target.ContainsParameter("MinecartStorage") || !this.Parent.IsAuthorized(player.User, AccessType.ConsumerAccess) || Vector3.Distance(player.User.Position, this.Parent.Position) > 3) return;
        var mounts = this.Parent.GetComponent<MountComponent>();
        if (mounts.MountedPlayers.Contains(player) && mounts.Driver != player)
        { mounts.TryDismountPlayer(player); return; }
        if (this.Parent.GetComponent<MinecartRidingComponent>()?.StorageLocked == true) { return; }
        this.Parent.OpenUI(player);
    }

    [Interaction(InteractionTrigger.RightClick, "Toggle minecart handbrake", modifier: InteractionModifier.Shift, interactionDistance: 2.5f, priority: 30, authRequired: AccessType.FullAccess)]
    public void Brake(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (!this.Parent.IsAuthorized(player.User, AccessType.FullAccess) || Vector3.Distance(player.User.Position, this.Parent.Position) > 3) return;
        lock (this.gate)
        {
            if(this.RailVehicle.RailSpec.Powered)
            {
                this.EnsureDriveCommands();
                this.SetDriveCommands(this.serverThrottle,this.serverDirection,!this.CommandBrake);
            }
            else this.Handbrake=!this.Handbrake;
        }
    }

    private Quaternion Rotation => new(this.Parent.Rotation.x, this.Parent.Rotation.y, this.Parent.Rotation.z, this.Parent.Rotation.w);

    private bool TryBindRail(bool recoverOldPlacement = false)
    {
        if (this.Derailed) return false;
        // Do not immediately re-capture the endpoint just left. Native gravity
        // owns the cart until it clears that endpoint, but other rails can catch it.
        if (this.departedRail is { } departed)
        {
            var offset = this.Parent.Position - this.departurePoint;
            if (offset.LengthSquared() > 1) this.departedRail = null;
            else
            {
                var candidate = TrackWorld.Capture(this.Parent.Position, this.Parent.Rotation.RotateVector(Vector3.UnitZ), industrial: this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster);
                if (candidate == null || candidate.Value.Rail.Cell == departed.Cell
                    || Vector3.Dot(offset, this.departureDirection) < .05f) return false;
                this.departedRail = null;
            }
        }
        if (this.current is { } old && !TrackWorld.Contains(old)) { this.current = null; this.state = default; }
        if (this.current is { } bound && Vector3.Distance(this.Parent.Position, this.RailPosition(bound,this.t,this.NextRail)) > .65f)
        { this.current = null; this.state = default; } // Lifted/moved externally: abandon the old rail lock.
        if (this.current != null) return true;
        var nearest = TrackWorld.Capture(this.Parent.Position, this.Parent.Rotation.RotateVector(Vector3.UnitZ), industrial: this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster);
        if (nearest == null && this.RailVehicle.RailSpec.Wheelbase > 1)
        {
            var best = float.MaxValue;
            foreach (var candidate in TrackWorld.Near(this.Parent.Position))
            {
                // A long body's axle chord is inside the curve centreline. Test
                // its physical pose rather than increasing the magnetic range.
                var body = RailGuidance.ProjectPose(candidate,this.Parent.Position,this.NextRail,
                    halfWheelbase:this.RailVehicle.RailSpec.Wheelbase/2);
                var offset = body.Point-this.Parent.Position;
                if (new Vector2(offset.X,offset.Z).Length()>.24f || Math.Abs(offset.Y)>.32f || body.Distance>=best) continue;
                nearest=(candidate,body.T); best=body.Distance;
            }
        }
        // Old fallback cubes placed the cart almost one block above the actual
        // rail. Only recover downward onto a rail directly underneath, on grab.
        if (nearest == null && recoverOldPlacement)
            nearest = TrackWorld.UnderCart(this.Parent.Position, this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster);
        if (nearest == null) return false;
        this.current = nearest.Value.Rail;
        this.inFlight = false;
        this.parkedOffRail = false;
        this.nativeGroundPhysics = false;
        this.t = nearest.Value.T;
        this.facing = Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ), this.current.Value.Profile.Tangent(this.t)) < 0 ? -1 : 1;
        ((RailVehicleObject)this.Parent).SetRailGuidance(true);
        this.groundVelocity = Vector3.Zero;
        var targetPose = this.RailPosition(this.current.Value, this.t, (r, end) => this.NextRail(r, end));
        this.captureOffset = this.Parent.Position - targetPose;
        if (recoverOldPlacement)
        {
            this.Parent.Position = targetPose;
            this.captureOffset = Vector3.Zero;
        }
        return true;
    }

    internal bool RecoverOnRail()
    {
        lock (this.gate)
        {
            if (this.Parent.GetComponent<MountComponent>().IsMounted || this.Parent.GetComponent<RailCouplingComponent>().Linked) return false;
            var nearest = TrackWorld.Capture(this.Parent.Position,this.Parent.Rotation.RotateVector(Vector3.UnitZ),.65f,.75f,this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster);
            if (nearest == null || nearest.Value.Rail.Profile.Radius+.001 < this.RailVehicle.RailSpec.MinimumRadius
                || this.Parent.GetComponent<RailCouplingComponent>().Load.Mass > nearest.Value.Rail.Profile.MaximumSupportedKg) return false;
            this.Derailed = false; this.current = null; this.departedRail = null; this.inFlight = false;
            this.state = default; this.Handbrake = true;
            return this.TryBindRail();
        }
    }

    public override void Tick()
    {
        lock (this.gate)
        {
            if (this.motionStopped || this.Parent.IsDestroyed || this.pickupPending) return;
            if(DateTime.UtcNow<this.nextMotionRetry) return;
            if(this.nextMotionRetry!=default)
            {
                this.nextMotionRetry=default;
            }
            var coupling = this.Parent.GetComponent<RailCouplingComponent>();
            var isFollower = coupling?.IsFollower == true;
            if (coupling is { AwaitingLoad: true })
            {
                this.RailVehicle.SetRailGuidance(true);
                if (!coupling.LoadMembersReady || isFollower || !this.TryBindRail()) return;
                if (this.current is not { } restored || !this.CanRestoreCoupledPose(restored, this.t)
                    || !coupling.FollowTrain(restored, this.t, this.facing, Vector3.Zero, restoring: true)) return;
                this.AcceptCoupledPose(restored, this.t, this.facing, Vector3.Zero);
                coupling.CompleteLoad();
                this.lastTick = DateTime.UtcNow;
                return;
            }
            // Guided followers already surrender their independent solver.
            // detectCollisions=false also removes player contact and raycasts
            // against passenger-seat targets, not just train-to-train forces.
            this.Parent.SetAnimatedState("VehicleCollisionsEnabled", true);
            if (isFollower) { this.RailVehicle.SetRailGuidance(true); return; }
            if (this.placementSnapPending)
            {
                this.placementSnapPending = false;
                this.SnapPlacedCart();
            }
            var now = DateTime.UtcNow;
            // Native world-object ticks may be much slower than a render frame.
            // Preserve normal elapsed time and substep instead of slowing physics.
            var elapsed = this.lastTick == default ? 0 : (now - this.lastTick).TotalSeconds;
            var dt = Math.Clamp(elapsed, 0, 2);
            this.lastTick = now;
            var driver = this.Parent.GetComponent<MountComponent>().Driver;
            // Operator exit/disconnect events perform the handoff. A vacant cab
            // alone is not a reason to undo Stop and Disable Autopilot each tick.
            // Rigidbody suspension carries this vehicle, not every car in the
            // consist. Aggregate mass remains in server train-performance math.
            this.Parent.SetAnimatedState("TrainMass", (float)this.Parent.GetComponent<RailCouplingComponent>().Load.Mass);
            if (driver != null && !this.serverDriving)
            {
                this.inFlight = false;
                ((RailVehicleObject)this.Parent).SetRailGuidance(false);
                // Correct a pulled pose only after an accepted native physics
                // packet (OnNativePhysicsPose), not continuously between packets.
                // Repeated ForcePosition RPCs fight the driver's native solver.
                // Network poses arrive less frequently than the 20 Hz solver.
                // Measure between changed poses, not one tiny timer interval,
                // otherwise a normal packet becomes a huge release impulse.
                if (!this.nativeWasPulling && !this.nativeVelocityAvailable)
                {
                    this.nativePosition = this.Parent.Position;
                    this.nativeVelocity = Vector3.Zero;
                    this.nativeSampleTime = now;
                }
                else if (!this.nativeVelocityAvailable)
                {
                    var delta = this.Parent.Position - this.nativePosition;
                    var sampleDt = (now - this.nativeSampleTime).TotalSeconds;
                    if (delta.LengthSquared() > .000001f && sampleDt > .005)
                    {
                        var measured = delta / (float)sampleDt;
                        this.nativeVelocity = delta.Length() < 2 && measured.Length() < 6
                            ? Vector3.Lerp(this.nativeVelocity, measured, .65f) : Vector3.Zero;
                        this.nativePosition = this.Parent.Position;
                        this.nativeSampleTime = now;
                    }
                    else if (sampleDt > .25)
                        this.nativeVelocity *= (float)Math.Exp(-dt / .15);
                }
                this.nativeWasPulling = true;
                this.current = null;
                // Native packets arrive less often than this component's 20 Hz
                // timer. Reproject the most recent accepted pose between packets
                // so a sharp turn does not wait a whole network interval.
                if (this.nativeVelocityAvailable) this.GuidePulledRail();
                this.Sound(TrackWorld.Nearest(this.Parent.Position, .65f, this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster) != null ? this.nativeVelocity.Length() : 0);
                return; // Native cart client owns motion while pulling.
            }
            if (this.nativeWasPulling)
            {
                this.nativeWasPulling = false;
                this.current = null;
                if (!this.ReleaseRailMotion()) this.groundVelocity = this.nativeVelocity;
                this.nativeVelocityAvailable = false;
            }
            if (dt <= 0 || this.pickupPending || this.Parent.IsDestroyed) { this.Sound(0); return; }
            if (this.inFlight)
            {
                this.TickFlight(dt);
                return;
            }
            if (!this.TryBindRail())
            {
                if (this.serverDriving && this.current is { } retained)
                {
                    var retainedPoint = this.RailPosition(retained, this.t, (r, end) => this.NextRail(r, end));
                    var retainedTangent = this.RailTangent(retained, this.t, (r, end) => this.NextRail(r, end));
                    this.Parent.Position = retainedPoint;
                    this.Parent.Rotation = Eco.Shared.Math.Quaternion.LookRotation(retainedTangent * this.facing);
                    this.RailVehicle.SetRailGuidance(true);
                    this.RailVehicle.PublishRailPose(Vector3.Zero);
                    return;
                }
                // Off rails, yield completely to native rigidbody physics. The
                // old horizontal ground solver wrote a fixed Y every tick and
                // could pin a lifted cart in midair indefinitely.
                this.ReleaseToGroundPhysics();
                return;
            }
            // Derailments now require explicit recovery; do not silently reattach.
            if (this.Derailed)
            {
                if (!this.serverDriving) { this.ReleaseToGroundPhysics(); return; }
                // Server throttle mode has no lateral steering input. Keep the
                // train on the selected rail instead of allowing a native body
                // contact to turn a straight run into a derailment.
                this.Derailed = false; this.Handbrake = true; this.state = this.state with { Speed = 0 };
            }
            var walkSpeed = 0f;
            var cargo = this.Parent.GetComponent<RailCouplingComponent>().Performance.Mass - this.Tuning.EmptyMassKg;
            var steps = Math.Max(1, (int)Math.Ceiling(dt / .02));
            for (var i = 0; i < steps && !this.Derailed && this.current != null; i++) this.Step(dt / steps, cargo, walkSpeed);
            if (this.current is { } rail)
            {
                this.captureOffset = RailGuidance.DecayOffset(this.captureOffset, dt);
                var point = this.RailPosition(rail, this.t, (r, end) => this.NextRail(r, end)) + this.captureOffset;
                var tangent = this.RailTangent(rail, this.t, (r, end) => this.NextRail(r, end));
                var velocity = tangent * (float)this.state.Speed;
                if (this.serverDriving)
                {
                    // This mode is deliberately authoritative: never let the
                    // mounted native rigidbody pull the locomotive off the rail.
                    this.captureOffset = Vector3.Zero;
                    point = this.RailPosition(rail, this.t, (r, end) => this.NextRail(r, end));
                    this.Parent.Position = point;
                    this.Parent.Rotation = this.RailRotation(rail,this.t,tangent * this.facing);
                    this.RailVehicle.SetRailGuidance(true);
                    this.RailVehicle.PublishRailPose(velocity);
                    this.lastPublishedRailVelocity = velocity;
                }
                else if ((rail.Profile.Coaster && Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitY),rail.Profile.Up(this.t))<.99995f)
                    || RailGuidance.ShouldPublishPose(this.Parent.Position, point,
                    this.Parent.Rotation.RotateVector(Vector3.UnitZ), tangent * this.facing,
                    this.lastPublishedRailVelocity, velocity))
                {
                    this.Parent.Position = point;
                    this.Parent.Rotation = this.RailRotation(rail,this.t,tangent * this.facing);
                    // Normal physics keyframes are interpolated by SyncPhysics.
                    // ForcePosition RPCs are for explicit snaps, not 20 Hz motion.
                    ((RailVehicleObject)this.Parent).PublishRailPose(velocity);
                    this.lastPublishedRailVelocity = velocity;
                }
                this.Parent.GetComponent<RailCouplingComponent>()?.FollowTrain(rail, this.t, this.facing, velocity);
            }
            this.Sound(this.current != null ? this.state.Speed : 0);
        }
    }

    private void TickGround(double dt, double elapsed)
    {
        var desired = Vector3.Zero;
        var cargo = this.Parent.GetComponent<PublicStorageComponent>().Inventory.NonEmptyStacks.Sum(s => (double)s.Weight) / 1000;
        cargo += (this.Parent.GetComponent<MinecartRidingComponent>()?.PassengerMassKg ?? 0);
        var position = this.Parent.Position;
        var rotation = this.Rotation;
        var steps = Math.Max(1, (int)Math.Ceiling(dt / .02));
        for (var step = 0; step < steps; step++)
        {
            if (desired.LengthSquared() > .04f)
            {
                var forward = Vector3.Transform(Vector3.UnitZ, rotation);
                var travel = Vector3.Normalize(desired) * (Vector3.Dot(forward, desired) < 0 ? -1 : 1);
                var heading = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(travel.X, travel.Z));
                var turned = Quaternion.Slerp(rotation, heading, (float)Math.Min(1, dt / steps * 2));
                if (GroundWorld.Resolve(position, turned, this.Parent) != null) rotation = turned;
            }
            position = GroundMotion.Step(position, ref this.groundVelocity, desired, Tuning.EmptyMassKg + cargo,
                dt / steps, this.Handbrake, p => GroundWorld.Resolve(p, rotation, this.Parent));
        }
        this.Parent.Position = position;
        this.Parent.Rotation = new Eco.Shared.Math.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
        this.Parent.SyncPositionAndRotation();
        ((RailVehicleObject)this.Parent).MarkPoseUpdated();
        this.Sound(0);
        this.Parent.GetComponent<RailCouplingComponent>()?.FollowFree(this.groundVelocity);
    }

    private void ReleaseToGroundPhysics()
    {
        if (this.RailVehicle.RailSpec.Powered && this.Parent.GetComponent<MountComponent>().Driver == null)
        {
            this.ParkOffRail();
            return;
        }
        if (!this.nativeGroundPhysics)
        {
            ((RailVehicleObject)this.Parent).SetRailGuidance(false);
            ((RailVehicleObject)this.Parent).SetPhysicsController(null!);
            this.nativeGroundPhysics = true;
        }
        this.Sound(0);
    }

    private void ParkOffRail()
    {
        if (!this.parkedOffRail || this.nativeGroundPhysics)
        {
            this.RailVehicle.SetRailGuidance(true);
            this.RailVehicle.PublishRailPose(Vector3.Zero);
        }
        this.parkedOffRail = true;
        this.nativeGroundPhysics = false;
        this.groundVelocity = Vector3.Zero;
        this.flightVelocity = Vector3.Zero;
        this.state = this.state with { Speed = 0, Distance = 0 };
        this.Handbrake = true;
        this.Sound(0);
    }

    private void TickFlight(double dt)
    {
        var count = Math.Max(1, (int)Math.Ceiling(dt * Math.Max(50, (this.flightVelocity.Length() + 9.81 * dt) / .05)));
        var position = this.Parent.Position;
        for (var i = 0; i < count && this.inFlight; i++)
        {
            var step = AirMotion.Step(position, this.flightVelocity, dt / count);
            var next = step.Position;
            this.flightVelocity = step.Velocity;
            // Rejoin rails only on descent, never magnetically attach to a rail
            // while launching upward. Preserve signed incoming momentum.
            this.Parent.Position = next;
            if (this.flightVelocity.Y <= 0 && this.TryBindRail())
            {
                this.state = new(0, RailGuidance.ReleaseSpeed(this.flightVelocity,
                    this.current!.Value.Profile.Tangent(this.t)), 0);
                this.captureOffset = Vector3.Zero;
                this.inFlight = false;
                break;
            }
            var floor = FlightWorld.LandingHeight(position, next, this.Rotation, this.departedRail?.Cell);
            if (floor is { } height)
            {
                next.Y = height;
                this.flightVelocity.Y = 0;
                this.inFlight = false;
            }
            if (FlightWorld.BodyBlocked(next, this.Rotation))
            {
                next.X = position.X; next.Z = position.Z;
                this.flightVelocity.X = 0; this.flightVelocity.Z = 0;
                if (this.flightVelocity.Y > 0) { next.Y = position.Y; this.flightVelocity.Y = 0; }
            }
            position = next;
            this.Parent.Position = position;
        }
        if (this.current != null)
        {
            ((RailVehicleObject)this.Parent).PublishRailPose(this.flightVelocity);
        }
        else if (this.inFlight)
            ((RailVehicleObject)this.Parent).PublishRailPose(this.flightVelocity);
        else if (this.RailVehicle.RailSpec.Powered && this.Parent.GetComponent<MountComponent>().Driver == null)
            this.ParkOffRail();
        else
        {
            ((RailVehicleObject)this.Parent).ReleaseRailPose(this.flightVelocity);
            this.nativeGroundPhysics = true;
        }
        this.Sound(0);
        this.Parent.GetComponent<RailCouplingComponent>()?.FollowFree(this.flightVelocity);
    }

    private void Step(double dt, double cargo, float walkSpeed)
    {
        var rail = this.current!.Value;
        if (!this.CheckFootprint(rail, this.t, this.state.Speed)) return;
        var tangent = this.RailTangent(rail, this.t, (r, end) => this.NextRail(r, end));
        var chain = rail.Profile.Chain ? MinecartChainDriveObject.LiftFor(rail.Cell, this.Parent.ID) : (Watts: 0d, Multiplier: 1f);
        this.chainSpeedMultiplier=chain.Multiplier;
        var lift = ChainLift.Force(Tuning.EmptyMassKg + cargo, tangent.Y, this.state.Speed, chain.Watts, ChainLift.TargetSpeed(chain.Multiplier));
        this.chainLiftEngaged = rail.Profile.Chain && lift > 0 && tangent.Y > .001f;
        var tramPower=this.RailVehicle.RailSpec.Tram && rail.Profile.Tram
            ? TramCableDriveObject.PowerFor(rail.Cell,this.Parent.ID) : 0f;
        var control = this.Parent.GetComponent<TrainControllerComponent>();
        // Both controllers operate the same command state and safety envelope.
        var input = control?.Control(rail,this.t,this.facing,this.state.Speed,dt)
            ?? (Force:this.serverDriving ? this.Parent.GetComponent<RailCouplingComponent>().Performance.DriveForce(this.state.Speed)
                * this.serverThrottle * this.serverDirection * this.facing : 0d, Brake:this.serverDriving && this.CommandBrake);
        if(this.RailVehicle.RailSpec.Tram && rail.Profile.Tram)
            input=TramPowerRules.UsesCable(true,tramPower) ? (input.Force*tramPower,input.Brake) : (0,true);
        if ((this.serverDriving || control?.Active==true) && input.Force != 0
            && !(this.RailVehicle.RailSpec.Tram && !TramPowerRules.UsesOnboardFuel(rail.Profile.Tram)))
        {
            var fuel = this.Parent.GetComponent<FuelSupplyComponent>();
            var joules = (float)(RailEconomy.FuelWatts(this.RailVehicle.RailSpec) * dt * this.serverThrottle);
            if (fuel == null || fuel.ConsumeAsMuchAsPossible(joules) + .001f < joules)
            {
                this.Handbrake = true;
                control?.ReportSafety("Out of fuel");
                input = (0, true);
            }
            if(Parent.GetComponent<RailConditionComponent>()?.ConditionPercent<=0)
            {
                this.Handbrake=true; input=(0,true);
            }
        }
        if (this.serverDriving || control?.Active == true) this.Handbrake = input.Brake;
        if(this.RailVehicle.RailSpec.Coaster && CoasterStationComponent.At(rail.Cell) is {} station)
        {
            input=station.Control(this.Parent.GetComponent<RailCouplingComponent>(),this.facing,this.state.Speed);
            this.Handbrake=input.Brake;
            this.coasterApproachBraking=false;
        }
        else if(this.RailVehicle.RailSpec.Coaster)
        {
            var approaching=CoasterStationComponent.ApproachBrake(rail,this.t,this.state.Speed,this.Parent.GetComponent<RailCouplingComponent>());
            if(approaching||this.coasterApproachBraking)this.Handbrake=approaching;
            this.coasterApproachBraking=approaching;
        }
        this.Parent.SetAnimatedState("AutoRunning", (this.serverDriving || control?.Active == true) && input.Force != 0);
        var push = input.Force;
        var brake = this.Handbrake ? 1 : 0;
        this.state = RailPhysics.Integrate(this.state with { Distance = 0 },
            new MinecartInput(cargo, push + lift, brake, tangent.Y, rail.Profile.Curvature), Tuning, dt).State;
        if (this.state.Derailed) { this.Derailed = true; this.state = this.state with { Speed = 0 }; return; }
        var limitedTravel = RailGuidance.LimitBufferTravel(rail, this.t, this.state.Distance, (r, end) => this.NextRail(r, end), this.RailVehicle.CouplerOffset + .194f);
        limitedTravel = this.LimitVehicleTravel(rail, this.t, limitedTravel);
        if (Math.Abs(limitedTravel - this.state.Distance) > .000001)
            this.state = this.state with { Speed = 0, Distance = limitedTravel };
        var distance = this.t * rail.Profile.Length + (float)this.state.Distance;
        for (var crossed = 0; crossed < 8; crossed++)
        {
            if (distance >= 0 && distance <= rail.Profile.Length) break;
            var end = distance < 0 ? 0 : 1;
            var overshoot = distance < 0 ? -distance : distance - rail.Profile.Length;
            var next = this.NextRail(rail, end);
            if (next == null)
            {
                var endTangent = this.RailTangent(rail, end, (r, e) => this.NextRail(r, e));
                var velocity = endTangent * (float)this.state.Speed;
                this.departureDirection = endTangent * (end == 0 ? -1 : 1);
                this.departurePoint = this.RailPosition(rail, end, (r, e) => this.NextRail(r, e));
                this.Parent.Position = this.departurePoint + this.departureDirection * overshoot;
                this.Parent.Rotation = Eco.Shared.Math.Quaternion.LookRotation(endTangent * this.facing);
                this.departedRail = rail;
                this.current = null;
                this.captureOffset = Vector3.Zero;
                this.nativeGroundPhysics = false;
                this.flightVelocity = velocity;
                this.inFlight = true;
                ((RailVehicleObject)this.Parent).SetRailGuidance(true);
                ((RailVehicleObject)this.Parent).PublishRailPose(velocity);
                this.Sound(0);
                return;
            }
            var sign = end == next.Value.End ? -1 : 1;
            this.state = this.state with { Speed = this.state.Speed * sign };
            this.facing *= sign;
            rail = next.Value.Rail;
            if (!this.CheckRail(rail, this.state.Speed)) return;
            distance = next.Value.End == 0 ? overshoot : rail.Profile.Length - overshoot;
        }
        this.current = rail;
        this.t = Math.Clamp(distance / rail.Profile.Length, 0, 1);
    }

    private float chainSpeedMultiplier=1;
    internal bool EffectiveHandbrake => this.Parent.GetComponent<RailCouplingComponent>() is { IsFollower:true } coupling
        ? coupling.Leader().Parent.GetComponent<MinecartMotionComponent>().Handbrake : this.Handbrake;
    private void Sound(double speed)
    {
        if(this.RailVehicle.RailSpec.Powered)
            this.Parent.SetAnimatedState("CabTravelGatesClosed",Math.Abs(speed)>.05);
        if (this.RailVehicle.RailSpec.HumanPowered)
            this.Parent.SetAnimatedState("HandcarPumping", this.Parent.GetComponent<MountComponent>().Driver != null && Math.Abs(speed) > .05);
        var sound = CartSound.AtSpeed(speed);
        this.Parent.SetAnimatedState("RailVolume", sound.Volume);
        this.Parent.SetAnimatedState("RailPitch", sound.Pitch);
        this.Parent.SetAnimatedState("ChainLiftPitch",this.chainSpeedMultiplier);
        var curvature = this.current?.Profile.Curvature ?? this.pulledRail?.Profile.Curvature ?? 0;
        this.Parent.SetAnimatedState("CornerVolume", CartSound.CornerVolume(speed, curvature));
        var braking=this.EffectiveHandbrake;
        this.Parent.SetAnimatedState("ChainLiftRunning", this.current is { } chainRail && chainRail.Profile.Chain
            && this.chainLiftEngaged && !braking && speed > .03);
        var brake = CartSound.Braking(speed, braking);
        this.Parent.SetAnimatedState("BrakeVolume", brake.Volume);
        for (var tier = 1; tier <= 3; tier++)
            this.Parent.SetAnimatedState("BrakeSparks" + tier, brake.SparkTier == tier);
    }
}

