namespace Eco.Minecarts.Physics;

public static class TramPowerRules
{
    public static bool UsesCable(bool tramRail,double availableFraction){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Tram propulsion/UsesCable"); return tramRail&&double.IsFinite(availableFraction)&&availableFraction>.05; }
    public static double SpeedLimit(double maximumSpeed,bool tramRail,double availableFraction)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Tram propulsion/SpeedLimit");
        var full=Math.Max(0,maximumSpeed);
        return UsesCable(tramRail,availableFraction) ? full*Math.Sqrt(Math.Clamp(availableFraction,0,1)) : 0;
    }
}
