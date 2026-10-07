namespace Eco.Minecarts.Physics;

/// <summary>Passive friction above chain speed; existing chain traction takes over below it.</summary>
public static class CoasterChainBrake
{
    public static double Force(double massKg, double speed, double targetSpeed)
    {
        if (!double.IsFinite(massKg) || !double.IsFinite(speed) || !double.IsFinite(targetSpeed) || massKg <= 0) return 0;
        var excess = Math.Abs(speed) - Math.Max(0, targetSpeed);
        if (excess <= 0) return 0;
        // At most 4 m/s², tapering near chain speed: never a velocity overwrite.
        return -Math.Sign(speed) * massKg * Math.Min(4, excess * 3);
    }
}
