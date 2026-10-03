namespace Eco.Minecarts.Physics;

public sealed record RailVehicleSpec(string Key, string Name, double EmptyKg, double CargoKg, int Slots,
    float Length, float Wheelbase, float MinimumRadius, double MaximumSpeed, double PowerWatts,
    double TractionN, double BrakeN, double IntendedTrainKg, int PassengerSeats = 0,
    bool Pullable = false, bool Tender = false, double DurabilityHours = 200, string Model = "Cargo", string Tier = "Iron")
{
    public bool HumanPowered => Model == "Handcar";
    public bool Coaster => Model == "Coaster";
    public bool Tram => Model == "Tram";
    public bool Powered => PowerWatts > 0 && !HumanPowered;
    public bool Industrial =>
#if INDUSTRIAL_TRACKS
        Tier == "Industry";
#else
        false;
#endif
    public float HalfGauge => Industrial ? 1f : .30f;
    public float BodyWidth => Tram ? 1.34f : Coaster ? 1.2f : Industrial ? 2.2f : .84f;
    public double MaximumAcceleration => Model == "Express" ? 2.5 : Model is "Freight" or "LargeEngine" ? .65 : 1.5;
    public static readonly RailVehicleSpec Minecart = new("Minecart", "Minecart", 280, 2500, 12, 1.612f, .82f, .5f, 18, 0, 0, 4800, 0, Pullable: true);
    public static readonly RailVehicleSpec MineTrain = new("MineTrain", "Mine Train", 600, 250, 4, 1.86f, .82f, .5f, 30, 12000, 5500, 6000, 8000, Model: "Engine");
    public static readonly RailVehicleSpec[] Additional =
    [
        new("HeritageTram", "Heritage Tram", 1250, 100, 4, 2.8f, 1.7f, .5f, 20, 9000, 7000, 9000, 2500, PassengerSeats:6, Model:"Tram"),
        new("RollerCoasterCart", "Roller Coaster Cart", 300, 0, 1, 1.8f, .8f, .5f, 40, 0, 0, 7000, 0, PassengerSeats: 2, Model: "Coaster"),
        new("RailroadHandcar", "Railroad Handcar", 180, 100, 4, 1.9f, 1.0f, .5f, 5, 450, 700, 2500, 900, Model: "Handcar"),
        new("WoodenMinecart", "Wooden Minecart", 80, 400, 8, 1.612f, .82f, .5f, 12, 0, 0, 1300, 0, Pullable: true, DurabilityHours: 40, Model: "Wood", Tier: "Wood"),
        new("PassengerLocomotive", "Passenger Locomotive", 1100, 150, 4, 2.6f, 1.25f, .5f, 30, 38000, 5000, 8500, 5000, Model: "Express", Tier: "Steel"),
        new("FreightLocomotive", "Heavy-Haul Locomotive", 2200, 400, 6, 3.0f, 1.6f, 1.0f, 30, 75000, 18000, 13000, 25000, Model: "Freight", Tier: "Steel"),
        new("PassengerCar", "Passenger Car", 650, 100, 4, 2.4f, 1.2f, .5f, 30, 0, 0, 2200, 0, PassengerSeats: 4, Model: "Passenger"),
        new("CoalTender", "Coal Tender", 480, 3000, 12, 2.0f, 1.0f, .5f, 30, 0, 0, 1800, 0, Tender: true, Model: "Tender"),
        new("LargeTrainEngine", "Large Train Engine", 7000, 1000, 8, 4.8f, 2.6f, 2.5f, 30, 250000, 70000, 40000, 100000, Model: "LargeEngine", Tier: "Industry"),
        new("LargeCargoCar", "Large Cargo Car", 5000, 20000, 48, 4.4f, 2.8f, 2.5f, 30, 0, 0, 8000, 0, Model: "LargeCargo", Tier: "Industry"),
        new("LargePassengerCar", "Large Passenger Car", 4500, 500, 8, 4.6f, 2.8f, 2.5f, 30, 0, 0, 6500, 0, PassengerSeats: 8, Model: "LargePassenger", Tier: "Industry"),
        new("LargeCoalTender", "Large Coal Tender", 3500, 12000, 32, 3.8f, 2.4f, 2.5f, 30, 0, 0, 6000, 0, Tender: true, Model: "LargeTender", Tier: "Industry")
    ];
    public static RailVehicleSpec Find(string key) => key == Minecart.Key ? Minecart : key == MineTrain.Key ? MineTrain : Additional.Single(x => x.Key == key);
}

