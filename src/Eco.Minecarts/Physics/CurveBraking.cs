namespace Eco.Minecarts.Physics;

public static class CurveBraking
{
    public const double ComfortableDeceleration = .65;

    public static double ApproachDeceleration(double available) =>
        Math.Min(ComfortableDeceleration, Math.Max(.01, available) * .7);

    public static double BrakeFraction(double speed, double limit, double seconds,
        double available, double gradeAlongTravel, double mass, double brakingForce)
    {
        var excess=Math.Max(0,Math.Abs(speed)-limit);
        if(excess==0)return 0;
        // Never hold a full brake for a timed release at a nonzero curve speed.
        // Match the approach envelope and avoid braking past this tick's target.
        var requested=Math.Min(Math.Max(.01,available),Math.Min(
            ApproachDeceleration(available)+excess*1.2,excess/Math.Max(.001,seconds)));
        return Math.Clamp((mass*1.06*requested-mass*9.80665*gradeAlongTravel)
            /Math.Max(1,brakingForce),0,1);
    }
}
