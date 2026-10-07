using Eco.Core.Utils;
using Eco.Core.Controller;
using Eco.Core.Utils.PropertyScanning;
using Eco.Gameplay.Civics.GameValues;
using Eco.Gameplay.Civics.Misc;
using Eco.Minecarts.Physics;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Utils;
using Eco.Shared.View;
using Eco.Gameplay.Players;

namespace Eco.Minecarts.Runtime;

// Advertises the same native context contract as WorkParty's LaborerContext.
[Eco]
public sealed class StationDepartureContext : IContextObject, IController
{
    // RequiredContext is sent as a ViewClassInfo, not just a CLR Type. Like
    // LaborerContext, this must be a controller so the client can resolve it.
    private int controllerID;
    public ref int ControllerID => ref controllerID;
    [Eco, LocDisplayName("Train at this station")] public bool TrainAtStation { get; set; } = true;
    public double Elapsed { get; init; }
    public double CargoPercent { get; init; }
    public bool CargoKnown { get; init; }
    public bool Full { get; init; }
    public bool Empty { get; init; }
    public int CarCount { get; init; }
    public int PassengerSeats { get; init; }
    public int OccupiedPassengerSeats { get; init; }
    public double MinimumConditionPercent { get; init; } = double.NaN;
    public double FuelMinutes { get; init; } = double.NaN;
    public double FuelPercent { get; init; } = double.NaN;
    public double FuelMegajoules { get; init; } = double.NaN;
    public IReadOnlyList<User> Passengers { get; init; } = [];
}

public abstract class StationRuleValue : GameValue<bool>
{
    public abstract StationDepartureCondition Rule { get; }
    protected override Eval<bool> Evaluate(IContextObject action)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Evaluate");
        if(action is not StationDepartureContext context || !context.TrainAtStation)
            return Eval.MakeFailLoc<bool>($"This condition requires a train stopped at a station.");
        var rule=Rule;
        if(rule.Kind!=StationConditionKind.WaitSeconds && !context.CargoKnown)
            return Eval.MakeFailLoc<bool>($"Selected cargo cars are missing or unavailable.");
        return Eval.Make(Description(),rule.Test(context.Elapsed,context.CargoPercent,context.Full,context.Empty));
    }
}

