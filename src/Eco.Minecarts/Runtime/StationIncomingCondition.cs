using Eco.Core.Controller;
using Eco.Core.Utils;
using Eco.Core.Utils.PropertyScanning;
using Eco.Gameplay.Civics.GameValues;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.View;

namespace Eco.Minecarts.Runtime;

internal interface IStationTrainCondition
{
    Guid StationId {get;set;}
}

[Eco, Serialized, LocCategory("Train Station"), LocDisplayName("Train on the way to station"),
 LocDescription("Depart when another train is committed to the chosen station. Choose this station to release a waiting service when its replacement is on the way. Parked trains and this train do not count."),
 RequiredContext(typeof(StationDepartureContext))]
public sealed class StationTrainIncomingCondition : GameValue<bool>, IStationTrainCondition
{
    [Serialized, SyncToView] public Guid StationId {get;set;}
    [Eco, PropReadOnly, LocDisplayName("Station")] public string SelectedStation
    {
        get=>TrainStationComponent.Find(StationId)?.StationName??"Choose station when applying";
        private set { }
    }
    public override LocString Description()=>Localizer.Do($"Another train is on the way to {SelectedStation}");
    protected override Eval<bool> Evaluate(IContextObject action)
    {
        if(action is not StationDepartureContext context||context.StationIncoming is not {} incoming
            ||incoming(StationId) is not {} expected)
            return Eval.MakeFailLoc<bool>($"Station is unavailable or disconnected. Use Sync Now at the departure station.");
        return Eval.Make(Description(),expected);
    }
}
