using System.Collections;
using System.Reflection;
using Eco.Core.Controller;
using Eco.Core.Utils;
using Eco.Core.Utils.PropertyScanning;
using Eco.Gameplay.Aliases;
using Eco.Gameplay.Civics.GameValues;
using Eco.Gameplay.Players;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.Networking;

namespace Eco.Minecarts.Runtime;

[Serialized] public enum PassengerMatchMode { Any, All, AtLeast, None }

[Eco]
public sealed class StationPassengerContext : IContextObject, IController
{
    private int controllerId;
    public ref int ControllerID=>ref controllerId;
    [Eco,LocDisplayName("Passenger aboard this train")] public User? Passenger {get;init;}
}

[Eco,LocCategory("Train Station"),LocDisplayName("Passenger aboard this train"),RequiredContext(typeof(StationPassengerContext))]
public sealed class StationPassengerUserValue : GameValue<User>
{
    protected override Eval<User> Evaluate(IContextObject action)=>action is StationPassengerContext {Passenger:{} passenger}
        ? Eval.Make(Description(),passenger) : Eval.MakeFailLoc<User>($"No passenger is being evaluated.");
    public override LocString Description()=>Localizer.DoStr("Passenger aboard this train");
}
[Eco,LocCategory("Train Station"),LocDisplayName("Passenger aboard this train"),RequiredContext(typeof(StationPassengerContext))]
public sealed class StationPassengerAliasValue : GameValue<IAlias>
{
    protected override Eval<IAlias> Evaluate(IContextObject action)=>action is StationPassengerContext {Passenger:{} passenger}
        ? Eval.Make<IAlias>(Description(),passenger) : Eval.MakeFailLoc<IAlias>($"No passenger is being evaluated.");
    public override LocString Description()=>Localizer.DoStr("Passenger aboard this train");
}

[Eco,LocCategory("Train Station"),LocDisplayName("Passengers match character conditions"),
 LocDescription("Evaluate the enclosed citizen, inventory, skill or other character conditions against seated passengers. An empty train never matches."),RequiredContext(typeof(StationDepartureContext))]
public sealed class StationPassengerMatchCondition : GameValue<bool>
{
    private readonly object evaluationGate=new();
    private GameValue<bool>? evaluatedSource,passengerCondition;
    [Eco,LocDisplayName("Match passengers")] public PassengerMatchMode Match {get;set;}=PassengerMatchMode.Any;
    [Eco,LocDisplayName("Required matching passengers")] public int RequiredMatches {get;set;}=1;
    [Eco,LocDisplayName("Passenger conditions")] public GameValue<bool>? Condition {get;set;}
    public override LocString Description()=>Localizer.DoStr($"{(Match==PassengerMatchMode.AtLeast?$"At least {Math.Clamp(RequiredMatches,1,100)}":Match.ToString())} passengers: {Condition?.Description().ToString()??"choose character conditions"}");
    protected override Eval<bool> Evaluate(IContextObject action)
    {
        if(action is not StationDepartureContext context || !context.TrainAtStation || context.Passengers.Count==0)
            return Eval.MakeFailLoc<bool>($"There are no passengers aboard this train.");
        if(!Enum.IsDefined(Match) || !StationPassengerConditions.Valid(Condition))
            return Eval.MakeFailLoc<bool>($"Choose valid passenger conditions.");
        GameValue<bool> bound;
        lock(evaluationGate)
        {
            // Also bind restored/legacy expressions that never passed through
            // the editor. Never let a saved fixed Citizen choose the subject.
            if(evaluatedSource!=Condition || passengerCondition==null)
            {
                passengerCondition=StationNativeConditions.Copy(Condition)!;
                StationPassengerConditions.BindSubjects(passengerCondition);
                evaluatedSource=Condition;
            }
            bound=passengerCondition;
        }
        var matched=0;
        foreach(var passenger in context.Passengers)
        {
            try
            {
                var result=bound.Value(new StationPassengerContext{Passenger=passenger});
                if(result==null || result.Invalid)return Eval.MakeFailLoc<bool>($"Passenger information required by these conditions is unavailable.");
                if(result.Val)matched++;
            }
            catch(Exception){return Eval.MakeFailLoc<bool>($"The selected character condition cannot evaluate this passenger.");}
        }
        return Eval.Make(Description(),Matches(context.Passengers.Count,matched,Match,RequiredMatches));
    }
    internal static bool Matches(int count,int matching,PassengerMatchMode mode,int required)=>count>0 && mode switch
    {
        PassengerMatchMode.Any=>matching>0, PassengerMatchMode.All=>matching==count,
        PassengerMatchMode.None=>matching==0, PassengerMatchMode.AtLeast=>matching>=Math.Clamp(required,1,100), _=>false
    };
}

