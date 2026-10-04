using System.Numerics;

namespace Eco.Minecarts.Physics;

/// <summary>Server-owned ballistic motion after leaving an open rail.</summary>
public static class AirMotion
{
    public static Quaternion FollowTrajectory(Quaternion current, Vector3 velocity, int facingSign, double seconds)
    {
        current = Quaternion.Normalize(current);
        if (velocity.LengthSquared() < .0025f || !float.IsFinite(velocity.LengthSquared())) return current;
        var forward = Vector3.Transform(Vector3.UnitZ, current);
        var targetForward = Vector3.Normalize(velocity) * (facingSign < 0 ? -1 : 1);
        var dot = Math.Clamp(Vector3.Dot(forward, targetForward), -1, 1);
        if (dot > .999999f) return current;
        var axis = Vector3.Cross(forward, targetForward);
        // Transport the existing orientation along the arc. Rebuilding a look
        // rotation with world-up discards bank and chooses a different sideways
        // axis at vertical ascent/descent. At the exact apex reversal, pitch
        // around the cart's own axle instead of inventing a yaw/roll direction.
        if (axis.LengthSquared() < .00000001f)
            axis = Vector3.Transform(Vector3.UnitX, current);
        else axis = Vector3.Normalize(axis);
        var arc = Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
        return TurnTowards(current, Quaternion.Normalize(arc * current), seconds);
    }

    public static Quaternion TurnTowards(Quaternion current, Quaternion target, double seconds, float radiansPerSecond = 2.4f)
    {
        if (seconds <= 0 || !double.IsFinite(seconds) || radiansPerSecond <= 0) return Quaternion.Normalize(current);
        current = Quaternion.Normalize(current);
        target = Quaternion.Normalize(target);
        var angle = 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(current, target)), 0, 1));
        if (angle < .0001f) return target;
        var fraction = Math.Clamp((float)(seconds * radiansPerSecond) / angle, 0, 1);
        return Quaternion.Normalize(Quaternion.Slerp(current, target, fraction));
    }

    public static (Vector3 Position, Vector3 Velocity) Step(Vector3 position, Vector3 velocity, double dt)
    {
        var gravity = new Vector3(0, -9.80665f, 0);
        var seconds = (float)Math.Max(0, dt);
        return (position + velocity * seconds + gravity * (.5f * seconds * seconds), velocity + gravity * seconds);
    }
}
