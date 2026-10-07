using System.Numerics;
namespace Eco.Minecarts.Track;

// Rail anchors are indexed, preserving the previous Near() anchor-radius rule.
// Queries copy local candidates before callers enter object/component locks.
internal sealed class RailSpatialIndex<T> where T : class
{
    private const int BucketSize = 30;
    private readonly object gate = new();
    private readonly Dictionary<(int,int,int), Dictionary<RailCell,T>> buckets = new();
    private static (int,int,int) Bucket(Vector3 position) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Bucket"); return ((int)MathF.Floor(position.X/BucketSize),(int)MathF.Floor(position.Y/BucketSize),(int)MathF.Floor(position.Z/BucketSize)); }
    internal void Add(RailCell cell,T value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Add");
        lock(gate)
        {
            var bucket=Bucket(cell.Origin);
            if(!buckets.TryGetValue(bucket,out var members)) buckets[bucket]=members=new();
            members[cell]=value;
        }
    }
    internal void Remove(RailCell cell,T value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Remove");
        lock(gate)
        {
            var bucket=Bucket(cell.Origin);
            if(!buckets.TryGetValue(bucket,out var members) || !members.TryGetValue(cell,out var current) || current!=value) return;
            members.Remove(cell);
            if(members.Count==0) buckets.Remove(bucket);
        }
    }
    internal T[] Near(Vector3 position,float radius)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Near");
        var min=Bucket(position-new Vector3(radius)); var max=Bucket(position+new Vector3(radius));
        List<T>? result=null;
        lock(gate)
        for(var x=min.Item1;x<=max.Item1;x++) for(var y=min.Item2;y<=max.Item2;y++) for(var z=min.Item3;z<=max.Item3;z++)
            if(buckets.TryGetValue((x,y,z),out var members))
                foreach(var pair in members)
                    if(Vector3.DistanceSquared(position,pair.Key.Origin)<radius*radius) (result??=new()).Add(pair.Value);
        return result?.ToArray() ?? [];
    }
}
