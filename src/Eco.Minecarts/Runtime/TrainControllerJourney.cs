using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;

namespace Eco.Minecarts.Runtime;

public sealed partial class TrainControllerComponent
{
    // The terminal decision is made before player-authored conditions run.
    // Later discovery must not bless that decision with a newer network.
    private readonly record struct DepartureObservation(long Revision,long Stations)
    {
        public bool IsCurrent=>Revision==RailSimulationFrame.Revision && Stations==TrainStationComponent.RoutingRevision;
    }
    private (int Station,int Direction,double MinimumRadius,long Revision,long Stations,bool Terminal)? terminalTram;
    private bool? TerminalTramStation(TrainStationComponent station,VoxelRail rail,float t,int direction,TrainPerformance train)
    {
        var revision=RailSimulationFrame.Revision;
        var stations=TrainStationComponent.RoutingRevision;
        if(!rail.Profile.Tram || !train.Fits(rail.Profile.Radius))return false;
        if(terminalTram is {} cached && cached.Station==station.Parent.ID && cached.Direction==direction && cached.MinimumRadius==train.MinimumRadius
            && cached.Revision==revision && cached.Stations==stations)return cached.Terminal;
        var plan=RailJourneyPlan.Build(rail,direction,t,0,
            // A buffer beyond unusable track cannot authorize reversal here.
            // Keep physical turnout selection, but stop at the same track
            // family and radius boundaries as automatic vehicle traversal.
            (part,exit)=>TrackWorld.Neighbor(part,exit) is {} next && next.Rail.Profile.Tram && train.Fits(next.Rail.Profile.Radius)?next:null,
            (part,travel)=>TrainStationComponent.At(RailPowerConnectionComponent.PhysicalCell(part.Cell))
                .Where(s=>s.Parent.ID!=station.Parent.ID && s.MatchesTrack(part)).Select(s=>new RailJourneyPlan.Stop(s.Parent.ID,s.TrackT)).ToArray(),
            normalizeCell:RailPowerConnectionComponent.PhysicalCell);
        var last=plan.Nodes[^1].Rail;
        var terminal=!plan.Limited && !plan.EndsAtStation && plan.LoopStart<0 && last.Profile.Tram && last.Profile.Shape=="Stopper";
        // The scan may already have passed a newly bound station. Do not
        // stamp that old answer with its new revision or dispatch across an
        // interrupted topology observation. The stopped controller retries.
        if(revision!=RailSimulationFrame.Revision || stations!=TrainStationComponent.RoutingRevision)return null;
        terminalTram=(station.Parent.ID,direction,train.MinimumRadius,revision,stations,terminal);
        return terminal;
    }

    private readonly record struct JourneyContext(long Stations,long Tram,double MinimumRadius,bool Automatic,int CommandDirection,int DepartedStation,Guid Destination);
    private sealed record CachedJourney(JourneyContext Context,long ValidatedRevision,RailJourneyPlan Plan,HashSet<RailCell> Watched);
    private sealed record NormalDeparture(Guid Station,CachedJourney Journey,long TramAfterDeparture);
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

    private void ValidateBrakedTarget(VoxelRail rail,float t,int direction,TrainPerformance train)
    {
        if(targetStationId==0)return;
        var cached=ValidJourney(JourneyContextFor(true,train));
        var forward=Parent.Rotation.RotateVector(System.Numerics.Vector3.UnitZ)*Parent.GetComponent<MinecartMotionComponent>().ServerDirection;
        var nose=Parent.GetComponent<RailCouplingComponent>().ProjectedNose(forward);
        if(cached!=null && cached.Plan.TryCursor(rail,direction,out var cursor)
            && !cached.Plan.PassedStation(cursor,t,nose))return;
        // Keep braking cheap: withdraw an obsolete normal-route observation
        // and its inbound claim, then discover the next stop on resume. An
        // intentional diversion/return remains owned by the destination code.
        journey=null;route.Clear();SetTargetStation(0);
    }

    private void PublishNormalDeparture(NormalDeparture departure,bool turnaround,VoxelRail rail,float t,int direction,double nose,TrainPerformance train)
    {
        // Carry the selected path through only the dispatch's expected changes.
        // A route edit must never be accepted as ordinary service progression.
        var context=departure.Journey.Context;
        journey=departure.Journey with{Context=context with{Tram=departure.TramAfterDeparture,
            CommandDirection=context.CommandDirection*(turnaround?-1:1),DepartedStation=departedStation}};
        var cached=ValidJourney(JourneyContextFor(true,train));
        if(cached!=null && cached.Plan.TryCursor(rail,direction,out var cursor)
            && !cached.Plan.PassedStation(cursor,t,nose)
            && TrainStationComponent.Find(cached.Plan.TargetStation(cursor,t,nose,departedStation))?.Parent.ObjectID==departure.Station)
        {SetTargetStation(cached.Plan.TargetStation(cursor,t,nose,departedStation));return;}
        journey=null;route.Clear();SetTargetStation(0);
    }

    private static HashSet<RailCell> WatchJourney(RailJourneyPlan plan)
    {
        var watched=plan.Nodes.Select(n=>RailPowerConnectionComponent.PhysicalCell(n.Rail.Cell)).ToHashSet();
        // A repair or extension beyond a cached track end restarts discovery.
        var last=plan.Nodes[^1];var endpoint=last.Rail.Point(1-last.Entry);
        var x=(int)MathF.Floor(endpoint.X+.5f);var y=(int)MathF.Floor(endpoint.Y+.5f);var z=(int)MathF.Floor(endpoint.Z+.5f);
        for(var dx=-2;dx<=2;dx++)for(var dy=-2;dy<=2;dy++)for(var dz=-2;dz<=2;dz++)
            watched.Add(RailPowerConnectionComponent.PhysicalCell(new(x+dx,y+dy,z+dz)));
        return watched;
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
            ? DestinationEdge(destinationPath,part,exit)
            : active?NextAutomatic(part,exit,train):TrackWorld.Neighbor(part,exit);
        RailJourneyPlan.Stop[] Stops(VoxelRail part,int travelDirection)
        {
            if(!active)return [];
            var stations=TrainStationComponent.At(RailPowerConnectionComponent.PhysicalCell(part.Cell));
            if(stations.Length==0)return [];
            return stations.Where(s=>s.Parent.ID!=departedStation && s.MatchesTrack(part) && StationApplicable(s,travelDirection))
                .Select(s=>new RailJourneyPlan.Stop(s.Parent.ID,s.TrackT)).ToArray();
        }
        var plan=RailJourneyPlan.Build(rail,direction,t,nose,Next,Stops,normalizeCell:RailPowerConnectionComponent.PhysicalCell);
        var watched=WatchJourney(plan);
        if(revision!=RailSimulationFrame.Revision || context!=JourneyContextFor(active,train))return null;
        cached=new(context,revision,plan,watched);
        Volatile.Write(ref journey,cached);
        return cached;
    }
}
