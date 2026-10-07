namespace Eco.Minecarts.Physics;

public static class ChainLift
{
    public static float SpeedMultiplier(float value,float maximum=3) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Chain propulsion/SpeedMultiplier"); return float.IsFinite(value) ? Math.Clamp(value,.25f,maximum) : 1f; }
    public static float GridDemand(int blocks,float multiplier,float maximum=3) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Chain propulsion/GridDemand"); return Math.Max(0,blocks)*2f*SpeedMultiplier(multiplier,maximum); }
    // Coaster lifts need brisk dispatch between tall elements; ordinary minecart
    // chain rails retain their existing walking-speed target.
    public static double TargetSpeed(float multiplier, bool coaster = false) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Chain propulsion/TargetSpeed"); return (coaster ? 1d : .5d)
        * (float.IsFinite(multiplier) ? Math.Clamp(multiplier,0,5) : 0); }
    // All drives on a grid use requested (not already throttled) demand for a
    // stable proportional share. Quarter-speed steps match the user controls.
    public static float SupportedMultiplier(float requested, float totalRequestedWatts, float supply, float otherDemand,float maximum=3)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Chain propulsion/SupportedMultiplier");
        if (!float.IsFinite(totalRequestedWatts) || !float.IsFinite(supply) || !float.IsFinite(otherDemand)
            || totalRequestedWatts<=0 || supply<=0) return 0;
        var fraction=Math.Clamp((supply-Math.Max(0,otherDemand))/totalRequestedWatts,0,1);
        if(fraction>=1) return SpeedMultiplier(requested,maximum);
        return (float)Math.Floor(SpeedMultiplier(requested,maximum)*fraction*4)/4;
    }
    // A speed-governed traction force, bounded by available mechanical power and
    // chain strength. It cannot create unlimited force or drag faster carts backwards.
    public static double Force(double massKg, double grade, double speed, double watts, double targetSpeed = .5)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Chain propulsion/Force");
        if (massKg <= 0 || watts <= 0 || grade < 0 || speed > targetSpeed + .01) return 0;
        var load = massKg * 9.80665 * (grade + .008);
        var desired = load + massKg * (targetSpeed - speed) * 3;
        return Math.Clamp(desired, 0, Math.Min(15000, watts / Math.Max(.1, Math.Abs(speed))));
    }
}
