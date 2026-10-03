using System.Numerics;

namespace Eco.Minecarts.Physics;

public static class HandleConstraint
{
    public const float StandOff = 1.15f;
    public const float Tolerance = .06f;

    // Keep the player upright and leave gravity/steps to their character controller.
    public static Vector3 Anchor(Vector3 cart, Quaternion rotation, int side, float playerY)
    {
        var forward = Vector3.Transform(Vector3.UnitZ, rotation);
        forward.Y = 0;
        if (forward.LengthSquared() < .0001f) forward = Vector3.UnitZ;
        var anchor = cart + Vector3.Normalize(forward) * (side < 0 ? -StandOff : StandOff);
        anchor.Y = playerY;
        return anchor;
    }

    public static bool ClearSweep(Vector3 from, Vector3 to, Func<Vector3, bool> clear)
    {
        var distance = Vector3.Distance(from, to);
        if (!float.IsFinite(distance) || distance > 3.5f) return false;
        var steps = Math.Max(1, (int)Math.Ceiling(distance / .05f));
        for (var i = 0; i <= steps; i++)
            if (!clear(Vector3.Lerp(from, to, i / (float)steps))) return false;
        return true;
    }
}
