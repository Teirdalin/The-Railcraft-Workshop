using System.Numerics;

namespace Eco.Minecarts.Physics;

/// <summary>Tick-rate independent walking input, with bounded physical push force.</summary>
public static class HandcartControl
{
    public const float MaximumWalkingSpeed = 2.5f;

    public static bool IsContinuousMovement(Vector3 delta, double seconds) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/IsContinuousMovement"); return seconds > 0 && float.IsFinite(delta.LengthSquared()) && delta.Length() <= 1 + 8 * seconds; }

    public static float DesiredSpeed(Vector3 delta, double seconds, Vector3 tangent, float gripError) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/DesiredSpeed"); return Math.Clamp(Vector3.Dot(delta / (float)seconds, tangent) + gripError * 2,
            -MaximumWalkingSpeed, MaximumWalkingSpeed); }

    public static double Force(double desiredSpeed, double cartSpeed) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/Force"); return Math.Clamp((desiredSpeed - cartSpeed) * 650, -650, 650); }
}