internal static class StationPassengerConditions
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type,PropertyInfo[]> Properties=new();
    private static PropertyInfo[] Inputs(Type type)=>Properties.GetOrAdd(type,t=>t.GetProperties().Where(p=>p.CanRead&&p.GetIndexParameters().Length==0
        && p.GetCustomAttributes().Any(a=>a.GetType().Name=="EcoAttribute")).ToArray());
    // Native citizen tests frequently keep their subject in an advanced Citizen
    // field. Force that subject to the mounted passenger, never a fixed citizen.
    internal static void BindSubjects(GameValue value)
    {
        var visited=new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object? node,int depth)
        {
            if(node==null || depth>12 || visited.Count>=128 || !visited.Add(node))return;
            if(node is IEnumerable list && node is not string){foreach(var child in list)Walk(child,depth+1);return;}
            if(node is not GameValue)return;
            foreach(var property in Inputs(node.GetType()))
            {
                if(property.CanWrite && property.Name is "Citizen" or "User" or "Player")
                {
                    if(property.PropertyType==typeof(GameValue<User>)){property.SetValue(node,new StationPassengerUserValue());continue;}
                    if(property.PropertyType==typeof(GameValue<IAlias>)){property.SetValue(node,new StationPassengerAliasValue());continue;}
                }
                Walk(property.GetValue(node),depth+1);
            }
        }
        Walk(value,0);
    }
    internal static bool Valid(GameValue<bool>? condition)
    {
        if(condition==null)return false;
        var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);var count=0;
        bool Visit(object? value,int depth)
        {
            if(value==null)return true;
            if(depth>12 || ++count>128 || !seen.Add(value))return false;
            if(value is SetOfConditions {List.Count:0})return false;
            if(value is StationRuleValue or StationTrainValue or StationPassengerMatchCondition)return false;
            if(value is GameValue game)
            {
                if(game is not StationPassengerUserValue and not StationPassengerAliasValue
                    && game.GetType().Assembly!=typeof(GameValue).Assembly)return false;
                foreach(var input in Inputs(game.GetType()))
                {
                    var child=input.GetValue(game);
                    if(child is GameValue && !Visit(child,depth+1))return false;
                    if(child is IEnumerable list && child is not string)
                        foreach(var item in list)if(item is GameValue && !Visit(item,depth+1))return false;
                }
            }
            seen.Remove(value);return true;
        }
        return Visit(condition,0);
    }
    internal static GameValue<bool>? Normalize(GameValue<bool>? value)
    {
        if(value==null)return null;
        var copy=StationNativeConditions.Copy(value)!;
        GameValue<bool> Walk(GameValue<bool> node)=>node switch
        {
            SetOfConditions set=>NormalizeSet(set),
            Not not=>new Not{NotValue=not.NotValue==null?new SetOfConditions():Walk(not.NotValue)},
            StationRuleValue or StationTrainValue or Yes or No=>node,
            GameValueContext<bool> context when context.Name==nameof(StationDepartureContext.TrainAtStation)=>node,
            StationPassengerMatchCondition passenger=>Bind(passenger),
            _=>Bind(new StationPassengerMatchCondition{Condition=node})
        };
        SetOfConditions NormalizeSet(SetOfConditions set)
        {var next=new SetOfConditions{Comparison=set.Comparison};foreach(var child in set.List)next.List.Add(Walk(child));return next;}
        StationPassengerMatchCondition Bind(StationPassengerMatchCondition passenger)
        {if(passenger.Condition!=null)BindSubjects(passenger.Condition);return passenger;}
        return Walk(copy);
    }
}
