using Eco.Minecarts.Physics;

namespace Eco.Minecarts.Track;

/// <summary>Immutable track itinerary; following it performs no world discovery.</summary>
public sealed class RailJourneyPlan
{
    public readonly record struct Stop(int Id,float T);
    public readonly record struct Node(VoxelRail Rail,int Entry,double Start,double Length,double Grade);
    private readonly record struct Run(int First,int Last,double Radius,double Grade);
    private readonly record struct StopEvent(int Node,int Id,double Position,bool Buffer);
    public readonly record struct Safety(double SpeedLimit,double Deceleration,double Obstacle,int Station,bool Buffer);
    private readonly Node[] nodes;
    private readonly Run[] runs;
    private readonly StopEvent[] stops;
    private readonly int[] switchNodes;
    public ReadOnlySpan<int> SwitchNodes=>switchNodes;
    private readonly Dictionary<(VoxelRail,int),int> indices;
    private readonly Func<RailCell,RailCell> normalize;
    private readonly RailCell loopShift;
    public IReadOnlyList<Node> Nodes=>nodes;
    public int LoopStart {get;}
    public bool Limited {get;}
    public bool EndsAtStation {get;}
    public bool TrackEnd {get;}
    public double Length=>nodes[^1].Start+nodes[^1].Length;

    private RailJourneyPlan(Node[] nodes,StopEvent[] stops,Dictionary<(VoxelRail,int),int> indices,
        int loopStart,bool limited,bool station,bool trackEnd,Func<RailCell,RailCell> normalize,RailCell loopShift)
    {
        this.nodes=nodes;this.stops=stops;this.indices=indices;
        switchNodes=nodes.Select((node,i)=>(node,i)).Where(x=>x.node.Rail.Profile.Shape=="Switch").Select(x=>x.i).ToArray();
        this.normalize=normalize;this.loopShift=loopShift;
        LoopStart=loopStart;Limited=limited;EndsAtStation=station;TrackEnd=trackEnd;
        var groups=new List<Run>();
        for(var i=0;i<nodes.Length;i++)
        {
            var node=nodes[i];var radius=node.Rail.Profile.Radius;
            if(groups.Count>0 && groups[^1].Radius==radius && groups[^1].Grade==node.Grade)
                groups[^1]=groups[^1] with{Last=i};
            else groups.Add(new(i,i,radius,node.Grade));
        }
        runs=groups.ToArray();
    }

    public static RailJourneyPlan Build(VoxelRail start,int direction,float t,double nose,
        Func<VoxelRail,int,(VoxelRail Rail,int End)?> next,Func<VoxelRail,int,Stop[]> stations,int maximumPieces=4096,
        Func<RailCell,RailCell>? normalizeCell=null)
    {
        if(maximumPieces<2)throw new ArgumentOutOfRangeException(nameof(maximumPieces));
        var nodes=new List<Node>();var stops=new List<StopEvent>();var indices=new Dictionary<(VoxelRail,int),int>();
        var normalize=normalizeCell??(static cell=>cell);var loopShift=default(RailCell);
        var rail=start;var entry=direction>0?0:1;var distance=0d;
        var initial=(direction>0?(double)t:1d-t)*start.Profile.Length;
        var loop=-1;var limited=false;var terminalStation=false;var end=false;
        while(true)
        {
            var key=(new VoxelRail(normalize(rail.Cell),rail.Profile),entry);
            if(indices.TryGetValue(key,out loop))
            {var cell=nodes[loop].Rail.Cell;loopShift=new(rail.Cell.X-cell.X,rail.Cell.Y-cell.Y,rail.Cell.Z-cell.Z);break;}
            loop=-1;
            if(nodes.Count>=maximumPieces){limited=true;break;}
            var index=nodes.Count;indices[key]=index;
            var sign=entry==0?1:-1;var length=rail.Profile.Length;
            nodes.Add(new(rail,entry,distance,length,Math.Max(0,-rail.Profile.Tangent(entry).Y*sign)));
            foreach(var stop in stations(rail,sign))
            {
                var position=distance+((double)stop.T-entry)*length*sign;
                stops.Add(new(index,stop.Id,position,false));
                if(position-initial-nose>=-.4)terminalStation=true;
            }
            var beam=distance+(.8-entry)*length*sign;
            var buffer=rail.Profile.Shape=="Stopper";
            if(buffer)stops.Add(new(index,0,beam,true));
            distance+=length;
            if(terminalStation || buffer && beam>=initial)break;
            if(next(rail,1-entry) is not {} continuation){end=true;break;}
            rail=continuation.Rail;entry=continuation.End;
        }
        return new(nodes.ToArray(),stops.ToArray(),indices,loop,limited,terminalStation,end,normalize,loopShift);
    }

