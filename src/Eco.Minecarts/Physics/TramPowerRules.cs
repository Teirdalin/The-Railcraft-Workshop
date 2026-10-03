namespace Eco.Minecarts.Physics;

public static class TramPowerRules
{
    public static bool UsesCable(bool tramRail,double availableFraction)=>tramRail&&double.IsFinite(availableFraction)&&availableFraction>.05;
    public static bool UsesOnboardFuel(bool tramRail)=>!tramRail;
    public static double SpeedLimit(double maximumSpeed,bool tramRail,double availableFraction)
    {
        var full=Math.Max(0,maximumSpeed);
        return tramRail ? UsesCable(true,availableFraction) ? full*Math.Sqrt(Math.Clamp(availableFraction,0,1)) : 0 : full/3;
    }
}
