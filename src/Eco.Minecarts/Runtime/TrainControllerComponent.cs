using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.Networking;
using Eco.Gameplay.Players;
using Eco.Mods.TechTree;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, AutogenClass, CreateComponentTabLoc("Autopilot", true), LocDisplayName("Train Autopilot")]
public sealed class TrainControllerComponent : WorldObjectComponent
{
    [Serialized] public bool Autopilot { get; set; }
    [Serialized] public bool Reverse { get; set; }
    [Serialized] private float targetSpeedKmH;
    [Serialized] private bool speedControlActive;
    [Serialized] private bool paused;
    [Serialized] private bool targetSpeedSet;
    [Serialized] private bool throttleControlSelected;
    [Serialized] private float requestedThrottlePercent;
    private DateTime nextSpeedAdjustment;
    private double bufferWaitSeconds;
    internal static double TargetThrottle(double targetKmH,double speed) => Math.Clamp((targetKmH/3.6-Math.Abs(speed))*.8,0,1);
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Autopilot Mode")]
    public string AutopilotMode => !Autopilot ? "Off" : paused ? "Paused" : speedControlActive ? "Cruise control" : "Maintaining throttle";
    [SyncToView, Autogen, LocDisplayName("Target Speed (km/h)"), LocDescription("Enter a cruising speed to enable cruise control. Zero applies the brakes.")]
    public float TargetSpeed => targetSpeedKmH;
    [SyncToView, Autogen, Eco.Shared.Networking.Range(0,100), LocDisplayName("Throttle (%)"), LocDescription("Enter 0 to 100 to hold that throttle instead of cruise control. Zero coasts; use Pause and Brake to stop.")]
    public float Throttle => throttleControlSelected ? requestedThrottlePercent : (float)(Parent?.GetComponent<MinecartMotionComponent>()?.ServerThrottle ?? 0)*100;
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Current Speed (km/h)")]
    public string CurrentSpeed => $"{Parent.GetComponent<MinecartMotionComponent>().CurrentRailVelocity.Length()*3.6f:0.0}";
    private bool CanConfigure(Player player) => player!=null && Parent!=null && !Parent.IsDestroyed && Parent.IsAuthorized(player.User, Eco.Shared.Items.AccessType.FullAccess)
        && (Parent.GetComponent<MountComponent>().Driver is not {} driver || driver==player)
        && Vector3.Distance(player.User.Position, Parent.Position) <= 6;
    public void SetAutopilot(Player player, bool value)
    {
        if (!CanConfigure(player)) return;
        if(!value && Parent.GetComponent<MineTrainDrivingComponent>() is {} controls)
        {
            // Shift+E on the engine works from outside the cab too. Only mount
            // the player if they are already in the control position; otherwise
            // switch modes without changing the persistent train commands.
            if(!controls.TakeControls(player)) SetControllerMode(false);
            return;
        }
        var mounts = Parent.GetComponent<MountComponent>();
        if (value && mounts.Driver != null)
        {
            if (mounts.Driver != player) return;
            mounts.TryDismountPlayer(player);
            if (mounts.Driver != null) return;
        }
        SetControllerMode(value);
    }
    internal void HandoffToAutopilot() => SetControllerMode(true);
    internal void TakeManualControl() => SetControllerMode(false);
    private void SetControllerMode(bool automatic)
    {
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        if(Autopilot==automatic && motion.ServerDriving) return;
        motion.EnsureDriveCommands();
        Autopilot=automatic;
        if(!automatic)
        {
            Parent.GetComponent<TramRouteComponent>()?.SuspendService();
            speedControlActive=false; paused=false; throttleControlSelected=false;
            // Station dwell belongs to the automated route, not the train's
            // physical brake. Manual takeover must not inherit an invisible
            // station hold from the previous controller.
            stoppedStation=0;
        }
        Reverse=motion.ServerDirection<0;
        motion.PrepareServerDriver();
        // Route/station safety state and motion commands survive controller changes.
        this.Changed(nameof(Autopilot)); this.Changed(nameof(AutopilotMode)); this.Changed(nameof(TargetSpeed)); Parent.SetDirty();
    }
    private float MaximumTargetSpeed => (float)Math.Max(0,Parent.GetComponent<RailCouplingComponent>().Performance.SpeedLimit*3.6);
    private void PublishSpeedSettings()
    {
        this.Changed(nameof(AutopilotMode)); this.Changed(nameof(TargetSpeed)); this.Changed(nameof(Throttle)); this.Changed(nameof(Status)); Parent.SetDirty();
    }
    private void CommandTarget(Player player,float value)
    {
        if(!CanConfigure(player) || !float.IsFinite(value)) return;
        SetAutopilot(player,true);
        if(!Autopilot) return;
        ApplyTargetCommand(value);
    }
    internal void ApplyTargetCommand(float value)
    {
        if(!float.IsFinite(value)) return;
        SetControllerMode(true);
        var target=Math.Clamp(value,0,MaximumTargetSpeed);
        targetSpeedKmH=target; targetSpeedSet=true; speedControlActive=true; throttleControlSelected=false; paused=false;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        var throttle=target>0?TargetThrottle(target,motion.CurrentRailVelocity.Length()):0;
        motion.SetDriveCommands(throttle,motion.ServerDirection,target<=0);
        nextSpeedAdjustment=DateTime.MinValue;
        Status=target>0?"Accelerating to target speed":"Stopped by autopilot";
        PublishSpeedSettings();
    }
    // Explicit setters route the stock numeric editors through authorization and
    // validation, rather than an unrestricted generated property setter.
    [RPC] public void SetTargetSpeed(Player player,float value) => CommandTarget(player,value);
    [RPC] public void SetThrottle(Player player,float value)
    {
        if(!CanConfigure(player) || !float.IsFinite(value)) return;
        SetAutopilot(player,true);
        if(Autopilot) ApplyThrottleCommand(value);
    }
    internal void ApplyThrottleCommand(float value)
    {
        if(!float.IsFinite(value)) return;
        SetControllerMode(true);
        requestedThrottlePercent=Math.Clamp(value,0,100);
        throttleControlSelected=true; speedControlActive=false; paused=false;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        motion.SetDriveCommands(requestedThrottlePercent/100d,motion.ServerDirection,false);
        Status=requestedThrottlePercent>0?"Maintaining throttle":"Coasting";
        PublishSpeedSettings();
    }
    [RPC] public void IncreaseTargetSpeed(Player player) => CommandTarget(player,targetSpeedKmH+5);
    [RPC] public void DecreaseTargetSpeed(Player player) => CommandTarget(player,targetSpeedKmH-5);
    [RPC, Autogen] public void StartAutopilot(Player player)
    {
        if(!CanConfigure(player)) return;
        SetAutopilot(player,true);
        if(Autopilot) ResumeAutopilotCommand();
    }
    internal void ResumeAutopilotCommand()
    {
        if(throttleControlSelected) ApplyThrottleCommand(requestedThrottlePercent);
        else ApplyTargetCommand(targetSpeedSet || speedControlActive || targetSpeedKmH>0 ? targetSpeedKmH : MaximumTargetSpeed);
    }
    [RPC, Autogen] public void PauseAndBrake(Player player) => PauseAutopilot(player);
    [RPC, Autogen] public void StopAndDisableAutopilot(Player player) => StopAutopilot(player);
    // Keep old RPC names usable by already-open views, but show only the clearer actions.
    [RPC] public void PauseAutopilot(Player player)
    {
        if(!CanConfigure(player) || !Autopilot) return;
        paused=true;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        motion.SetDriveCommands(0,motion.ServerDirection,true);
        Status="Autopilot paused"; PublishSpeedSettings();
    }
    [RPC] public void StopAutopilot(Player player)
    {
        if(!CanConfigure(player)) return;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        motion.SetDriveCommands(0,motion.ServerDirection,true);
        SetControllerMode(false);
        Status="Autopilot off"; PublishSpeedSettings();
    }
    public void SetReverse(Player player, bool value)
    {
        if(!CanConfigure(player)) return;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        if(motion.CurrentRailVelocity.Length()>.08f) return;
        motion.EnsureDriveCommands();
        motion.SetDriveCommands(motion.ServerThrottle,value?-1:1,motion.CommandBrake);
        Reverse=value; route.Clear(); this.Changed(nameof(Reverse)); Parent.SetDirty();
    }
    [SyncToView, Autogen, PropReadOnly] public string Status { get; private set; } = "Manual control";
    internal void ReportSafety(string reason) { Status=reason; this.Changed(nameof(Status)); }
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Total Train Weight (kg)")] public float TrainWeightKg { get; private set; }
    [SyncToView] public float MaximumSpeed { get; private set; }
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Maximum Speed (km/h)")] public string SpeedLimit => $"{MaximumSpeed * 3.6f:0.#}";
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Connected Vehicles")] public string ConnectedCars { get; private set; } = "";
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(RailCell Cell,int Turn,int End), (VoxelRail Rail, int End)> route = new();
    private DateTime nextFuelScan;
    private DateTime arrived;
    private int stoppedStation;
    private int departedStation;
    private Vector3 departedPosition;
    private bool StationApplicable(TrainStationComponent station,int travelDirection)
    {
        var route=Parent.GetComponent<TramRouteComponent>();
        var stop=station.Parent.GetComponent<TramStopComponent>();
        if(stop!=null) return stop.Accepts(route,travelDirection);
        return route==null || route.Servicing && route.Wants(station.Parent.DisplayName.ToString());
    }
    public bool Active => this.Autopilot && this.Parent.GetComponent<MountComponent>().Driver == null
        && !this.Parent.GetComponent<RailCouplingComponent>().AwaitingLoad
        && this.Parent.GetComponent<RailCouplingComponent>().Leader().Parent == this.Parent;

    public override void Tick()
    {
        base.Tick();
        var coupling = this.Parent.GetComponent<RailCouplingComponent>();
        var performance = coupling.Performance;
        this.TrainWeightKg = (float)performance.Mass; this.MaximumSpeed = (float)performance.SpeedLimit;
        this.ConnectedCars = string.Join(", ", coupling.Group().GroupBy(x => x.Parent.DisplayName.ToString()).Select(g => $"{g.Count()} × {g.Key}"));
        this.Changed(nameof(TrainWeightKg)); this.Changed(nameof(MaximumSpeed)); this.Changed(nameof(SpeedLimit)); this.Changed(nameof(ConnectedCars)); this.Changed(nameof(Status)); this.Changed(nameof(CurrentSpeed));
        this.Changed(nameof(TargetSpeed)); this.Changed(nameof(Throttle));
        if (DateTime.UtcNow >= this.nextFuelScan)
        {
            if (coupling.Leader() == coupling) TenderSupply.Refill(coupling);
            this.nextFuelScan = DateTime.UtcNow.AddSeconds(1);
        }
        if (!this.Active)
        {
            this.Status = Parent.GetComponent<MineTrainDrivingComponent>()?.HasStandingOperator==true ? "Manual control" : "Autopilot off";
        }
    }

    internal (VoxelRail Rail, int End)? Next(VoxelRail rail, int end)
    {
        // A selected turnout is authoritative even for automation. Radius
        // incompatibility is handled by the existing derailment checks.
        var physical=TrackWorld.Neighbor(rail,end);
        if(RailSwitchComponent.At(rail.Cell)!=null || physical is {} linked && RailSwitchComponent.At(linked.Rail.Cell)!=null) return physical;
        if (!this.Active) return TrackWorld.Neighbor(rail, end);
        var performance = this.Parent.GetComponent<RailCouplingComponent>().Performance;
        if (this.route.TryGetValue((rail.Cell,rail.Profile.QuarterTurns,end), out var cached) && TrackWorld.Contains(cached.Rail)
            && rail.Connects(end,cached.Rail,cached.End)
            && performance.Fits(cached.Rail.Profile.Radius)) return cached;
        var candidates = TrackWorld.Neighbors(rail, end).Where(x => performance.Fits(x.Rail.Profile.Radius)).ToArray();
        // Prefer a branch that reaches a station, then the longest compatible continuation.
        // The bounded search never takes an incompatible first edge.
        var selected = candidates.OrderByDescending(x => this.RouteScore(x.Rail, x.End, performance)).FirstOrDefault();
        if (selected.Rail.Profile.Shape == null) return null;
        // Bound per-engine route history on long-lived public worlds. Entries
        // are only a choice cache; clearing them never changes physical points.
        if(this.route.Count>=4096) this.route.Clear();
        this.route[(rail.Cell,rail.Profile.QuarterTurns,end)] = selected;
        this.route[(selected.Rail.Cell,selected.Rail.Profile.QuarterTurns,selected.End)] = (rail, end);
        return selected;
    }

    private double RouteScore(VoxelRail start, int entry, TrainPerformance performance)
    {
        var pending = new Queue<(VoxelRail Rail, int Entry, double Distance)>();
        var seen = new HashSet<(RailCell,int,int)>(); var best = 0d;
        pending.Enqueue((start, entry, 0));
        while (pending.TryDequeue(out var node) && seen.Count < 512)
        {
            if (!seen.Add((node.Rail.Cell,node.Rail.Profile.QuarterTurns,node.Entry)) || !performance.Fits(node.Rail.Profile.Radius)) continue;
            var distance = node.Distance + node.Rail.Profile.Length; best = Math.Max(best, distance);
            if (TrainStationComponent.At(node.Rail.Cell).Any(x => x.Parent.ID != this.departedStation
                && StationApplicable(x,node.Entry==0?1:-1))) return 100000 - distance;
            if (distance > 300) continue;
            foreach (var next in TrackWorld.Neighbors(node.Rail, 1 - node.Entry)) pending.Enqueue((next.Rail, next.End, distance));
        }
        return best;
    }

    internal (double Force, bool Brake) Control(VoxelRail rail, float t, int facing, double speed, double dt)
    {
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        if (!this.Active && !motion.ServerDriving) { bufferWaitSeconds=0; return (0, false); }
        motion.EnsureDriveCommands();
        var coupling = this.Parent.GetComponent<RailCouplingComponent>();
        var train = coupling.Performance;
        var direction = facing * motion.ServerDirection;
        if(this.Active && speedControlActive && !paused && targetSpeedKmH>0 && DateTime.UtcNow>=nextSpeedAdjustment)
        {
            var throttle=TargetThrottle(targetSpeedKmH,speed);
            if(Math.Abs(throttle-motion.ServerThrottle)>.05)
                motion.SetDriveCommands(throttle,motion.ServerDirection,false);
            nextSpeedAdjustment=DateTime.UtcNow.AddMilliseconds(500);
        }
        if(motion.CommandBrake) { bufferWaitSeconds=0; this.Status=paused?"Autopilot paused":"Commanded brakes"; return (0,true); }
        if (this.departedStation != 0 && Vector3.Distance(this.departedPosition, this.Parent.Position) > train.Length + 3)
            this.departedStation = 0;
        if (this.Active && this.stoppedStation != 0)
        {
            var station = TrainStationComponent.Find(this.stoppedStation);
            var dwell=(DateTime.UtcNow-this.arrived).TotalSeconds;
            var tramStop=station?.Parent.GetComponent<TramStopComponent>();
            var ready=station==null || tramStop!=null ? station==null || tramStop!.Ready(dwell)
                : Parent.GetComponent<TramRouteComponent>()!=null && !station.HasDepartureConditions ? dwell>=15 : station.Ready(coupling,dwell);
            if (ready)
            {
                station?.Parent.GetComponent<RailAutomationComponent>()?.Emit(RailEvent.Dispatch,this.Parent.ObjectID);
                if(station!=null) Parent.GetComponent<TramRouteComponent>()?.Departed(tramStop?.StopName??station.Parent.DisplayName.ToString());
                this.departedStation = this.stoppedStation; this.departedPosition = this.Parent.Position;
                this.stoppedStation = 0; this.route.Clear();
            }
            else { bufferWaitSeconds=0; this.Status = "Waiting at station"; return (0, true); }
        }
        var limit = train.SpeedLimit;
        if(coupling.Vehicle.RailSpec.Tram)
        {
            var cable=rail.Profile.Tram?TramCableDriveObject.PowerFor(rail.Cell,Parent.ID):0;
            limit=Math.Min(limit,TramPowerRules.SpeedLimit(train.SpeedLimit,rail.Profile.Tram,cable));
        }
        var deceleration = train.BrakeDeceleration(Math.Max(0,-rail.Profile.Tangent(t).Y*direction));
        var stopping = train.StoppingDistance(speed, Math.Max(0, -rail.Profile.Tangent(t).Y * direction));
        var scanDistance = Math.Min(1500, Math.Max(80, stopping + train.Length + 25));
        var walked = 0d; var cursor = rail; var parameter = (double)t; var localDirection = direction;
        var visited = new HashSet<(RailCell, int)>();
        var obstacle = double.PositiveInfinity; TrainStationComponent? targetStation = null;
        var targetBuffer = false;
        var forward = this.Parent.Rotation.RotateVector(Vector3.UnitZ) * motion.ServerDirection;
        var nose = coupling.Group().Max(x => Vector3.Dot(x.Parent.Position - this.Parent.Position, forward) + x.Vehicle.CouplerOffset);
        for (var n = 0; n < 1024 && walked < scanDistance; n++)
        {
            if (!train.Fits(cursor.Profile.Radius)) { obstacle = Math.Max(0, walked - nose - .25); targetStation=null; targetBuffer=false; break; }
            deceleration = Math.Min(deceleration,train.BrakeDeceleration(Math.Max(0,-cursor.Profile.Tangent((float)parameter).Y*localDirection)));
            var curveLimit = train.CurveSpeed(cursor.Profile.Radius);
            limit = Math.Min(limit, Math.Sqrt(curveLimit * curveLimit + 2 * deceleration * Math.Max(0, walked - nose)));
            if (this.Active)
                foreach (var station in TrainStationComponent.At(cursor.Cell))
                {
                    var distance = walked + (station.TrackT - parameter) * cursor.Profile.Length * localDirection - nose;
                    if (station.Parent.ID == this.departedStation || !StationApplicable(station,localDirection)
                        || distance < -.4 || distance >= obstacle) continue;
                    obstacle = Math.Max(0, distance); targetStation = station; targetBuffer=false;
                }
            // Match the actual buffer beam and the physical travel limiter.
            // A buffer behind us must not cause another turnaround on departure.
            var beamAhead=(.8-parameter)*cursor.Profile.Length*localDirection;
            var bufferDistance=Math.Max(0,walked+beamAhead-nose-.194);
            if(cursor.Profile.Shape=="Stopper" && beamAhead>=0 && bufferDistance<obstacle)
            { obstacle=bufferDistance; targetStation=null; targetBuffer=true; }
            var exit = localDirection > 0 ? 1 : 0;
            walked += (localDirection > 0 ? 1 - parameter : parameter) * cursor.Profile.Length;
            if (!visited.Add((cursor.Cell, exit))) break;
            if (this.Next(cursor, exit) is not { } next)
            {
                var distance=Math.Max(0,walked-nose-.2);
                if(distance<obstacle) { obstacle=distance; targetStation=null; targetBuffer=false; }
                break;
            }
            cursor = next.Rail; parameter = next.End; localDirection = next.End == 0 ? 1 : -1;
        }
        var canTurn=this.Active && coupling.Vehicle.RailSpec.Tram && targetBuffer && obstacle<.18
            && Math.Abs(speed)<.01 && !paused && motion.ServerThrottle>.001 && limit>0
            && this.Parent.GetComponent<RailConditionComponent>()?.ConditionPercent>0;
        bufferWaitSeconds=canTurn ? bufferWaitSeconds+Math.Clamp(dt,0,.1) : 0;
        if(bufferWaitSeconds>=.75)
        {
            motion.SetDriveCommands(motion.ServerThrottle,-motion.ServerDirection,false);
            Reverse=motion.ServerDirection<0; route.Clear(); bufferWaitSeconds=0;
            this.Status="Reversing at buffer stop";
            this.Changed(nameof(Reverse)); this.Changed(nameof(Status)); Parent.SetDirty();
            return (0,true); // Remain stopped this step; depart on the next one.
        }
        if (obstacle < .18 && Math.Abs(speed) < .12)
        {
            if (targetStation != null)
            {
                this.stoppedStation = targetStation.Parent.ID; this.arrived = DateTime.UtcNow;
                targetStation.Parent.GetComponent<RailAutomationComponent>()?.Emit(RailEvent.Arrive,this.Parent.ObjectID);
            }
            this.Status = canTurn ? "Turning around at buffer stop"
                : targetStation == null ? "Stopped: track end or incompatible route" : "Arrived at station";
            return (0, true);
        }
        limit = Math.Min(limit, Math.Sqrt(2 * deceleration * Math.Max(0, obstacle - .08)));
        if(this.Active && speedControlActive) limit=Math.Min(limit,targetSpeedKmH/3.6+.15);
        if (Math.Abs(speed) > limit || speed * direction < -.05) { this.Status = "Braking"; return (0, true); }
        if (this.Parent.GetComponent<RailConditionComponent>()?.ConditionPercent <= 0) { this.Status = "Repair required"; return (0, true); }
        this.Status = speedControlActive&&this.Active ? "Following target speed" : motion.ServerThrottle<=.001 ? "Coasting" : "Following rails";
        return (train.DriveForce(speed) * motion.ServerThrottle * direction, false);
    }
}
