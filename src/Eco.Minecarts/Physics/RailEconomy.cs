namespace Eco.Minecarts.Physics;

/// <summary>Base costs before native skill/table reductions. Physical traction is separate from fuel use.</summary>
public readonly record struct RailBuildCost(int Timber, int Metal, int HewnLogs, int Stoves, int Labor, float Minutes, int Fabric = 0);

public static class RailEconomy
{
    public static RailBuildCost BuildCost(string key) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/BuildCost"); return key switch
    {
        "WoodenMinecart" => new(6,0,2,0,70,2),
        "RailroadHandcar" => new(10,10,0,0,140,5),
        "HeritageTram" => new(20,36,0,1,360,10,12),
        "PassengerLocomotive" => new(16,40,0,1,500,12,4),
        "FreightLocomotive" => new(16,56,0,1,600,14,4),
        "PassengerCar" => new(20,20,0,0,240,7,16),
        "CoalTender" => new(8,16,0,0,150,4),
        "LargeTrainEngine" => new(24,96,0,1,900,20,4),
        "LargeCargoCar" => new(16,48,0,0,400,10),
        "LargePassengerCar" => new(24,48,0,0,500,12,32),
        "LargeCoalTender" => new(16,40,0,0,300,8),
        _ => throw new ArgumentOutOfRangeException(nameof(key),key,"No rolling-stock recipe budget")
    }; }

    public static float FuelWatts(RailVehicleSpec spec) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/FuelWatts"); return !spec.Powered || spec.Tram ? 0 : spec.Key switch
    {
        "MineTrain" => 110,
        "PassengerLocomotive" => 240,
        "FreightLocomotive" => 450,
        "LargeTrainEngine" => 900,
        _ => (float)(spec.PowerWatts/100)
    }; }

    // One 100 W vanilla waterwheel supports 200 rail cells and one tram (80 W).
    // A longer network or additional trams still needs additional generation.
    public static float TramCableWatts(int rails,int trams) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/TramCableWatts"); return rails<=0 ? 0 : 10+rails*.25f+Math.Max(0,trams)*20; }

    public static float SharedCableAllocation(float supply,float otherDemand,float ownDemand,float totalCableDemand)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/SharedCableAllocation");
        if(!float.IsFinite(supply)||!float.IsFinite(otherDemand)||!float.IsFinite(ownDemand)
            ||!float.IsFinite(totalCableDemand)||ownDemand<=0||totalCableDemand<=0)return 0;
        var spare=Math.Max(0,supply-Math.Max(0,otherDemand));
        return ownDemand*Math.Clamp(spare/Math.Max(ownDemand,totalCableDemand),0,1);
    }
}
