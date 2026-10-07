namespace Eco.Minecarts.Physics;

public static class TramStationBraking
{
    public const double ComfortableDeceleration=.65;
    public static double ApproachSpeed(double distance,double physicalDeceleration)=>
        Math.Sqrt(2*Math.Min(ComfortableDeceleration,Math.Max(.01,physicalDeceleration)*.7)*Math.Max(0,distance-.08));
    public static double BrakeFraction(double speed,double target,double physicalDeceleration,double gradeAlongTravel,double mass,double brakingForce)
    {
        var comfort=Math.Min(ComfortableDeceleration,Math.Max(.01,physicalDeceleration)*.7);
        var requested=Math.Min(physicalDeceleration,comfort+Math.Max(0,Math.Abs(speed)-target)*1.2);
        // Wheel inertia matches the guided solver. Uphill gravity assists the
        // brakes, downhill gravity opposes them. Rolling loss adds a small margin.
        return Math.Clamp((mass*1.06*requested-mass*9.80665*gradeAlongTravel)/Math.Max(1,brakingForce),0,1);
    }
}
