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
    private sealed record DestinationPath(Guid Destination,int Author,double MinimumRadius,long StationsRevision,long Revision,RailDestinationSearch.Step[] Steps,
        Dictionary<(VoxelRail Rail,int Exit),(VoxelRail Rail,int End)> Edges,HashSet<RailCell> Watched,HashSet<RailDestinationSearch.Step> Cursors);
    private DestinationPath? destinationPath;
    private DateTime nextDepartureEvaluation;
    private int evaluatedStation,evaluatedSettings;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Destination Status")] public string DestinationStatus {get;private set;}="Normal route";
    private void DestinationMessage(string value){if(DestinationStatus==value)return;DestinationStatus=value;this.Changed(nameof(DestinationStatus));}
    private void ClearDestination(string reason,bool clearSchedule=false)
    {
        conditionalDestination=Guid.Empty;destinationAuthor=0;destinationPath=null;journey=null;route.Clear();
        returningToSchedule=false;if(clearSchedule)resumeDestination=Guid.Empty;
        DestinationMessage(reason);Parent.SetDirty();
    }
    private DestinationPath? FindDestination(VoxelRail rail,float t,int direction,double nose,TrainPerformance train,TrainStationComponent target,int authorId)
    {
        using var profile=RailProfile.Measure("Rail Network/Autopilot and stops/Find destination",Parent);
        var author=UserManager.Users.FirstOrDefault(u=>u.Id==authorId);if(author==null)return null;
        var spec=((RailVehicleObject)Parent).RailSpec;var revision=RailSimulationFrame.Revision;
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
            if(target.Cell is not {} cell || RailPowerConnectionComponent.PhysicalCell(part.Cell)!=RailPowerConnectionComponent.PhysicalCell(cell))return null;
            if(spec.Tram && target.Parent.GetComponent<TramStopComponent>() is {} stop
                && !stop.Compatible(Parent.GetComponent<TramRouteComponent>(),travelDirection))return null;
            return target.TrackT;
        }
        var steps=RailDestinationSearch.Find(rail,direction,t,nose,train,Neighbors,Goal,RailPowerConnectionComponent.PhysicalCell);
        if(steps==null || revision!=RailSimulationFrame.Revision)return null;
        var edges=new Dictionary<(VoxelRail,int),(VoxelRail,int)>();
        for(var i=0;i+1<steps.Length;i++)edges[(steps[i].Rail,1-steps[i].Entry)]=(steps[i+1].Rail,steps[i+1].Entry);
        return new(target.Parent.ObjectID,authorId,train.MinimumRadius,TrainStationComponent.RoutingRevision,revision,steps,edges,steps.Select(s=>RailPowerConnectionComponent.PhysicalCell(s.Rail.Cell)).ToHashSet(),steps.ToHashSet());
    }
    private Guid NormalNextStation(TrainStationComponent current,VoxelRail rail,float t,int direction,double nose,TrainPerformance train)
    {
        var tram=Parent.GetComponent<TramRouteComponent>();var nextName=tram?.NextAfterDeparture(current.StationName);
        RailJourneyPlan.Stop[] Stops(VoxelRail part,int travelDirection)=>TrainStationComponent.At(RailPowerConnectionComponent.PhysicalCell(part.Cell))
            .Where(s=>s!=current && (tram==null || (nextName==null || string.Equals(s.StationName,nextName,StringComparison.OrdinalIgnoreCase))
                && (s.Parent.GetComponent<TramStopComponent>()?.Compatible(tram,travelDirection)??true)))
            .Select(s=>new RailJourneyPlan.Stop(s.Parent.ID,s.TrackT)).ToArray();
        var plan=RailJourneyPlan.Build(rail,direction,t,nose,(part,exit)=>NextAutomatic(part,exit,train),Stops,normalizeCell:RailPowerConnectionComponent.PhysicalCell);
        return TrainStationComponent.Find(plan.TargetStation(0,t,nose,0))?.Parent.ObjectID??Guid.Empty;
    }
    private DestinationPath? DeparturePath(VoxelRail rail,float t,int direction,double nose,RailCouplingComponent train,TrainStationComponent target,int author,bool allowReverse=true)
    {
        var path=FindDestination(rail,t,direction,nose,train.Performance,target,author);
        if(path!=null || !allowReverse)return path;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        // Reversing is safe only at a stopped station, never during travel.
        if(motion.CurrentRailVelocity.Length()>.01f)return null;
        var backward=Parent.Rotation.RotateVector(System.Numerics.Vector3.UnitZ)*-motion.ServerDirection;
        var rear=train.Group().Max(c=>System.Numerics.Vector3.Dot(c.Parent.Position-Parent.Position,backward)+c.Vehicle.CouplerOffset);
        path=FindDestination(rail,t,-direction,rear,train.Performance,target,author);
        if(path!=null)
        {
            motion.SetDriveCommands(motion.ServerThrottle,-motion.ServerDirection,false);Reverse=motion.ServerDirection<0;this.Changed(nameof(Reverse));Parent.SetDirty();
        }
        return path;
    }
    private bool ChooseStationDeparture(TrainStationComponent station,RailCouplingComponent coupling,double dwell,VoxelRail rail,float t,int direction,double nose,bool allowReverse=true)
    {
        // Evaluate passenger/cargo/fuel conditions once per second while held,
        // rather than repeating UI expressions at every 20 Hz motion update.
        if(evaluatedStation==station.Parent.ID&&evaluatedSettings==station.SettingsRevision&&DateTime.UtcNow<nextDepartureEvaluation)return false;
        evaluatedStation=station.Parent.ID;evaluatedSettings=station.SettingsRevision;nextDepartureEvaluation=DateTime.UtcNow.AddSeconds(1);
        var messages=new List<string>();
        foreach(var rule in station.MatchingDestinations(coupling,dwell))
        {
            var author=UserManager.Users.FirstOrDefault(u=>u.Id==rule.Author);
            var target=TrainStationComponent.Find(rule.Destination);
            if(author==null || !station.Parent.IsAuthorized(author,AccessType.FullAccess)){messages.Add("Rule author no longer has station access");continue;}
            if(target==null){messages.Add("Destination was removed or not selected");continue;}
            if(target==station){messages.Add("Destination is this station");continue;}
            var normal=resumeDestination==Guid.Empty&&conditionalDestination==Guid.Empty || returningToSchedule&&station.Parent.ObjectID==conditionalDestination
                ?NormalNextStation(station,rail,t,direction,nose,coupling.Performance):resumeDestination;
            var path=DeparturePath(rail,t,direction,nose,coupling,target,rule.Author,allowReverse);
            if(path==null){messages.Add(target.StationName+": no compatible forward route or switch access");continue;}
            conditionalDestination=target.Parent.ObjectID;destinationAuthor=rule.Author;destinationPath=path;journey=null;route.Clear();Parent.SetDirty();
            resumeDestination=normal;returningToSchedule=false;
            DestinationMessage("Temporary destination: "+target.StationName);station.ReportRouting("Selected "+target.StationName);return true;
        }
        var ready=ReadyToDepart(station,coupling,dwell,Parent.GetComponent<TramRouteComponent>()!=null);
        station.ReportRouting(messages.Count==0?"Normal route":string.Join("; ",messages)+"; using normal departure rules");
        return ready;
    }
    private void FinishDiversion(TrainStationComponent station,VoxelRail rail,float t,int direction,double nose,RailCouplingComponent train)
    {
        var author=destinationAuthor;var resume=TrainStationComponent.Find(resumeDestination);
        if(!returningToSchedule && resume!=null && resume!=station && DeparturePath(rail,t,direction,nose,train,resume,author) is {} path)
        {
            conditionalDestination=resume.Parent.ObjectID;destinationPath=path;returningToSchedule=true;journey=null;route.Clear();
            DestinationMessage("Returning to scheduled station: "+resume.StationName);Parent.SetDirty();return;
        }
        var message=resumeDestination!=Guid.Empty&&resume==null?"Scheduled station removed; continuing normal route"
            : !returningToSchedule&&resume!=null&&resume!=station?"Scheduled station unreachable; continuing normal route"
            : "Diversion complete; resuming normal route";
        ClearDestination(message,true);
    }
    private DestinationPath? EnsureDestination(VoxelRail rail,float t,int direction,double nose,TrainPerformance train)
    {
        if(conditionalDestination==Guid.Empty)return null;
        var target=TrainStationComponent.Find(conditionalDestination);
        if(target==null){ClearDestination("Destination removed; resuming normal route",true);return null;}
        var cached=destinationPath;
        if(cached!=null && cached.Destination==conditionalDestination && cached.Author==destinationAuthor && cached.MinimumRadius==train.MinimumRadius && cached.StationsRevision==TrainStationComponent.RoutingRevision
            && cached.Cursors.Contains(new(rail,direction>0?0:1)))
        {
            if(cached.Revision==RailSimulationFrame.Revision)return cached;
            var changes=RailSimulationFrame.ChangesAffect(cached.Revision,cached.Watched);
            if(!changes.Affected)return destinationPath=cached with{Revision=changes.Revision};
        }
        destinationPath=FindDestination(rail,t,direction,nose,train,target,destinationAuthor);
        if(destinationPath==null)ClearDestination("Destination unreachable; resuming normal route",true);
        return destinationPath;
    }
    private bool PrepareDestinationSwitches(RailJourneyPlan plan,int cursor,float t,double nose,double horizon)
    {
        if(conditionalDestination==Guid.Empty)return true;
        User? author=null;
        var position=plan.Position(cursor,t);
        foreach(var i in plan.SwitchNodes)
        {
            if(i<cursor)continue;
            var node=plan.Nodes[i];if(node.Start-position>horizon+nose)break;
            var points=RailSwitchComponent.At(RailPowerConnectionComponent.PhysicalCell(node.Rail.Cell));
            if(points==null||points.SelectedRouteValue==node.Rail.Profile.SwitchRoute)continue;
            author??=UserManager.Users.FirstOrDefault(u=>u.Id==destinationAuthor);
            if(author==null||!points.Parent.IsAuthorized(author,AccessType.FullAccess))
            {ClearDestination("Switch access changed; resuming normal route");return false;}
            if(!points.SelectAutomatic(author,node.Rail.Profile.SwitchRoute))
            {
                stationTravel=Math.Min(stationTravel,Math.Max(0,node.Start-position-nose-.25));
                DestinationMessage("Waiting for an occupied switch");Status="Waiting for an occupied switch";return false;
            }
        }
        DestinationMessage((returningToSchedule?"Returning to scheduled station: ":"Temporary destination: ")+(TrainStationComponent.Find(conditionalDestination)?.StationName??"station removed"));return true;
    }
}
