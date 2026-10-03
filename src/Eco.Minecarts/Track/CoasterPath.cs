using System.Numerics;

namespace Eco.Minecarts.Track;

/// <summary>
/// Shared server/asset path source. T is normalized ARC LENGTH, not the spline
/// parameter. Frames remain defined at vertical tangents and through inversions.
/// No path is allowed to supply propulsion: gravity belongs to RailPhysics.
/// </summary>
public sealed class CoasterPath
{
    private const int Samples = 2048;
    private readonly Vector3[] points = new Vector3[Samples + 1];
    private readonly Vector3[] tangents = new Vector3[Samples + 1];
    private readonly Vector3[] ups = new Vector3[Samples + 1];
    private readonly float[] distances = new float[Samples + 1];
    public string Key { get; }
    public bool Chain { get; }
    public float Length => distances[^1];

    private CoasterPath(string key, Func<float, Vector3> curve, bool chain = false,
        Func<float, float>? bank = null)
    {
        Key = key; Chain = chain;
        for (var i = 0; i <= Samples; i++)
        {
            var u = i / (float)Samples;
            points[i] = curve(u) + new Vector3(0, .15f, 0);
            tangents[i] = Vector3.Normalize(curve(Math.Min(1, u + .0001f)) - curve(Math.Max(0, u - .0001f)));
            if (i > 0) distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            // Rotation-minimizing parallel transport, not world-up LookRotation.
            var up = i == 0 ? Vector3.UnitY : ups[i - 1];
            if (i > 0)
            {
                var axis = Vector3.Cross(tangents[i - 1], tangents[i]);
                var sine = axis.Length();
                if (sine > .0000001f)
                    up = Vector3.Transform(up, Quaternion.CreateFromAxisAngle(axis / sine,
                        MathF.Atan2(sine, Vector3.Dot(tangents[i - 1], tangents[i]))));
            }
            ups[i] = Vector3.Normalize(up - tangents[i] * Vector3.Dot(up, tangents[i]));
        }
        // Remove transport holonomy so each modular exit meets the next piece
        // upright. This also prevents a lateral-offset loop leaving a rolled car.
        var endTangent = tangents[^1];
        var endUp = Vector3.Normalize(Vector3.UnitY - endTangent * Vector3.Dot(Vector3.UnitY, endTangent));
        var correction = MathF.Atan2(Vector3.Dot(endTangent, Vector3.Cross(ups[^1], endUp)), Vector3.Dot(ups[^1], endUp));
        for (var i = 0; i <= Samples; i++)
            ups[i] = Vector3.Transform(ups[i], Quaternion.CreateFromAxisAngle(tangents[i], correction * distances[i] / Length));
        // Bank is applied after transport so the bank angle does not accumulate.
        if (bank != null)
            for (var i = 0; i <= Samples; i++)
                ups[i] = Vector3.Transform(ups[i], Quaternion.CreateFromAxisAngle(tangents[i], bank(i / (float)Samples)));
    }

    private (int Index, float Blend) Locate(float t)
    {
        if (!float.IsFinite(t)) throw new ArgumentOutOfRangeException(nameof(t));
        var distance = Math.Clamp(t, 0, 1) * Length;
        var index = Array.BinarySearch(distances, distance);
        if (index < 0) index = ~index - 1;
        index = Math.Clamp(index, 0, Samples - 1);
        while(index<Samples-1&&distances[index+1]==distances[index])index++;
        while(index>0&&distances[index+1]==distances[index])index--;
        return (index, (distance - distances[index]) / (distances[index + 1] - distances[index]));
    }
    public Vector3 Point(float t) { var (i, f) = Locate(t); return Vector3.Lerp(points[i], points[i + 1], f); }
    public Vector3 Tangent(float t) { var (i, f) = Locate(t); return Vector3.Normalize(Vector3.Lerp(tangents[i], tangents[i + 1], f)); }
    public Vector3 Up(float t)
    {
        var (i, f) = Locate(t); var up = Vector3.Lerp(ups[i], ups[i + 1], f); var tangent = Tangent(t);
        return Vector3.Normalize(up - tangent * Vector3.Dot(up, tangent));
    }
    public (float T, Vector3 Point, float Distance) Nearest(Vector3 position)
    {
        // A loop has multiple local minima. A single ternary search is invalid.
        var best = float.PositiveInfinity; var result = Vector3.Zero; var distance = 0f;
        for (var i = 0; i < Samples; i++)
        {
            var edge = points[i + 1] - points[i];
            if(edge.LengthSquared()==0)continue;
            var f = Math.Clamp(Vector3.Dot(position - points[i], edge) / edge.LengthSquared(), 0, 1);
            var candidate = points[i] + edge * f;
            var error = Vector3.DistanceSquared(position, candidate);
            if (error >= best) continue;
            best = error; result = candidate;
            distance = distances[i] + f * (distances[i + 1] - distances[i]);
        }
        return (distance / Length, result, MathF.Sqrt(best));
    }

