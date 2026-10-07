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
        var initial=(direction>0?t:1-t)*start.Profile.Length;
        while(queue.TryDequeue(out var at,out _))
        {
            var visit=visits[at];var step=visit.Step;
            var key=new Step(new(normalize(step.Rail.Cell),step.Rail.Profile),step.Entry);
            if(best.TryGetValue(key,out var known)&&known<=visit.Distance)continue;
            if(best.Count>=maximumStates || visits.Count>=maximumStates*8)return null;
            best[key]=visit.Distance;
            var sign=step.Entry==0?1:-1;
            if(destination(step.Rail,sign) is {} stopT
                && visit.Distance+(sign>0?stopT:1-stopT)*step.Rail.Profile.Length-initial-nose>=-.4)
            {
                var path=new List<Step>();for(var i=at;i>=0;i=visits[i].Previous)path.Add(visits[i].Step);
                path.Reverse();return path.ToArray();
            }
            if(step.Rail.Profile.Shape=="Stopper")continue;
            foreach(var next in neighbors(step.Rail,1-step.Entry))
            {
                if(!train.Fits(next.Rail.Profile.Radius))continue;
                var candidate=new Step(next.Rail,next.End);
                var normalized=new Step(new(normalize(candidate.Rail.Cell),candidate.Rail.Profile),candidate.Entry);
                var distance=visit.Distance+step.Rail.Profile.Length;
                if(best.TryGetValue(normalized,out known)&&known<=distance)continue;
                var index=visits.Count;visits.Add(new(candidate,at,distance));queue.Enqueue(index,(distance,index));
            }
        }
        return null;
    }
}
