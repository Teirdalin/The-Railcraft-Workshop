using System.Numerics;

namespace Eco.Minecarts.Physics;

/// <summary>Oriented bounds and swept contact for server-driven rail vehicles.</summary>
public readonly record struct VehicleBounds(Vector3 Position, Quaternion Rotation, Vector3 HalfSize)
{
    public Vector3 Center => this.Position + Vector3.Transform(new Vector3(0, this.HalfSize.Y + .08f, 0), this.Rotation);
    public bool Overlaps(VehicleBounds other)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/Overlaps");
        ReadOnlySpan<Vector3> axes = stackalloc Vector3[] { Vector3.Transform(Vector3.UnitX, this.Rotation), Vector3.Transform(Vector3.UnitY, this.Rotation), Vector3.Transform(Vector3.UnitZ, this.Rotation) };
        ReadOnlySpan<Vector3> theirs = stackalloc Vector3[] { Vector3.Transform(Vector3.UnitX, other.Rotation), Vector3.Transform(Vector3.UnitY, other.Rotation), Vector3.Transform(Vector3.UnitZ, other.Rotation) };
        ReadOnlySpan<float> sizes = stackalloc float[] { this.HalfSize.X, this.HalfSize.Y, this.HalfSize.Z };
        ReadOnlySpan<float> others = stackalloc float[] { other.HalfSize.X, other.HalfSize.Y, other.HalfSize.Z };
        var offset = other.Center - this.Center;
        for(var i=0;i<3;i++) if(Separated(axes[i],axes,theirs,sizes,others,offset)) return false;
        for(var i=0;i<3;i++) if(Separated(theirs[i],axes,theirs,sizes,others,offset)) return false;
        foreach (var a in axes) foreach (var b in theirs) if (Separated(Vector3.Cross(a,b),axes,theirs,sizes,others,offset)) return false;
        return true;
    }
    private static bool Separated(Vector3 axis,ReadOnlySpan<Vector3> axes,ReadOnlySpan<Vector3> theirs,
        ReadOnlySpan<float> sizes,ReadOnlySpan<float> others,Vector3 offset)
        {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/Separated");
            if (axis.LengthSquared() < .000001f) return false;
            var span = 0f;
            for (var i = 0; i < 3; i++) span += sizes[i] * Math.Abs(Vector3.Dot(axes[i], axis)) + others[i] * Math.Abs(Vector3.Dot(theirs[i], axis));
            return Math.Abs(Vector3.Dot(offset, axis)) >= span - .001f;
        }

    public static float TravelFraction(VehicleBounds from, VehicleBounds to, IEnumerable<VehicleBounds> obstacles)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/TravelFraction");
        // Use bounding radii, not a fixed four-block neighbourhood: long coaches
        // can touch while their origins are much farther apart. Include the
        // rotated centre offset because the vehicle pivot is at rail height.
        var sweepRadius = Vector3.Distance(from.Position, to.Position)
            + from.HalfSize.Length() + from.HalfSize.Y + .08f;
        var nearby = obstacles.Where(x => Vector3.DistanceSquared(x.Position, from.Position)
            <= MathF.Pow(sweepRadius + x.HalfSize.Length() + x.HalfSize.Y + .08f, 2)).ToArray();
        // No obstacle intersects the conservative swept bounding radius.
        // Avoid sampling rotations along an empty path, especially long
        // endpoint/world-wrap corrections reported during coaster jumps.
        if (nearby.Length == 0) return 1;
        var steps = Math.Max(1, (int)Math.Ceiling(Vector3.Distance(from.Position, to.Position) / .04f));
        steps = Math.Max(steps, 12); // Also sweep rotation through narrow turns.
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var pose = new VehicleBounds(Vector3.Lerp(from.Position, to.Position, t), Quaternion.Slerp(from.Rotation, to.Rotation, t), from.HalfSize);
            foreach (var obstacle in nearby)
            {
                if (!pose.Overlaps(obstacle)) continue;
                // Existing overlapping saved placements may move apart.
                if (from.Overlaps(obstacle) && Vector3.DistanceSquared(pose.Center, obstacle.Center) > Vector3.DistanceSquared(from.Center, obstacle.Center) + .00001f) continue;
                return (i - 1f) / steps;
            }
        }
        return 1;
    }
}