    private static Vector3 Straight(float t) => new(0, 0, -2 + 4 * t);
    private static Vector3 Bend(float t, int side)
    {
        var a = t * MathF.PI / 2;
        return new(side * 4 * (1 - MathF.Cos(a)), 0, -2 + 4 * MathF.Sin(a));
    }
    private static Vector3 Loop(float t, int side)
    {
        // One full vertical revolution with two metres of lateral separation
        // between entry and exit: avoids overlapping approach/departure rails.
        // Smootherstep gives horizontal, unbanked endpoints on integer anchors.
        var a = t * 2 * MathF.PI;
        var lateral = t * t * t * (t * (6 * t - 15) + 10);
        return new(side * 2 * lateral, 6 * (1 - MathF.Cos(a)), -2 + 6 * MathF.Sin(a) + 4 * t);
    }
    public static readonly CoasterPath[] All = new CoasterPath[]
    {
        new("CoasterStraight", Straight),
        new("CoasterBendLeft", t => Bend(t, -1)),
        new("CoasterBendRight", t => Bend(t, 1)),
        new("CoasterBankLeft", t => Bend(t, -1), bank: t => MathF.PI / 4 * MathF.Pow(MathF.Sin(MathF.PI * t), 2)),
        new("CoasterBankRight", t => Bend(t, 1), bank: t => -MathF.PI / 4 * MathF.Pow(MathF.Sin(MathF.PI * t), 2)),
        new("CoasterHill", t => new Vector3(0, 2 * MathF.Pow(MathF.Sin(MathF.PI * t), 2), -2 + 8 * t)),
        new("CoasterValley", t => new Vector3(0, -2 * MathF.Pow(MathF.Sin(MathF.PI * t), 2), -2 + 8 * t)),
        // Keep the original key for placed right-hand loops and saved carts.
        new("CoasterLoop", t => Loop(t, 1)),
        new("CoasterLoopLeft", t => Loop(t, -1)),
    }.Select(GridSockets).ToArray();
    // Whole sections and hammer rails share grid-face sockets, never integer
    // centre sockets. Half-cell straight leads also preserve endpoint frames.
    private static CoasterPath GridSockets(CoasterPath source)
    {
        var length=source.Length;
        return Section(source.Key,t=>{
            var distance=t*(length+1)-.5f;
            var u=Math.Clamp(distance/length,0,1);
            var point=source.Point(u);
            if(distance<0)point+=source.Tangent(0)*distance;
            if(distance>length)point+=source.Tangent(1)*(distance-length);
            return(point,source.Up(u),source.Tangent(u));
        },source.Chain);
    }
    private static readonly IReadOnlyDictionary<string, CoasterPath> ByKey = All.ToDictionary(p => p.Key);
    public static CoasterPath Find(string key) => ByKey.TryGetValue(key, out var path) ? path : CoasterTerrainPath.Find(key).Path;
    // A whole-section item's origin is the first buildable cell, with the
    // entrance socket on that cell's rear face. The rail path remains centered
    // on its original grid coordinates for save-stable physics profiles.
    public Vector3 PlacementOffset => new(0, 0, MathF.Round(-Point(0).Z - .5f));

    // A terrain section inherits the source frame, including inverted/banked
    // endpoints. Re-running world-up transport per voxel would unwind a loop.
    internal static CoasterPath Section(string key, Func<float,(Vector3 Point,Vector3 Up,Vector3 Tangent)> frame,bool chain)
    {
        return new CoasterPath(key,frame,chain);
    }
    private CoasterPath(string key,Func<float,(Vector3 Point,Vector3 Up,Vector3 Tangent)> frame,bool chain)
    {
        Key=key; Chain=chain;
        for(var i=0;i<=Samples;i++)
        {
            var t=i/(float)Samples; var value=frame(t);
            points[i]=value.Point;
            // Tiny voxel-corner sections amplify float cancellation if their
            // tangent is reconstructed from a microscopic finite difference.
            tangents[i]=Vector3.Normalize(value.Tangent);
            ups[i]=Vector3.Normalize(value.Up-tangents[i]*Vector3.Dot(value.Up,tangents[i]));
            if(i>0) distances[i]=distances[i-1]+Vector3.Distance(points[i-1],points[i]);
        }
    }
}