    public bool TryCursor(VoxelRail rail,int direction,out int index)=>indices.TryGetValue((new VoxelRail(normalize(rail.Cell),rail.Profile),direction>0?0:1),out index);
    public bool TryNext(VoxelRail rail,int exit,out (VoxelRail Rail,int End)? next)
    {
        next=null;
        if(!indices.TryGetValue((new VoxelRail(normalize(rail.Cell),rail.Profile),1-exit),out var index))return false;
        var source=nodes[index].Rail.Cell;
        VoxelRail Shift(VoxelRail target,bool wrap)
        {
            var cell=target.Cell;
            return new(new(cell.X+rail.Cell.X-source.X+(wrap?loopShift.X:0),
                cell.Y+rail.Cell.Y-source.Y+(wrap?loopShift.Y:0),cell.Z+rail.Cell.Z-source.Z+(wrap?loopShift.Z:0)),target.Profile);
        }
        if(index+1<nodes.Length){var node=nodes[index+1];next=(Shift(node.Rail,false),node.Entry);return true;}
        if(LoopStart>=0){var node=nodes[LoopStart];next=(Shift(node.Rail,true),node.Entry);return true;}
        // Bodies may reach beyond a terminal station or a limited chunk. Their
        // normal physical traversal remains available there.
        return TrackEnd;
    }
    public double Position(int index,float t)=>nodes[index].Start+(nodes[index].Entry==0?(double)t:1d-t)*nodes[index].Length;
    // The destination remains visible beyond the braking lookahead. This reads
    // the saved itinerary only; it performs no new track discovery.
    public int TargetStation(int index,float t,double nose,int departedStation)
    {
        var position=Position(index,t);var nearest=double.PositiveInfinity;var target=0;
        foreach(var stop in stops)
        {
            if(stop.Buffer || stop.Id==departedStation)continue;
            var remaining=stop.Position-position-nose;
            if(remaining<-.4 && LoopStart>=0 && stop.Node>=LoopStart)
                remaining+=Length-nodes[LoopStart].Start;
            if(remaining<-.4 || remaining>=nearest)continue;
            nearest=remaining;target=stop.Id;
        }
        return target;
    }
    public bool PassedStation(int index,float t,double nose)
    {
        if(!EndsAtStation)return false;
        var position=Position(index,t);
        foreach(var stop in stops)if(!stop.Buffer && stop.Position-position-nose>=-.4)return false;
        return true;
    }

    public Safety Follow(int index,float t,double nose,TrainPerformance train,double horizon,double limit,double deceleration,int departedStation)
    {
        var position=Position(index,t);var current=nodes[index];
        var obstacle=double.PositiveInfinity;var station=0;var buffer=false;
        if(!train.Fits(current.Rail.Profile.Radius))return new(limit,deceleration,0,0,false);
        limit=Math.Min(limit,train.CurveSpeed(current.Rail.Profile.Radius));

        void Slice(int first,int last,double offset,bool currentSlice)
        {
            // Runs with identical radius/entry grade have their tightest curve
            // bound at the nearest future node. Flat straight runs add no bound.
            var geometryFirst=currentSlice?first+1:first;
            var low=0;var high=runs.Length;
            while(low<high){var middle=(low+high)/2;if(runs[middle].Last<geometryFirst)low=middle+1;else high=middle;}
            for(var r=low;r<runs.Length && runs[r].First<=last;r++)
            {
                var run=runs[r];var node=Math.Max(geometryFirst,run.First);
                if(node>Math.Min(last,run.Last))continue;
                var walked=nodes[node].Start+offset-position;
                if(walked>=horizon)break;
                if(!train.Fits(run.Radius))
                {obstacle=Math.Max(0,walked-nose-.25);station=0;buffer=false;return;}
                if(double.IsPositiveInfinity(run.Radius)&&run.Grade==0)continue;
                deceleration=Math.Min(deceleration,train.BrakeDeceleration(run.Grade));
                var curve=train.CurveSpeed(run.Radius);
                limit=Math.Min(limit,Math.Sqrt(curve*curve+2*deceleration*Math.Max(0,walked-nose)));
            }
            foreach(var stop in stops)
            {
                if(stop.Node<first || stop.Node>last || nodes[stop.Node].Start+offset-position>=horizon)continue;
                var ahead=stop.Position+offset-position;
                var remaining=ahead-nose-(stop.Buffer?.194:0);
                if(stop.Buffer?ahead<0:stop.Id==departedStation || remaining<-.4)continue;
                if(Math.Max(0,remaining)>=obstacle)continue;
                obstacle=Math.Max(0,remaining);station=stop.Id;buffer=stop.Buffer;
            }
        }
        Slice(index,nodes.Length-1,0,true);
        if(LoopStart>=0)
        {
            var offset=Length-nodes[LoopStart].Start;
            Slice(LoopStart,index>=LoopStart?index:LoopStart,offset,false);
        }
        else if(TrackEnd && nodes[^1].Start-position<horizon)
        {
            var remaining=Math.Max(0,Length-position-nose-.2);
            if(remaining<obstacle){obstacle=remaining;station=0;buffer=false;}
        }
        return new(limit,deceleration,obstacle,station,buffer);
    }
}
