using Eco.Core.Controller;
using Eco.Core.Utils.PropertyScanning;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.Networking;
using Eco.Gameplay.Civics.GameValues;

namespace Eco.Minecarts.Runtime;

[Serialized] public enum StationFuelMeasure { StoragePercent, Megajoules, ReserveMinutes }
[Serialized] public enum StationFuelComparison { Below, AtMost, AtLeast, Above }

[Eco,LocCategory("Train Station"),LocDisplayName("Train fuel comparison"),
 LocDescription("Compare the lowest fuel reserve among the train's burner engines. Storage percent measures fuel inventory fill; energy and minutes include the active burner. Tenders count after transfer. Track-powered trams have no burner fuel."),RequiredContext(typeof(StationDepartureContext))]
public sealed class StationFuelCondition : StationTrainValue
{
    [Eco,LocDisplayName("Fuel measure")] public StationFuelMeasure Measure {get;set;}=StationFuelMeasure.StoragePercent;
    [Eco] public StationFuelComparison Comparison {get;set;}=StationFuelComparison.Below;
    [Eco] public float Threshold {get;set;}=25;
    private double Amount(StationDepartureContext context)=>Measure switch
    {StationFuelMeasure.StoragePercent=>context.FuelPercent,StationFuelMeasure.Megajoules=>context.FuelMegajoules,StationFuelMeasure.ReserveMinutes=>context.FuelMinutes,_=>double.NaN};
    protected override bool Known(StationDepartureContext context)=>Enum.IsDefined(Comparison)&&float.IsFinite(Threshold)&&Threshold>=0&&double.IsFinite(Amount(context));
    protected override bool Test(StationDepartureContext context)=>Comparison switch
    {StationFuelComparison.Below=>Amount(context)<Threshold,StationFuelComparison.AtMost=>Amount(context)<=Threshold,StationFuelComparison.AtLeast=>Amount(context)>=Threshold,StationFuelComparison.Above=>Amount(context)>Threshold,_=>false};
    public override LocString Description()=>Localizer.DoStr($"Train fuel {Measure}: {Comparison} {Threshold:0.##}");
}
