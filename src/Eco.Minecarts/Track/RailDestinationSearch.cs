using Eco.Minecarts.Physics;

namespace Eco.Minecarts.Track;

// Finds a directed itinerary over the same physical ports used by rail motion.
// Search runs at a station or on topology change; following uses RailJourneyPlan.
public static class RailDestinationSearch
{
    public readonly record struct Step(VoxelRail Rail,int Entry);
    private sealed record Visit(Step Step,int Previous,double Distance);
    public static Step[]? Find(VoxelRail start,int direction,float t,double nose,TrainPerformance train,
        Func<VoxelRail,int,IEnumerable<(VoxelRail Rail,int End)>> neighbors,
        Func<VoxelRail,int,float?> destination,Func<RailCell,RailCell> normalize,int maximumStates=8192)
    {
        var visits=new List<Visit>{new(new(start,direction>0?0:1),-1,0)};
        var queue=new PriorityQueue<int,(double Distance,int Order)>();queue.Enqueue(0,(0,0));
        var best=new Dictionary<Step,double>();
        // Converging branches can discover the same directed approach before
        // it is settled. Coalesce dominated queued visits as well as settled
        // ones, so redundant paths do not consume the bounded visit budget.
        var queued=new Dictionary<Step,double>
        { [new(new(normalize(start.Cell),start.Profile),direction>0?0:1)]=0 };
        var revisited=false;
        var goal=-1;var goalDistance=double.PositiveInfinity;
        var initial=(direction>0?t:1-t)*start.Profile.Length;
        var clearDistance=initial+nose-.4;
        while(queue.TryDequeue(out var at,out _))
        {
            var visit=visits[at];var step=visit.Step;
            // Entry distance is a lower bound, not the distance to the stop.
            // The other end of a long destination rail can reach its bound
            // stop sooner even if that rail entrance is settled later.
            if(visit.Distance>=goalDistance)break;
            var key=new Step(new(normalize(step.Rail.Cell),step.Rail.Profile),step.Entry);
            // Retain only the frontier: a long unbranched railway needs one
            // queued approach, rather than a second whole visited-state map.
            if(queued.TryGetValue(key,out var pending)&&pending==visit.Distance)queued.Remove(key);
            var seen=best.TryGetValue(key,out var known);
            // A visit behind the leading nose cannot dominate a later lap.
            // Equal costs still coalesce; beyond the nose, ordinary shortest-
            // distance pruning applies. The existing visit bound caps loops.
            if(seen&&known<=visit.Distance&&(known==visit.Distance||known>=clearDistance))continue;
            if(!seen&&best.Count>=maximumStates || visits.Count>=(long)maximumStates*8)return null;
            revisited|=seen;
            best[key]=visit.Distance;
            var sign=step.Entry==0?1:-1;
            if(destination(step.Rail,sign) is {} stopT
                && RailGuidance.CanReachStop(step.Rail,at==0?t:step.Entry,stopT,sign)
                && visit.Distance+(sign>0?stopT:1-stopT)*step.Rail.Profile.Length-initial-nose>=-.4)
            {
                var distance=visit.Distance+(sign>0?stopT:1-stopT)*step.Rail.Profile.Length;
                if(distance<goalDistance){goal=at;goalDistance=distance;}
                // Continuing beyond this stop can only lengthen this approach.
                continue;
            }
            // A buffer blocks destinations on its rail as well as subsequent
            // rails, but a departure with the beam behind it can continue.
            if(!RailGuidance.CanReachStop(step.Rail,at==0?t:step.Entry,1-step.Entry,sign))continue;
            // A repeated directed rail must retain its earlier outgoing edge.
            // Reject a conflicting extension before it settles a destination
            // and hides a valid alternative still in the search queue.
            Step? requiredNext=null;
            if(seen)
                for(int child=at,parent=visit.Previous;parent>=0;child=parent,parent=visits[parent].Previous)
                {
                    var earlier=visits[parent].Step;
                    if(earlier.Entry!=step.Entry || earlier.Rail.Profile!=step.Rail.Profile || normalize(earlier.Rail.Cell)!=key.Rail.Cell)continue;
                    var next=visits[child].Step;
                    requiredNext=new(new(normalize(next.Rail.Cell),next.Rail.Profile),next.Entry);
                    break;
                }
            foreach(var next in neighbors(step.Rail,1-step.Entry))
            {
                if(!train.Fits(next.Rail.Profile.Radius))continue;
                var candidate=new Step(next.Rail,next.End);
                var normalized=new Step(new(normalize(candidate.Rail.Cell),candidate.Rail.Profile),candidate.Entry);
                if(requiredNext is {} required && normalized!=required)continue;
                var distance=visit.Distance+step.Rail.Profile.Length;
                if(best.TryGetValue(normalized,out known)&&known<=distance&&(known==distance||known>=clearDistance))continue;
                // A first approach behind the leading nose still cannot
                // dominate a later lap. Preserve the settlement rule and
                // stable first-discovered ordering for equal-cost choices.
                if(queued.TryGetValue(normalized,out known)&&known<=distance&&(known==distance||known>=clearDistance))continue;
                queued[normalized]=distance;
                var index=visits.Count;visits.Add(new(candidate,at,distance));queue.Enqueue(index,(distance,index));
            }
        }
        if(goal<0)return null;
        var path=new List<Step>();for(var i=goal;i>=0;i=visits[i].Previous)path.Add(visits[i].Step);
        path.Reverse();
        // The follower keeps one outgoing edge per directed physical rail.
        // Reuse completed search scratch only after the best stop is settled.
        if(revisited)
        {
            best.Clear();
            for(var i=0;i+1<path.Count;i++)
            {
                var part=path[i];var choice=path[i+1];
                var physical=new Step(new(normalize(part.Rail.Cell),part.Rail.Profile),part.Entry);
                if(best.TryGetValue(physical,out var previous))
                {
                    var earlier=path[(int)previous+1];
                    if(earlier.Entry!=choice.Entry || earlier.Rail.Profile!=choice.Rail.Profile
                        || normalize(earlier.Rail.Cell)!=normalize(choice.Rail.Cell))return null;
                }
                else best[physical]=i;
            }
        }
        return path.ToArray();
    }
}
