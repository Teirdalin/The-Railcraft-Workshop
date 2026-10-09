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
public sealed partial class MinecartMotionComponent : WorldObjectComponent, IHasEnvVars, IPickupConfirmationComponent
{
    [Serialized] public Guid CoasterHomeStation { get; set; }
    [Serialized] public long CoasterDepartureOrder { get; set; }
    [Serialized] public int CoasterRecoveryFacing {get;set;}=1;
    internal void RecoverAtStation(VoxelRail rail, float parameter, int orientation)
    {
        lock(this.gate)
        {
            this.Derailed=false;
            this.ResetCoupledMotion();
            Interlocked.Exchange(ref this.pendingCoupledPose,null);
            this.coupledLandedEpoch=Volatile.Read(ref this.coupledAirborneEpoch);
            this.loadedMomentum=null;this.resumeMomentumPending=false;
            this.placementSnapPending=false;
            this.flightVelocity=this.freeFollowerTrajectory=Vector3.Zero;
            this.freeFollowerPose=false;
            this.parkedOffRail=false;
            this.pulledRail=null;
            this.nextMotionRetry=default;
            this.CommandBrake=true;
            this.AcceptCoupledPose(rail,parameter,orientation,Vector3.Zero);
            this.ArmStationQueue();
            this.Parent.SyncPositionAndRotation();
            this.Parent.SetDirty();
        }
    }
    internal static bool WithRecoveryLocks(IEnumerable<MinecartMotionComponent> members,Func<bool> action)
    {
        var acquired=new List<object>();
        try
        {
            foreach(var motion in members.OrderBy(m=>m.Parent.ID))
            {
                if(!Monitor.TryEnter(motion.gate))return false;
                acquired.Add(motion.gate);
            }
            return action();
        }
        finally{foreach(var memberGate in acquired.AsEnumerable().Reverse())Monitor.Exit(memberGate);}
    }
    private bool handbrake;
    [Serialized] public bool Handbrake
    {
        get => this.handbrake;
        set { if (this.handbrake == value) return; this.handbrake = value; this.WakeMotion(); }
    }
    // These are train-owned commands, not the lifetime of an occupied seat.
    [Serialized] private bool serverDriving;
    private bool coasterApproachBraking;
    [Serialized] private double serverThrottle;
    [Serialized] private int serverDirection = 1;
    [Serialized] private bool commandsInitialized;
    private bool commandBrake = true;
    [Serialized] public bool CommandBrake
    {
        get => this.commandBrake;
        internal set { if (this.commandBrake == value) return; this.commandBrake = value; this.WakeMotion(); }
    }
    internal bool ServerDriving => this.serverDriving;
    internal double ServerThrottle => this.serverThrottle;
    internal int ServerDirection => this.serverDirection;
    internal Vector3 CurrentRailVelocity=>ManualCart is {} manual?manual.Velocity:this.inFlight?this.flightVelocity:this.freeFollowerPose?this.freeFollowerTrajectory:this.current is {} rail?rail.Profile.Tangent(this.t)*(float)this.state.Speed:this.nativeVelocity;
    [Serialized] public bool Derailed { get; set; }
    private readonly object gate = new();
    // Tracks constrain the cart. Curve overload must not disable automated routes.
    private static readonly MinecartTuning CartTuning = new(DerailLateralAcceleration: double.PositiveInfinity);
    private static readonly MinecartTuning TrainTuning = new(EmptyMassKg: 600, DerailLateralAcceleration: double.PositiveInfinity);
    private RailVehicleObject RailVehicle => (RailVehicleObject)this.Parent;
    private MinecartTuning Tuning
    {
        get
        {
            var performance=this.Parent.GetComponent<RailCouplingComponent>()?.Performance;
            return new(EmptyMassKg:this.RailVehicle.RailSpec.EmptyKg,
                AerodynamicDrag:.65+(performance?.Length??1)*.15,
                StaticResistanceN:this.RailVehicle.RailSpec.Coaster?15:95+(performance?.Cars.Count??1)*22,
                MaximumHandbrakeForceN:performance?.BrakingForce??this.RailVehicle.RailSpec.BrakeN,
                DerailLateralAcceleration:double.PositiveInfinity);
        }
    }
    private double guidedSpeed;
    internal (VoxelRail Rail, int End)? NextRail(VoxelRail rail, int end)
    {
        if(ManualCart is {} manual)return manual.Next(rail,end);
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/NextRail", this.Parent);
        var control = this.Parent.GetComponent<RailCouplingComponent>()?.Leader().Parent.GetComponent<TrainControllerComponent>();
        var next=control?.Active == true ? control.Next(rail, end) : TrackWorld.NeighborForVehicle(rail, end);
        return this.RailVehicle.RailSpec.Tram && next is {} adjacent && !adjacent.Rail.Profile.Tram?null:next;
    }
    private Vector3 RailPosition(VoxelRail rail, float parameter, Func<VoxelRail, int, (VoxelRail Rail, int End)?> neighbor) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/RailPosition", this.Parent); return RailGuidance.PosePosition(rail, parameter, neighbor, this.RailVehicle.RailSpec.Wheelbase / 2); }
    private Vector3 RailTangent(VoxelRail rail, float parameter, Func<VoxelRail, int, (VoxelRail Rail, int End)?> neighbor) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/RailTangent", this.Parent); return RailGuidance.AxleTangent(rail, parameter, neighbor, this.RailVehicle.RailSpec.Wheelbase / 2); }

