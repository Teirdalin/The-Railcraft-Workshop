using System.Collections.Concurrent;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized,NoIcon,AutogenClass,LocDisplayName("Coaster Loading")]
public sealed partial class CoasterStationComponent:WorldObjectComponent
{
    private static readonly ConcurrentDictionary<RailCell,CoasterStationComponent> Stations=new();
    private readonly object gate=new();
    private readonly Dictionary<Guid,DateTime> arrivals=new();
    private readonly HashSet<Guid> dispatched=new();
    private string? signText;
    private void PublishSignText()
    {
        var text=Parent.GetComponent<Eco.Gameplay.Components.CustomTextComponent>()?.TextData?.Text??"";
        if(signText==text)return;
        Parent.SetAnimatedState("StationSignText",text);signText=text;
    }
    private readonly Dictionary<Guid,DateTime> securing=new();
    internal const double RestraintCloseSeconds=1.0;
    internal static CoasterStationComponent? ForHome(Guid id)=>Stations.Values.FirstOrDefault(s=>s.Parent.ObjectID==id&&!s.Parent.IsDestroyed);
    private sealed record DockingTrain(RailCouplingComponent Front,int Direction);
    private readonly Dictionary<Guid,DockingTrain> trains=new();
    private static readonly ConcurrentDictionary<Guid,CoasterStationComponent> ActiveStations=new();
    private static Guid TrainId(RailCouplingComponent train)=>train.Group().MinBy(c=>c.Parent.ID)!.Parent.ObjectID;
    internal static CoasterStationComponent? At(RailCell cell){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/At"); return Stations.TryGetValue(cell,out var station)&&!station.Parent.IsDestroyed?station:null; }
    internal static bool ApproachBrake(VoxelRail rail,float t,double speed,RailCouplingComponent train)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/ApproachBrake");
        if(Stations.IsEmpty)return false;
        var rootFacing=train.Parent.GetComponent<MinecartMotionComponent>().BoundConsistPose?.Facing??1;
        var front=train.LeadingEnd((speed<0?-1:1)*rootFacing);
        if(train.GuidedMemberPose(front.Car) is not {} pose)return false;
        rail=pose.Rail;t=pose.T;
        speed*=rootFacing*train.FacingRelativeTo(front.Car)*pose.Facing;
        if(Math.Abs(speed)<.05)return false;
        var direction=speed<0?0:1;
        var distance=direction==0?t*rail.Profile.Length:(1-t)*rail.Profile.Length;
        var deceleration=train.Performance.BrakeDeceleration(1);
        var range=speed*speed/(2*deceleration)+4;
        var seen=new HashSet<RailCell>{rail.Cell};
        for(var i=0;i<64&&distance<=range;i++)
        {
            var next=TrackWorld.NeighborForVehicle(rail,direction);
            if(next==null||!seen.Add(next.Value.Rail.Cell))return false;
            rail=next.Value.Rail;direction=1-next.Value.End;
            if(At(rail.Cell) is {} station)
            {
                lock(station.gate)
                    if(station.dispatched.Contains(TrainId(train)))return false;
                // Aim to stop inside the loading segment, not before its socket.
                return speed*speed>2*deceleration*(distance+rail.Profile.Length*.5);
            }
            distance+=rail.Profile.Length;
        }
        return false;
    }
    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/PostInitialize", this.Parent);
        base.PostInitialize();
        CartReturnStatus=ReturnedStations.ContainsKey(Parent.ObjectID)?"Used; available after the next server restart":"Available once per server restart";
        PublishSignText();
        ((Eco.Mods.TechTree.CoasterStationObject)Parent).CompactLegacyFootprint();
        Stations[Parent.GetComponent<CoasterRailComponent>().Rail.Cell]=this;
    }
    internal static bool ControlsTrain(RailCouplingComponent train)
    {
        if(Stations.IsEmpty)return false;
        if(ActiveStations.ContainsKey(TrainId(train)))return true;
        var facing=train.Parent.GetComponent<MinecartMotionComponent>().BoundConsistPose?.Facing??1;
        return train.LeadingEnd(facing).Car.Parent.GetComponent<MinecartMotionComponent>().BoundRailCell is {} cell&&At(cell)!=null;
    }
    internal static bool TryControl(RailCouplingComponent train,int facing,double speed,out (double Force,bool Brake) input)
    {
        if(Stations.IsEmpty){input=default;return false;}
        var id=TrainId(train);
        var front=train.LeadingEnd((speed<0?-1:1)*facing);
        var station=ActiveStations.GetValueOrDefault(id);
        if(station==null&&train.GuidedMemberPose(front.Car) is {} pose)station=At(pose.Rail.Cell);
        if(station==null||station.Parent.IsDestroyed){input=default;return false;}
        input=station.Control(train,facing,speed,front);return true;
    }
    internal double LimitTravel(RailCouplingComponent train,int facing,double travel)
    {
        lock(gate)
        {
            var id=TrainId(train);
            if(dispatched.Contains(id)||!trains.TryGetValue(id,out var dock)
                ||train.GuidedMemberPose(dock.Front) is not {} pose
                ||pose.Rail.Cell!=Parent.GetComponent<CoasterRailComponent>().Rail.Cell)return travel;
            var direction=dock.Direction*dock.Front.FacingRelativeTo(train)*facing;
            if(travel*direction<=0)return travel;
            var remaining=(.5-pose.T)*pose.Rail.Profile.Length*pose.Facing*dock.Direction;
            return direction*Math.Min(Math.Abs(travel),Math.Max(0,remaining));
        }
    }
    internal static double LimitStationTravel(RailCouplingComponent train,int facing,double travel)=>ActiveStations.IsEmpty?travel:
        ActiveStations.GetValueOrDefault(TrainId(train))?.LimitTravel(train,facing,travel)??travel;
    private (double Force,bool Brake) Control(RailCouplingComponent train,int facing,double speed,
        (RailCouplingComponent Car,int Direction) leading)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Control", this.Parent);
        lock(gate)
        {
            if(loadingCart)return(0,true);
            var id=TrainId(train);
            if(!trains.TryGetValue(id,out var dock))
            {
                trains[id]=dock=new(leading.Car,leading.Direction);
                ActiveStations[id]=this;
                Parent.GetComponent<TrainStationComponent>().NotifyVehicle(RailEvent.Enter,id);
            }
            var driveDirection=dock.Direction*dock.Front.FacingRelativeTo(train)*facing;
            // Crawl to the centre using the normal force integrator. Only the
            // leading car's contact defines docking; the occupied car may be
            // several rail pieces behind it. The travel limit prevents overrun.
            if(!dispatched.Contains(id)&&train.GuidedMemberPose(dock.Front) is {} pose
                &&pose.Rail.Cell==Parent.GetComponent<CoasterRailComponent>().Rail.Cell)
            {
                var remaining=(.5-pose.T)*pose.Rail.Profile.Length*pose.Facing*dock.Direction;
                if(remaining>.001)
                {
                    var deceleration=train.Performance.BrakeDeceleration(1);
                    if(speed*speed>=2*deceleration*remaining)return(0,true);
                    var target=Math.Min(.6,Math.Sqrt(2*deceleration*remaining));
                    return(train.Performance.Mass*Math.Clamp((target-speed*driveDirection)*1.5,0,.7)*driveDirection,false);
                }
            }
            if(!arrivals.ContainsKey(id))
            {
                arrivals[id]=DateTime.UtcNow;
                Parent.GetComponent<TrainStationComponent>().NotifyVehicle(RailEvent.Arrive,id);
            }
            if(!dispatched.Contains(id)&&Math.Abs(speed)<.05
                &&Parent.GetComponent<TrainStationComponent>().ReadyToDepart(train,(DateTime.UtcNow-arrivals[id]).TotalSeconds))
            {
                if(!securing.TryGetValue(id,out var begun))securing[id]=begun=DateTime.UtcNow;
                foreach(var car in train.Group())((RailVehicleObject)car.Parent).SetRestraints(true);
                if((DateTime.UtcNow-begun).TotalSeconds>=RestraintCloseSeconds)
                {
                    dispatched.Add(id);
                    RememberDeparture(dock.Front,dock.Direction);
                    Parent.GetComponent<TrainStationComponent>().NotifyVehicle(RailEvent.Dispatch,id);
                }
            }
            else if(!dispatched.Contains(id))
            {
                securing.Remove(id);
                if(Math.Abs(speed)<.05)foreach(var car in train.Group())((RailVehicleObject)car.Parent).SetRestraints(false);
            }
            // Station-only rolling drive starts a dispatched train. Hills and
            // loops outside this station have no artificial propulsion.
            return dispatched.Contains(id)?(Math.Abs(speed)<2?train.Performance.Mass*.7*driveDirection:0,false):(0,true);
        }
    }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Tick", this.Parent);
        base.Tick();
        PublishSignText();
        lock(gate)
        {
            var cell=Parent.GetComponent<CoasterRailComponent>().Rail.Cell;
            foreach(var pair in trains.ToArray())
                if(pair.Value.Front.Parent.IsDestroyed||TrainId(pair.Value.Front)!=pair.Key||!SpansStation(pair.Value,cell))
                {
                    Parent.GetComponent<TrainStationComponent>().NotifyVehicle(RailEvent.Leave,pair.Key);
                    trains.Remove(pair.Key);arrivals.Remove(pair.Key);dispatched.Remove(pair.Key);securing.Remove(pair.Key);
                    ActiveStations.TryRemove(new KeyValuePair<Guid,CoasterStationComponent>(pair.Key,this));
                }
        }
    }
    public override void Destroy(){
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Destroy", this.Parent);
        foreach(var pair in ActiveStations.Where(p=>p.Value==this))ActiveStations.TryRemove(pair);
        Stations.TryRemove(Parent.GetComponent<CoasterRailComponent>().Rail.Cell,out _);base.Destroy();}

    private static bool SpansStation(DockingTrain dock,RailCell cell)
    {
        // A short station can fall between two car centres. Keep its dispatch
        // state until the whole consist has passed, rather than parking the
        // next car as a new arrival during that gap.
        var group=dock.Front.Group();
        var range=group.Sum(c=>c.Vehicle.CouplerOffset*2+.10f)+1;
        foreach(var car in group)
        {
            if(car.Parent.GetComponent<MinecartMotionComponent>().BoundConsistPose is not {} pose)continue;
            var rail=pose.Rail;var t=pose.T;
            var direction=dock.Direction*dock.Front.FacingRelativeTo(car)*pose.Facing;
            var distance=0d;var seen=new HashSet<RailCell>();
            while(distance<=range&&seen.Add(rail.Cell))
            {
                if(rail.Cell==cell)return true;
                distance+=(direction>0?1-t:t)*rail.Profile.Length;
                if(distance>range||TrackWorld.NeighborForVehicle(rail,direction>0?1:0) is not {} next)break;
                rail=next.Rail;t=next.End;direction=next.End==0?1:-1;
            }
        }
        return false;
    }
}
