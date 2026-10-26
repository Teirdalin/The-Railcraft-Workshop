namespace Eco.Minecarts.Physics;

/// <summary>Base costs before native skill/table reductions. Metal counts are plates; physical traction is separate from fuel use.</summary>
public readonly record struct RailBuildCost(int Timber, int Metal, int HewnLogs, int Stoves, int Labor, float Minutes, int Fabric = 0);

public static class RailEconomy
{
    public static RailBuildCost BuildCost(string key) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/BuildCost"); return key switch
    {
        "WoodenMinecart" => new(6,0,2,0,70,1),
        "RailroadHandcar" => new(10,7,0,0,140,2.5f),
        "HeritageTram" => new(20,24,0,1,360,5,12),
        "PassengerLocomotive" => new(16,9,0,1,500,6,4),
        "FreightLocomotive" => new(16,12,0,1,600,7,4),
        "PassengerCar" => new(20,13,0,0,240,3.5f,16),
        "CoalTender" => new(8,11,0,0,150,2),
        "LargeTrainEngine" => new(24,21,0,1,900,10,4),
        "LargeCargoCar" => new(16,11,0,0,400,5),
        "LargePassengerCar" => new(24,11,0,0,500,6,32),
        "LargeCoalTender" => new(16,9,0,0,300,4),
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

    // Preserve each locomotive's existing maximum. Haul mass includes every
    // coupled vehicle, cargo, fuel and occupant, not just the engine inventory.
    // Empty running costs 20% of the cap; rated and larger hauls use the cap.
    public static float HaulFuelWatts(RailVehicleSpec spec, double consistMassKg, double throttle)
    {
        var maximum = FuelWatts(spec);
        if (maximum <= 0 || !double.IsFinite(consistMassKg) || !double.IsFinite(throttle)) return 0;
        var load = Math.Clamp((consistMassKg - spec.EmptyKg) / Math.Max(1, spec.IntendedTrainKg - spec.EmptyKg), 0, 1);
        return (float)(maximum * (.20 + .80 * load) * Math.Clamp(throttle, 0, 1));
    }

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
