using System.Numerics;

namespace Eco.Minecarts.Track;

/// <summary>Matches the one-cell Unity track geometry. Position is relative to block bottom-center.</summary>
public readonly record struct VoxelTrackProfile(string Shape, int QuarterTurns, bool Chain, bool Wooden = false, bool Industrial = false, float SwitchRadius = 0, int SwitchRoute = 0, bool Coaster = false, bool Tram = false)
{
    public float HalfGauge => Industrial ? 1f : .30f;
    public bool IsBend => SwitchRadius>0 ? SwitchRoute!=0 : Shape is "Bend" or "BendLeft" or "WideBend" or "IndustrialBend" or "IndustrialBendLeft";
    public bool Mirrored => SwitchRadius>0 ? SwitchRoute<0 : Shape is "BendLeft" or "IndustrialBendLeft";
    private float BendCenter => SwitchRadius>0 ? SwitchRadius : Industrial ? 3.5f : Shape == "WideBend" ? 1.5f : .5f;
    public float Radius => SwitchRadius>0 ? (IsBend ? SwitchRadius : float.PositiveInfinity) : Industrial && IsBend ? 5.5f : this.Shape == "WideBend" ? 2.5f : this.Shape is "Bend" or "BendLeft" ? .5f : float.PositiveInfinity;
    public double MaximumSupportedKg => this.Wooden ? 900 : double.PositiveInfinity;
    public static bool TryParse(string typeName, out VoxelTrackProfile profile)
    {
        profile = default;
        if(CoasterTerrainPath.TryProfile(typeName,out profile))return true;
        var chain = typeName.StartsWith("MinecartChain", StringComparison.Ordinal);
        var tram = typeName.StartsWith("TramTrack", StringComparison.Ordinal);
        var wooden = typeName.StartsWith("WoodenTrack", StringComparison.Ordinal);
        var prefix = wooden ? "WoodenTrack" : chain ? "MinecartChain" : tram ? "TramTrack" : "MinecartTrack";
        if (!typeName.StartsWith(prefix, StringComparison.Ordinal) || !typeName.EndsWith("Block", StringComparison.Ordinal)) return false;
        var shape = typeName[prefix.Length..^5];
        // The material's origin block is rendered as a straight rail as well.
        // Older/default placements must follow that same path; resource stacks
        // remain excluded below.
        if (shape.Length == 0) shape = "Straight";
        var turns = 0;
        foreach (var degrees in new[] { 270, 180, 90 })
            if (shape.EndsWith(degrees.ToString(), StringComparison.Ordinal)) { turns = degrees / 90; shape = shape[..^degrees.ToString().Length]; break; }
        if (shape == "BendLeft" && !chain) return false;
        if (shape is not ("Straight" or "Bend" or "BendLeft" or "Stopper" or "Crossing" or "Slope1" or "Slope2" or "Slope3" or "Slope4" or "RampTop" or "RampTop1" or "RampTop2" or "RampTop3" or "RampTop4")) return false;
        profile = new(shape, turns, chain, wooden, Tram:tram);
        return true;
    }

    public Vector3 Point(float t)
    {
        t = Math.Clamp(t, 0, 1);
        if (Coaster) return Rotate(CoasterPath.Find(Shape).Point(t));
        Vector3 point;
        if (this.IsBend)
        {
            var angle = MathF.PI - t * MathF.PI / 2;
            var center = this.BendCenter;
            point = new(center + this.Radius * MathF.Cos(angle), .15f, -center + this.Radius * MathF.Sin(angle));
            if (this.Mirrored) point.X = -point.X;
        }
        else if(SwitchRadius>0) point=new(0,.15f,(2*t-1)*SwitchRadius);
        else
        {
            var overlay = this.Shape.StartsWith("RampTop", StringComparison.Ordinal);
            var full = this.Shape == "RampTop";
            var slope = overlay || this.Shape.StartsWith("Slope", StringComparison.Ordinal);
            var phase = !slope || full ? 1 : int.Parse(this.Shape[(overlay ? 7 : 5)..]);
            point = new(0, .15f + (phase - 1) * .25f - (overlay ? 1 : 0) + t * (full ? 1 : slope ? .25f : 0), t - .5f);
        }
        return Vector3.Transform(point, Quaternion.CreateFromAxisAngle(Vector3.UnitY, this.QuarterTurns * MathF.PI / 2));
    }

    private Vector3 Rotate(Vector3 vector) => Vector3.Transform(vector, Quaternion.CreateFromAxisAngle(Vector3.UnitY, QuarterTurns * MathF.PI / 2));
    public Vector3 Up(float t) => Coaster ? Rotate(CoasterPath.Find(Shape).Up(t)) : Vector3.Normalize(Vector3.Cross(Tangent(t), Vector3.Cross(Vector3.UnitY, Tangent(t))));
    public Vector3 Tangent(float t) => Coaster ? Rotate(CoasterPath.Find(Shape).Tangent(t)) : Vector3.Normalize(this.Point(Math.Min(.9999f, t) + .0001f) - this.Point(Math.Max(.0001f, t) - .0001f));
    public float Curvature => float.IsPositiveInfinity(this.Radius) ? 0 : 1 / this.Radius;
    public float Length => Coaster ? CoasterPath.Find(Shape).Length : this.Curvature > 0 ? MathF.PI / 2 * this.Radius : Vector3.Distance(this.Point(0), this.Point(1));

    public (float T, Vector3 Point, float Distance) Nearest(Vector3 local)
    {
        if (Coaster)
        {
            var p = CoasterPath.Find(Shape).Nearest(Vector3.Transform(local, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -QuarterTurns * MathF.PI / 2)));
            return (p.T, Rotate(p.Point), p.Distance);
        }
        if (!this.IsBend)
        {
            var delta = this.Point(1) - this.Point(0);
            var t = Math.Clamp(Vector3.Dot(local - this.Point(0), delta) / delta.LengthSquared(), 0, 1);
            var point = this.Point(t);
            return (t, point, Vector3.Distance(point, local));
        }
        var unrotated = Vector3.Transform(local, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -this.QuarterTurns * MathF.PI / 2));
        if (this.Mirrored) unrotated.X = -unrotated.X;
        var center = this.BendCenter;
        var angle = MathF.Atan2(unrotated.Z + center, unrotated.X - center);
        if (angle < 0) angle += 2 * MathF.PI;
        var curveT = Math.Clamp((MathF.PI - angle) / (MathF.PI / 2), 0, 1);
        // Compare endpoints too for points behind the arc's angular wrap.
        foreach (var endpoint in new[] { 0f, 1f })
            if (Vector3.DistanceSquared(this.Point(endpoint), local) < Vector3.DistanceSquared(this.Point(curveT), local)) curveT = endpoint;
        return (curveT, this.Point(curveT), Vector3.Distance(this.Point(curveT), local));
    }
}
