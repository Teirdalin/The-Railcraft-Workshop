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
public sealed partial class TrainControllerComponent : WorldObjectComponent
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
    internal double ServiceBrake {get;private set;}
    internal static double TargetThrottle(double targetKmH,double speed) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/TargetThrottle"); return Math.Clamp((targetKmH/3.6-Math.Abs(speed))*.8,0,1); }
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Autopilot Mode")]
    public string AutopilotMode => !Autopilot ? "Off" : paused ? "Paused" : speedControlActive ? "Cruise control" : "Maintaining throttle";
    private int targetStationId;
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Target Station"), LocDescription("The next stop on the selected track route. Switches retain their selected direction.")]
    public string TargetStation => !Autopilot ? "Autopilot off"
        : (TrainStationComponent.Find(stoppedStation!=0?stoppedStation:targetStationId)??TrainStationComponent.Find(conditionalDestination)) is {} station
            ? station.Parent.GetComponent<TramStopComponent>()?.StopName ?? station.Parent.DisplayName.ToString()
            : "No compatible station found on selected route";
    private void SetTargetStation(int id)
    {
        if(targetStationId==id)return;
        targetStationId=id;this.Changed(nameof(TargetStation));
    }
    [SyncToView, Autogen, LocDisplayName("Target Speed (km/h)"), LocDescription("Enter a cruising speed to enable cruise control. Zero applies the brakes.")]
    public float TargetSpeed => targetSpeedKmH;
    [SyncToView, Autogen, Eco.Shared.Networking.Range(0,100), LocDisplayName("Throttle (%)"), LocDescription("Enter 0 to 100 to hold that throttle instead of cruise control. Zero coasts; use Pause and Brake to stop.")]
    public float Throttle => throttleControlSelected ? requestedThrottlePercent : (float)(Parent?.GetComponent<MinecartMotionComponent>()?.ServerThrottle ?? 0)*100;
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Current Speed (km/h)")]
    public string CurrentSpeed => $"{Parent.GetComponent<MinecartMotionComponent>().CurrentRailVelocity.Length()*3.6f:0.0}";
    internal bool CanConfigure(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/CanConfigure", this.Parent);
        if(player==null || Parent==null || Parent.IsDestroyed)return false;
        if(!Parent.IsAuthorized(player.User,Eco.Shared.Items.AccessType.FullAccess))
        {player.InfoBoxLoc($"You need full vehicle access to change its autopilot.");return false;}
        if(Parent.GetComponent<MountComponent>().Driver is {} driver && driver!=player
            || Parent.GetComponent<MineTrainDrivingComponent>() is {HasStandingOperator:true} cab && cab.OperatorId!=player.ID)
        {player.InfoBoxLoc($"Another player is operating this vehicle.");return false;}
        var delta=player.User.Position-Parent.Position;
        var size=Eco.World.World.VoxelSize;
        // Guided vehicles use continuous positions across the torus seam;
        // the player may have already wrapped into the neighboring copy.
        if(size.X>0)delta.X-=MathF.Round(delta.X/size.X)*size.X;
        if(size.Z>0)delta.Z-=MathF.Round(delta.Z/size.Z)*size.Z;
        if(delta.LengthSquared()>36)
        {player.InfoBoxLoc($"Move within six metres of this vehicle's control area.");return false;}
        return true;
    }
    public void SetAutopilot(Player player, bool value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/SetAutopilot", this.Parent);
        if (!CanConfigure(player)) return;
        if(!value)
        {
            StopAutomaticMotion();
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
    internal void HandoffToAutopilot() { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/HandoffToAutopilot", this.Parent); SetControllerMode(true); }
    internal void TakeManualControl() { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/TakeManualControl", this.Parent); SetControllerMode(false); }
    internal void StopAutomaticMotion()
    {
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        motion.SetDriveCommands(0,motion.ServerDirection,true);
        SetControllerMode(false);
        Status="Autopilot off; brakes applied";
        PublishSpeedSettings();
    }
    private void SetControllerMode(bool automatic)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/SetControllerMode", this.Parent);
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        if(Autopilot==automatic && motion.ServerDriving) return;
        motion.EnsureDriveCommands();
        Autopilot=automatic;
        journey=null;
        SetTargetStation(0);
        if(!automatic)
        {
            ClearDestination("Autopilot off",true);
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
        this.Changed(nameof(Autopilot)); this.Changed(nameof(AutopilotMode)); this.Changed(nameof(TargetSpeed)); this.Changed(nameof(TargetStation)); Parent.SetDirty();
    }
    private float MaximumTargetSpeed => (float)Math.Max(0,Parent.GetComponent<RailCouplingComponent>().Performance.SpeedLimit*3.6);
    private void PublishSpeedSettings()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/PublishSpeedSettings", this.Parent);
        this.Changed(nameof(AutopilotMode)); this.Changed(nameof(TargetSpeed)); this.Changed(nameof(Throttle)); this.Changed(nameof(Status)); Parent.SetDirty();
    }
    private void CommandTarget(Player player,float value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/CommandTarget", this.Parent);
        if(!CanConfigure(player) || !float.IsFinite(value)) return;
        SetAutopilot(player,true);
        if(!Autopilot) return;
        ApplyTargetCommand(value);
    }
    internal void ApplyTargetCommand(float value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/ApplyTargetCommand", this.Parent);
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
    [RPC] public void SetTargetSpeed(Player player,float value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/SetTargetSpeed", this.Parent); CommandTarget(player,value); }
    [RPC] public void SetThrottle(Player player,float value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/SetThrottle", this.Parent);
        if(!CanConfigure(player) || !float.IsFinite(value)) return;
        SetAutopilot(player,true);
        if(Autopilot) ApplyThrottleCommand(value);
    }
    internal void ApplyThrottleCommand(float value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/ApplyThrottleCommand", this.Parent);
        if(!float.IsFinite(value)) return;
        SetControllerMode(true);
        requestedThrottlePercent=Math.Clamp(value,0,100);
        throttleControlSelected=true; speedControlActive=false; paused=false;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        motion.SetDriveCommands(requestedThrottlePercent/100d,motion.ServerDirection,false);
        Status=requestedThrottlePercent>0?"Maintaining throttle":"Coasting";
        PublishSpeedSettings();
    }
    [RPC] public void IncreaseTargetSpeed(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/IncreaseTargetSpeed", this.Parent); CommandTarget(player,targetSpeedKmH+5); }
    [RPC] public void DecreaseTargetSpeed(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/DecreaseTargetSpeed", this.Parent); CommandTarget(player,targetSpeedKmH-5); }
    [RPC, Autogen] public void StartAutopilot(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/StartAutopilot", this.Parent);
        if(!CanConfigure(player)) return;
        SetAutopilot(player,true);
        if(Autopilot) ResumeAutopilotCommand();
    }
    internal void ResumeAutopilotCommand()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/ResumeAutopilotCommand", this.Parent);
        if(throttleControlSelected) ApplyThrottleCommand(requestedThrottlePercent);
        else ApplyTargetCommand(targetSpeedSet || speedControlActive || targetSpeedKmH>0 ? targetSpeedKmH : MaximumTargetSpeed);
    }
    [RPC, Autogen] public void PauseAndBrake(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/PauseAndBrake", this.Parent); PauseAutopilot(player); }
    [RPC, Autogen] public void StopAndDisableAutopilot(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/StopAndDisableAutopilot", this.Parent); StopAutopilot(player); }
    // Keep old RPC names usable by already-open views, but show only the clearer actions.
    [RPC] public void PauseAutopilot(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/PauseAutopilot", this.Parent);
        if(!CanConfigure(player) || !Autopilot) return;
        paused=true;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        motion.SetDriveCommands(0,motion.ServerDirection,true);
        Status="Autopilot paused"; PublishSpeedSettings();
    }
    [RPC] public void StopAutopilot(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/StopAutopilot", this.Parent);
        if(!CanConfigure(player)) return;
        StopAutomaticMotion();
    }
    public void SetReverse(Player player, bool value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/SetReverse", this.Parent);
        if(!CanConfigure(player)) return;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        if(motion.CurrentRailVelocity.Length()>.08f) return;
        motion.EnsureDriveCommands();
        motion.SetDriveCommands(motion.ServerThrottle,value?-1:1,motion.CommandBrake);
        Reverse=value; route.Clear(); this.Changed(nameof(Reverse)); Parent.SetDirty();
    }
    [SyncToView, Autogen, PropReadOnly] public string Status { get; private set; } = "Manual control";
    internal void ReportSafety(string reason) {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/ReportSafety", this.Parent); Status=reason; this.Changed(nameof(Status)); }
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Total Train Weight (kg)")] public float TrainWeightKg { get; private set; }
    [SyncToView] public float MaximumSpeed { get; private set; }
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Maximum Speed (km/h)")] public string SpeedLimit => $"{MaximumSpeed * 3.6f:0.#}";
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Connected Vehicles")] public string ConnectedCars { get; private set; } = "";
    private readonly record struct RouteStamp(long Track, long Stations, long Tram, int Departed, double MinimumRadius);
    private readonly record struct RouteChoice(RouteStamp Stamp, long Expires, (VoxelRail Rail,int End)? Next);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(VoxelRail Rail,int End), RouteChoice> route = new();
    private DateTime nextFuelScan;
    private DateTime arrived;
    private int stoppedStation;
    private int departedStation;
    private Vector3 departedPosition;
    private double departedTrackTravel;
    internal void RecordTrackTravel(double distance)
    {if(departedStation!=0 && double.IsFinite(distance))departedTrackTravel+=Math.Abs(distance);}
    private bool StationApplicable(TrainStationComponent station,int travelDirection)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/StationApplicable", this.Parent);
        if(station.Parent.IsDestroyed)return false;
        if(conditionalDestination!=Guid.Empty)return station.Parent.ObjectID==conditionalDestination;
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/Tick", this.Parent);
        base.Tick();
        var coupling = this.Parent.GetComponent<RailCouplingComponent>();
        var performance = coupling.Performance;
        this.TrainWeightKg = (float)performance.Mass; this.MaximumSpeed = (float)performance.SpeedLimit;
        this.ConnectedCars = string.Join(", ", coupling.Group().GroupBy(x => x.Parent.DisplayName.ToString()).Select(g => $"{g.Count()} × {g.Key}"));
        this.Changed(nameof(TrainWeightKg)); this.Changed(nameof(MaximumSpeed)); this.Changed(nameof(SpeedLimit)); this.Changed(nameof(ConnectedCars)); this.Changed(nameof(Status)); this.Changed(nameof(CurrentSpeed));
        this.Changed(nameof(TargetSpeed)); this.Changed(nameof(Throttle));
        this.Changed(nameof(TargetStation));
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/Next", this.Parent);
        if(!this.Active)return TrackWorld.Neighbor(rail,end);
        var train=this.Parent.GetComponent<RailCouplingComponent>().Performance;
        var planned=ValidJourney(JourneyContextFor(true,train));
        if(planned!=null && planned.Plan.TryNext(rail,end,out var next))return next;
        return NextAutomatic(rail,end,train);
    }
    private (VoxelRail Rail,int End)? NextAutomatic(VoxelRail rail,int end,TrainPerformance performance)
    {
        var couplingTram=((RailVehicleObject)Parent).RailSpec.Tram;
        var stamp=new RouteStamp(RailSimulationFrame.Revision,TrainStationComponent.RoutingRevision,
            Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0,departedStation,performance.MinimumRadius);
        var now=Environment.TickCount64;
        if(route.TryGetValue((rail,end),out var cached) && cached.Stamp==stamp && now<cached.Expires)return cached.Next;
        // A selected turnout is authoritative even for automation. Radius
        // incompatibility is handled by the existing derailment checks.
        var physical=TrackWorld.Neighbor(rail,end);
        if(couplingTram && physical is {} ordinary && !ordinary.Rail.Profile.Tram)physical=null;
        (VoxelRail Rail,int End)? selected;
        if(RailSwitchComponent.At(rail.Cell)!=null || physical is {} linked && RailSwitchComponent.At(linked.Rail.Cell)!=null)
            selected=physical;
        else if(physical is {} only)
            selected=performance.Fits(only.Rail.Profile.Radius)?only:null;
        else if(cached.Next is {} choice && cached.Stamp.Stations==stamp.Stations && cached.Stamp.Tram==stamp.Tram
            && cached.Stamp.Departed==stamp.Departed && TrackWorld.Contains(choice.Rail)
            && (!couplingTram || choice.Rail.Profile.Tram)
            && rail.Connects(end,choice.Rail,choice.End) && performance.Fits(choice.Rail.Profile.Radius))
            selected=choice;
        else
        {
            // Score genuine branches only. A straight continuation needs no
            // 512-node station search, and an absent edge is cached too.
            var candidates=TrackWorld.Neighbors(rail,end).Where(x=>performance.Fits(x.Rail.Profile.Radius) && (!couplingTram || x.Rail.Profile.Tram)).ToArray();
            selected=candidates.Length==0?null:candidates.Length==1?candidates[0]
                :candidates.OrderByDescending(x=>RouteScore(x.Rail,x.End,performance)).First();
        }
        if(this.route.Count>=4096) this.route.Clear();
        this.route[(rail,end)] = new(stamp,now+1000,selected);
        // Retain the return edge for wheelbase/follower traversal through an
        // ambiguous merge. Never override a turnout's selected physical path.
        if(selected is {} back && RailSwitchComponent.At(rail.Cell)==null && RailSwitchComponent.At(back.Rail.Cell)==null)
            this.route[(back.Rail,back.End)]=new(stamp,now+1000,(rail,end));
        return selected;
    }

    private double RouteScore(VoxelRail start, int entry, TrainPerformance performance)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/RouteScore", this.Parent);
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

    private double stationTravel=double.PositiveInfinity;
    private int stationTravelDirection;
    internal static bool ReadyToDepart(TrainStationComponent station,RailCouplingComponent train,double dwell,bool tram)
    {
        var stop=station.Parent.GetComponent<TramStopComponent>();
        // Station Departure is authoritative for all vehicles. A tram's old
        // timer is only a default for stops without configured departure rules.
        if(station.HasDepartureConditions)return station.Ready(train,dwell);
        return stop!=null ? stop.Ready(dwell) : tram && dwell>=15;
    }
    internal double LimitStationTravel(double distance)=>distance*stationTravelDirection>0 && Math.Abs(distance)>stationTravel
        ? Math.CopySign(stationTravel,distance) : distance;
    internal (double Force, bool Brake) Control(VoxelRail rail, float t, int facing, double speed, double dt)
    {
        ServiceBrake=0;
        stationTravel=double.PositiveInfinity;stationTravelDirection=0;
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/Control", this.Parent);
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        var active=this.Active;
        if (!active && !motion.ServerDriving) { bufferWaitSeconds=0; journey=null; return (0, false); }
        if(!active && !Autopilot && Parent.GetComponent<MineTrainDrivingComponent>()?.HasStandingOperator!=true)
        {
            // Recover saved manual throttle without a real operator, including
            // trams, which have passenger seats but no manual driving position.
            if(motion.ServerThrottle!=0 || !motion.CommandBrake)motion.SetDriveCommands(0,motion.ServerDirection,true);
            bufferWaitSeconds=0;journey=null;Status="Autopilot off; brakes applied";
            return (0,true);
        }
        motion.EnsureDriveCommands();
        var coupling = this.Parent.GetComponent<RailCouplingComponent>();
        var train = coupling.Performance;
        var direction = facing * motion.ServerDirection;
        var forward = this.Parent.Rotation.RotateVector(Vector3.UnitZ) * motion.ServerDirection;
        var nose = coupling.Group().Max(x => Vector3.Dot(x.Parent.Position - this.Parent.Position, forward) + x.Vehicle.CouplerOffset);
        if(active && speedControlActive && !paused && targetSpeedKmH>0 && DateTime.UtcNow>=nextSpeedAdjustment)
        {
            var throttle=TargetThrottle(targetSpeedKmH,speed);
            if(Math.Abs(throttle-motion.ServerThrottle)>.05)
                motion.SetDriveCommands(throttle,motion.ServerDirection,false);
            nextSpeedAdjustment=DateTime.UtcNow.AddMilliseconds(500);
        }
        if(motion.CommandBrake) { bufferWaitSeconds=0; this.Status=paused?"Autopilot paused":"Commanded brakes"; return (0,true); }
        if (this.departedStation != 0 && departedTrackTravel > train.Length + 3)
        { this.departedStation = 0; departedTrackTravel=0; this.route.Clear(); journey=null; }
        if (active && this.stoppedStation != 0)
        {
            var station = TrainStationComponent.Find(this.stoppedStation);
            var dwell=(DateTime.UtcNow-this.arrived).TotalSeconds;
            var tramStop=station?.Parent.GetComponent<TramStopComponent>();
            var arrivedAtDiversion=station!=null&&station.Parent.ObjectID==conditionalDestination;
            var completingSchedule=returningToSchedule;
            var turnaround=station!=null && coupling.Vehicle.RailSpec.Tram && TerminalTramStation(station,rail,t,direction);
            var departureDirection=turnaround?-direction:direction;
            var departureForward=Parent.Rotation.RotateVector(Vector3.UnitZ)*(turnaround?-motion.ServerDirection:motion.ServerDirection);
            var departureNose=coupling.Group().Max(x=>Vector3.Dot(x.Parent.Position-Parent.Position,departureForward)+x.Vehicle.CouplerOffset);
            var ready=station==null || ChooseStationDeparture(station,coupling,dwell,rail,t,departureDirection,departureNose,allowReverse:!turnaround);
            if (ready)
            {
                if(turnaround)
                {
                    motion.SetDriveCommands(motion.ServerThrottle,-motion.ServerDirection,false);
                    Reverse=motion.ServerDirection<0; this.Changed(nameof(Reverse)); Parent.SetDirty();
                }
                station?.Parent.GetComponent<RailAutomationComponent>()?.Emit(RailEvent.Dispatch,this.Parent.ObjectID);
                if(station!=null && (!arrivedAtDiversion || completingSchedule)) Parent.GetComponent<TramRouteComponent>()?.Departed(tramStop?.StopName??station.Parent.DisplayName.ToString());
                if(arrivedAtDiversion && station!.Parent.ObjectID==conditionalDestination)FinishDiversion(station,rail,t,departureDirection,departureNose,coupling);
                this.departedStation = this.stoppedStation; this.departedPosition = this.Parent.Position;
                departedTrackTravel=0;
                this.stoppedStation = 0; this.route.Clear(); journey=null;
                SetTargetStation(0);
            }
            else { bufferWaitSeconds=0; this.Status = "Waiting at station"; return (0, true); }
        }
        direction=facing*motion.ServerDirection;
        forward=Parent.Rotation.RotateVector(Vector3.UnitZ)*motion.ServerDirection;
        nose=coupling.Group().Max(x=>Vector3.Dot(x.Parent.Position-Parent.Position,forward)+x.Vehicle.CouplerOffset);
        var limit = train.SpeedLimit;
        if(coupling.Vehicle.RailSpec.Tram)
        {
            var cable=rail.Profile.Tram?TramCableDriveObject.PowerFor(rail.Cell,Parent.ID):0;
            if(!TramPowerRules.UsesCable(rail.Profile.Tram,cable))
            {
                Status=rail.Profile.Tram ? "Waiting for track power: power a Tram Cable Drive and press Connect" : "Tram Rail required";
                return (0,true);
            }
            limit=Math.Min(limit,TramPowerRules.SpeedLimit(train.SpeedLimit,rail.Profile.Tram,cable));
        }
        var deceleration = train.BrakeDeceleration(Math.Max(0,-rail.Profile.Tangent(t).Y*direction));
        var stopping = train.StoppingDistance(speed, Math.Max(0, -rail.Profile.Tangent(t).Y * direction));
        var scanDistance = Math.Min(1500, Math.Max(80, stopping + train.Length + 25));
        if(active)EnsureDestination(rail,t,direction,nose,train);
        var cached=PlanJourney(rail,t,direction,nose,train,active,scanDistance,out var cursor);
        if(cached==null){this.Status="Track changed: replanning";return (0,true);}
        if(active)SetTargetStation(cached.Plan.TargetStation(cursor,t,nose,departedStation));
        if(active && !PrepareDestinationSwitches(cached.Plan,cursor,t,nose,scanDistance)){stationTravelDirection=direction;return(0,true);}
        RailJourneyPlan.Safety safety;
        using(RailProfile.Measure("Rail Network/Autopilot and stops/Follow journey",Parent))
            safety=cached.Plan.Follow(cursor,t,nose,train,scanDistance,limit,deceleration,departedStation);
        limit=safety.SpeedLimit;deceleration=safety.Deceleration;
        var obstacle=safety.Obstacle;var targetBuffer=safety.Buffer;
        var targetStation=safety.Station==0?null:TrainStationComponent.Find(safety.Station);
        if(active && targetStation!=null){stationTravel=Math.Max(0,obstacle);stationTravelDirection=direction;}
        var canTurn=active && coupling.Vehicle.RailSpec.Tram && targetBuffer && obstacle<.18
            && Math.Abs(speed)<.01 && !paused && motion.ServerThrottle>.001 && limit>0
            && this.Parent.GetComponent<RailConditionComponent>()?.ConditionPercent>0;
        bufferWaitSeconds=canTurn ? bufferWaitSeconds+Math.Clamp(dt,0,.1) : 0;
        if(bufferWaitSeconds>=.75)
        {
            motion.SetDriveCommands(motion.ServerThrottle,-motion.ServerDirection,false);
            Reverse=motion.ServerDirection<0; route.Clear(); journey=null; bufferWaitSeconds=0;
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
        if(active && speedControlActive) limit=Math.Min(limit,targetSpeedKmH/3.6+.15);
        if (Math.Abs(speed) > limit || speed * direction < -.05) { this.Status = "Braking"; return (0, true); }
        if (this.Parent.GetComponent<RailConditionComponent>()?.ConditionPercent <= 0) { this.Status = "Repair required"; return (0, true); }
        var stationApproach=active && coupling.Vehicle.RailSpec.Tram && targetStation!=null;
        var approachLimit=stationApproach?Math.Min(limit,TramStationBraking.ApproachSpeed(obstacle,deceleration)):limit;
        if(stationApproach && Math.Abs(speed)>approachLimit)
        {
            var travelGrade=RailGuidance.AxleTangent(rail,t,motion.NextRail,coupling.Vehicle.RailSpec.Wheelbase/2).Y*direction;
            ServiceBrake=TramStationBraking.BrakeFraction(speed,approachLimit,deceleration,travelGrade,train.Mass,train.BrakingForce);
            Status="Slowing for station";
            return (0,false);
        }
        this.Status = speedControlActive&&active ? "Following target speed" : motion.ServerThrottle<=.001 ? "Coasting" : "Following rails";
        var grade=coupling.Vehicle.RailSpec.Tram
            ? RailGuidance.AxleTangent(rail,t,motion.NextRail,coupling.Vehicle.RailSpec.Wheelbase/2).Y*direction : 0;
        var force=train.DriveForce(speed,grade);
        var driveThrottle=motion.ServerThrottle;
        if(stationApproach)driveThrottle=Math.Min(driveThrottle,TargetThrottle(approachLimit*3.6,speed));
        if(coupling.Vehicle.RailSpec.Tram && active && speedControlActive && targetSpeedKmH>0 && grade>0)
        {
            // At a low target speed the ordinary speed-error throttle can be
            // below the effort needed just to hold the hill. Add that holding
            // effort within the existing throttle range, not beyond motor or
            // allocated-power limits. The motion solver applies cable power.
            var cable=TramCableDriveObject.PowerFor(rail.Cell,Parent.ID);
            driveThrottle=Math.Clamp(driveThrottle+train.Mass*9.80665*grade/Math.Max(1,force*cable),0,1);
        }
        return (force * driveThrottle * direction, false);
    }
}
