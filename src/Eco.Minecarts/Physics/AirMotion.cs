using System.Numerics;

namespace Eco.Minecarts.Physics;

/// <summary>Server-owned ballistic motion after leaving an open rail.</summary>
public static class AirMotion
{
    public static (Vector3 Position, Vector3 Velocity) Step(Vector3 position, Vector3 velocity, double dt)
    {
        var gravity = new Vector3(0, -9.80665f, 0);
        var seconds = (float)Math.Max(0, dt);
        return (position + velocity * seconds + gravity * (.5f * seconds * seconds), velocity + gravity * seconds);
    }
}
