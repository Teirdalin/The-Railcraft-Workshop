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
    private readonly Dictionary<Guid,RailCouplingComponent> trains=new();
    internal static CoasterStationComponent? At(RailCell cell){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/At"); return Stations.TryGetValue(cell,out var station)&&!station.Parent.IsDestroyed?station:null; }
    internal static bool ApproachBrake(VoxelRail rail,float t,double speed,RailCouplingComponent train)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/ApproachBrake");
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
                    if(station.dispatched.Contains(train.Leader().Parent.ObjectID))return false;
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
    internal (double Force,bool Brake) Control(RailCouplingComponent train,int facing,double speed)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Control", this.Parent);
        lock(gate)
        {
            if(loadingCart)return(0,true);
            var id=train.Leader().Parent.ObjectID;
            if(!arrivals.ContainsKey(id))
            {
                arrivals[id]=DateTime.UtcNow;trains[id]=train;
                Parent.GetComponent<TrainStationComponent>().NotifyVehicle(RailEvent.Enter,id);
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
                    RememberDeparture(train);
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
            return dispatched.Contains(id)?(Math.Abs(speed)<2?train.Performance.Mass*.7*facing:0,false):(0,true);
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
                if(pair.Value.Parent.IsDestroyed||!pair.Value.Group().Any(c=>c.Parent.GetComponent<MinecartMotionComponent>().BoundRailCell==cell))
                {
                    Parent.GetComponent<TrainStationComponent>().NotifyVehicle(RailEvent.Leave,pair.Key);
                    trains.Remove(pair.Key);arrivals.Remove(pair.Key);dispatched.Remove(pair.Key);securing.Remove(pair.Key);
                }
        }
    }
    public override void Destroy(){
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Destroy", this.Parent);
        Stations.TryRemove(Parent.GetComponent<CoasterRailComponent>().Rail.Cell,out _);base.Destroy();}
}
