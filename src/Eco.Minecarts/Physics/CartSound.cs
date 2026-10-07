namespace Eco.Minecarts.Physics;

public static class CartSound
{
    public static float CornerVolume(double signedMetersPerSecond, double curvature)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/CornerVolume");
        if (!double.IsFinite(signedMetersPerSecond) || !double.IsFinite(curvature) || curvature <= 0) return 0;
        return (float)(.12 * Math.Clamp((Math.Abs(signedMetersPerSecond) - .15) / 3.85, 0, 1)
            * Math.Clamp(curvature / 2, 0, 1));
    }

    // Brake pitch stays at the recorded pitch; only loudness and spark density
    // follow speed. Parked brakes must never hiss or emit sparks.
    public static (float Volume, int SparkTier) Braking(double signedMetersPerSecond, bool applied)
        =>Braking(signedMetersPerSecond,applied?1d:0d);
    public static (float Volume, int SparkTier) Braking(double signedMetersPerSecond, double effort)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/Braking");
        var speed = double.IsFinite(signedMetersPerSecond) ? Math.Abs(signedMetersPerSecond) : 0;
        effort=double.IsFinite(effort)?Math.Clamp(effort,0,1):0;
        // Ignore crawl-speed corrections and small nudges. A squared envelope
        // keeps slow braking subdued instead of opening the squeal linearly.
        if (effort<=0 || speed <= .35) return (0, 0);
        var envelope=Math.Clamp((speed-.35)/7.65,0,1);
        return ((float)(.85*envelope*envelope*effort),
            effort<.25?0:speed < 1.5 ? 1 : speed < 4 || effort<.65 ? 2 : 3);
    }

    public static (float Volume, float Pitch) AtSpeed(double signedMetersPerSecond)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/AtSpeed");
        var speed = double.IsFinite(signedMetersPerSecond) ? Math.Abs(signedMetersPerSecond) : 0;
        return ((float)Math.Clamp((speed - .02) / 5.98, 0, .85), (float)Math.Clamp(.65 + speed * .16, .65, 1.8));
    }
}
