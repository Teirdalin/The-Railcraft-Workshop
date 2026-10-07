using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;

namespace Eco.Minecarts.Runtime;

public sealed partial class TrainControllerComponent
{
    private (int Station,int Direction,long Revision,long Stations,bool Terminal)? terminalTram;
    private bool TerminalTramStation(TrainStationComponent station,VoxelRail rail,float t,int direction)
    {
        var revision=RailSimulationFrame.Revision;
        if(terminalTram is {} cached && cached.Station==station.Parent.ID && cached.Direction==direction && cached.Revision==revision && cached.Stations==TrainStationComponent.RoutingRevision)return cached.Terminal;
        var plan=RailJourneyPlan.Build(rail,direction,t,0,
            (part,exit)=>TrackWorld.Neighbor(part,exit),
            (part,travel)=>TrainStationComponent.At(RailPowerConnectionComponent.PhysicalCell(part.Cell))
                .Where(s=>s.Parent.ID!=station.Parent.ID).Select(s=>new RailJourneyPlan.Stop(s.Parent.ID,s.TrackT)).ToArray(),
            normalizeCell:RailPowerConnectionComponent.PhysicalCell);
        var last=plan.Nodes[^1].Rail;
        var terminal=!plan.Limited && !plan.EndsAtStation && plan.LoopStart<0 && last.Profile.Tram && last.Profile.Shape=="Stopper";
        terminalTram=(station.Parent.ID,direction,revision,TrainStationComponent.RoutingRevision,terminal);
        return terminal;
    }

    private readonly record struct JourneyContext(long Stations,long Tram,double MinimumRadius,bool Automatic,int CommandDirection,int DepartedStation,Guid Destination);
    private sealed record CachedJourney(JourneyContext Context,long ValidatedRevision,RailJourneyPlan Plan,HashSet<RailCell> Watched);
    private CachedJourney? journey;
    private JourneyContext JourneyContextFor(bool active,TrainPerformance train)=>new(TrainStationComponent.RoutingRevision,
        Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0,train.MinimumRadius,active,Parent.GetComponent<MinecartMotionComponent>().ServerDirection,departedStation,conditionalDestination);

    private CachedJourney? ValidJourney(JourneyContext context)
    {
        var cached=Volatile.Read(ref journey);
        if(cached==null || cached.Context!=context)return null;
        if(cached.ValidatedRevision==RailSimulationFrame.Revision)return cached;
        var changes=RailSimulationFrame.ChangesAffect(cached.ValidatedRevision,cached.Watched);
        if(changes.Affected)return null;
        var updated=cached with{ValidatedRevision=changes.Revision};
        Interlocked.CompareExchange(ref journey,updated,cached);
        return updated;
    }

    private CachedJourney? PlanJourney(VoxelRail rail,float t,int direction,double nose,TrainPerformance train,bool active,double horizon,out int cursor)
    {
        var context=JourneyContextFor(active,train);
        var cached=ValidJourney(context);
        if(cached!=null && cached.Plan.TryCursor(rail,direction,out cursor)
            && !cached.Plan.PassedStation(cursor,t,nose)
            && !(cached.Plan.Limited && cached.Plan.Length-cached.Plan.Position(cursor,t)<horizon+nose+2))return cached;
        cursor=0;
        using var profile=RailProfile.Measure("Rail Network/Autopilot and stops/Build journey",Parent);
        var revision=RailSimulationFrame.Revision;
        (VoxelRail Rail,int End)? Next(VoxelRail part,int exit)=>active && conditionalDestination!=Guid.Empty && destinationPath!=null
            ? destinationPath.Edges.TryGetValue((part,exit),out var edge)?edge:null
            : active?NextAutomatic(part,exit,train):TrackWorld.Neighbor(part,exit);
        RailJourneyPlan.Stop[] Stops(VoxelRail part,int travelDirection)
        {
            if(!active)return [];
            var stations=TrainStationComponent.At(RailPowerConnectionComponent.PhysicalCell(part.Cell));
            if(stations.Length==0)return [];
            return stations.Where(s=>s.Parent.ID!=departedStation && StationApplicable(s,travelDirection))
                .Select(s=>new RailJourneyPlan.Stop(s.Parent.ID,s.TrackT)).ToArray();
        }
        var plan=RailJourneyPlan.Build(rail,direction,t,nose,Next,Stops,normalizeCell:RailPowerConnectionComponent.PhysicalCell);
        var watched=plan.Nodes.Select(n=>RailPowerConnectionComponent.PhysicalCell(n.Rail.Cell)).ToHashSet();
        // Watch the endpoint's empty neighborhood as well: placing a repair or
        // extension beyond a cached track end must restart discovery.
        var last=plan.Nodes[^1];var endpoint=last.Rail.Point(1-last.Entry);
        var x=(int)MathF.Floor(endpoint.X+.5f);var y=(int)MathF.Floor(endpoint.Y+.5f);var z=(int)MathF.Floor(endpoint.Z+.5f);
        for(var dx=-2;dx<=2;dx++)for(var dy=-2;dy<=2;dy++)for(var dz=-2;dz<=2;dz++)
            watched.Add(RailPowerConnectionComponent.PhysicalCell(new(x+dx,y+dy,z+dz)));
        if(revision!=RailSimulationFrame.Revision || context!=JourneyContextFor(active,train))return null;
        cached=new(context,revision,plan,watched);
        Volatile.Write(ref journey,cached);
        return cached;
    }
}
