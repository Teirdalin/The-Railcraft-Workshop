using Eco.Gameplay.Players;
using Eco.Core.Controller;
using Eco.Shared.Localization;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Items;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

public sealed partial class TrainControllerComponent
{
    [Serialized] private Guid conditionalDestination;
    [Serialized] private int destinationAuthor;
    [Serialized] private Guid resumeDestination;
    [Serialized] private bool returningToSchedule;
    private sealed record DestinationPath(Guid Destination,int Author,double MinimumRadius,long StationsRevision,long TramRevision,long Revision,RailDestinationSearch.Step[] Steps,
        Dictionary<(VoxelRail Rail,int Exit),(VoxelRail Rail,int End)> Edges,HashSet<RailCell> Watched,HashSet<RailDestinationSearch.Step> Cursors);
    private DestinationPath? destinationPath;
    // Route identity is physical; an edge is returned in the querying axle's
    // world image. Keep the relative displacement across a seam instead of
    // folding the target independently and stretching the connection by a lap.
    private static VoxelRail DestinationRail(VoxelRail rail)=>new(RailPowerConnectionComponent.PhysicalCell(rail.Cell),rail.Profile);
    private static (VoxelRail Rail,int End)? DestinationEdge(DestinationPath path,VoxelRail rail,int exit)
    {
        var physical=DestinationRail(rail);
        if(!path.Edges.TryGetValue((physical,exit),out var edge))return null;
        var cell=edge.Rail.Cell;var source=physical.Cell;var image=rail.Cell;
        return(new(new(cell.X+image.X-source.X,cell.Y+image.Y-source.Y,cell.Z+image.Z-source.Z),edge.Rail.Profile),edge.End);
    }
    private DateTime nextDepartureEvaluation;
    private int evaluatedStation,evaluatedSettings;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Destination Status")] public string DestinationStatus {get;private set;}="Normal route";
    private void DestinationMessage(string value){if(DestinationStatus==value)return;DestinationStatus=value;this.Changed(nameof(DestinationStatus));}
    private void ClearDestination(string reason,bool clearSchedule=false)
    {
        conditionalDestination=Guid.Empty;destinationAuthor=0;destinationPath=null;journey=null;route.Clear();
        reservedTrafficStation=Guid.Empty;
        returningToSchedule=false;if(clearSchedule)resumeDestination=Guid.Empty;
        UpdateStationIntent();
        DestinationMessage(reason);Parent.SetDirty();
    }
    // A temporary destination can already be the next scheduled stop. Its
    // dispatch completes that stop without requiring a separate return leg.
    private bool AtScheduledDestination(TrainStationComponent station)=>station.Parent.ObjectID==conditionalDestination
        && (returningToSchedule || station.Parent.ObjectID==resumeDestination);
    private DestinationPath? FindDestination(VoxelRail rail,float t,int direction,double nose,TrainPerformance train,TrainStationComponent target,int authorId)
    {
        using var profile=RailProfile.Measure("Rail Network/Autopilot and stops/Find destination",Parent);
        var author=UserManager.Users.FirstOrDefault(u=>u.Id==authorId);if(author==null)return null;
        var spec=((RailVehicleObject)Parent).RailSpec;var revision=RailSimulationFrame.Revision;
        var stationsRevision=TrainStationComponent.RoutingRevision;
        var tramRevision=Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0;
        IEnumerable<(VoxelRail Rail,int End)> Neighbors(VoxelRail part,int exit)
        {
            var emitted=new HashSet<(VoxelRail,int)>();
            foreach(var nearby in TrackWorld.Near(part.Point(exit),spec.Coaster))
            {
                if(RailPowerConnectionComponent.PhysicalCell(nearby.Cell)==RailPowerConnectionComponent.PhysicalCell(part.Cell))continue;
                var points=RailSwitchComponent.At(RailPowerConnectionComponent.PhysicalCell(nearby.Cell));
                foreach(var candidate in TrackWorld.NetworkPaths(nearby))
                {
                    if(candidate.Profile.Industrial!=spec.Industrial || candidate.Profile.Coaster!=spec.Coaster || spec.Tram&&!candidate.Profile.Tram)continue;
                    if(points!=null&&candidate.Profile.SwitchRoute!=points.SelectedRouteValue&&!points.Parent.IsAuthorized(author,AccessType.FullAccess))continue;
                    for(var entry=0;entry<2;entry++)if(part.Connects(exit,candidate,entry)&&emitted.Add((candidate,entry)))yield return(candidate,entry);
                }
            }
        }
        float? Goal(VoxelRail part,int travelDirection)
        {
            if(!target.MatchesTrack(part))return null;
            if(spec.Tram && target.Parent.GetComponent<TramStopComponent>() is {} stop
                && !stop.Compatible(Parent.GetComponent<TramRouteComponent>(),travelDirection))return null;
            return target.TrackT;
        }
        var steps=RailDestinationSearch.Find(rail,direction,t,nose,train,Neighbors,Goal,RailPowerConnectionComponent.PhysicalCell);
        // A line/stop edit during the search must not publish an already-stale path.
        if(steps==null || revision!=RailSimulationFrame.Revision || stationsRevision!=TrainStationComponent.RoutingRevision
            || tramRevision!=(Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0))return null;
        var edges=new Dictionary<(VoxelRail,int),(VoxelRail,int)>();
        for(var i=0;i+1<steps.Length;i++)
        {
            var source=steps[i].Rail;var physical=DestinationRail(source);var next=steps[i+1];var cell=next.Rail.Cell;
            edges[(physical,1-steps[i].Entry)]=(new(new(cell.X+physical.Cell.X-source.Cell.X,
                cell.Y+physical.Cell.Y-source.Cell.Y,cell.Z+physical.Cell.Z-source.Cell.Z),next.Rail.Profile),next.Entry);
        }
        return new(target.Parent.ObjectID,authorId,train.MinimumRadius,stationsRevision,tramRevision,revision,steps,edges,
            steps.Select(s=>RailPowerConnectionComponent.PhysicalCell(s.Rail.Cell)).ToHashSet(),steps.Select(s=>new RailDestinationSearch.Step(DestinationRail(s.Rail),s.Entry)).ToHashSet());
    }
    // Discover the route after this dispatch without advancing the saved cursor.
    // Fork scoring and stop discovery must use the same prospective schedule.
    private sealed record DepartureService(int Station,TramRouteComponent Tram,string? NextStop,long Revision);
    private static bool DepartureStopApplies(TrainStationComponent station,int direction,DepartureService service)
        =>station.Parent.ID!=service.Station && !station.Parent.IsDestroyed && service.Tram.Servicing
            && (service.NextStop==null || string.Equals(station.StationName,service.NextStop,StringComparison.OrdinalIgnoreCase))
            && (station.Parent.GetComponent<TramStopComponent>()?.Compatible(service.Tram,direction)??true);
    private NormalDeparture? NormalNextStation(TrainStationComponent current,VoxelRail rail,float t,int direction,double nose,TrainPerformance train)
    {
        var context=JourneyContextFor(true,train);var revision=RailSimulationFrame.Revision;
        var tram=Parent.GetComponent<TramRouteComponent>();var tramAfterDeparture=0L;
        var nextName=tram?.NextAfterDeparture(current.StationName,out tramAfterDeparture);
        var service=tram==null?null:new DepartureService(current.Parent.ID,tram,nextName,tramAfterDeparture);
        RailJourneyPlan.Stop[] Stops(VoxelRail part,int travelDirection)=>TrainStationComponent.At(RailPowerConnectionComponent.PhysicalCell(part.Cell))
            .Where(s=>s!=current && s.MatchesTrack(part) && (service==null || DepartureStopApplies(s,travelDirection,service)))
            .Select(s=>new RailJourneyPlan.Stop(s.Parent.ID,s.TrackT)).ToArray();
        var plan=RailJourneyPlan.Build(rail,direction,t,nose,(part,exit)=>NextAutomaticRoute(part,exit,train,service),Stops,normalizeCell:RailPowerConnectionComponent.PhysicalCell);
        var watched=WatchJourney(plan);
        if(revision!=RailSimulationFrame.Revision || context!=JourneyContextFor(true,train))return null;
        return new(TrainStationComponent.Find(plan.TargetStation(0,t,nose,0))?.Parent.ObjectID??Guid.Empty,
            new(context,revision,plan,watched),tramAfterDeparture);
    }
    private readonly record struct DepartureChoice(DestinationPath? Path,bool Reverse,bool Retry);
    private DepartureChoice DeparturePath(VoxelRail rail,float t,int direction,double nose,RailCouplingComponent train,TrainStationComponent target,int author,bool allowReverse=true)
    {
        var revision=RailSimulationFrame.Revision;
        var stationsRevision=TrainStationComponent.RoutingRevision;
        var tramRevision=Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0;
        bool Changed()=>revision!=RailSimulationFrame.Revision || stationsRevision!=TrainStationComponent.RoutingRevision
            || tramRevision!=(Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0);
        var path=FindDestination(rail,t,direction,nose,train.Performance,target,author);
        // An invalidated forward search is not proof that reversing or a
        // lower-priority rule is appropriate. Retry the same departure later.
        if(Changed())return new(null,false,true);
        if(path!=null || !allowReverse)return new(path,false,false);
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        // Reversing is safe only at a stopped station, never during travel.
        if(motion.CurrentRailVelocity.Length()>.01f)return new(null,false,false);
        var backward=Parent.Rotation.RotateVector(System.Numerics.Vector3.UnitZ)*-motion.ServerDirection;
        var rear=train.ProjectedNose(backward);
        path=FindDestination(rail,t,-direction,rear,train.Performance,target,author);
        if(Changed())return new(null,false,true);
        // Discovery has no drive side effects: the caller must first accept
        // its station settings and departure stage before changing direction.
        return new(path,path!=null,false);
    }
    private void ApplyDepartureDirection(DepartureChoice choice)
    {
        if(!choice.Reverse)return;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        motion.SetDriveCommands(motion.ServerThrottle,-motion.ServerDirection,false);
        Reverse=motion.ServerDirection<0;this.Changed(nameof(Reverse));Parent.SetDirty();
    }
    private bool ChooseStationDeparture(TrainStationComponent station,RailCouplingComponent coupling,double dwell,VoxelRail rail,float t,int direction,double nose,out NormalDeparture? normalDeparture,bool allowReverse=true,DepartureObservation? terminalObservation=null)
    {
        normalDeparture=null;
        if(!StationHoldCurrent(station))return false;
        // Evaluate passenger/cargo/fuel conditions once per second while held,
        // rather than repeating UI expressions at every 20 Hz motion update.
        var settings=station.SettingsRevision;
        if(evaluatedStation==station.Parent.ID&&evaluatedSettings==settings&&DateTime.UtcNow<nextDepartureEvaluation)return false;
        evaluatedStation=station.Parent.ID;evaluatedSettings=settings;nextDepartureEvaluation=DateTime.UtcNow.AddSeconds(1);
        var messages=new List<string>();
        var matches=station.MatchingDestinations(coupling,dwell,out var observation);
        if(!StationHoldCurrent(station))return false;
        // An invalidated sample is not an empty rule match. Hold rather than
        // falling through to a newly edited normal departure in this update.
        if(settings!=station.SettingsRevision)
        {station.ReportRouting("Station settings changed; retrying departure");return false;}
        if(terminalObservation is {IsCurrent:false})
        {station.ReportRouting("Route changed; retrying departure");return false;}
        if(observation is {IsCurrent:false})
        {station.ReportRouting("Train or passengers changed; retrying departure");return false;}
        foreach(var rule in matches)
        {
            var author=UserManager.Users.FirstOrDefault(u=>u.Id==rule.Author);
            var target=TrainStationComponent.Find(rule.Destination);
            if(author==null || !station.Parent.IsAuthorized(author,AccessType.FullAccess)){messages.Add("Rule author no longer has station access");continue;}
            // A temporary registration gap is not an unreachable rule. Keep
            // its priority and physical station hold until discovery completes.
            if(target!=station && TrainStationComponent.RegistrationPending(rule.Destination,target))
            {station.ReportRouting("Waiting for conditional destination station to finish loading or moving");return false;}
            if(target==null){messages.Add("Destination was removed or not selected");continue;}
            if(target==station){messages.Add("Destination is this station");continue;}
            var revision=RailSimulationFrame.Revision;
            var networkRevision=RailSimulationFrame.NetworkRevision;
            var stationsRevision=TrainStationComponent.RoutingRevision;
            var tramRevision=Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0;
            var choice=DeparturePath(rail,t,direction,nose,coupling,target,rule.Author,allowReverse);
            // A saved return belongs only to an active diversion. Older saves
            // can retain an orphan after cancellation; capture today's route.
            // Failed candidates need no scheduled-return discovery. Sample it
            // only for a reachable choice, under the same revision checks.
            var normal=choice.Path==null?Guid.Empty:conditionalDestination==Guid.Empty || AtScheduledDestination(station)
                ?NormalNextStation(station,rail,t,direction,nose,coupling.Performance)?.Station??Guid.Empty:resumeDestination;
            if(!StationHoldCurrent(station))return false;
            if(settings!=station.SettingsRevision)
            {station.ReportRouting("Station settings changed; retrying departure");return false;}
            // Capture and departure discovery must describe the same network.
            // A coherent later search cannot validate an obsolete return stop.
            if(terminalObservation is {IsCurrent:false} || choice.Retry || revision!=RailSimulationFrame.Revision || stationsRevision!=TrainStationComponent.RoutingRevision
                || tramRevision!=(Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0))
            {station.ReportRouting("Route changed; retrying departure to "+target.StationName);return false;}
            if(choice.Path is not {} path){messages.Add(target.StationName+": no compatible forward route or switch access");continue;}
            // Character/count conditions were sampled before route discovery.
            // Do not apply their result after boarding or consist changes.
            if(observation is {IsCurrent:false})
            {station.ReportRouting("Train or passengers changed; retrying departure");return false;}
            if(!station.ReserveDeparture(this,target.Parent.ObjectID,observation,settings,networkRevision,stationsRevision,tramRevision))
            {station.ReportRouting("Station traffic changed; retrying departure");return false;}
            reservedTrafficStation=target.Parent.ObjectID;
            ApplyDepartureDirection(choice);
            conditionalDestination=target.Parent.ObjectID;destinationAuthor=rule.Author;destinationPath=path;journey=null;route.Clear();Parent.SetDirty();
            resumeDestination=normal;returningToSchedule=false;
            DestinationMessage("Temporary destination: "+target.StationName);station.ReportRouting("Selected "+target.StationName);return true;
        }
        if(observation is {IsCurrent:false})
        {station.ReportRouting("Train or passengers changed; retrying departure");return false;}
        StationDepartureContext? normalObservation=null;
        var normalRails=RailSimulationFrame.NetworkRevision;
        var normalStations=TrainStationComponent.RoutingRevision;
        var normalTram=Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0;
        var stop=station.Parent.GetComponent<TramStopComponent>();
        var ready=!coupling.RepairRequired&&(station.HasDepartureConditions
            ?station.Ready(coupling,dwell,out normalObservation)
            :stop!=null?stop.Ready(dwell):Parent.GetComponent<TramRouteComponent>()!=null&&dwell>=15);
        if(!StationHoldCurrent(station))return false;
        if(settings!=station.SettingsRevision)
        {station.ReportRouting("Station settings changed; retrying departure");return false;}
        if(terminalObservation is {IsCurrent:false})
        {station.ReportRouting("Route changed; retrying departure");return false;}
        if(ready)
        {
            if(conditionalDestination==Guid.Empty)
            {
                normalDeparture=NormalNextStation(station,rail,t,direction,nose,coupling.Performance);
                if(normalDeparture==null)
                {station.ReportRouting("Route changed; retrying departure");return false;}
            }
            var next=conditionalDestination!=Guid.Empty?conditionalDestination:normalDeparture!.Station;
            if(!StationHoldCurrent(station))return false;
            if(terminalObservation is {IsCurrent:false} || normalDeparture is {} selected && (selected.Journey.ValidatedRevision!=RailSimulationFrame.Revision
                || selected.Journey.Context!=JourneyContextFor(true,coupling.Performance)))
            {station.ReportRouting("Route changed; retrying departure");return false;}
            if(settings!=station.SettingsRevision||normalObservation is {IsCurrent:false}
                ||!station.ReserveDeparture(this,next,normalObservation,settings,normalRails,normalStations,normalTram))
            {station.ReportRouting("Station traffic changed; retrying departure");return false;}
            reservedTrafficStation=next;
        }
        station.ReportRouting(messages.Count==0?"Normal route":string.Join("; ",messages.Distinct(StringComparer.Ordinal))+"; using normal departure rules");
        return ready;
    }
    private bool FinishDiversion(TrainStationComponent station,VoxelRail rail,float t,int direction,double nose,RailCouplingComponent train,bool allowReverse=true,DepartureObservation? terminalObservation=null)
    {
        if(!StationHoldCurrent(station))return false;
        var author=destinationAuthor;var resume=TrainStationComponent.Find(resumeDestination);
        if(!returningToSchedule && resumeDestination!=Guid.Empty && resume!=station
            && TrainStationComponent.RegistrationPending(resumeDestination,resume))
        {
            destinationPath=null;journey=null;route.Clear();
            DestinationMessage("Waiting for scheduled station to finish loading or moving");
            station.ReportRouting("Waiting for scheduled station to finish loading or moving");return false;
        }
        if(!returningToSchedule && resume!=null && resume!=station)
        {
            var settings=station.SettingsRevision;
            var choice=DeparturePath(rail,t,direction,nose,train,resume,author,allowReverse);
            if(!StationHoldCurrent(station))return false;
            if(settings!=station.SettingsRevision)
            {station.ReportRouting("Station settings changed; retrying departure");return false;}
            if(terminalObservation is {IsCurrent:false} || choice.Retry)
            {
                destinationPath=null;journey=null;route.Clear();
                DestinationMessage("Route changed; retrying scheduled return: "+resume.StationName);
                station.ReportRouting("Route changed; retrying scheduled return to "+resume.StationName);
                return false;
            }
            if(choice.Path is {} path)
            {
                ApplyDepartureDirection(choice);
                conditionalDestination=resume.Parent.ObjectID;destinationPath=path;returningToSchedule=true;journey=null;route.Clear();
                reservedTrafficStation=resume.Parent.ObjectID;UpdateStationIntent();
                DestinationMessage("Returning to scheduled station: "+resume.StationName);Parent.SetDirty();return true;
            }
        }
        var message=resumeDestination!=Guid.Empty&&resume==null?"Scheduled station removed; continuing normal route"
            : !returningToSchedule&&resume!=null&&resume!=station?"Scheduled station unreachable; continuing normal route"
            : "Diversion complete; resuming normal route";
        if(!StationHoldCurrent(station))return false;
        if(terminalObservation is {IsCurrent:false})
        {station.ReportRouting("Route changed; retrying departure");return false;}
        ClearDestination(message,true);return true;
    }
    private DestinationPath? EnsureDestination(VoxelRail rail,float t,int direction,double nose,TrainPerformance train)
    {
        if(conditionalDestination==Guid.Empty)return null;
        var target=TrainStationComponent.Find(conditionalDestination);
        if(TrainStationComponent.RegistrationPending(conditionalDestination,target))
        {
            destinationPath=null;journey=null;route.Clear();SetTargetStation(0,preserveReservation:true);
            DestinationMessage("Waiting for destination station to finish loading or moving");return null;
        }
        if(target==null){ClearDestination("Destination removed; resuming normal route",true);return null;}
        var cached=destinationPath;
        if(cached!=null && cached.Destination==conditionalDestination && cached.Author==destinationAuthor && cached.MinimumRadius==train.MinimumRadius && cached.StationsRevision==TrainStationComponent.RoutingRevision
            && cached.TramRevision==(Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0)
            && cached.Cursors.Contains(new(DestinationRail(rail),direction>0?0:1))
            // The same directed rail can now be beyond the cached final stop,
            // for example after manual travel. Replan using the existing arrival
            // margin instead of keeping a destination with no stop ahead. An
            // outgoing edge can still carry an earlier occurrence around a loop.
            && (cached.Edges.ContainsKey((DestinationRail(rail),direction>0?1:0))
                || RailGuidance.CanReachStop(rail,t,target.TrackT,direction)
                    && (target.TrackT-(double)t)*direction*rail.Profile.Length-nose>=-.4))
        {
            if(cached.Revision==RailSimulationFrame.Revision)return cached;
            var changes=RailSimulationFrame.ChangesAffect(cached.Revision,cached.Watched);
            if(!changes.Affected)return destinationPath=cached with{Revision=changes.Revision};
        }
        var revision=RailSimulationFrame.Revision;
        var stationsRevision=TrainStationComponent.RoutingRevision;
        var tramRevision=Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0;
        destinationPath=FindDestination(rail,t,direction,nose,train,target,destinationAuthor);
        // A rejected snapshot does not prove that the destination is unreachable.
        // Retain the diversion (including its scheduled return) and brake until
        // the next ordinary control update can search a coherent network.
        if(destinationPath==null && (revision!=RailSimulationFrame.Revision
            || stationsRevision!=TrainStationComponent.RoutingRevision
            || tramRevision!=(Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0)))
        {
            journey=null;route.Clear();SetTargetStation(0);
            DestinationMessage("Route changed; retrying destination: "+target.StationName);
            return null;
        }
        if(destinationPath==null)ClearDestination("Destination unreachable; resuming normal route",true);
        return destinationPath;
    }
    private bool PrepareDestinationSwitches(RailJourneyPlan plan,int cursor,float t,double nose,double horizon)
    {
        if(conditionalDestination==Guid.Empty)return true;
        User? author=null;
        var position=plan.Position(cursor,t);
        var switches=plan.SwitchNodes;
        for(var s=0;s<switches.Length;s++)
        {
            if(!plan.IsFirstSwitchAhead(s,cursor))continue;
            var i=switches[s];
            var node=plan.Nodes[i];if(node.Start-position>horizon+nose)break;
            var points=RailSwitchComponent.At(RailPowerConnectionComponent.PhysicalCell(node.Rail.Cell));
            if(points==null||points.SelectedRouteValue==node.Rail.Profile.SwitchRoute)continue;
            author??=UserManager.Users.FirstOrDefault(u=>u.Id==destinationAuthor);
            if(author==null||!points.Parent.IsAuthorized(author,AccessType.FullAccess))
            {
                // Abandon the entire diversion, including its saved return.
                // A later rule must capture the current normal route anew.
                ClearDestination("Switch access changed; resuming normal route",true);return false;
            }
            if(!points.SelectAutomatic(author,node.Rail.Profile.SwitchRoute))
            {
                // Stay outside the switch's existing body clearance (.4 m),
                // so the approaching train itself cannot prevent the retry.
                stationTravel=Math.Min(stationTravel,Math.Max(0,node.Start-position-nose-.5));
                DestinationMessage("Waiting for an occupied switch");Status="Waiting for an occupied switch";return false;
            }
        }
        DestinationMessage((returningToSchedule?"Returning to scheduled station: ":"Temporary destination: ")+(TrainStationComponent.Find(conditionalDestination)?.StationName??"station removed"));return true;
    }
}
