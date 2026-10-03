using System.Numerics;

namespace Eco.Minecarts.Physics;

/// <summary>Bounded manual ground motion; every 5 cm is checked, including on slow server ticks.</summary>
public static class GroundMotion
{
    public const float MaximumSpeed = 1.5f;
    public static Vector3 DesiredVelocity(Vector3 movement, double elapsed, Vector3 gripError)
    {
        if (elapsed <= 0 || !HandcartControl.IsContinuousMovement(movement, elapsed)) return Vector3.Zero;
        var desired = movement / (float)elapsed + gripError * 2;
        desired.Y = 0;
        return desired.Length() > MaximumSpeed ? Vector3.Normalize(desired) * MaximumSpeed : desired;
    }

    public static Vector3 Step(Vector3 position, ref Vector3 velocity, Vector3 desired, double mass,
        double dt, bool brake, Func<Vector3, Vector3?> resolveGround)
    {
        if (brake || dt <= 0) { velocity = Vector3.Zero; return position; }
        mass = Math.Max(280, mass);
        var force = (desired - velocity) * 650;
        force.Y = 0;
        if (force.Length() > 650) force = Vector3.Normalize(force) * 650;
        velocity += force * (float)(dt / mass);
        var speed = velocity.Length();
        var reduction = (float)(.045 * 9.81 * dt);
        velocity = speed <= reduction ? Vector3.Zero : velocity * ((speed - reduction) / speed);
        if (velocity.Length() > MaximumSpeed) velocity = Vector3.Normalize(velocity) * MaximumSpeed;
        var distance = velocity * (float)dt;
        var count = Math.Max(1, (int)Math.Ceiling(distance.Length() / .05));
        for (var step = 0; step < count; step++)
        {
            var next = resolveGround(position + distance / count);
            if (next == null) { velocity = Vector3.Zero; break; }
            position = next.Value;
        }
        return position;
    }
}
