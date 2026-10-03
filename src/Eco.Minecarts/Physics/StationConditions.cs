using Eco.Shared.Items;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Physics;

[Serialized]
public enum StationConditionKind { WaitSeconds, CargoAtLeastPercent, CargoAtMostPercent, CarsFull, CarsEmpty }

[Serialized]
public sealed class StationDepartureCondition
{
    [Serialized] public StationConditionKind Kind { get; set; }
    [Serialized] public double Threshold { get; set; }
    public StationDepartureCondition() { }
    public StationDepartureCondition(StationConditionKind kind, double threshold)
    { Kind = kind; Threshold = Normalize(kind, threshold); }
    public static double Normalize(StationConditionKind kind, double value) => !double.IsFinite(value) ? 0
        : kind == StationConditionKind.WaitSeconds ? Math.Clamp(value, 0, 86400) : Math.Clamp(value, 0, 100);
    public bool Test(double elapsed, double percent, bool full, bool empty) => Kind switch
    {
        StationConditionKind.WaitSeconds => double.IsFinite(elapsed) && elapsed >= Normalize(Kind, Threshold),
        StationConditionKind.CargoAtLeastPercent => double.IsFinite(percent) && percent >= Normalize(Kind, Threshold),
        StationConditionKind.CargoAtMostPercent => double.IsFinite(percent) && percent <= Normalize(Kind, Threshold),
        StationConditionKind.CarsFull => full,
        StationConditionKind.CarsEmpty => empty,
        _ => false
    };
    public static bool Ready(IReadOnlyList<StationDepartureCondition> rules, RequiredTrue comparison,
        double elapsed, double percent, bool full, bool empty, bool cargoKnown = true)
    {
        // Unlike civic expressions, an empty station rule list must not send a train away.
        if (rules.Count == 0 || rules.Any(r => r == null || !Enum.IsDefined(r.Kind))) return false;
        // Unknown cargo is not equivalent to zero cargo, including under NONE.
        bool Known(StationDepartureCondition r) => r.Kind switch
        {
            StationConditionKind.WaitSeconds => double.IsFinite(elapsed),
            StationConditionKind.CargoAtLeastPercent or StationConditionKind.CargoAtMostPercent => cargoKnown && double.IsFinite(percent),
            _ => cargoKnown
        };
        var values = rules.Select(r => Known(r) && r.Test(elapsed, percent, full, empty));
        return comparison switch { RequiredTrue.All => values.All(x => x), RequiredTrue.Any => values.Any(x => x),
            RequiredTrue.None => rules.All(Known) && values.All(x => !x), _ => false };
    }
}
