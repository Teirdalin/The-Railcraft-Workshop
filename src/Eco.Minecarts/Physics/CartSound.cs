namespace Eco.Minecarts.Physics;

public static class CartSound
{
    public static float CornerVolume(double signedMetersPerSecond, double curvature)
    {
        if (!double.IsFinite(signedMetersPerSecond) || !double.IsFinite(curvature) || curvature <= 0) return 0;
        return (float)(.12 * Math.Clamp((Math.Abs(signedMetersPerSecond) - .15) / 3.85, 0, 1)
            * Math.Clamp(curvature / 2, 0, 1));
    }

    // Brake pitch stays at the recorded pitch; only loudness and spark density
    // follow speed. Parked brakes must never hiss or emit sparks.
    public static (float Volume, int SparkTier) Braking(double signedMetersPerSecond, bool applied)
    {
        var speed = double.IsFinite(signedMetersPerSecond) ? Math.Abs(signedMetersPerSecond) : 0;
        if (!applied || speed <= .08) return (0, 0);
        return ((float)Math.Clamp((speed - .08) / 5.92, 0, .85), speed < 1.5 ? 1 : speed < 4 ? 2 : 3);
    }

    public static (float Volume, float Pitch) AtSpeed(double signedMetersPerSecond)
    {
        var speed = double.IsFinite(signedMetersPerSecond) ? Math.Abs(signedMetersPerSecond) : 0;
        return ((float)Math.Clamp((speed - .02) / 5.98, 0, .85), (float)Math.Clamp(.65 + speed * .16, .65, 1.8));
    }
}
