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
    // Transient output of the same authoritative control request as Force.
    // Fuel must follow governed/hill-holding effort, not the earlier cab command.
    internal double DriveEffort {get;private set;}
    internal static double TargetThrottle(double targetKmH,double speed) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/TargetThrottle"); return Math.Clamp((targetKmH/3.6-Math.Abs(speed))*.8,0,1); }
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Autopilot Mode")]
    public string AutopilotMode => !Autopilot ? "Off" : paused ? "Paused" : speedControlActive ? "Cruise control" : "Maintaining throttle";
    private int targetStationId;
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Target Station"), LocDescription("The next stop on the selected track route. Switches retain their selected direction.")]
    public string TargetStation => !Autopilot ? "Autopilot off"
        : (TrainStationComponent.Find(stoppedStation!=0?stoppedStation:targetStationId)??TrainStationComponent.Find(conditionalDestination)) is {} station
            ? station.Parent.GetComponent<TramStopComponent>()?.StopName ?? station.Parent.DisplayName.ToString()
            : "No compatible station found on selected route";
    private Guid reservedTrafficStation;
    private void SetTargetStation(int id,bool preserveReservation=false)
    {
        if(!preserveReservation)reservedTrafficStation=TrainStationComponent.Find(id)?.Parent.ObjectID??Guid.Empty;
        UpdateStationIntent();
        if(targetStationId==id)return;
        targetStationId=id;this.Changed(nameof(TargetStation));
    }
    private void UpdateStationIntent()=>TrainStationComponent.SetInbound(this,
        Autopilot&&!Parent.IsDestroyed&&!Parent.GetComponent<MinecartMotionComponent>().Derailed&&Active
            ?conditionalDestination!=Guid.Empty?conditionalDestination:reservedTrafficStation:Guid.Empty);
    public override void Destroy(){TrainStationComponent.SetInbound(this,Guid.Empty);base.Destroy();}
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
        if(value)ResumeAutopilotCommand();
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
            stoppedStation=0;stoppedBinding=null;
        }
        Reverse=motion.ServerDirection<0;
        motion.PrepareServerDriver();
        Parent.GetComponent<TramRouteComponent>()?.RefreshService();
        // Route/station safety state and motion commands survive controller changes.
        this.Changed(nameof(Autopilot)); this.Changed(nameof(AutopilotMode)); this.Changed(nameof(TargetSpeed)); this.Changed(nameof(TargetStation)); Parent.SetDirty();
    }
    private float MaximumTargetSpeed => (float)Math.Max(0,Parent.GetComponent<RailCouplingComponent>().Performance.SpeedLimit*Parent.GetComponent<RailCouplingComponent>().MaintenanceSpeedFactor*3.6);
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
    [RPC, Autogen, UITypeName("BigButton")] public void StartAutopilot(Player player)
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
    [RPC, Autogen, UITypeName("BigButton")] public void PauseAndBrake(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/PauseAndBrake", this.Parent); PauseAutopilot(player); }
    [RPC, Autogen, UITypeName("BigButton")] public void StopAndDisableAutopilot(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/StopAndDisableAutopilot", this.Parent); StopAutopilot(player); }
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
    internal void ReportRepairRequired(VoxelRail rail,float t,int facing)
    {
        ServiceBrake=0;DriveEffort=0;bufferWaitSeconds=0;
        // The motor's repair gate bypasses Control. Maintain the existing
        // stop/route observation there too, without searching or dispatching.
        if(Active)
        {
            if(stoppedStation!=0)
            {if(!StationHoldCurrent(TrainStationComponent.Find(stoppedStation)))ReplanStationStop();}
            else ValidateBrakedTarget(rail,t,facing*Parent.GetComponent<MinecartMotionComponent>().ServerDirection,
                Parent.GetComponent<RailCouplingComponent>().Performance);
        }
        ReportSafety("Repair required");
    }
    internal void ReportRailUnavailable(bool energyChanged)
    {
        var reason=Active?paused?"Autopilot paused":Parent.GetComponent<MinecartMotionComponent>().Derailed?"Recovery required":"Waiting for compatible rail":Status;
        ServiceBrake=0;DriveEffort=0;bufferWaitSeconds=0;
        SetTargetStation(0);
        // Use the existing property notifications here. The normal component
        // tick remains the sole writer of the cached panel snapshot.
        if(Status!=reason)
        {
            Status=reason;this.Changed(nameof(Status));this.Changed(nameof(CurrentSpeed));
        }
        if(energyChanged)this.Changed(nameof(EnergyUsage));
    }
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Total Train Weight (kg)")] public float TrainWeightKg { get; private set; }
    [SyncToView] public float MaximumSpeed { get; private set; }
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Energy Usage (W)"), LocDescription("Current fuel use. Haul weight and throttle set the demand up to this locomotive's maximum.")]
    public float EnergyUsage => ((RailVehicleObject)Parent).FuelDemandWatts;
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Maximum Speed (km/h)")] public string SpeedLimit => $"{MaximumSpeed * 3.6f:0.#}";
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Connected Vehicles")] public string ConnectedCars { get; private set; } = "";
    private readonly record struct RouteStamp(long Track, long Stations, long Tram, int Departed, double MinimumRadius);
    private readonly record struct RouteChoice(RouteStamp Stamp, long Expires, (VoxelRail Rail,int End)? Next, bool Return = false);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(VoxelRail Rail,int End), RouteChoice> route = new();
    private DateTime nextFuelScan;
    private DateTime arrived;
    private int stoppedStation;
    // A station ID survives relocation. Dwell and departure authority belong
    // to the physical stop at which this controller actually arrived.
    private (RailCell Cell,float T,VoxelTrackProfile Profile)? stoppedBinding;
    private bool StationHoldCurrent(TrainStationComponent? station)=>station?.Parent.ID==stoppedStation
        && stoppedBinding is {} stop && station.BoundStop is {} current
        // Equivalent world images can round a fractional parameter slightly.
        && current.Cell==stop.Cell && Math.Abs(current.T-stop.T)<.0001f && current.Profile==stop.Profile
        // A rail replacement or switch edit can retain both coordinates.
        // Check the live path even before the station's next binding tick.
        && TrackWorld.Contains(new(stop.Cell,stop.Profile));
    private void ReplanStationStop()
    {
        stoppedStation=0;stoppedBinding=null;journey=null;route.Clear();terminalTram=null;
        evaluatedStation=0;nextDepartureEvaluation=DateTime.MinValue;
        SetTargetStation(0);bufferWaitSeconds=0;Status="Station changed: replanning";
    }
    private int departedStation;
    private Vector3 departedPosition;
    // Suppress this platform until the consist has cleared it, including when
    // a save is reloaded immediately after dispatch. Native numeric IDs belong
    // to one process; resolve the saved object identity after registration.
    [Serialized] private Guid departedStationObject;
    [Serialized] private double departedTrackTravel;
    private bool departureRestored;
    private bool RestoreDeparture()
    {
        if(departureRestored)return true;
        if(departedStationObject!=Guid.Empty)
        {
            var station=TrainStationComponent.Find(departedStationObject);
            if(TrainStationComponent.RegistrationPending(departedStationObject,station))
            {Status="Waiting for departed station to finish loading or moving";return false;}
            departedStation=station?.Parent.ID??0;
            if(departedStation==0){departedStationObject=Guid.Empty;departedTrackTravel=0;Parent.SetDirty();}
            else if(!double.IsFinite(departedTrackTravel)||departedTrackTravel<0)departedTrackTravel=0;
        }
        departureRestored=true;return true;
    }
    internal void RecordTrackTravel(double distance)
    {if((departedStation!=0 || departedStationObject!=Guid.Empty) && double.IsFinite(distance))departedTrackTravel+=Math.Abs(distance);}
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

    // Compare the values exposed by the existing panel, including formatted
    // speed and the live target's name. Runtime scratch must not enter saves.
    private readonly record struct MotionView(float Weight,float MaximumSpeed,string SpeedLimit,string Cars,string Status,
        string Speed,float TargetSpeed,float Throttle,float EnergyUsage,string TargetStation);
    private MotionView? publishedMotionView;
    private void PublishMotionView()
    {
        var current=new MotionView(TrainWeightKg,MaximumSpeed,SpeedLimit,ConnectedCars,Status,
            CurrentSpeed,TargetSpeed,Throttle,EnergyUsage,TargetStation);
        var first=publishedMotionView==null;
        var old=publishedMotionView.GetValueOrDefault();
        publishedMotionView=current;
        if(first || !current.Weight.Equals(old.Weight))this.Changed(nameof(TrainWeightKg));
        if(first || !current.MaximumSpeed.Equals(old.MaximumSpeed))this.Changed(nameof(MaximumSpeed));
        if(first || current.SpeedLimit!=old.SpeedLimit)this.Changed(nameof(SpeedLimit));
        if(first || current.Cars!=old.Cars)this.Changed(nameof(ConnectedCars));
        if(first || current.Status!=old.Status)this.Changed(nameof(Status));
        if(first || current.Speed!=old.Speed)this.Changed(nameof(CurrentSpeed));
        if(first || !current.TargetSpeed.Equals(old.TargetSpeed))this.Changed(nameof(TargetSpeed));
        if(first || !current.Throttle.Equals(old.Throttle))this.Changed(nameof(Throttle));
        if(first || !current.EnergyUsage.Equals(old.EnergyUsage))this.Changed(nameof(EnergyUsage));
        if(first || current.TargetStation!=old.TargetStation)this.Changed(nameof(TargetStation));
    }

    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/Tick", this.Parent);
        base.Tick();
        UpdateStationIntent();
        var coupling = this.Parent.GetComponent<RailCouplingComponent>();
        var performance = coupling.Performance;
        this.TrainWeightKg = (float)performance.Mass; this.MaximumSpeed = (float)(performance.SpeedLimit*coupling.MaintenanceSpeedFactor);
        this.ConnectedCars = string.Join(", ", coupling.Group().GroupBy(x => x.Parent.DisplayName.ToString()).Select(g => $"{g.Count()} × {g.Key}"));
        if (DateTime.UtcNow >= this.nextFuelScan)
        {
            if (coupling.Leader() == coupling) TenderSupply.Refill(coupling);
            this.nextFuelScan = DateTime.UtcNow.AddSeconds(1);
        }
        if (!this.Active)
        {
            this.Status = Parent.GetComponent<MineTrainDrivingComponent>()?.HasStandingOperator==true ? "Manual control" : "Autopilot off";
        }
        PublishMotionView();
    }

    internal (VoxelRail Rail, int End)? Next(VoxelRail rail, int end)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/Next", this.Parent);
        if(!this.Active)return TrackWorld.Neighbor(rail,end);
        // A saved or invalidated diversion must be replanned before traversing
        // another rail; the ordinary route is not its selected itinerary.
        if(conditionalDestination!=Guid.Empty && destinationPath==null)return null;
        var train=this.Parent.GetComponent<RailCouplingComponent>().Performance;
        var planned=ValidJourney(JourneyContextFor(true,train));
        if(planned!=null && planned.Plan.TryNext(rail,end,out var next))return next;
        // A topology edit can expire the journey after control prepared it.
        // Keep its destination edge when physically present, or stop at that
        // edge until control replans/prepares the points on its next update.
        // Rear axles and bodies beyond the destination retain physical lookup.
        if(conditionalDestination!=Guid.Empty && DestinationEdge(destinationPath!,rail,end) is {} intended)
            return TrackWorld.Contains(intended.Rail) && rail.Connects(end,intended.Rail,intended.End)?intended:null;
        return NextAutomatic(rail,end,train);
    }
    private (VoxelRail Rail,int End)? NextAutomatic(VoxelRail rail,int end,TrainPerformance performance)
        =>NextAutomaticRoute(rail,end,performance,null);

    private (VoxelRail Rail,int End)? NextAutomaticRoute(VoxelRail rail,int end,TrainPerformance performance,DepartureService? departure)
    {
        var couplingTram=((RailVehicleObject)Parent).RailSpec.Tram;
        var stamp=new RouteStamp(RailSimulationFrame.Revision,TrainStationComponent.RoutingRevision,
            departure?.Revision??Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0,departure?.Station??departedStation,performance.MinimumRadius);
        var now=Environment.TickCount64;
        if(route.TryGetValue((rail,end),out var cached) && cached.Stamp==stamp && now<cached.Expires)return cached.Next;
        // A selected turnout is authoritative even for automation. Radius
        // incompatibility is handled by the existing derailment checks.
        var physical=TrackWorld.Neighbor(rail,end);
        if(couplingTram && physical is {} ordinary && !ordinary.Rail.Profile.Tram)physical=null;
        (VoxelRail Rail,int End)? selected;var returning=false;
        if(RailSwitchComponent.At(rail.Cell)!=null || physical is {} linked && RailSwitchComponent.At(linked.Rail.Cell)!=null)
            selected=physical;
        else if(physical is {} only)
            selected=performance.Fits(only.Rail.Profile.Radius)?only:null;
        // Forward preferences must be scored again when downstream track or
        // the consist's radius changes. A reciprocal edge instead remembers
        // the path already taken through a merge by the wheels/followers.
        else if(cached.Next is {} choice && (cached.Return || cached.Stamp.Track==stamp.Track && cached.Stamp.MinimumRadius==stamp.MinimumRadius)
            && cached.Stamp.Stations==stamp.Stations && cached.Stamp.Tram==stamp.Tram
            && cached.Stamp.Departed==stamp.Departed && TrackWorld.Contains(choice.Rail)
            && (!couplingTram || choice.Rail.Profile.Tram)
            && rail.Connects(end,choice.Rail,choice.End) && performance.Fits(choice.Rail.Profile.Radius))
        {selected=choice;returning=cached.Return;}
        else
        {
            // Score genuine branches only. A straight continuation needs no
            // 512-node station search, and an absent edge is cached too.
            var candidates=TrackWorld.Neighbors(rail,end).Where(x=>performance.Fits(x.Rail.Profile.Radius) && (!couplingTram || x.Rail.Profile.Tram)).ToArray();
            selected=candidates.Length==0?null:candidates.Length==1?candidates[0]
                :candidates.OrderByDescending(x=>ScoreAutomaticRoute(x.Rail,x.End,performance,departure)).First();
        }
        if(this.route.Count>=4096) this.route.Clear();
        this.route[(rail,end)] = new(stamp,now+1000,selected,returning);
        // Retain the return edge for wheelbase/follower traversal through an
        // ambiguous merge. Never override a turnout's selected physical path.
        if(!returning && selected is {} back && RailSwitchComponent.At(rail.Cell)==null && RailSwitchComponent.At(back.Rail.Cell)==null)
            this.route[(back.Rail,back.End)]=new(stamp,now+1000,(rail,end),Return:true);
        return selected;
    }

    private double RouteScore(VoxelRail start, int entry, TrainPerformance performance)
        =>ScoreAutomaticRoute(start,entry,performance,null);

    private double ScoreAutomaticRoute(VoxelRail start,int entry,TrainPerformance performance,DepartureService? departure)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/RouteScore", this.Parent);
        var couplingTram=((RailVehicleObject)Parent).RailSpec.Tram;
        var pending = new PriorityQueue<(VoxelRail Rail, int Entry, double Distance),(double Distance,int Order)>();
        var seen = new HashSet<(VoxelRail,int)>(); var best = 0d;
        // Small forks keep the original queue. Allocate a frontier index only
        // when convergence can retain a substantial backlog of approaches.
        Dictionary<(VoxelRail,int),double>? frontier=null;
        var stationDistance=double.PositiveInfinity;var order=0;
        static void Enqueue(PriorityQueue<(VoxelRail Rail,int Entry,double Distance),(double Distance,int Order)> pending,
            HashSet<(VoxelRail,int)> seen,ref Dictionary<(VoxelRail,int),double>? frontier,ref int order,
            VoxelRail rail,int end,double distance)
        {
            if(frontier==null && pending.Count<128)
            {pending.Enqueue((rail,end,distance),(distance,order++));return;}
            if(frontier==null)
            {
                frontier=new();
                foreach(var item in pending.UnorderedItems)
                {
                    var part=item.Element;
                    var approach=(new VoxelRail(RailPowerConnectionComponent.PhysicalCell(part.Rail.Cell),part.Rail.Profile),part.Entry);
                    if(!frontier.TryGetValue(approach,out var previous) || part.Distance<previous)frontier[approach]=part.Distance;
                }
            }
            var key=(new VoxelRail(RailPowerConnectionComponent.PhysicalCell(rail.Cell),rail.Profile),end);
            if(seen.Contains(key) || frontier.TryGetValue(key,out var known) && known<=distance)return;
            frontier[key]=distance;
            pending.Enqueue((rail,end,distance),(distance,order++));
        }
        pending.Enqueue((start,entry,0),(0,order++));
        while (pending.TryDequeue(out var node,out _) && seen.Count < 512)
        {
            // Entry distance is a lower bound for every stop on this rail.
            // Settle unequal-length branches in travel order, not rail count.
            if(node.Distance>=stationDistance)break;
            // Score only track this vehicle can actually follow, including
            // rails deeper in the search beyond its first candidate edge.
            var key=(new VoxelRail(RailPowerConnectionComponent.PhysicalCell(node.Rail.Cell),node.Rail.Profile),node.Entry);
            if(frontier!=null && frontier.TryGetValue(key,out var queued) && queued==node.Distance)frontier.Remove(key);
            if (!seen.Add(key) || !performance.Fits(node.Rail.Profile.Radius)
                || couplingTram && !node.Rail.Profile.Tram) continue;
            var distance = node.Distance + node.Rail.Profile.Length; best = Math.Max(best, distance);
            foreach(var station in TrainStationComponent.At(node.Rail.Cell))
                if(station.Parent.ID!=(departure?.Station??departedStation) && station.MatchesTrack(node.Rail)
                    && RailGuidance.CanReachStop(node.Rail,node.Entry,station.TrackT,node.Entry==0?1:-1)
                    && (departure is {} service?DepartureStopApplies(station,node.Entry==0?1:-1,service):StationApplicable(station,node.Entry==0?1:-1)))
                    stationDistance=Math.Min(stationDistance,node.Distance+Math.Abs(station.TrackT-node.Entry)*node.Rail.Profile.Length);
            // A buffer ends the motion itinerary even when track continues
            // behind it. A station beyond it cannot favor this branch.
            if (distance > 300 || distance>=stationDistance || node.Rail.Profile.Shape=="Stopper") continue;
            var exit=1-node.Entry;
            var physical=TrackWorld.Neighbor(node.Rail,exit);
            // Score the same physical turnout traversal as NextAutomatic.
            // Raw neighbors omit a legal spring merge from an unselected
            // branch, and can see branches the selected points cannot take.
            if(physical is {} next)Enqueue(pending,seen,ref frontier,ref order,next.Rail,next.End,distance);
            else if(RailSwitchComponent.At(node.Rail.Cell)==null)
                foreach(var alternative in TrackWorld.Neighbors(node.Rail,exit))Enqueue(pending,seen,ref frontier,ref order,alternative.Rail,alternative.End,distance);
        }
        return double.IsPositiveInfinity(stationDistance)?best:100000-stationDistance;
    }

    private double stationTravel=double.PositiveInfinity;
    private bool speedLimitBraking;
    private double belowLimitSeconds;
    internal bool SpeedLimitBrake(double speed,double limit,double dt)
    {
        // Start safety braking immediately, then require a stable lower speed
        // before releasing. Avoid alternating brake/throttle every solver step.
        if(Math.Abs(speed)>limit){speedLimitBraking=true;belowLimitSeconds=0;}
        else if(speedLimitBraking)
        {
            belowLimitSeconds=Math.Abs(speed)<=Math.Max(0,limit-.15)
                ?belowLimitSeconds+Math.Clamp(dt,0,.1):0;
            if(belowLimitSeconds>=.3){speedLimitBraking=false;belowLimitSeconds=0;}
        }
        return speedLimitBraking;
    }
    private int stationTravelDirection;
    internal static bool ReadyToDepart(TrainStationComponent station,RailCouplingComponent train,double dwell,bool tram)
    {
        var stop=station.Parent.GetComponent<TramStopComponent>();
        // Station Departure is authoritative for all vehicles. A tram's old
        // timer is only a default for stops without configured departure rules.
        return station.ReadyToDepart(train,dwell,stop!=null?stop.Ready(dwell):tram&&dwell>=15);
    }
    internal double LimitStationTravel(double distance)=>distance*stationTravelDirection>0 && Math.Abs(distance)>stationTravel
        ? Math.CopySign(stationTravel,distance) : distance;
    internal (double Force, bool Brake) Control(VoxelRail rail, float t, int facing, double speed, double dt)
    {
        ServiceBrake=0;DriveEffort=0;
        stationTravel=double.PositiveInfinity;stationTravelDirection=0;
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Autopilot and stops/Control", this.Parent);
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        if(Parent.GetComponent<RailCouplingComponent>().RepairRequired)
        { ReportRepairRequired(rail,t,facing); return (0,true); }
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
        if(active && !RestoreDeparture())return (0,true);
        motion.EnsureDriveCommands();
        var coupling = this.Parent.GetComponent<RailCouplingComponent>();
        var train = coupling.Performance;
        var direction = facing * motion.ServerDirection;
        if(active && speedControlActive && !paused && targetSpeedKmH>0 && DateTime.UtcNow>=nextSpeedAdjustment)
        {
            var throttle=TargetThrottle(targetSpeedKmH,speed);
            if(Math.Abs(throttle-motion.ServerThrottle)>.05)
                motion.SetDriveCommands(throttle,motion.ServerDirection,false);
            nextSpeedAdjustment=DateTime.UtcNow.AddMilliseconds(500);
        }
        if(motion.CommandBrake)
        {
            // Braking suspends departure and searches, not validation of the
            // held stop or an en-route target and its incoming-traffic claim.
            if(active)
            {
                if(stoppedStation!=0)
                {if(!StationHoldCurrent(TrainStationComponent.Find(stoppedStation)))ReplanStationStop();}
                else ValidateBrakedTarget(rail,t,direction,train);
            }
            bufferWaitSeconds=0; this.Status=paused?"Autopilot paused":"Commanded brakes"; return (0,true);
        }
        if (this.departedStation != 0 && departedTrackTravel > train.Length + 3)
        { this.departedStation = 0; departedStationObject=Guid.Empty; departedTrackTravel=0; this.route.Clear(); journey=null; Parent.SetDirty(); }
        if (active && this.stoppedStation != 0)
        {
            var station = TrainStationComponent.Find(this.stoppedStation);
            if(station==null || !StationHoldCurrent(station))
            {
                // Replan from the train, without dispatching from a remote
                // station, reversing at its terminal or advancing its service.
                ReplanStationStop();
                return (0,true);
            }
            var dwell=(DateTime.UtcNow-this.arrived).TotalSeconds;
            var tramStop=station?.Parent.GetComponent<TramStopComponent>();
            var arrivedAtDiversion=station!=null&&station.Parent.ObjectID==conditionalDestination;
            var completingSchedule=station!=null && AtScheduledDestination(station);
            DepartureObservation? terminalObservation=coupling.Vehicle.RailSpec.Tram
                ?new(RailSimulationFrame.Revision,TrainStationComponent.RoutingRevision):null;
            var terminal=station!=null && coupling.Vehicle.RailSpec.Tram ? TerminalTramStation(station,rail,t,direction,train) : false;
            if(!StationHoldCurrent(station))
            {ReplanStationStop();return (0,true);}
            if(terminal==null){bufferWaitSeconds=0;Status="Track changed: replanning";return (0,true);}
            var turnaround=terminal==true;
            var departureDirection=turnaround?-direction:direction;
            var departureForward=Parent.Rotation.RotateVector(Vector3.UnitZ)*(turnaround?-motion.ServerDirection:motion.ServerDirection);
            var departureNose=coupling.ProjectedNose(departureForward);
            NormalDeparture? normalDeparture=null;
            var ready=station==null || ChooseStationDeparture(station,coupling,dwell,rail,t,departureDirection,departureNose,out normalDeparture,allowReverse:!turnaround,terminalObservation:terminalObservation);
            if(!StationHoldCurrent(station!))
            {ReplanStationStop();return (0,true);}
            if (ready)
            {
                // A rejected return search must retain the station hold before
                // dispatch, service progression or terminal reversal is applied.
                if(arrivedAtDiversion && station!.Parent.ObjectID==conditionalDestination
                    && !FinishDiversion(station,rail,t,departureDirection,departureNose,coupling,allowReverse:!turnaround,terminalObservation:terminalObservation))
                {
                    if(!StationHoldCurrent(station))ReplanStationStop();
                    else {bufferWaitSeconds=0;Status="Replanning scheduled return";}
                    return (0,true);
                }
                if(!StationHoldCurrent(station))
                {ReplanStationStop();return (0,true);}
                if(turnaround)
                {
                    motion.SetDriveCommands(motion.ServerThrottle,-motion.ServerDirection,false);
                    Reverse=motion.ServerDirection<0; this.Changed(nameof(Reverse)); Parent.SetDirty();
                }
                station?.NotifyVehicle(RailEvent.Dispatch,this.Parent.ObjectID);
                if(station!=null && (!arrivedAtDiversion || completingSchedule)) Parent.GetComponent<TramRouteComponent>()?.Departed(tramStop?.StopName??station.Parent.DisplayName.ToString());
                this.departedStation = this.stoppedStation; this.departedPosition = this.Parent.Position;
                departedStationObject=station!.Parent.ObjectID;departedTrackTravel=0;Parent.SetDirty();
                this.stoppedStation = 0; this.route.Clear(); journey=null;
                stoppedBinding=null;
                SetTargetStation(0,preserveReservation:true);
                if(normalDeparture is {} normal && conditionalDestination==Guid.Empty)
                    PublishNormalDeparture(normal,turnaround,rail,t,departureDirection,departureNose,coupling.Performance);
            }
            else { bufferWaitSeconds=0; this.Status = "Waiting at station"; return (0, true); }
        }
        direction=facing*motion.ServerDirection;
        var forward=Parent.Rotation.RotateVector(Vector3.UnitZ)*motion.ServerDirection;
        var nose=coupling.ProjectedNose(forward);
        var limit = train.SpeedLimit*coupling.MaintenanceSpeedFactor;
        if(coupling.Vehicle.RailSpec.Tram)
        {
            var cable=rail.Profile.Tram?TramCableDriveObject.PowerFor(rail.Cell,Parent.ID):0;
            if(!TramPowerRules.UsesCable(rail.Profile.Tram,cable))
            {
                // An outage suspends discovery, but an existing next stop must
                // still describe this train and network before claiming traffic.
                if(active)ValidateBrakedTarget(rail,t,direction,train);
                Status=rail.Profile.Tram ? "Waiting for track power: power a Tram Cable Drive and press Connect" : "Tram Rail required";
                return (0,true);
            }
            limit=Math.Min(limit,TramPowerRules.SpeedLimit(train.SpeedLimit,rail.Profile.Tram,cable));
        }
        var deceleration = train.BrakeDeceleration(Math.Max(0,-rail.Profile.Tangent(t).Y*direction));
        var stopping = train.StoppingDistance(speed, Math.Max(0, -rail.Profile.Tangent(t).Y * direction));
        var scanDistance = Math.Min(1500, Math.Max(80, stopping + train.Length + 25));
        if(active && EnsureDestination(rail,t,direction,nose,train)==null && conditionalDestination!=Guid.Empty)
        {this.Status="Replanning destination route";return (0,true);}
        var cached=PlanJourney(rail,t,direction,nose,train,active,scanDistance,out var cursor);
        if(cached==null){this.Status="Track changed: replanning";return (0,true);}
        if(active)SetTargetStation(cached.Plan.TargetStation(cursor,t,nose,departedStation));
        var waitingSwitch=active && !PrepareDestinationSwitches(cached.Plan,cursor,t,nose,scanDistance);
        // An occupied future encounter is a protected stopping point. Keep
        // enough motion to clear the previous passage of that same turnout;
        // the ordinary braking envelope and travel clamp guard the next one.
        // Access loss/replanning has no finite stopping point and holds now.
        if(waitingSwitch && double.IsPositiveInfinity(stationTravel)){stationTravelDirection=direction;return(0,true);}
        RailJourneyPlan.Safety safety;
        using(RailProfile.Measure("Rail Network/Autopilot and stops/Follow journey",Parent))
            safety=cached.Plan.Follow(cursor,t,nose,train,scanDistance,limit,deceleration,departedStation);
        limit=safety.SpeedLimit;deceleration=safety.Deceleration;
        var obstacle=safety.Obstacle;var targetBuffer=safety.Buffer;
        var targetStation=safety.Station==0?null:TrainStationComponent.Find(safety.Station);
        if(waitingSwitch && stationTravel<=obstacle){obstacle=stationTravel;targetStation=null;targetBuffer=false;}
        else waitingSwitch=false;
        if(active && (targetStation!=null || waitingSwitch)){stationTravel=Math.Min(stationTravel,Math.Max(0,obstacle));stationTravelDirection=direction;}
        var canTurn=active && coupling.Vehicle.RailSpec.Tram && targetBuffer && obstacle<.18
            && Math.Abs(speed)<.01 && !paused && motion.ServerThrottle>.001 && limit>0
            && this.Parent.GetComponent<RailConditionComponent>()?.RepairRequired==false;
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
                // Do not latch a station that moved after journey discovery.
                var binding=targetStation.BoundStop;
                if(binding is not {} stop || !TrackWorld.Contains(new(stop.Cell,stop.Profile))
                    || cached.Context.Stations!=TrainStationComponent.RoutingRevision
                    || cached.ValidatedRevision!=RailSimulationFrame.Revision)
                {journey=null;Status="Station changed: replanning";return (0,true);}
                this.stoppedStation = targetStation.Parent.ID; this.arrived = DateTime.UtcNow;
                stoppedBinding=stop;
                targetStation.NotifyVehicle(RailEvent.Arrive,this.Parent.ObjectID);
            }
            this.Status = waitingSwitch ? "Waiting for an occupied switch" : canTurn ? "Turning around at buffer stop"
                : targetStation == null ? "Stopped: track end or incompatible route" : "Arrived at station";
            return (0, true);
        }
        var stoppingLimit=Math.Sqrt(2*deceleration*Math.Max(0,obstacle-.08));
        limit=Math.Min(limit,stoppingLimit);
        if(active && speedControlActive) limit=Math.Min(limit,targetSpeedKmH/3.6+.15);
        if(speed*direction<-.05){this.Status="Braking";return(0,true);}
        if(limit>0 && safety.CurveSpeedLimit<=limit+.000001 && safety.CurveSpeedLimit<stoppingLimit-.000001)
        {
            // Full-brake hysteresis can consume all momentum before its 0.3s
            // release delay expires on a tight corner. Regulate its nonzero
            // speed with service brakes; retain full braking for protected stops.
            speedLimitBraking=false;belowLimitSeconds=0;
            if(Math.Abs(speed)>limit)
            {
                var travelGrade=RailGuidance.AxleTangent(rail,t,motion.NextRail,coupling.Vehicle.RailSpec.Wheelbase/2).Y*direction;
                ServiceBrake=CurveBraking.BrakeFraction(speed,limit,dt,deceleration,travelGrade,train.Mass,train.BrakingForce);
                Status="Slowing for curve";
                return(0,false);
            }
        }
        else if(SpeedLimitBrake(speed,limit,dt)){this.Status=waitingSwitch?"Braking for an occupied switch":"Braking";return(0,true);}
        if (this.Parent.GetComponent<RailConditionComponent>()?.RepairRequired==true) { this.Status = "Repair required"; return (0, true); }
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
        var grade=coupling.ConsistGrade(RailGuidance.AxleTangent(rail,t,motion.NextRail,coupling.Vehicle.RailSpec.Wheelbase/2).Y,facing)*direction;
        var force=train.DriveForce(speed,grade);
        var driveThrottle=motion.ServerThrottle;
        if(stationApproach)driveThrottle=Math.Min(driveThrottle,TargetThrottle(approachLimit*3.6,speed));
        if(coupling.Vehicle.RailSpec.Powered && active && speedControlActive && targetSpeedKmH>0 && grade>0)
        {
            // At a low target speed the ordinary speed-error throttle can be
            // below the effort needed just to hold the hill. Add that holding
            // effort within the existing throttle range, not beyond motor or
            // allocated-power limits. The motion solver applies cable power.
            var cable=coupling.Vehicle.RailSpec.Tram?TramCableDriveObject.PowerFor(rail.Cell,Parent.ID):1;
            driveThrottle=Math.Clamp(driveThrottle+train.Mass*9.80665*grade/Math.Max(1,force*cable),0,1);
        }
        // The safety lookahead can lower the permitted speed while the current
        // rail is still straight. Reduce powered effort before that bound instead
        // of accelerating into a repeated full-brake/release cycle at cab throttle.
        var speedHeadroom=Math.Clamp((limit-Math.Abs(speed))*.8,0,1);
        var holdingThrottle=force>0?train.Mass*9.80665*Math.Max(0,grade)/force:0;
        driveThrottle=Math.Min(driveThrottle,Math.Clamp(speedHeadroom+holdingThrottle,0,1));
        DriveEffort=driveThrottle;
        return (force * driveThrottle * direction, false);
    }
}