    internal bool CheckRail(VoxelRail rail, double speed)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/CheckRail", this.Parent);
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
                this.RailVehicle.RailSpec.Pullable || this.RailVehicle.RailSpec.HumanPowered || this.RailVehicle.RailSpec.Coaster)) return true;
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
        this.flightFacingSign = Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ), velocity) < 0 ? -1 : 1;
        this.RailVehicle.SetRailGuidance(true);
        this.RailVehicle.PublishRailPose(velocity);
        return false;
    }
    private bool CheckFootprint(VoxelRail rail, float parameter, double speed)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/CheckFootprint", this.Parent);
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
    // Transient presentation offset: rail position and momentum remain authoritative.
    // Decay relative to the live rail frame so bends/banks keep moving during landing.
    private Quaternion landingRotationOffset = Quaternion.Identity;
    private Vector3 landingPivot;
    private double landingRotationElapsed = .45;
    private bool LandingRotationActive => this.landingRotationElapsed < .45;
    private void BeginLandingRotation(VoxelRail rail, float parameter, Vector3 forward)
    {
        if (!this.RailVehicle.RailSpec.Coaster) return;
        var frame = this.RailRotation(rail, parameter, forward);
        var target = new Quaternion(frame.x, frame.y, frame.z, frame.w);
        this.landingRotationOffset = Quaternion.Normalize(this.Rotation * Quaternion.Inverse(target));
        this.landingPivot=Vector3.Zero;
        this.landingRotationElapsed = 0;
    }
    private void AdvanceLandingRotation(double seconds)
    {
        if (this.LandingRotationActive && double.IsFinite(seconds))
            this.landingRotationElapsed = Math.Min(.45, this.landingRotationElapsed + Math.Max(0, seconds));
    }
    private Eco.Shared.Math.Quaternion GuidedRotation(VoxelRail rail, float parameter, Vector3 forward)
    {
        var frame = this.RailRotation(rail, parameter, forward);
        if (!this.LandingRotationActive) return frame;
        var progress = (float)(this.landingRotationElapsed / .45);
        var smooth = progress * progress * (3 - 2 * progress);
        var offset = Quaternion.Slerp(this.landingRotationOffset, Quaternion.Identity, smooth);
        var result = Quaternion.Normalize(offset * new Quaternion(frame.x, frame.y, frame.z, frame.w));
        return new(result.X, result.Y, result.Z, result.W);
    }
    private Vector3 LandingPosition(Vector3 point, VoxelRail rail, float parameter, Vector3 forward, Eco.Shared.Math.Quaternion rotation)
    {
        if (!this.LandingRotationActive) return point;
        return this.RailSupportPosition(point,rail,parameter,forward,rotation,this.landingPivot);
    }
    private Vector3 RailSupportPosition(Vector3 point,VoxelRail rail,float parameter,Vector3 forward,Eco.Shared.Math.Quaternion rotation,Vector3 pivot)
    {
        var guided=this.RailRotation(rail,parameter,forward);
        // While the incoming body pitch is being retained, its lowest wheel
        // must stay above its normal contact plane. Settle each car on its own
        // running gear instead of rotating a rail-height centre into the rail.
        var lift=AirMotion.LandingClearance(new(rotation.x,rotation.y,rotation.z,rotation.w),
            new(guided.x,guided.y,guided.z,guided.w),rail.Profile.Up(parameter),
            this.RailVehicle.RailSpec.Wheelbase/2,this.RailVehicle.ContactHalfSize.X);
        var offset=guided.RotateVector(pivot)-rotation.RotateVector(pivot);
        return point+offset+rail.Profile.Up(parameter)*Math.Max(0,lift-Vector3.Dot(offset,rail.Profile.Up(parameter)));
    }
    internal RailCell? BoundRailCell=>ManualCart is {} manual?manual.State?.Rail.Cell:this.current?.Cell;
    internal Eco.Shared.Math.Quaternion RailRotation(VoxelRail rail,float parameter,Vector3 forward)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Orientation/RailRotation", this.Parent);
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
    private readonly RailMotionSleepClock sleepClock = new();
    private Vector3 sleepingPosition;
    private Eco.Shared.Math.Quaternion sleepingRotation;
    private DateTime nextMotionRetry;
    internal void RecoverMotionError(Exception error)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/RecoverMotionError", this.Parent);
        lock(this.gate)
        {
            if(this.motionStopped || Parent.IsDestroyed) return;
            this.WakeMotion();
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
    private DateTime lastParkedPosePublished;
    private Vector3 flightVelocity;
    private int flightFacingSign = 1;
    private bool freeFollowerPose;
    private DateTime lastFreeFollowerAt;
    private bool chainLiftEngaged;

    [Interaction(InteractionTrigger.InteractKey, "Shove minecart", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "MinecartHandle" }, interactionDistance: 2.5f, priority: 50,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void Shove(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if(ManualCart is {} manual)
        {
            lock(gate){if(!motionStopped&&!pickupPending&&!Parent.IsDestroyed&&target.ContainsParameter("MinecartHandle")
                &&Parent.IsAuthorized(player.User,AccessType.FullAccess)&&Vector3.Distance(player.User.Position,Parent.Position)<=3)manual.Shove(player);}
            return;
        }
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/Shove", this.Parent);
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
            var direction = RailGuidance.SelectShoveDirection(player.User.Position, this.Parent.Position,
                this.Parent.Rotation.RotateVector(Vector3.UnitZ)) * this.facing;
            this.lastShoveAt = now;
            this.WakeMotion();
            this.Handbrake = false;
            var mass = this.Parent.GetComponent<RailCouplingComponent>().Performance.Mass;
            this.state = this.state with { Speed = Math.Clamp(this.state.Speed + direction * 180 / mass, -12, 12) };
        }
    }

    [Interaction(InteractionTrigger.InteractKey, "Shove coaster cart", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "CoasterEnd" }, interactionDistance: 2.5f, priority: 50,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ShoveCoasterFromEnd(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
        { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/ShoveCoasterFromEnd", this.Parent); this.ShoveCoaster(player, trigger, target); }

    [Interaction(InteractionTrigger.InteractKey, "Shove coaster cart", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "CoasterShove" }, interactionDistance: 2.5f, priority: 50,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ShoveCoaster(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/ShoveCoaster", this.Parent);
        if(!this.RailVehicle.RailSpec.Coaster || player==null || player.MountManager.IsMounted
            || (!target.ContainsParameter("CoasterShove") && !target.ContainsParameter("CoasterEnd"))
            || !this.Parent.IsAuthorized(player.User,AccessType.FullAccess)) return;
        var leader=this.Parent.GetComponent<RailCouplingComponent>().Leader().Parent.GetComponent<MinecartMotionComponent>();
        leader.ApplyCoasterShove(player,this.Parent.GetComponent<RailCouplingComponent>());
    }
    private void ApplyCoasterShove(Player player,RailCouplingComponent touchedCart)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/ApplyCoasterShove", this.Parent);
        lock(this.gate)
        {
            var now=DateTime.UtcNow;
            if(!this.RailVehicle.RailSpec.Coaster || this.motionStopped || this.pickupPending || this.Parent.IsDestroyed || this.Derailed
                || this.Parent.GetComponent<RailCouplingComponent>().AwaitingLoad
                || this.Parent.GetComponent<RailConditionComponent>()?.ConditionPercent<=0
                || (now-this.lastShoveAt).TotalSeconds<.8
                || Vector3.Distance(player.User.Position,touchedCart.Parent.Position)>3
                || !this.Parent.IsAuthorized(player.User,AccessType.FullAccess)
                || Math.Abs(this.state.Speed)>.35 || !this.TryBindRail()) return;
            var relativeFacing=this.Parent.GetComponent<RailCouplingComponent>().FacingRelativeTo(touchedCart);
            if(relativeFacing==0) return;
            var direction=RailGuidance.SelectShoveDirection(player.User.Position,touchedCart.Parent.Position,
                touchedCart.Parent.Rotation.RotateVector(Vector3.UnitZ))*relativeFacing*this.facing;
            var mass=Math.Max(1,this.Parent.GetComponent<RailCouplingComponent>().Performance.Mass);
            this.lastShoveAt=now;
            this.coasterQueueFeed=false;
            this.WakeMotion();
            this.Handbrake=false;
            this.CommandBrake=false;
            this.state=this.state with { Speed=direction*Math.Clamp(350/mass,.45,1.6) };
            this.Parent.SetDirty();
        }
    }

    [Interaction(InteractionTrigger.InteractKey, "Shove train car", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "IsShoveableTrain" }, interactionDistance: 3, priority: 40,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ShoveTrainCar(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/ShoveTrainCar", this.Parent);
        if (player == null || player.MountManager.IsMounted || !this.IsShoveableTrain(player.User)
            || !this.Parent.IsAuthorized(player.User, AccessType.FullAccess)) return;
        var coupling = this.Parent.GetComponent<RailCouplingComponent>();
        var leader=coupling.Leader().Parent.GetComponent<MinecartMotionComponent>();
        if(leader.ManualCart!=null)leader.QueueManualShove(player,coupling);
        else leader.ApplyTrainShove(player,coupling);
    }

    private void ApplyTrainShove(Player player, RailCouplingComponent touchedCar)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/ApplyTrainShove", this.Parent);
        lock (this.gate)
        {
            var now = DateTime.UtcNow;
            if (this.motionStopped || this.pickupPending || this.Parent.IsDestroyed || this.Derailed
                || (now - this.lastShoveAt).TotalSeconds < .8
                || Vector3.Distance(player.User.Position, touchedCar.Parent.Position) > 3) return;
            var group = this.Parent.GetComponent<RailCouplingComponent>().Group();
            if (group.Any(car => car.AwaitingLoad || car.Parent.IsDestroyed
                || !car.Parent.IsAuthorized(player.User, AccessType.FullAccess)
                || car.Parent.GetComponent<MinecartMotionComponent>() is { } motion
                    && (motion.Derailed || motion.motionStopped || motion.pickupPending)
                || car.Parent.GetComponent<RailConditionComponent>()?.ConditionPercent <= 0)) return;
            if (group.Any(car => car.Parent.GetComponent<MountComponent>().Driver != null
                || car.Parent.GetComponent<MineTrainDrivingComponent>()?.HasStandingOperator == true
                || car.Parent.GetComponent<TrainControllerComponent>()?.Autopilot == true
                || car.Parent.GetComponent<MinecartMotionComponent>().ServerThrottle > .001))
            {
                player.InfoBoxLoc($"Stop the train, turn off autopilot and leave the controls before shoving.");
                return;
            }
            if (this.nativeWasPulling)
            {
                this.nativeWasPulling = false;
                this.ReleaseRailMotion();
                this.nativeVelocityAvailable = false;
            }
            if (Math.Abs(this.state.Speed) > .35 || !this.TryBindRail()) return;
            var relativeFacing=this.Parent.GetComponent<RailCouplingComponent>().FacingRelativeTo(touchedCar);
            if(relativeFacing==0) return;
            var direction=RailGuidance.SelectShoveDirection(player.User.Position,touchedCar.Parent.Position,
                touchedCar.Parent.Rotation.RotateVector(Vector3.UnitZ))*relativeFacing*this.facing;
            // Move the consist through its one rail solver, including when the
            // touched passenger/cargo car is a follower or faces the other way.
            // Coasting mode prevents old commanded brakes cancelling the nudge.
            var mass = Math.Max(1, this.Parent.GetComponent<RailCouplingComponent>().Performance.Mass);
            this.lastShoveAt = now;
            this.WakeMotion();
            this.serverDriving = false;
            this.serverThrottle = 0;
            this.commandsInitialized = true;
            this.Handbrake = false;
            this.CommandBrake = false;
            this.state = this.state with { Speed = direction * Math.Clamp(900 / mass, .2, 1.2) };
            this.RailVehicle.SetRailGuidance(true);
            this.Parent.SetDirty();
        }
    }

    private double nativePacketSeconds = .05;
    internal void OnNativePhysicsPose(Vector3 velocity, double packetSeconds = .05)
    {
        if(ManualCart!=null)return;
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/OnNativePhysicsPose", this.Parent);
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/OnCreate", this.Parent);
        base.OnCreate();
        // Placement is the only route allowed a wider snap envelope. Do not
        // apply this to loaded/moved carts, which may intentionally be off-rail.
        this.placementSnapPending = true;
    }

    internal bool SnapPlacedCart()
    {
        if(ManualCart is {} manual){lock(gate)return manual.Bind(true);}
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/SnapPlacedCart", this.Parent);
        lock (this.gate)
        {
            var nearest = TrackWorld.Capture(this.Parent.Position,
                this.Parent.Rotation.RotateVector(Vector3.UnitZ), this.RailVehicle.RailSpec.Coaster?.5f:.45f,
                this.RailVehicle.RailSpec.Coaster?1.5f:1.1f, this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster);
            if (nearest == null) return false;
            this.AlignToRail(nearest.Value.Rail, nearest.Value.T);
            return true;
        }
    }

    internal void GuideNativePullPose()
    {
        if(ManualCart!=null)return;
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/GuideNativePullPose", this.Parent);
        lock (this.gate)
        {
            if (this.motionStopped || this.Parent.IsDestroyed || this.pickupPending
                || this.Parent.GetComponent<MountComponent>().Driver == null) return;
            this.GuidePulledRail();
        }
    }

    internal bool GuidePulledRail()
    {
        if(ManualCart is {} manual)return manual.Bind(false);
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/GuidePulledRail", this.Parent);
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
        if(ManualCart is {} manual){lock(gate){manual.ReleaseHandle();return manual.Bind(false);}}
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/ReleaseRailMotion", this.Parent);
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
                // Complete the owner handoff at the exact rail pose. A normal
                // keyframe can otherwise leave the former driver at its last
                // dynamic body pose, especially when the parked solver sleeps.
                this.AlignToRail(rail, this.t);
                this.RailVehicle.SetRailGuidance(true);
                this.RailVehicle.PublishRailPose(tangent * (float)this.state.Speed);
                this.Parent.SyncPositionAndRotation();
                this.Parent.GetComponent<RailCouplingComponent>()?.FollowTrain(rail, this.t, this.facing,
                    tangent * (float)this.state.Speed);
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
        if(ManualCart is {} manual){lock(gate){Handbrake=true;manual.Stop();}return;}
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/ResetCoupledMotion", this.Parent);
        lock (this.gate)
        {
            this.WakeMotion();
            this.Handbrake = true; this.state = default; this.nativeVelocity = Vector3.Zero;
            this.nativeWasPulling = false; this.nativeVelocityAvailable = false;
            this.groundVelocity = Vector3.Zero; this.inFlight = false;
            this.landingRotationElapsed = .45;
            ((RailVehicleObject)this.Parent).SetRailGuidance(true);
            ((RailVehicleObject)this.Parent).PublishRailPose(Vector3.Zero);
        }
    }

    // Coupling can change after two independent timers already entered their
    // motion gates. Never wait for a second vehicle gate while holding the
    // leader's: publish the latest pose for its next own callback instead.
    private sealed record CoupledPoseRequest(long Sequence,long Topology,VoxelRail? Rail,float Parameter,int Orientation,Vector3 Position,Vector3 Forward,Vector3 Velocity,int Leader=0,Vector3 Pivot=default,long AirborneEpoch=0,bool GroundedLeader=false,bool Trajectory=false,Vector3 SupportForward=default);
    private CoupledPoseRequest? pendingCoupledPose;
    private long coupledPoseSequence,lastAppliedCoupledPose;
    // The mailbox coalesces poses, but must retain the airborne transition even
    // if a busy follower consumes only the newer guided touchdown pose.
    private long coupledAirborneEpoch,coupledLandedEpoch;
    private Vector3 freeFollowerTrajectory;
    internal bool AwaitingCoupledLanding => this.RailVehicle.RailSpec.Coaster && (this.freeFollowerPose || this.inFlight
        || Volatile.Read(ref coupledAirborneEpoch)>Volatile.Read(ref coupledLandedEpoch));
    internal bool CanLandCoupledAt(VoxelRail rail, float parameter, Vector3 position, int connectedEnd)
    {
        var point = this.RailPosition(rail, parameter, this.NextRail);
        var up = rail.Profile.Up(parameter);
        var forward=this.RailTangent(rail,parameter,this.NextRail);
        if(Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ),forward)<0)forward=-forward;
        point=this.RailSupportPosition(point,rail,parameter,forward,this.Parent.Rotation,this.RailVehicle.ConnectionPoints.Coupler(connectedEnd));
        var offset = position - point;
        // The path cursor identifies where this car belongs along the train,
        // but only its own descending body can establish rail contact there.
        var height = Vector3.Dot(offset, up);
        var lateral = offset - up * height;
        // A child ahead of a grounded rear solver can already penetrate the
        // plane through its tilted coupler. Recover that bounded penetration
        // immediately; a spherical gate used to reject it until the rear car
        // finished turning. Cars above the plane still wait for own contact.
        return height <= .08f && height >= -this.RailVehicle.RailSpec.Length
            && lateral.LengthSquared() <= .65f * .65f
            && (Vector3.Dot(this.freeFollowerTrajectory,up)<=0 || Vector3.Dot(position - this.Parent.Position, up) <= .01f);
    }
    internal void AcceptCoupledPose(VoxelRail rail, float parameter, int orientation, Vector3 velocity)
        =>ReceiveCoupledPose(new(Interlocked.Increment(ref coupledPoseSequence),RailCouplingComponent.TopologyRevision,rail,parameter,orientation,default,default,velocity));
    internal void AcceptCoupledFreePose(Vector3 position, Vector3 forward, Vector3 velocity)
        =>ReceiveCoupledPose(new(Interlocked.Increment(ref coupledPoseSequence),RailCouplingComponent.TopologyRevision,null,0,0,position,forward,velocity,AirborneEpoch:Interlocked.Increment(ref coupledAirborneEpoch)));
    internal void AcceptFollowerPose(int leader,VoxelRail rail,float parameter,int orientation,Vector3 velocity)
        =>ReceiveCoupledPose(new(Interlocked.Increment(ref coupledPoseSequence),RailCouplingComponent.TopologyRevision,rail,parameter,orientation,default,default,velocity,leader,AirborneEpoch:Volatile.Read(ref coupledAirborneEpoch)));
    internal void AcceptContactFollowerPose(int leader,VoxelRail rail,float parameter,int orientation,Vector3 velocity,int connectedEnd)
        =>ReceiveCoupledPose(new(Interlocked.Increment(ref coupledPoseSequence),RailCouplingComponent.TopologyRevision,rail,parameter,orientation,default,default,velocity,leader,this.RailVehicle.ConnectionPoints.Coupler(connectedEnd),Volatile.Read(ref coupledAirborneEpoch)));
    internal void AcceptFreeFollowerPose(int leader,Vector3 position,Vector3 forward,Vector3 velocity,int connectedEnd)
        =>ReceiveCoupledPose(new(Interlocked.Increment(ref coupledPoseSequence),RailCouplingComponent.TopologyRevision,null,0,0,position,forward,velocity,leader,this.RailVehicle.ConnectionPoints.Coupler(connectedEnd),Interlocked.Increment(ref coupledAirborneEpoch)));
    internal void AcceptLandingFollowerPose(int leader,Vector3 position,Vector3 forward,Vector3 velocity,int connectedEnd)
        =>ReceiveCoupledPose(new(Interlocked.Increment(ref coupledPoseSequence),RailCouplingComponent.TopologyRevision,null,0,0,position,forward,velocity,leader,this.RailVehicle.ConnectionPoints.Coupler(connectedEnd),Interlocked.Increment(ref coupledAirborneEpoch),true));
    internal Vector3 TrajectoryContactPoint(RailMotionTrail.Pose pose,int bodyTravelSign)
    {
        if(pose.Rail is not {} rail)return pose.Point;
        var tangent=this.RailTangent(rail,pose.Parameter,this.NextRail);
        var travelSign=Vector3.Dot(pose.Velocity,tangent)<0?-1:1;
        var point=this.RailPosition(rail,pose.Parameter,this.NextRail);
        if(this.current!=null && !this.LandingRotationActive && !this.AwaitingCoupledLanding)return point;
        return RailSupportPosition(point,rail,pose.Parameter,
            tangent*travelSign*bodyTravelSign,this.Parent.Rotation,Vector3.Zero);
    }
    internal void AcceptTrajectoryFollowerPose(int leader,RailMotionTrail.Pose pose,int bodyTravelSign,int connectedEnd,int travelDirection=0,bool constrained=false,Vector3 supportForward=default)
    {
        if(pose.Rail is {} rail)
        {
            var sign=travelDirection!=0?travelDirection:Vector3.Dot(pose.Velocity,rail.Profile.Tangent(pose.Parameter))<0?-1:1;
            ReceiveCoupledPose(new(Interlocked.Increment(ref coupledPoseSequence),RailCouplingComponent.TopologyRevision,
                rail,pose.Parameter,sign*bodyTravelSign,default,default,pose.Velocity,leader,AirborneEpoch:Volatile.Read(ref coupledAirborneEpoch),Trajectory:true));
        }
        else
        {
            var forward=pose.Velocity.LengthSquared()>.0025f?Vector3.Normalize(pose.Velocity)*bodyTravelSign:Parent.Rotation.RotateVector(Vector3.UnitZ);
            ReceiveCoupledPose(new(Interlocked.Increment(ref coupledPoseSequence),RailCouplingComponent.TopologyRevision,
                null,0,0,pose.Point,forward,pose.Velocity,leader,constrained?this.RailVehicle.ConnectionPoints.Coupler(connectedEnd):Vector3.Zero,AirborneEpoch:Interlocked.Increment(ref coupledAirborneEpoch),Trajectory:true,SupportForward:supportForward));
        }
    }
    private void ReceiveCoupledPose(CoupledPoseRequest request)
    {
        if(!Monitor.TryEnter(gate)){
            CoupledPoseRequest? previous;
            do{previous=Volatile.Read(ref pendingCoupledPose);if(previous?.Sequence>=request.Sequence)return;}
            while(Interlocked.CompareExchange(ref pendingCoupledPose,request,previous)!=previous);
            return;
        }
        try{ApplyCoupledPose(request);}finally{Monitor.Exit(gate);}
    }
    private void ApplyCoupledPose(CoupledPoseRequest request)
    {
        if(motionStopped||pickupPending||Parent.IsDestroyed||request.Sequence<=lastAppliedCoupledPose)return;
        lastAppliedCoupledPose=request.Sequence;
        if(request.Topology!=RailCouplingComponent.TopologyRevision)return;
        if(request.Leader!=0&&Parent.GetComponent<RailCouplingComponent>().Leader().Parent.ID!=request.Leader)return;
        if(request.Rail is {} rail)ApplyRailFollowerPose(rail,request.Parameter,request.Orientation,request.Velocity,request.AirborneEpoch,request.Pivot,request.Trajectory);
        else ApplyFreeFollowerPose(request.Position,request.Forward,request.Velocity,request.Pivot,request.GroundedLeader,request.Trajectory,request.SupportForward);
    }
    private void ApplyRailFollowerPose(VoxelRail rail, float parameter, int orientation, Vector3 velocity, long airborneEpoch,Vector3 pivot,bool trajectory=false,Vector3 supportForward=default)
    {
        if(ManualCart is {} manual){lock(gate)manual.Accept(rail,parameter,orientation,velocity);return;}
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/AcceptCoupledPose", this.Parent);
        lock (this.gate)
        {
            if (this.Derailed || !this.CheckFootprint(rail, parameter, velocity.Length())) return;
            var unboundContact=trajectory && this.current==null && this.RailVehicle.RailSpec.Coaster
                && Math.Abs(Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ),this.RailTangent(rail,parameter,this.NextRail)))<.998f;
            var landing = unboundContact || this.freeFollowerPose || this.inFlight || airborneEpoch>this.coupledLandedEpoch;
            Interlocked.Exchange(ref this.coupledLandedEpoch,Math.Max(this.coupledLandedEpoch,airborneEpoch));
            var now = DateTime.UtcNow;
            var seconds = Math.Clamp((now - this.lastFreeFollowerAt).TotalSeconds, 0, .15);
            this.current = rail; this.t = parameter; this.facing = orientation;
            this.freeFollowerPose = false;
            this.placementSnapPending = false;
            this.captureOffset = Vector3.Zero; this.nativeWasPulling = false; this.inFlight = false;
            this.state = new(0, Vector3.Dot(velocity, rail.Profile.Tangent(parameter)), 0);
            var point = this.RailPosition(rail, parameter, (r, e) => this.NextRail(r, e));
            var tangent = this.RailTangent(rail, parameter, (r, e) => this.NextRail(r, e));
            if (landing) { this.BeginLandingRotation(rail, parameter, tangent * orientation); this.landingPivot=pivot; }
            else this.AdvanceLandingRotation(seconds);
            this.lastFreeFollowerAt = now;
            ((RailVehicleObject)this.Parent).SetRailGuidance(true);
            var rotation=this.GuidedRotation(rail,parameter,tangent * orientation);
            this.Parent.Position = this.LandingPosition(point,rail,parameter,tangent*orientation,rotation);
            this.Parent.Rotation = rotation;
            ((RailVehicleObject)this.Parent).PublishRailPose(velocity);
            this.Parent.GetComponent<RailCouplingComponent>().RecordMotion(rail,parameter,point,velocity);
            if(!this.RailVehicle.RailSpec.Coaster)
            {
                var speed=rail.Profile.Chain?MinecartChainDriveObject.PoweredSpeedFor(rail.Cell):0;
                this.chainSpeedMultiplier=speed>0?speed:1;this.chainLiftEngaged=speed>0;
            }
            this.Sound(velocity.Length());
            MinecartDumpRailComponent.TryUnload(rail.Cell,this.Parent);
        }
    }

    private void ApplyFreeFollowerPose(Vector3 position, Vector3 forward, Vector3 velocity, Vector3 pivot, bool groundedLeader, bool trajectory=false,Vector3 supportForward=default)
    {
        if(ManualCart is {} manual){lock(gate){manual.Stop();}return;}
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/AcceptCoupledFreePose", this.Parent);
        lock (this.gate)
        {
            this.landingRotationElapsed = .45;
            this.current = null; this.pulledRail = null; this.nativeWasPulling = false;
            var now = DateTime.UtcNow;
            if (!this.freeFollowerPose)
                this.flightFacingSign = Vector3.Dot(forward, velocity) < 0 ? -1 : 1;
            var seconds = this.freeFollowerPose ? Math.Clamp((now - this.lastFreeFollowerAt).TotalSeconds, 0, .15) : 0;
            this.freeFollowerPose = true;
            this.lastFreeFollowerAt = now;
            // Once supported by a grounded parent's coupler, retain this car's
            // incoming pitch. Coupler motion is not a new flight trajectory;
            // rail alignment starts only at this car's own supported contact.
            if(trajectory || !groundedLeader || seconds<=0)this.freeFollowerTrajectory=velocity;
            var localTravel=this.freeFollowerTrajectory;
            if ((trajectory || !groundedLeader) && seconds > 0 && (localTravel.LengthSquared() > .0025f || supportForward.LengthSquared()>.0025f))
            {
                // Each off-rail follower turns toward its OWN travel at its own
                // position. Copying the leader's rotation makes a long train a
                // rigid plank over hills, curves and launch transitions.
                var connector = this.Parent.Rotation.RotateVector(pivot);
                var supported=supportForward.LengthSquared()>.0025f;
                var turned = AirMotion.FollowTrajectory(this.Rotation, supported?supportForward:localTravel, supported?1:this.flightFacingSign, seconds);
                this.Parent.Rotation = new Eco.Shared.Math.Quaternion(turned.X, turned.Y, turned.Z, turned.W);
                // Rotate around the coupled end. Leaving the centre computed
                // from the old angle creates a false vertical movement on the
                // next tick, which can keep the car rocking above the rail.
                position += connector - this.Parent.Rotation.RotateVector(pivot);
            }
            this.Parent.Position = position;
            ((RailVehicleObject)this.Parent).SetRailGuidance(true);
            ((RailVehicleObject)this.Parent).PublishRailPose(velocity);
            this.Sound(0);
        }
    }

    internal bool CanRestoreCoupledPose(VoxelRail rail, float parameter)
    {
        if(ManualCart is {} manual)return manual.CanRestore(rail,parameter);
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/CanRestoreCoupledPose", this.Parent);
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Collision/LimitVehicleTravel", this.Parent);
        var coupling = this.Parent.GetComponent<RailCouplingComponent>();
        if (coupling == null || Math.Abs(travel) < .000001) return travel;
        return coupling.LimitTrainTravel(rail, parameter, travel);
    }

    private void AlignToRail(VoxelRail rail, float railT, bool pulling = false)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Orientation/AlignToRail", this.Parent);
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
            if (distance < .035f && alignment > .99999f) return;
            if (distance < .75f && alignment > .55f)
            {
                var pose=RailGuidance.SmoothNativePose(this.Parent.Position,forward,point,tangent,this.nativePacketSeconds);
                this.Parent.Position = pose.Position;
                var blendedForward = pose.Forward;
                if (blendedForward.LengthSquared() > .0001f)
                    this.Parent.Rotation = Eco.Shared.Math.Quaternion.LookRotation(Vector3.Normalize(blendedForward));
                // The native owner does not receive ordinary MarkPoseUpdated
                // snapshots. Send a bounded correction at most twenty times per
                // second whenever the driver's local cart drifts sideways,
                // including straight rail. A server-only blend otherwise leaves
                // the mounted avatar walking beside the track until a bend.
                if ((distance > .04f || alignment < .99999f) && DateTime.UtcNow >= this.nextPulledCornerCorrection)
                {
                    this.Parent.SyncPositionAndRotation();
                    this.nextPulledCornerCorrection = DateTime.UtcNow.AddMilliseconds(50);
                }
                // Remote viewers and coupled followers must see the same accepted
                // correction, not the raw transform cached by ReceiveUpdate.
                this.RailVehicle.PublishRailPose(tangent*Vector3.Dot(this.nativeVelocity,tangent));
                return;
            }
        }
        var tolerance = pulling ? .03f : .008f;
        if (distance < tolerance && alignment > (pulling ? .999f : .99995f) && frameAligned) return;
        var rotation=this.GuidedRotation(rail,railT,tangent);
        this.Parent.Position = this.LandingPosition(point,rail,railT,tangent,rotation);
        this.Parent.Rotation = rotation;
        this.Parent.SyncPositionAndRotation();
        ((RailVehicleObject)this.Parent).MarkPoseUpdated();
    }

    internal (VoxelRail Rail,float T,int Facing)? BoundConsistPose => ManualCart is {} manual?manual.Bound:this.current is {} rail
        ? (rail,this.t,this.facing) : this.pulledRail is {} pulled
        ? (pulled,this.pulledT,Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ),pulled.Profile.Tangent(this.pulledT))<0?-1:1) : null;

    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/PostInitialize", this.Parent);
        base.PostInitialize();
        ManualCart?.Initialize();
        if (this.RailVehicle.RailSpec.Coaster && !WorldObjectManager.Init.Initialized) this.resumeMomentumPending = true;
        // A locomotive placed on bare ground has no rail to capture. Keep its
        // parked pose authoritative from the first client frame; otherwise a
        // WheelCollider/terrain contact can launch the local Rigidbody while
        // the server continues to hold the original placement position.
        if (this.RailVehicle.RailSpec.Powered && this.Parent.GetComponent<MountComponent>().Driver == null)
            this.RailVehicle.SetRailGuidance(true);
        // Eco's WorldObjectManager waits 1–5 seconds between ordinary ticks.
        // Only this component needs 20 Hz motion; do not retime the whole world.
        var mounts = this.Parent.GetComponent<MountComponent>();
        mounts.PlayerMountedEvent += this.WakeMotion;
        mounts.PlayerDismountedEvent += this.DriverDismounted;
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/Destroy", this.Parent);
        lock (this.gate)
        {
            this.motionStopped = true;
            ManualCart?.ReleaseHandle();
            var mounts = this.Parent.GetComponent<MountComponent>();
            mounts.PlayerMountedEvent -= this.WakeMotion;
            mounts.PlayerDismountedEvent -= this.DriverDismounted;
            this.motionTimer?.Dispose();
            this.motionTimer = null;
        }
        base.Destroy();
    }

    [Notify, EnvVar]
    public bool IsHandcart(User user) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/IsHandcart", this.Parent); return ((RailVehicleObject)this.Parent).RailSpec.Pullable; }

    [Notify, EnvVar]
    public bool IsShoveableTrain(User user) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/IsShoveableTrain", this.Parent); return !this.RailVehicle.RailSpec.Pullable && !this.RailVehicle.RailSpec.Coaster; }

    internal void PrepareNativeDriver()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/PrepareNativeDriver", this.Parent);
        lock (this.gate)
        {
            this.WakeMotion();
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/PrepareServerDriver", this.Parent);
        lock (this.gate)
        {
            this.WakeMotion();
            this.EnsureDriveCommands();
            this.serverDriving = true;
            this.current ??= this.TryBindRail() ? this.current : null;
            this.RailVehicle.SetRailGuidance(true);
        }
    }

    internal void EnsureDriveCommands()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/EnsureDriveCommands", this.Parent);
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/SetDriveCommands", this.Parent);
        lock(this.gate)
        {
            this.WakeMotion();
            this.EnsureDriveCommands();
            this.serverDriving=true;
            this.serverThrottle=double.IsFinite(throttle)?Math.Clamp(throttle,0,1):0;
            this.serverDirection=direction<0?-1:1;
            this.CommandBrake=brake;
            this.Handbrake=brake;
            ((RailVehicleObject)Parent).PublishCabControls(this.serverThrottle,brake,this.serverDirection);
            Parent.SetDirty();
        }
    }

    internal void StopServerDriver()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/StopServerDriver", this.Parent);
        // Explicit emergency/reset path only; never called for an operator exit.
        lock (this.gate) { this.serverDriving = false; this.serverThrottle = 0; this.CommandBrake=true; this.Handbrake = true; Parent.SetDirty(); }
    }

    internal void SetServerThrottle(double value, int direction)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/SetServerThrottle", this.Parent);
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/Grab", this.Parent);
        if (!((RailVehicleObject)this.Parent).RailSpec.Pullable || target.ContainsParameter("MinecartStorage") || !this.Parent.IsAuthorized(player.User, AccessType.FullAccess) || Vector3.Distance(player.User.Position, this.Parent.Position) > 3) return;
        lock (this.gate)
        {
            if(ManualCart is {} manual)
            {
                var local=Vector3.Transform(player.User.Position-Parent.Position,Quaternion.Inverse(Rotation));
                if(!motionStopped&&!pickupPending)manual.ToggleHandle(player,ResolveGripSide(target,local));
                return;
            }
            var mounts = this.Parent.GetComponent<MountComponent>();
            if (mounts.Driver == player) { mounts.TryDismountPlayer(player); return; }
            if (player.MountManager.IsMounted) { return; }
            if (mounts.Driver != null) { return; }
            if (this.Parent.GetComponent<RailCouplingComponent>()?.CanBoard == false) return;
            if (this.pickupPending) return;
            var localPlayer = Vector3.Transform(player.User.Position - this.Parent.Position, Quaternion.Inverse(this.Rotation));
            var side = this.ResolveGripSide(target, localPlayer);
            var coupling = this.Parent.GetComponent<RailCouplingComponent>();
            if (coupling?.EndAvailable(side) == false)
            {
                player.InfoBoxLoc($"Grab the free end of the cart, or decouple this end first.");
                return;
            }
            // The symmetric cart has one native driver mount. Turn it toward the
            // selected end before mounting so either modeled grip can be taken.
            if (side < 0)
            {
                coupling?.ReverseEnds();
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/ResolveGripSide", this.Parent);
        if (target.TryGetParameter("MinecartHandle", out var value) && int.TryParse(value?.ToString(), out var requested) && requested != 0)
            return Math.Sign(requested);
        return localPlayer.Z < 0 ? -1 : 1;
    }

    // SpecificInteractable child colliders can bypass the client's generic
    // world-object pickup affordance. Route explicitly to Eco's real hammer RPC,
    // retaining cargo/persistent-data, permissions, laws and inventory checks.
    [Notify, EnvVar]
    public bool HoldingHammer(User user) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/HoldingHammer", this.Parent); return user.Inventory.Toolbar.SelectedItem is HammerItem; }
    [Notify, EnvVar]
    public bool VehicleCoupled(User user) => this.Parent.GetComponent<RailCouplingComponent>()?.Linked == true;

    // Generic pickup RPCs may not bypass the explicit hammer-hit interaction.
    public Eco.Core.Utils.Result CanPickup()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/CanPickup", this.Parent);
        lock(this.gate) return this.pickupPending ? Eco.Core.Utils.Result.Succeeded : Eco.Core.Utils.Result.Fail(LocString.Empty);
    }

    [Interaction(InteractionTrigger.LeftClick, "Decouple To Dismantle", requiredEnvVars: new[] { "HoldingHammer", "VehicleCoupled" }, interactionDistance: 5, priority: 60, authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ExplainCoupledDismantle(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (this.VehicleCoupled(player.User) && this.HoldingHammer(player.User))
            player.InfoBoxLocStr("Decouple To Dismantle");
    }

    [Interaction(InteractionTrigger.LeftClick, "Dismantle", requiredEnvVars: new[] { "HoldingHammer" }, interactionDistance: 5, priority: 40, authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction, AnimationDriven = true, DisallowedEnvVars = new[] { "VehicleCoupled" })]
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
            ManualCart?.Stop();
            this.pickupPending = true;
            this.groundVelocity = Vector3.Zero;
            this.state = this.state with { Speed = 0 };
        }
        try { await hammer.PickupWorldObject(player, this.Parent); }
        finally { lock (this.gate) { this.pickupPending = false; this.WakeMotion(); } }
    }

    private bool CanQuickTransfer(Player player, InteractionTarget target) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/CanQuickTransfer", this.Parent); return player != null && !this.Parent.IsDestroyed
        && target.ContainsParameter("MinecartStorage")
        && this.Parent.IsAuthorized(player.User, AccessType.ConsumerAccess)
        && Vector3.Distance(player.User.Position, this.Parent.Position) <= 3
        && this.Parent.GetComponent<MinecartRidingComponent>()?.StorageLocked != true; }

    // SpecificInteractable bucket targets need explicit forwarding to Eco's
    // stockpile actions. The native methods retain inventory laws/restrictions.
    [Interaction(InteractionTrigger.RightClick, "Put %SelectedNonTool%", requiredEnvVars: new[] { "MinecartStorage", "SelectedNonTool", "CanPut" },
        interactionDistance: 3, priority: 35, authRequired: AccessType.ConsumerAccess, flags: InteractionFlags.BlocksOtherInteraction, MinCaloriesRequired = 0)]
    public void PutCargo(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/PutCargo", this.Parent);
        if (!CanQuickTransfer(player, target)) return;
        var storage = this.Parent.GetComponent<PublicStorageComponent>();
        if (storage.CanPut(player.User)) storage.PutItem(player, trigger, target);
    }

    [Interaction(InteractionTrigger.LeftClick, "Take %CanTake%", requiredEnvVars: new[] { "MinecartStorage", "CanTake" },
        interactionDistance: 3, priority: 35, authRequired: AccessType.ConsumerAccess, flags: InteractionFlags.BlocksOtherInteraction,
        MinCaloriesRequired = 0, DisallowedEnvVars = new[] { "HoldingHammer" })]
    public void TakeCargo(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/TakeCargo", this.Parent);
        if (!CanQuickTransfer(player, target) || HoldingHammer(player.User)) return;
        var storage = this.Parent.GetComponent<PublicStorageComponent>();
        if (storage.CanTake(player.User) != null) storage.TakeItem(player, trigger, target);
    }

    [Interaction(InteractionTrigger.InteractKey, "Open minecart inventory", requiredEnvVars: new[] { "MinecartStorage" }, interactionDistance: 2.5f, priority: 20, authRequired: AccessType.ConsumerAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void OpenInventory(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/OpenInventory", this.Parent);
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/Brake", this.Parent);
        if (!this.Parent.IsAuthorized(player.User, AccessType.FullAccess) || Vector3.Distance(player.User.Position, this.Parent.Position) > 3) return;
        lock (this.gate)
        {
            if(ManualCart!=null){Handbrake=!Handbrake;return;}
            this.coasterQueueFeed=false;
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/TryBindRail", this.Parent);
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
        if (this.current is { } old && (!TrackWorld.Contains(old) || this.RailVehicle.RailSpec.Tram && !old.Profile.Tram)) { this.current = null; this.state = default; }
        if (this.current is { } bound && Vector3.Distance(this.Parent.Position,
            this.LandingPosition(this.RailPosition(bound,this.t,this.NextRail),bound,this.t,
                this.RailTangent(bound,this.t,this.NextRail)*this.facing,this.Parent.Rotation)) > .65f)
        { this.current = null; this.state = default; } // Lifted/moved externally: abandon the old rail lock.
        if (this.current != null) return true;
        var nearest = this.inFlight && this.RailVehicle.RailSpec.Coaster
            ? TrackWorld.CaptureContact(this.Parent.Position,this.Rotation,this.flightVelocity,this.RailVehicle.RailSpec.Wheelbase/2,this.RailVehicle.ContactHalfSize.X)
            : TrackWorld.Capture(this.Parent.Position, this.Parent.Rotation.RotateVector(Vector3.UnitZ), industrial: this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster);
        if(this.RailVehicle.RailSpec.Tram && nearest is {} found && !found.Rail.Profile.Tram)nearest=null;
        if (nearest == null && this.RailVehicle.RailSpec.Wheelbase > 1)
        {
            var best = float.MaxValue;
            foreach (var candidate in TrackWorld.Near(this.Parent.Position, this.RailVehicle.RailSpec.Coaster))
            {
                if(this.RailVehicle.RailSpec.Tram && !candidate.Profile.Tram)continue;
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
        if (nearest == null || this.RailVehicle.RailSpec.Tram && !nearest.Value.Rail.Profile.Tram) return false;
        var landing = this.inFlight;
        this.current = nearest.Value.Rail;
        this.inFlight = false;
        this.parkedOffRail = false;
        this.nativeGroundPhysics = false;
        this.t = nearest.Value.T;
        this.facing = Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ), this.current.Value.Profile.Tangent(this.t)) < 0 ? -1 : 1;
        ((RailVehicleObject)this.Parent).SetRailGuidance(true);
        this.groundVelocity = Vector3.Zero;
        var targetPose = this.RailPosition(this.current.Value, this.t, (r, end) => this.NextRail(r, end));
        if (landing) this.BeginLandingRotation(this.current.Value, this.t,
            this.RailTangent(this.current.Value, this.t, this.NextRail) * this.facing);
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
        if(ManualCart is {} manual){lock(gate){manual.Stop();return manual.Bind(true);}}
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/RecoverOnRail", this.Parent);
        lock (this.gate)
        {
            this.WakeMotion();
            if (this.Parent.GetComponent<MountComponent>().IsMounted || this.Parent.GetComponent<RailCouplingComponent>().Linked) return false;
            var nearest = TrackWorld.Capture(this.Parent.Position,this.Parent.Rotation.RotateVector(Vector3.UnitZ),.65f,.75f,this.RailVehicle.RailSpec.Industrial, coaster: this.RailVehicle.RailSpec.Coaster);
            if (nearest == null || nearest.Value.Rail.Profile.Radius+.001 < this.RailVehicle.RailSpec.MinimumRadius
                || this.Parent.GetComponent<RailCouplingComponent>().Load.Mass > nearest.Value.Rail.Profile.MaximumSupportedKg) return false;
            this.Derailed = false; this.current = null; this.departedRail = null; this.inFlight = false;
            this.landingRotationElapsed = .45;
            this.state = default; this.Handbrake = true;
            return this.TryBindRail();
        }
    }

    private void DriverDismounted()
    {
        lock(this.gate)
        {
            // The next physics tick may retain coasting momentum, but no
            // operator means no pump stroke. Do not leave a native speed latched.
            if(this.RailVehicle.RailSpec.HumanPowered&&this.Parent.GetComponent<MountComponent>().Driver==null)
            {
                this.RailVehicle.PublishWheelMotion(Vector3.Zero);
                this.Sound(0);
            }
            this.WakeMotion();
        }
    }

    private void WakeMotion()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/WakeMotion", this.Parent);
        lock (this.gate)
        {
            if (!this.sleepClock.Wake()) return;
            // Never integrate the idle interval as accumulated travel.
            this.lastTick = DateTime.UtcNow;
            this.manualCart?.ResetClock();
            if (!this.motionStopped) this.motionTimer?.Change(0, RailMotionSleepClock.ActiveIntervalMs);
        }
    }

    private bool CanSleepMotion()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/CanSleepMotion", this.Parent);
        if (this.motionStopped || this.pickupPending || this.placementSnapPending || this.Derailed
            || this.nextMotionRetry != default || this.inFlight || this.LandingRotationActive || this.nativeWasPulling
            || this.nativeGroundPhysics || this.state.Speed != 0
            || this.captureOffset.LengthSquared() > .000001f
            || this.Parent.GetComponent<MountComponent>().Driver != null
            || this.Parent.GetComponent<MineTrainDrivingComponent>()?.HasStandingOperator == true
            || this.Parent.GetComponent<TrainControllerComponent>()?.Autopilot == true
            || (this.serverDriving && !this.CommandBrake)
            || this.Parent.GetComponent<RailCouplingComponent>()?.AwaitingLoad == true) return false;
        if (this.current is not { } rail)
            return this.parkedOffRail && this.RailVehicle.ServerOnlyPhysics;
        // Station dwell/dispatch, lift polling and unbraked slope motion need the
        // normal solver. A brake must actually settle the car before sleeping.
        return RailMotionSleepClock.RailCanSleep(this.Handbrake, rail.Profile.Chain,
            CoasterStationComponent.At(rail.Cell) != null, rail.Profile.Tangent(this.t).Y);
    }

    public override void Tick()
    {
        // System.Threading.Timer and ordinary Eco ticks may arrive together.
        // Skip redundant callbacks instead of queuing workers behind the gate.
        if (!Monitor.TryEnter(this.gate))
        {
            using var skipped = RailProfile.Measure("Vehicle Simulation/Idle maintenance/Busy callback skipped", this.Parent);
            return;
        }
        try
        {
            using var _railProfileScope = RailProfile.Measure("Vehicle Simulation/Movement/Tick", this.Parent);
            using var frame = RailSimulationFrame.Begin();
            if (this.motionStopped || this.Parent.IsDestroyed || this.pickupPending) return;
            if(Interlocked.Exchange(ref pendingCoupledPose,null) is {} followPose)ApplyCoupledPose(followPose);
            if(ManualCart is {} manual)
            {
                var placement=placementSnapPending;placementSnapPending=false;
                var timestamp=DateTime.UtcNow;
                if(this.sleepClock.IsSleeping&&manual.CanSleep&&!placement)
                {
                    manual.Publish();manual.ResetClock();
                    if(!this.sleepClock.NeedsProbe(timestamp))return;
                }
                manual.Tick(placement);
                var previous=this.sleepClock.IsSleeping;
                var sleepingManual=this.sleepClock.Observe(manual.CanSleep,timestamp);
                if(previous!=sleepingManual)this.motionTimer?.Change(sleepingManual?RailMotionSleepClock.SleepingIntervalMs:RailMotionSleepClock.ActiveIntervalMs,
                    sleepingManual?RailMotionSleepClock.SleepingIntervalMs:RailMotionSleepClock.ActiveIntervalMs);
                return; // No legacy native pulling, train commands, stations or flight solver.
            }
            var now = DateTime.UtcNow;
            var wasSleeping = this.sleepClock.IsSleeping;
            if (wasSleeping)
            {
                if (this.Parent.Position != this.sleepingPosition || this.Parent.Rotation != this.sleepingRotation
                    || this.QueueCanAdvance()
                    || this.Parent.GetComponent<MountComponent>().Driver != null
                    || this.Parent.GetComponent<MineTrainDrivingComponent>()?.HasStandingOperator == true
                    || this.Parent.GetComponent<TrainControllerComponent>()?.Autopilot == true)
                {
                    this.WakeMotion();
                    wasSleeping = false;
                }
                else
                {
                    // Keep rail-only bodies authoritative, including the
                    // quarter-second parked keyframes that prevent launches.
                    this.RailVehicle.SetRailGuidance(true);
                    if (this.parkedOffRail) this.ParkOffRail();
                    else if (this.RailVehicle.ServerOnlyPhysics || this.serverDriving)
                        this.RailVehicle.PublishRailPose(Vector3.Zero);
                    this.lastTick = now.AddMilliseconds(-RailMotionSleepClock.ActiveIntervalMs);
                    if (!this.sleepClock.NeedsProbe(now)) return;
                }
            }
            this.TickMotion();
            var sleeping = this.sleepClock.Observe(this.CanSleepMotion(), now);
            if (sleeping)
            {
                this.sleepingPosition = this.Parent.Position;
                this.sleepingRotation = this.Parent.Rotation;
            }
            if (sleeping != wasSleeping)
                this.motionTimer?.Change(sleeping ? RailMotionSleepClock.SleepingIntervalMs : RailMotionSleepClock.ActiveIntervalMs,
                    sleeping ? RailMotionSleepClock.SleepingIntervalMs : RailMotionSleepClock.ActiveIntervalMs);
        }
        finally { Monitor.Exit(this.gate); }
    }

    private void TickMotion()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/TickMotion", this.Parent);
        lock (this.gate)
        {
            if (this.motionStopped || this.Parent.IsDestroyed || this.pickupPending) return;
            if(this.RailVehicle.RailSpec.Coaster && !WorldObjectManager.Init.Initialized) return;
            if(DateTime.UtcNow<this.nextMotionRetry) return;
            if(this.nextMotionRetry!=default)
            {
                this.nextMotionRetry=default;
            }
            var coupling = this.Parent.GetComponent<RailCouplingComponent>();
            var isFollower = coupling?.IsFollower == true;
            if(!isFollower && this.freeFollowerPose && this.RailVehicle.RailSpec.Coaster)
            { this.flightVelocity=this.freeFollowerTrajectory;this.inFlight=true;this.freeFollowerPose=false; }
            if (this.RailVehicle.RailSpec.Coaster && coupling != null && (this.resumeMomentumPending || coupling.AwaitingLoad))
            {
                this.ResumeConsistMomentum(coupling);
                return;
            }
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
            if (driver != null && !this.serverDriving && !this.RailVehicle.ServerOnlyPhysics)
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
                if(this.nativeVelocityAvailable&&(now-this.nativeSampleTime).TotalSeconds>.3)
                {
                    // Resting native bodies stop sending packets. Their final
                    // moving packet must not keep the visual controller running.
                    this.nativeVelocity*=(float)Math.Exp(-dt/.15);
                    if(this.nativeVelocity.LengthSquared()<.000001f)this.nativeVelocity=Vector3.Zero;
                }
                this.nativeWasPulling = true;
                this.current = null;
                // Native packets arrive less often than this component's 20 Hz
                // timer. Reproject the most recent accepted pose between packets
                // so a sharp turn does not wait a whole network interval.
                // Each accepted packet is guided once by OnNativePhysicsPose.
                // Do not feed the corrected server pose back into native pulling between packets.
                this.RailVehicle.PublishWheelMotion(this.nativeVelocity);
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
                // Park rail-only stock at its placed pose; hand-operated carts
                // retain native off-rail physics. Departing rails uses TickFlight.
                this.ReleaseToGroundPhysics();
                if (this.inFlight) this.TickFlight(dt);
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
                var landingRotating = this.LandingRotationActive;
                this.AdvanceLandingRotation(dt);
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
                    var rotation=this.GuidedRotation(rail,this.t,tangent * this.facing);
                    this.Parent.Position = this.LandingPosition(point,rail,this.t,tangent*this.facing,rotation);
                    this.Parent.Rotation = rotation;
                    this.RailVehicle.SetRailGuidance(true);
                    this.RailVehicle.PublishRailPose(velocity);
                    this.lastPublishedRailVelocity = velocity;
                }
                else if (landingRotating || (rail.Profile.Coaster && Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitY),rail.Profile.Up(this.t))<.99995f)
                    || RailGuidance.ShouldPublishPose(this.Parent.Position, point,
                    this.Parent.Rotation.RotateVector(Vector3.UnitZ), tangent * this.facing,
                    this.lastPublishedRailVelocity, velocity))
                {
                    var rotation=this.GuidedRotation(rail,this.t,tangent * this.facing);
                    this.Parent.Position = this.LandingPosition(point,rail,this.t,tangent*this.facing,rotation);
                    this.Parent.Rotation = rotation;
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/TickGround", this.Parent);
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
        this.RailVehicle.PublishWheelMotion(this.groundVelocity);
        this.Sound(0);
        this.Parent.GetComponent<RailCouplingComponent>()?.FollowFree(this.groundVelocity);
    }

    private void ReleaseToGroundPhysics()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/ReleaseToGroundPhysics", this.Parent);
        if (this.RailVehicle.ServerOnlyPhysics)
        {
            // Rail-only ownership is still authoritative, but unsupported
            // coaster carts need gravity instead of a permanent airborne park.
            if (this.RailVehicle.RailSpec.Coaster && FlightWorld.LandingHeight(
                this.Parent.Position + Vector3.UnitY*.06f,
                this.Parent.Position - Vector3.UnitY*.06f, this.Rotation, null) == null)
            {
                this.inFlight=true;
                this.parkedOffRail=false;
                this.nativeGroundPhysics=false;
                this.flightVelocity=Vector3.Zero;
                this.flightFacingSign=this.facing;
                this.RailVehicle.SetRailGuidance(true);
                this.WakeMotion();
                return;
            }
            this.ParkOffRail();
            return;
        }
        if (!this.nativeGroundPhysics)
        {
            ((RailVehicleObject)this.Parent).SetRailGuidance(false);
            ((RailVehicleObject)this.Parent).SetPhysicsController(null!);
            this.nativeGroundPhysics = true;
        }
        this.RailVehicle.PublishWheelMotion(Vector3.Zero);
        this.Sound(0);
    }

    private void ParkOffRail()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/ParkOffRail", this.Parent);
        // Ownership can be reassigned after placement. Reclaim it at each maintenance tick;
        // stationary keyframes prevent a local suspension impulse persisting.
        this.RailVehicle.SetRailGuidance(true);
        var now=DateTime.UtcNow;
        if (!this.parkedOffRail || this.nativeGroundPhysics
            || (now-this.lastParkedPosePublished).TotalSeconds >= .25)
        {
            this.RailVehicle.PublishRailPose(Vector3.Zero);
            this.lastParkedPosePublished=now;
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/TickFlight", this.Parent);
        this.landingRotationElapsed = .45;
        var count = Math.Max(1, (int)Math.Ceiling(dt * Math.Max(50, (this.flightVelocity.Length() + 9.81 * dt) / .05)));
        var position = this.Parent.Position;
        for (var i = 0; i < count && this.inFlight; i++)
        {
            this.Parent.GetComponent<RailCouplingComponent>().RecordMotion(null,0,position,this.flightVelocity);
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
                this.AlignToRail(this.current.Value,this.t);
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
        if (this.current == null && this.flightVelocity.LengthSquared() > .0025f)
        {
            // Gravity changes the velocity arc each substep. Pitch/roll the body
            // toward that arc at a bounded angular rate instead of retaining the
            // takeoff pose until landing or snapping to the new heading.
            var travel=this.flightVelocity;var orientation=this.flightFacingSign;
            if(this.RailVehicle.RailSpec.Coaster && this.Parent.GetComponent<RailCouplingComponent>().GroundedCouplerDirection() is {} supported)
            {travel=supported;orientation=1;}
            var turned = AirMotion.FollowTrajectory(this.Rotation, travel, orientation, dt);
            this.Parent.Rotation = new Eco.Shared.Math.Quaternion(turned.X, turned.Y, turned.Z, turned.W);
        }
        if (this.current != null)
        {
            ((RailVehicleObject)this.Parent).PublishRailPose(this.flightVelocity);
        }
        else if (this.inFlight)
            ((RailVehicleObject)this.Parent).PublishRailPose(this.flightVelocity);
        else if (this.RailVehicle.ServerOnlyPhysics)
            this.ParkOffRail();
        else
        {
            ((RailVehicleObject)this.Parent).ReleaseRailPose(this.flightVelocity);
            this.nativeGroundPhysics = true;
        }
        this.Sound(0);
        if(this.current is {} landed)
            this.Parent.GetComponent<RailCouplingComponent>()?.FollowTrain(landed,this.t,this.facing,
                this.RailTangent(landed,this.t,this.NextRail)*(float)this.state.Speed);
        else this.Parent.GetComponent<RailCouplingComponent>()?.FollowFree(this.flightVelocity);
    }

    private IRailForceDriver? motorDriver;
    private void Step(double dt, double cargo, float walkSpeed)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/Step", this.Parent);
        var rail = this.current!.Value;
        MinecartDumpRailComponent.TryUnload(rail.Cell,this.Parent);
        this.Parent.GetComponent<RailCouplingComponent>().RecordMotion(rail,this.t,this.RailPosition(rail,this.t,this.NextRail),rail.Profile.Tangent(this.t)*(float)this.state.Speed);
        LimitSharpCoasterSpeed(rail);
        if (!this.CheckFootprint(rail, this.t, this.state.Speed)) return;
        var tangent = this.RailTangent(rail, this.t, (r, end) => this.NextRail(r, end));
        var consistGrade=this.Parent.GetComponent<RailCouplingComponent>().ConsistGrade(tangent.Y,this.facing);
        var chain = rail.Profile.Chain&&this.RailVehicle.RailSpec.Coaster ? MinecartChainDriveObject.LiftFor(rail.Cell, this.Parent.ID,true) : (Watts: 0d, Multiplier: 1f);
        this.chainSpeedMultiplier=chain.Multiplier;
        var lift = ChainLift.Force(Tuning.EmptyMassKg + cargo, tangent.Y, this.state.Speed, chain.Watts,
            ChainLift.TargetSpeed(chain.Multiplier, this.RailVehicle.RailSpec.Coaster));
        {
            // A coaster's trailing axle can still be on a powered lift after
            // the leader passes its crest, just like any other coupled car.
            var traction=this.Parent.GetComponent<RailCouplingComponent>().ChainTraction(rail,this.facing,this.state.Speed,consistGrade);
            lift=traction.Force;this.chainSpeedMultiplier=traction.Multiplier;
        }
        this.chainLiftEngaged = rail.Profile.Chain && lift > 0 && (tangent.Y > .001f || rail.Profile.BrakingChain);
        var chainBrake = rail.Profile.BrakingChain ? CoasterChainBrake.Force(Tuning.EmptyMassKg + cargo,
            this.state.Speed, chain.Watts > 0 ? ChainLift.TargetSpeed(chain.Multiplier,true) : 0) : 0;
        var control=this.Parent.GetComponent<TrainControllerComponent>();
        var command=(this.motorDriver??=new RailMotorDriver(this.RailVehicle)).Request(
            new(this,rail,this.t,this.facing,this.state.Speed,dt,tangent.Y));
        var input=(Force:command.Force,Brake:command.Brake);
        if (this.serverDriving || control?.Active == true) this.Handbrake = input.Brake;
        if(this.RailVehicle.RailSpec.Coaster && CoasterStationComponent.At(rail.Cell) is {} station)
        {
            this.coasterQueueFeed=false;
            input=station.Control(this.Parent.GetComponent<RailCouplingComponent>(),this.facing,this.state.Speed);
            this.Handbrake=input.Brake;
            this.coasterApproachBraking=false;
        }
        else if(this.RailVehicle.RailSpec.Coaster)
        {
            if(this.QueueControl(out var queueInput)){input=queueInput;this.Handbrake=input.Brake;}
            var approaching=CoasterStationComponent.ApproachBrake(rail,this.t,this.state.Speed,this.Parent.GetComponent<RailCouplingComponent>());
            if(approaching||this.coasterApproachBraking)this.Handbrake=approaching;
            this.coasterApproachBraking=approaching;
        }
        this.Parent.SetAnimatedState("AutoRunning", (this.serverDriving || control?.Active == true) && input.Force != 0);
        var push = input.Force;
        var brake = this.Handbrake ? 1 : control?.ServiceBrake ?? 0;
        this.state = RailPhysics.Integrate(this.state with { Distance = 0 },
            new MinecartInput(cargo, push + lift + chainBrake, brake, consistGrade, rail.Profile.Curvature), Tuning, dt).State;
        if (this.state.Derailed) { this.Derailed = true; this.state = this.state with { Speed = 0 }; return; }
        var integratedSpeed=this.state.Speed;
        LimitSharpCoasterSpeed(rail);
        if(this.state.Speed!=integratedSpeed)
            this.state=this.state with {Distance=Math.CopySign(Math.Min(Math.Abs(this.state.Distance),Math.Abs(this.state.Speed)*dt),this.state.Distance)};
        var limitedTravel = RailGuidance.LimitBufferTravel(rail, this.t, this.state.Distance, (r, end) => this.NextRail(r, end), this.RailVehicle.CouplerOffset + .194f);
        if(control!=null)limitedTravel=control.LimitStationTravel(limitedTravel);
        limitedTravel = this.LimitVehicleTravel(rail, this.t, limitedTravel);
        if (Math.Abs(limitedTravel - this.state.Distance) > .000001)
            this.state = this.state with { Speed = 0, Distance = limitedTravel };
        control?.RecordTrackTravel(this.state.Distance);
        var startSpeed=this.state.Speed;
        var cursor=RailPathCursor.Travel(rail,this.t,this.state.Distance,this.NextRail,8,
            (entered,orientation)=>this.CheckRail(entered,startSpeed*orientation));
        if(!cursor.Valid)return;
        this.state=this.state with{Speed=this.state.Speed*cursor.Orientation};
        this.facing*=cursor.Orientation;
        rail=cursor.Rail;
        if(cursor.Remaining!=0)
        {
            var end=cursor.Remaining<0?0:1;
            var endTangent=this.RailTangent(rail,end,this.NextRail);
            this.departureDirection=endTangent*(end==0?-1:1);
            this.departurePoint=this.RailPosition(rail,end,this.NextRail);
            this.Parent.Position=this.departurePoint+this.departureDirection*(float)Math.Abs(cursor.Remaining);
            this.Parent.Rotation=this.RailRotation(rail,end,endTangent*this.facing);
            this.departedRail=rail;this.current=null;this.captureOffset=Vector3.Zero;
            this.nativeGroundPhysics=false;this.flightVelocity=endTangent*(float)this.state.Speed;
            this.flightFacingSign=Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ),this.flightVelocity)<0?-1:1;
            this.inFlight=true;
            this.RailVehicle.SetRailGuidance(true);this.RailVehicle.PublishRailPose(this.flightVelocity);this.Sound(0);
            // Departure is a coupled pose transition in this same tick. The
            // grounded branch below no longer runs after current is cleared;
            // deferring followers until the next timer stretched every drawbar.
            this.Parent.GetComponent<RailCouplingComponent>()?.FollowFree(this.flightVelocity);
            return;
        }
        this.current=rail;this.t=cursor.Progress;
    }

    private void LimitSharpCoasterSpeed(VoxelRail rail)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Movement/LimitSharpCoasterSpeed", this.Parent);
        if(!this.RailVehicle.RailSpec.Coaster || Math.Abs(this.state.Speed)<=2)return;
        // A half-metre-radius quarter turn can fit entirely between two native
        // pose packets at coaster speed. Interpolation then draws a diagonal
        // across it. Slow before entry and until the tail leaves the corner.
        var remaining=(float)(Math.Abs(this.state.Speed)*.3+this.Parent.GetComponent<RailCouplingComponent>().Performance.Length);
        foreach(var scanDirection in new[]{-1,1})
        {
            var cursor=rail;var parameter=this.t;var direction=scanDirection;var distance=remaining;
            for(var i=0;i<32 && distance>=0;i++)
            {
                if(cursor.Profile.Shape.StartsWith("CoasterTrackSharp",StringComparison.Ordinal))
                {this.state=this.state with {Speed=Math.CopySign(2,this.state.Speed)};return;}
                var end=direction>0?1:0;
                distance-=(direction>0?1-parameter:parameter)*cursor.Profile.Length;
                if(distance<0 || this.NextRail(cursor,end) is not {} next)break;
                cursor=next.Rail;parameter=next.End;direction=next.End==0?1:-1;
            }
        }
    }

    private float chainSpeedMultiplier=1;
    internal bool EffectiveHandbrake => this.Parent.GetComponent<RailCouplingComponent>() is { IsFollower:true } coupling
        ? coupling.Leader().Parent.GetComponent<MinecartMotionComponent>().Handbrake : this.Handbrake;
    private void Sound(double speed)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Wheel sound and animation states/Sound", this.Parent);
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
        var serviceBrake=this.Parent.GetComponent<RailCouplingComponent>()?.Leader().Parent.GetComponent<TrainControllerComponent>()?.ServiceBrake??0;
        var brake = CartSound.Braking(speed, braking?1:serviceBrake);
        this.Parent.SetAnimatedState("BrakeVolume", brake.Volume);
        for (var tier = 1; tier <= 3; tier++)
            this.Parent.SetAnimatedState("BrakeSparks" + tier, brake.SparkTier == tier);
    }
}
