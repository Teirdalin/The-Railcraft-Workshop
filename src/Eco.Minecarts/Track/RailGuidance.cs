using System.Numerics;

namespace Eco.Minecarts.Track;

/// <summary>Bounded rail capture and wheelbase-aware cart poses.</summary>
public static class RailGuidance
{
    public const float HalfWheelbase = .41f;
    /// <summary>Small packet-time corrections rather than heading deadbands followed by large snaps.</summary>
    public static (Vector3 Position, Vector3 Forward) SmoothNativePose(Vector3 position, Vector3 forward,
        Vector3 target, Vector3 targetForward, double seconds)
    {
        var dt=(float)Math.Clamp(double.IsFinite(seconds)?seconds:.05,.005,.15);
        var offset=target-position;
        var positionBlend=1-MathF.Exp(-dt/.12f);
        // Preserve suspension movement inside the rail-contact tolerance.
        var corrected=offset.LengthSquared()<.035f*.035f?position:position+offset*positionBlend;
        var from=Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, -Vector3.Normalize(forward), Vector3.UnitY));
        var to=Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(Vector3.Zero, -Vector3.Normalize(targetForward), Vector3.UnitY));
        var heading=Quaternion.Slerp(from,to,1-MathF.Exp(-dt/.075f));
        return (corrected,Vector3.Transform(Vector3.UnitZ,heading));
    }
    /// <summary>Push away from the player's entry end, independent of view direction or pitch.</summary>
    public static int SelectShoveDirection(Vector3 playerPosition, Vector3 cartPosition, Vector3 cartForward)
    {
        // Height must not reverse the shove on a slope or when reaching up
        // from below a car. At its exact side midpoint, consistently use forward.
        var offset = cartPosition - playerPosition;
        offset.Y = 0; cartForward.Y = 0;
        return Vector3.Dot(offset, cartForward) >= 0 ? 1 : -1;
    }
    /// <summary>Native drivers already integrate acceleration; constrain only unsafe speed.</summary>
    public static double LimitNativeTravel(double travel, double speedLimit, double seconds)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/LimitNativeTravel");
        if (!double.IsFinite(travel) || !double.IsFinite(speedLimit) || !double.IsFinite(seconds) || speedLimit <= 0 || seconds <= 0) return 0;
        return Math.CopySign(Math.Min(Math.Abs(travel), speedLimit * Math.Min(seconds, .3) + .10), travel);
    }
    public static int SelectPassengerSeat(Vector3 look, Vector3 cartForward, Vector3 entryOffset)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/SelectPassengerSeat");
        // Preserve the closest longitudinal facing, ignoring view pitch. Use
        // entry-end inward facing only when looking across the cart or vertically.
        look.Y = 0; cartForward.Y = 0; entryOffset.Y = 0;
        var alignment = Vector3.Dot(look, cartForward);
        if (Math.Abs(alignment) > .15f) return alignment > 0 ? 1 : 2;
        return Vector3.Dot(entryOffset, cartForward) > 0 ? 2 : 1;
    }
    public const float BufferClearance = 1f; // .87 m nose + .075 m buffer half-depth + native correction margin.

    public static double LimitBufferTravel(VoxelRail rail, float t, double travel,
        Func<VoxelRail, int, (VoxelRail Rail, int End)?> neighbor, float clearance = BufferClearance)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/LimitBufferTravel");
        if (travel == 0) return 0;
        var direction = Math.Sign(travel);
        var localDirection = direction;
        var distance = t * rail.Profile.Length;
        var walked = 0d;
        for (var crossed = 0; crossed < 32 && walked <= Math.Abs(travel) + clearance; crossed++)
        {
            // The exported buffer beam is at local z=.30, i.e. t=.80.
            if (rail.Profile.Shape == "Stopper")
            {
                var ahead = (.8f * rail.Profile.Length - distance) * localDirection;
                if (ahead >= 0)
                    return direction * Math.Min(Math.Abs(travel), Math.Max(0, walked + ahead - clearance));
            }
            var end = localDirection > 0 ? 1 : 0;
            walked += localDirection > 0 ? rail.Profile.Length - distance : distance;
            if (neighbor(rail, end) is not { } next) break;
            rail = next.Rail;
            localDirection = next.End == 0 ? 1 : -1;
            distance = next.End == 0 ? 0 : rail.Profile.Length;
        }
        return travel;
    }

    public static (VoxelRail Rail, float T) Advance(VoxelRail rail, float t, double travel,
        Func<VoxelRail, int, (VoxelRail Rail, int End)?> neighbor)
        { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Advance"); return Advance(rail, t, travel, neighbor, out _); }

    public static (VoxelRail Rail, float T) Advance(VoxelRail rail, float t, double travel,
        Func<VoxelRail, int, (VoxelRail Rail, int End)?> neighbor, out int orientation)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Advance");
        orientation = 1;
        var distance = t * rail.Profile.Length + (float)travel;
        for (var crossed = 0; crossed < 8; crossed++)
        {
            if (distance >= 0 && distance <= rail.Profile.Length) break;
            var end = distance < 0 ? 0 : 1;
            var overshoot = distance < 0 ? -distance : distance - rail.Profile.Length;
            if (neighbor(rail, end) is not { } next) { distance = Math.Clamp(distance, 0, rail.Profile.Length); break; }
            if (end == next.End) orientation = -orientation;
            rail = next.Rail;
            distance = next.End == 0 ? overshoot : rail.Profile.Length - overshoot;
        }
        return (rail, Math.Clamp(distance / rail.Profile.Length, 0, 1));
    }

    public static bool ShouldPublishPose(Vector3 previous, Vector3 next,
        Vector3 previousForward, Vector3 nextForward, Vector3 previousVelocity, Vector3 nextVelocity) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/ShouldPublishPose"); return Vector3.DistanceSquared(previous, next) >= .000004f // 2 mm; cumulative, not per-step.
        || Vector3.Dot(previousForward, nextForward) < .999999f
        || Vector3.DistanceSquared(previousVelocity, nextVelocity) >= .0001f
        || (previousVelocity != Vector3.Zero && nextVelocity == Vector3.Zero); }

    public static bool CanCapture(Vector3 offset) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/CanCapture"); return new Vector2(offset.X, offset.Z).Length() <= .24f && Math.Abs(offset.Y) <= .32f; }

    public static Vector3 DecayOffset(Vector3 offset, double seconds) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/DecayOffset"); return offset * (float)Math.Exp(-Math.Max(0, seconds) / .18); }

    public static double ReleaseSpeed(Vector3 velocity, Vector3 tangent) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/ReleaseSpeed"); return float.IsFinite(velocity.X) && float.IsFinite(velocity.Y) && float.IsFinite(velocity.Z)
            ? Math.Clamp(Vector3.Dot(velocity, tangent), -12, 12) : 0; }

    public static (float T, Vector3 Point, float Distance) ProjectPose(VoxelRail rail, Vector3 position,
        Func<VoxelRail, int, (VoxelRail Rail, int End)?> neighbor, bool horizontalOnly = false, float halfWheelbase = HalfWheelbase)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/ProjectPose");
        if (rail.Profile.Coaster)
        {
            var hit = rail.Profile.Nearest(position - rail.Cell.Origin);
            return (hit.T, hit.Point + rail.Cell.Origin, hit.Distance);
        }
        float Error(Vector3 point)
        {
            var offset = point - position;
            if (horizontalOnly) offset.Y = 0;
            return offset.LengthSquared();
        }
        var low = 0f; var high = 1f;
        for (var i = 0; i < 16; i++)
        {
            var a = low + (high - low) / 3;
            var b = high - (high - low) / 3;
            if (Error(PosePosition(rail, a, neighbor, halfWheelbase))
                < Error(PosePosition(rail, b, neighbor, halfWheelbase))) high = b;
            else low = a;
        }
        var t = (low + high) * .5f;
        foreach (var end in new[] { 0f, 1f })
            if (Error(PosePosition(rail, end, neighbor, halfWheelbase))
                < Error(PosePosition(rail, t, neighbor, halfWheelbase))) t = end;
        var point = PosePosition(rail, t, neighbor, halfWheelbase);
        return (t, point, MathF.Sqrt(Error(point)));
    }

    public static Vector3 PosePosition(VoxelRail rail, float t,
        Func<VoxelRail, int, (VoxelRail Rail, int End)?>? neighbor = null, float halfWheelbase = HalfWheelbase)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/PosePosition");
        neighbor ??= (_, _) => null;
        if (rail.Profile.Coaster) return rail.Point(t);
        var rear = Sample(rail, t * rail.Profile.Length - halfWheelbase, neighbor);
        var front = Sample(rail, t * rail.Profile.Length + halfWheelbase, neighbor);
        return (rear + front) * .5f;
    }

    public static Vector3 AxleTangent(VoxelRail rail, float t,
        Func<VoxelRail, int, (VoxelRail Rail, int End)?> neighbor, float halfWheelbase = HalfWheelbase)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/AxleTangent");
        if (rail.Profile.Coaster) return rail.Profile.Tangent(t);
        var rear = Sample(rail, t * rail.Profile.Length - halfWheelbase, neighbor);
        var front = Sample(rail, t * rail.Profile.Length + halfWheelbase, neighbor);
        var chord = front - rear;
        return chord.LengthSquared() > .0001f ? Vector3.Normalize(chord) : rail.Profile.Tangent(t);
    }

    private static Vector3 Sample(VoxelRail rail, float distance,
        Func<VoxelRail, int, (VoxelRail Rail, int End)?> neighbor)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Sample");
        for (var crossed = 0; crossed < 8; crossed++)
        {
            if (distance >= 0 && distance <= rail.Profile.Length)
                return rail.Point(distance / rail.Profile.Length);
            var end = distance < 0 ? 0 : 1;
            var overshoot = distance < 0 ? -distance : distance - rail.Profile.Length;
            if (neighbor(rail, end) is not { } next)
                return rail.Point(end) + rail.Profile.Tangent(end) * (end == 0 ? -overshoot : overshoot);
            rail = next.Rail;
            distance = next.End == 0 ? overshoot : rail.Profile.Length - overshoot;
        }
        return rail.Point(Math.Clamp(distance / rail.Profile.Length, 0, 1));
    }
}
