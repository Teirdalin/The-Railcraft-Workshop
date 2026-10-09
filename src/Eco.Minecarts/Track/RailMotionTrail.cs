using System.Numerics;

namespace Eco.Minecarts.Track;

// Spatial trajectory in the existing consist solver. Samples are separated by
// distance, never by a fixed car delay or by the controlling car's rotation.
internal sealed class RailMotionTrail
{
    internal readonly record struct Pose(Vector3 Point, Vector3 Velocity, Vector3 Up, VoxelRail? Rail, float Parameter);
    private readonly record struct Entry(double Distance, Pose Pose);
    private readonly Entry[] entries = new Entry[2048];
    private int start, count;
    private double distance;
    private double lastAir = double.NegativeInfinity;
    internal bool Empty => count == 0;
    internal void Clear(){start=count=0;distance=0;lastAir=double.NegativeInfinity;}
    internal bool RecentFlight(float length) => distance - lastAir <= length + .5;
    private Entry At(int index) => entries[(start + index) % entries.Length];
    internal void Add(Pose pose)
    {
        if (!float.IsFinite(pose.Point.LengthSquared()) || !float.IsFinite(pose.Velocity.LengthSquared())) return;
        if (count > 0)
        {
            var step = Vector3.Distance(At(count - 1).Pose.Point, pose.Point);
            if (step > 8) { start = count = 0; distance = 0; lastAir = double.NegativeInfinity; }
            else if (step < .00001f && At(count - 1).Pose.Rail.HasValue == pose.Rail.HasValue) return;
            else
            {
                distance+=step;
                // Keep distance coverage even at a crawl; timer-sized samples
                // would exhaust the bounded ring before the last car arrives.
                var last=At(count-1);
                if(count>1 && last.Pose.Rail?.Cell==pose.Rail?.Cell && last.Distance-At(count-2).Distance<.04)
                {
                    entries[(start+count-1)%entries.Length]=new(distance,pose);
                    if(pose.Rail==null)lastAir=distance;
                    return;
                }
            }
        }
        if (pose.Rail == null) lastAir = distance;
        if (count == entries.Length) { start = (start + 1) % entries.Length; count--; }
        entries[(start + count++) % entries.Length] = new(distance, pose);
        while (count > 2 && distance - At(1).Distance > 64) { start = (start + 1) % entries.Length; count--; }
    }
    internal bool Behind(float metres, Func<VoxelRail,int,(VoxelRail Rail,int End)?> neighbor, out Pose pose)
    {
        pose = default;
        var target = distance - metres;
        if (count == 0 || target < At(0).Distance - .00001 || target > distance) return false;
        var lo = 0; var hi = count - 1;
        while (lo < hi) { var mid = (lo + hi + 1) / 2; if (At(mid).Distance <= target) lo = mid; else hi = mid - 1; }
        var a = At(lo);
        if (lo == count - 1 || Math.Abs(target - a.Distance) < .00001) { pose = a.Pose; return true; }
        var b = At(lo + 1); var ratio = (float)((target - a.Distance) / Math.Max(.000001, b.Distance - a.Distance));
        if (a.Pose.Rail is { } rail && b.Pose.Rail != null)
        {
            var sign = Vector3.Dot(a.Pose.Velocity, rail.Profile.Tangent(a.Pose.Parameter)) < 0 ? -1 : 1;
            var cursor = RailPathCursor.Travel(rail, a.Pose.Parameter, sign * (float)(target - a.Distance), neighbor);
            if (cursor.Remaining == 0)
            {
                var tangent = cursor.Rail.Profile.Tangent(cursor.Progress) * sign * cursor.Orientation;
                pose = new(cursor.Rail.Point(cursor.Progress), tangent * a.Pose.Velocity.Length(), cursor.Rail.Profile.Up(cursor.Progress), cursor.Rail, cursor.Progress);
                return true;
            }
        }
        // Contact begins at the recorded contact point, rather than halfway
        // through an interpolated airborne-to-grounded segment.
        var up=Vector3.Lerp(a.Pose.Up,b.Pose.Up,ratio);
        if(up.LengthSquared()<.00001f)up=a.Pose.Up;
        pose = new(Vector3.Lerp(a.Pose.Point, b.Pose.Point, ratio), Vector3.Lerp(a.Pose.Velocity, b.Pose.Velocity, ratio),
            Vector3.Normalize(up), null, 0);
        return true;
    }
}