[Eco, LocCategory("Train Station"), LocDisplayName("Wait for elapsed time"), LocDescription("Wait until the train has spent this many seconds at the station."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationWaitCondition : StationRuleValue
{
    private float seconds;
    [Eco, LocDisplayName("Seconds")] public float Seconds { get=>seconds; set { seconds=(float)StationDepartureCondition.Normalize(StationConditionKind.WaitSeconds,value); this.Changed(nameof(Seconds)); this.Changed(nameof(Description)); } }
    public override StationDepartureCondition Rule=>new(StationConditionKind.WaitSeconds,Seconds);
    public override LocString Description(){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Description"); return Localizer.Do($"Wait at least {Seconds:0.##} seconds"); }
}
[Eco, LocCategory("Train Station"), LocDisplayName("Cargo at least"), LocDescription("Depart when selected cargo cars reach this load percentage."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationCargoAtLeastCondition : StationRuleValue
{
    private float percent=90;
    [Eco, LocDisplayName("Percent")] public float Percent { get=>percent; set { percent=(float)StationDepartureCondition.Normalize(StationConditionKind.CargoAtLeastPercent,value); this.Changed(nameof(Percent)); this.Changed(nameof(Description)); } }
    public override StationDepartureCondition Rule=>new(StationConditionKind.CargoAtLeastPercent,Percent);
    public override LocString Description(){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Description"); return Localizer.Do($"Cargo at least {Percent:0.##}%"); }
}
[Eco, LocCategory("Train Station"), LocDisplayName("Cargo at most"), LocDescription("Depart when selected cargo cars have this percentage or less remaining."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationCargoAtMostCondition : StationRuleValue
{
    private float percent=10;
    [Eco, LocDisplayName("Percent")] public float Percent { get=>percent; set { percent=(float)StationDepartureCondition.Normalize(StationConditionKind.CargoAtMostPercent,value); this.Changed(nameof(Percent)); this.Changed(nameof(Description)); } }
    public override StationDepartureCondition Rule=>new(StationConditionKind.CargoAtMostPercent,Percent);
    public override LocString Description(){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Description"); return Localizer.Do($"Cargo at most {Percent:0.##}%"); }
}
[Eco, LocCategory("Train Station"), LocDisplayName("Selected cars full"), LocDescription("Every selected cargo car must be full."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationCarsFullCondition : StationRuleValue
{
    public override StationDepartureCondition Rule=>new(StationConditionKind.CarsFull,0);
    public override LocString Description(){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Description"); return Localizer.DoStr("Selected cargo cars are full"); }
}
[Eco, LocCategory("Train Station"), LocDisplayName("Selected cars empty"), LocDescription("Every selected cargo car must be empty."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationCarsEmptyCondition : StationRuleValue
{
    public override StationDepartureCondition Rule=>new(StationConditionKind.CarsEmpty,0);
    public override LocString Description(){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Description"); return Localizer.DoStr("Selected cargo cars are empty"); }
}

public abstract class StationTrainValue : GameValue<bool>
{
    protected abstract bool Known(StationDepartureContext context);
    protected abstract bool Test(StationDepartureContext context);
    protected override Eval<bool> Evaluate(IContextObject action)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Evaluate");
        if(action is not StationDepartureContext context || !context.TrainAtStation)
            return Eval.MakeFailLoc<bool>($"This condition requires a train stopped at a station.");
        if(!Known(context)) return Eval.MakeFailLoc<bool>($"The required train information is unavailable.");
        return Eval.Make(Description(),Test(context));
    }
    protected static int Count(int value){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Count"); return Math.Clamp(value,0,100); }
    protected static float ClampPercent(float value){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/ClampPercent"); return float.IsFinite(value)?Math.Clamp(value,0,100):0; }
}

[Eco, LocCategory("Train Station"), LocDisplayName("Coupled cars at least"), LocDescription("Depart when the train has at least this many coupled vehicles, including its engine."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationCarCountCondition : StationTrainValue
{
    private int count=1;
    [Eco, LocDisplayName("Vehicles")] public int Vehicles { get=>count; set { count=Count(value); this.Changed(nameof(Vehicles)); this.Changed(nameof(Description)); } }
    protected override bool Known(StationDepartureContext context){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Known"); return context.CarCount>0; }
    protected override bool Test(StationDepartureContext context){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Test"); return context.CarCount>=Vehicles; }
    public override LocString Description(){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Description"); return Localizer.Do($"At least {Vehicles} coupled vehicles"); }
}

[Eco, LocCategory("Train Station"), LocDisplayName("Passengers at least"), LocDescription("Depart when at least this many passenger seats are occupied across the train."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationPassengersAtLeastCondition : StationTrainValue
{
    private int count=1;
    [Eco, LocDisplayName("Passengers")] public int Passengers { get=>count; set { count=Count(value); this.Changed(nameof(Passengers)); this.Changed(nameof(Description)); } }
    protected override bool Known(StationDepartureContext context){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Known"); return context.PassengerSeats>0; }
    protected override bool Test(StationDepartureContext context){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Test"); return context.OccupiedPassengerSeats>=Passengers; }
    public override LocString Description(){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Description"); return Localizer.Do($"At least {Passengers} passengers aboard"); }
}

[Eco, LocCategory("Train Station"), LocDisplayName("Passengers at most"), LocDescription("Depart when no more than this many passenger seats are occupied across the train; use zero for an empty train."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationPassengersAtMostCondition : StationTrainValue
{
    private int count;
    [Eco, LocDisplayName("Passengers")] public int Passengers { get=>count; set { count=Count(value); this.Changed(nameof(Passengers)); this.Changed(nameof(Description)); } }
    protected override bool Known(StationDepartureContext context){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Known"); return context.PassengerSeats>0; }
    protected override bool Test(StationDepartureContext context){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Test"); return context.OccupiedPassengerSeats<=Passengers; }
    public override LocString Description(){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Description"); return Localizer.Do($"At most {Passengers} passengers aboard"); }
}

[Eco, LocCategory("Train Station"), LocDisplayName("Vehicle condition at least"), LocDescription("Depart only if every coupled vehicle has at least this condition percentage."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationConditionAtLeastCondition : StationTrainValue
{
    private float percent=25;
    [Eco, LocDisplayName("Percent")] public float Percent { get=>percent; set { percent=ClampPercent(value); this.Changed(nameof(Percent)); this.Changed(nameof(Description)); } }
    protected override bool Known(StationDepartureContext context){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Known"); return double.IsFinite(context.MinimumConditionPercent); }
    protected override bool Test(StationDepartureContext context){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Test"); return context.MinimumConditionPercent>=Percent; }
    public override LocString Description(){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Description"); return Localizer.Do($"Every vehicle at least {Percent:0.##}% condition"); }
}

[Eco, LocCategory("Train Station"), LocDisplayName("Fuel reserve at least"), LocDescription("Depart when the train's powered engines have at least this many minutes of fuel at rated traction use. Tender fuel is counted after transfer into an engine."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationFuelReserveCondition : StationTrainValue
{
    private float minutes=5;
    [Eco, LocDisplayName("Minutes")] public float Minutes { get=>minutes; set { minutes=float.IsFinite(value)?Math.Clamp(value,0,1440):0; this.Changed(nameof(Minutes)); this.Changed(nameof(Description)); } }
    protected override bool Known(StationDepartureContext context){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Known"); return double.IsFinite(context.FuelMinutes); }
    protected override bool Test(StationDepartureContext context){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Test"); return context.FuelMinutes>=Minutes; }
    public override LocString Description(){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Description"); return Localizer.Do($"Fuel for at least {Minutes:0.##} minutes"); }
}

public static class StationNativeConditions
{
    public static GameValue<bool> FromRule(StationDepartureCondition rule){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/FromRule"); return rule.Kind switch
    {
        StationConditionKind.WaitSeconds=>new StationWaitCondition { Seconds=(float)rule.Threshold },
        StationConditionKind.CargoAtLeastPercent=>new StationCargoAtLeastCondition { Percent=(float)rule.Threshold },
        StationConditionKind.CargoAtMostPercent=>new StationCargoAtMostCondition { Percent=(float)rule.Threshold },
        StationConditionKind.CarsFull=>new StationCarsFullCondition(),
        StationConditionKind.CarsEmpty=>new StationCarsEmptyCondition(),
        _=>throw new ArgumentOutOfRangeException(nameof(rule))
    }; }
    public static SetOfConditions FromRules(IEnumerable<StationDepartureCondition> rules,RequiredTrue comparison)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/FromRules"); var set=new SetOfConditions { Comparison=comparison }; foreach(var rule in rules) set.List.Add(FromRule(rule)); return set; }
    public static GameValue<bool>? Copy(GameValue<bool>? value){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Copy"); return value==null?null:(GameValue<bool>)Cloner.Clone(value); }
    public static bool Valid(GameValue<bool>? value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Valid");
        if(value==null) return true;
        var seen=new HashSet<GameValue<bool>>(ReferenceEqualityComparer.Instance); var count=0;
        bool Visit(GameValue<bool>? node,int depth)
        {
            if(node==null || depth>8 || ++count>64 || !seen.Add(node)) return false;
            var valid=node switch
            {
                StationRuleValue or StationTrainValue=>true,
                GameValueContext<bool> context=>context.Name==nameof(StationDepartureContext.TrainAtStation),
                StationPassengerMatchCondition passenger=>StationPassengerConditions.Valid(passenger.Condition) && Enum.IsDefined(passenger.Match),
                Yes or No=>true,
                Not not=>Visit(not.NotValue,depth+1),
                SetOfConditions set=>Enum.IsDefined(set.Comparison)&&set.List!=null&&set.List.All(child=>Visit(child,depth+1)),
                _=>false
            };
            seen.Remove(node); return valid;
        }
        return Visit(value,0);
    }
    public static bool Ready(GameValue<bool>? expression,StationDepartureContext context)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Departure conditions/Ready");
        // Eco's generic empty SetOfConditions evaluates true. A blank station
        // must remain stopped, including an empty set inside a NOT expression.
        if(expression==null || !Valid(expression)) return false;
        // Empty nested groups must not become true via Eco's generic semantics,
        // and missing cargo must not become a successful negated condition.
        bool Complete(GameValue<bool> node)=>node switch
        {
            StationRuleValue leaf=>!leaf.Value(context).Invalid,
            StationTrainValue leaf=>!leaf.Value(context).Invalid,
            GameValueContext<bool> leaf=>!leaf.Value(context).Invalid,
            StationPassengerMatchCondition passenger=>!passenger.Value(context).Invalid,
            Yes or No=>true,
            Not not=>Complete(not.NotValue),
            SetOfConditions set=>set.List.Count>0&&set.List.All(Complete),
            _=>false
        };
        if(!Complete(expression)) return false;
        var result=expression.Value(context); return result!=null&&!result.Invalid&&result.Val;
    }
}
