using System.Collections.Concurrent;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized,NoIcon,LocDisplayName("Coaster Loading")]
public sealed class CoasterStationComponent:WorldObjectComponent
{
    private static readonly ConcurrentDictionary<RailCell,CoasterStationComponent> Stations=new();
    private readonly object gate=new();
    private readonly Dictionary<Guid,DateTime> arrivals=new();
    private readonly HashSet<Guid> dispatched=new();
    private readonly Dictionary<Guid,RailCouplingComponent> trains=new();
    internal static CoasterStationComponent? At(RailCell cell)=>Stations.TryGetValue(cell,out var station)&&!station.Parent.IsDestroyed?station:null;
    internal static bool ApproachBrake(VoxelRail rail,float t,double speed,RailCouplingComponent train)
    {
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
                return speed*speed>2*deceleration*(distance+1.5);
            }
            distance+=rail.Profile.Length;
        }
        return false;
    }
    public override void PostInitialize(){base.PostInitialize();Stations[Parent.GetComponent<CoasterRailComponent>().Rail.Cell]=this;}
    internal (double Force,bool Brake) Control(RailCouplingComponent train,int facing,double speed)
    {
        lock(gate)
        {
            var id=train.Leader().Parent.ObjectID;
            if(!arrivals.ContainsKey(id))
            {
                arrivals[id]=DateTime.UtcNow;trains[id]=train;
                Parent.GetComponent<RailAutomationComponent>().Emit(RailEvent.Enter,id);
                Parent.GetComponent<RailAutomationComponent>().Emit(RailEvent.Arrive,id);
            }
            if(!dispatched.Contains(id)&&Math.Abs(speed)<.05
                &&Parent.GetComponent<TrainStationComponent>().Ready(train,(DateTime.UtcNow-arrivals[id]).TotalSeconds))
            {
                dispatched.Add(id);
                Parent.GetComponent<RailAutomationComponent>().Emit(RailEvent.Dispatch,id);
            }
            // Station-only rolling drive starts a dispatched train. Hills and
            // loops outside this station have no artificial propulsion.
            return dispatched.Contains(id)?(Math.Abs(speed)<2?train.Performance.Mass*.7*facing:0,false):(0,true);
        }
    }
    public override void Tick()
    {
        base.Tick(); lock(gate)
        {
            var cell=Parent.GetComponent<CoasterRailComponent>().Rail.Cell;
            foreach(var pair in trains.ToArray())
                if(pair.Value.Parent.IsDestroyed||!pair.Value.Group().Any(c=>c.Parent.GetComponent<MinecartMotionComponent>().BoundRailCell==cell))
                {
                    Parent.GetComponent<RailAutomationComponent>().Emit(RailEvent.Leave,pair.Key);
                    trains.Remove(pair.Key);arrivals.Remove(pair.Key);dispatched.Remove(pair.Key);
                }
        }
    }
    public override void Destroy(){Stations.TryRemove(Parent.GetComponent<CoasterRailComponent>().Rail.Cell,out _);base.Destroy();}
}
