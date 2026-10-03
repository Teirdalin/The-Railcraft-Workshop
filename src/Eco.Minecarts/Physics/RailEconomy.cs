namespace Eco.Minecarts.Physics;

/// <summary>Base costs before native skill/table reductions. Physical traction is separate from fuel use.</summary>
public readonly record struct RailBuildCost(int Timber, int Metal, int HewnLogs, int Stoves, int Labor, float Minutes);

public static class RailEconomy
{
    public static RailBuildCost BuildCost(string key) => key switch
    {
        "WoodenMinecart" => new(6,0,2,0,70,2),
        "RailroadHandcar" => new(8,8,0,0,120,4),
        "HeritageTram" => new(16,32,0,1,300,8),
        "PassengerLocomotive" => new(16,40,0,1,500,12),
        "FreightLocomotive" => new(16,56,0,1,600,14),
        "PassengerCar" => new(16,20,0,0,200,6),
        "CoalTender" => new(8,16,0,0,150,4),
        "LargeTrainEngine" => new(24,96,0,1,900,20),
        "LargeCargoCar" => new(16,48,0,0,400,10),
        "LargePassengerCar" => new(24,56,0,0,500,12),
        "LargeCoalTender" => new(16,40,0,0,300,8),
        _ => throw new ArgumentOutOfRangeException(nameof(key),key,"No rolling-stock recipe budget")
    };

    public static float FuelWatts(RailVehicleSpec spec) => !spec.Powered ? 0 : spec.Key switch
    {
        "MineTrain" => 110,
        "HeritageTram" => 60,
        "PassengerLocomotive" => 240,
        "FreightLocomotive" => 450,
        "LargeTrainEngine" => 900,
        _ => (float)(spec.PowerWatts/100)
    };

    // One 100 W vanilla waterwheel supports 200 rail cells and one tram (80 W).
    // A longer network or additional trams still needs additional generation.
    public static float TramCableWatts(int rails,int trams) => rails<=0 ? 0 : 10+rails*.25f+Math.Max(0,trams)*20;

    public static float SharedCableAllocation(float supply,float otherDemand,float ownDemand,float totalCableDemand)
    {
        if(!float.IsFinite(supply)||!float.IsFinite(otherDemand)||!float.IsFinite(ownDemand)
            ||!float.IsFinite(totalCableDemand)||ownDemand<=0||totalCableDemand<=0)return 0;
        var spare=Math.Max(0,supply-Math.Max(0,otherDemand));
        return ownDemand*Math.Clamp(spare/Math.Max(ownDemand,totalCableDemand),0,1);
    }
}