public readonly record struct TrainLoad(RailVehicleSpec Spec, double CargoKg, double FuelKg, int Occupants)
{
    public double Mass => Spec.EmptyKg + Math.Max(0, CargoKg) + Math.Max(0, FuelKg) + Math.Max(0, Occupants) * 80;
}

public sealed class TrainPerformance(IReadOnlyList<TrainLoad> cars, RailVehicleSpec engine)
{
    public IReadOnlyList<TrainLoad> Cars { get; } = cars;
    public double Mass => Cars.Sum(x => x.Mass);
    public double Length => Cars.Sum(x => x.Spec.Length + .1);
    public double BrakingForce => Cars.Sum(x => x.Spec.BrakeN);
    public double MinimumRadius => Cars.Max(x => x.Spec.MinimumRadius);
    // Additional length adds air/rolling losses; over-capacity speed falls smoothly.
    public double SpeedLimit => Math.Min(Cars.Min(x => x.Spec.MaximumSpeed), engine.MaximumSpeed
        / Math.Pow(engine.Powered || engine.HumanPowered ? Math.Max(1, Mass / Math.Max(1, engine.IntendedTrainKg)) : 1, engine.Model == "Express" ? 1.2 : .65));
    public double DriveForce(double speed) => Math.Min(Mass * engine.MaximumAcceleration,
        Math.Min(engine.TractionN, engine.PowerWatts / Math.Max(1, Math.Abs(speed))))
        * Math.Clamp(1 - Math.Pow(Math.Abs(speed) / Math.Max(.1, SpeedLimit), 4), 0, 1);
    public double Resistance => Mass * .008 * 9.80665 + Cars.Count * 22 + Length * 5;
    public double BrakeDeceleration(double downhillGrade = 0) => Math.Max(.05, BrakingForce / (Mass * 1.06) - 9.80665 * Math.Max(0, downhillGrade));
    public double StoppingDistance(double speed, double downhillGrade = 0) => speed * speed / (2 * BrakeDeceleration(downhillGrade)) + Math.Abs(speed) * .3 + 1;
    public bool Fits(double radius) => double.IsPositiveInfinity(radius) || radius + .001 >= MinimumRadius;
    public double CurveSpeed(double radius) => double.IsPositiveInfinity(radius) ? SpeedLimit : Math.Min(SpeedLimit, Math.Sqrt(radius * LateralLimit));
    public bool ExceedsCurveLimit(double speed, double curvature, bool retainedCart) =>
        !retainedCart && speed * speed * curvature > LateralLimit * 1.12;
    public double LateralLimit => Math.Clamp(14 / (1 + Cars.Sum(x => x.CargoKg) / Math.Max(1, Cars.Sum(x => x.Spec.EmptyKg)) * .10), 5, 14);
}

public readonly record struct DepartureRules(bool Timed, double Seconds, bool AtLeast, double MinimumPercent,
    bool AtMost, double MaximumPercent, bool Loaded, bool Unloaded, bool All)
{
    public bool Ready(double elapsed, double percent, bool full, bool empty)
    {
        var enabled = new List<bool>();
        if (Timed) enabled.Add(elapsed >= Math.Max(0, Seconds));
        if (AtLeast) enabled.Add(percent >= Math.Clamp(MinimumPercent, 0, 100));
        if (AtMost) enabled.Add(percent <= Math.Clamp(MaximumPercent, 0, 100));
        if (Loaded) enabled.Add(full);
        if (Unloaded) enabled.Add(empty);
        return enabled.Count != 0 && (All ? enabled.All(x => x) : enabled.Any(x => x));
    }
}
