using System.Collections.Concurrent;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.Networking;
using Eco.Shared.Items;
using Eco.Gameplay.Players;
using Eco.Gameplay.Items;
using System.Runtime.CompilerServices;
using Eco.Core.Utils.PropertyScanning;
using Eco.Gameplay.Civics.GameValues;
using Eco.Gameplay.Systems;

namespace Eco.Minecarts.Runtime;

[Serialized]
public sealed class StationDepartureData
{
    // PersistentData supplies a detached deep snapshot under settingsGate.
    [Serialized, ThreadSafe] public List<StationDepartureCondition>? Rules { get; set; }
    [Serialized] public RequiredTrue Comparison { get; set; } = RequiredTrue.Any;
    [Serialized] public string SelectedCarIds { get; set; } = "";
    [Serialized] public GameValue<bool>? Expression { get; set; }
    [Serialized,ThreadSafe] public List<StationDestinationRule>? Destinations {get;set;}
    public StationDepartureData Copy() { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Copy"); return new()
    {
        Rules = Rules?.Where(r => r != null && Enum.IsDefined(r.Kind)).Take(64)
            .Select(r => new StationDepartureCondition(r.Kind,r.Threshold)).ToList(),
        Comparison = Enum.IsDefined(Comparison) ? Comparison : RequiredTrue.Any,
        SelectedCarIds = SelectedCarIds ?? "", Expression=StationNativeConditions.Copy(Expression),Destinations=Destinations?.Select(r=>r.Copy()).ToList()
    }; }
}

[Serialized, NoIcon, AutogenClass, LocDisplayName("Station Departure")]
public sealed partial class TrainStationComponent : WorldObjectComponent, IPersistentData, IProvidesContext
{
    private static readonly ConcurrentDictionary<int, TrainStationComponent> Stations = new();
    private static readonly ConcurrentDictionary<Guid,TrainStationComponent> StationObjects=new();
    private static readonly ConcurrentDictionary<RailCell, TrainStationComponent[]> TrackStations = new();
    private static readonly object TrackStationGate = new();
    private static long routingRevision;
    internal static long RoutingRevision => Volatile.Read(ref routingRevision);
    internal static void RoutingChanged() => Interlocked.Increment(ref routingRevision);
    private string? indexedName;
    private void BindTrack(RailCell? cell, float parameter)
    {
        lock(TrackStationGate)
        {
            if(Cell!=cell)
            {
                if(Cell is {} old && TrackStations.TryGetValue(old,out var entries))
                {
                    var kept=entries.Where(s=>s!=this).ToArray();
                    if(kept.Length==0)TrackStations.TryRemove(old,out _); else TrackStations[old]=kept;
                }
                if(cell is {} next)
                    TrackStations[next]=TrackStations.GetValueOrDefault(next,[]).Where(s=>s!=this)
                        .Append(this).OrderBy(s=>s.Parent.ID).ToArray();
            }
            var changed=Cell!=cell || TrackT!=parameter;
            Cell=cell; TrackT=parameter;
            if(changed)RoutingChanged();
        }
    }
    private sealed class SettingsClipboard { public StationDepartureData? Data; }
    private static readonly ConditionalWeakTable<object, SettingsClipboard> Clipboards = new();
    [Serialized] public bool WaitForTime { get; set; }
    [Serialized] public float WaitSeconds { get; set; }
    [Serialized] public bool WaitForMinimumCargo { get; set; }
    [Serialized] public float MinimumCargoPercent { get; set; } = 90;
    [Serialized] public bool WaitForMaximumCargo { get; set; }
    [Serialized] public float MaximumCargoPercent { get; set; } = 10;
    [Serialized] public bool WaitForFullCars { get; set; }
    [Serialized] public bool WaitForEmptyCars { get; set; }
    [Serialized] public bool RequireAllConditions { get; set; }
    [Serialized] public string SelectedCarIds { get; set; } = "";
    [SyncToView, Autogen, PropReadOnly] public string CargoCars => string.IsNullOrWhiteSpace(SelectedCarIds)
        ? "All cargo cars" : SelectedCarIds.Split(',',StringSplitOptions.RemoveEmptyEntries).Any(id=>!Guid.TryParse(id,out _))
        ? "Reselect cargo cars after update" : $"{SelectedCarIds.Split(',',StringSplitOptions.RemoveEmptyEntries).Length} selected cars";
    [RPC, Autogen, UITypeName("BigButton")] public void SelectCargoCars(Player player) {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/SelectCargoCars", this.Parent); _ = SelectCargoCarsAsync(player); }
    private async Task SelectCargoCarsAsync(Player player)
    {
        if(!CanConfigure(player)) return;
        try
        {
            var train=RailCouplingComponent.NearestTo(Parent.Position,12);
            var cars=train?.Group().Where(c=>!c.Vehicle.RailSpec.Powered && !c.Vehicle.RailSpec.Tender && c.Vehicle.RailSpec.PassengerSeats==0).ToArray() ?? [];
            var options=string.Join("\n",cars.Select((c,i)=>$"{i+1}. {c.Parent.DisplayName}"));
            var revision=settingsRevision;
            var text=await player.InputString(Localizer.DoStr("Select cargo cars from the nearby train by number, separated by commas, or enter all.\n"+options),Localizer.DoStr("all"));
            if(string.IsNullOrWhiteSpace(text) || !CanConfigure(player)) return;
            var selected=new List<int>();
            if(!string.Equals(text.Trim(),"all",StringComparison.OrdinalIgnoreCase))
                foreach(var token in text.Split(',',StringSplitOptions.TrimEntries))
                {
                    if(!int.TryParse(token,out var index) || index<1 || index>cars.Length || cars[index-1].Parent.IsDestroyed)
                    { player.InfoBoxLoc($"Choose a listed car number or all. Nothing changed."); return; }
                    selected.Add(index-1);
                }
            lock(settingsGate)
            {
                if(revision!=settingsRevision) { player.InfoBoxLoc($"Station settings changed. Reopen car selection."); return; }
                SelectedCarIds=string.Join(",",selected.Distinct().Select(i=>cars[i].Parent.ObjectID.ToString("N"))); SettingsChanged();
            }
        }
        catch(Exception error) { Eco.Shared.Logging.Log.WriteErrorLineLoc($"Station car selection failed: {error}"); }
    }
    [SyncToView, Serialized] public RequiredTrue Comparison { get; set; } = RequiredTrue.Any;
    // Autogen action buttons do not collect method arguments. Inputs must be
    // ordinary synchronized fields with explicit, authorized setter RPCs.
    // Retain old setter RPCs for stale views, but no longer expose the broken
    // custom enum/threshold editor. Conditions are edited in Eco's native popup.
    public StationConditionKind DraftCondition { get; set; } = StationConditionKind.WaitSeconds;
    public float DraftThreshold { get; set; }
    [SyncToView] public int DraftRuleNumber { get; set; } = 1;
    [SyncToView, Autogen, PropReadOnly] public string Conditions
    { get { lock(settingsGate) return this.expression!=null ? this.expression.Description().ToString() : this.Rules.Count == 0 ? "No conditions: train waits" : string.Join("\n", this.Rules.Select((r, i) => $"{i + 1}. {StationNativeConditions.FromRule(r).Description()}")); } }
    [SyncToView, Autogen, PropReadOnly] public string Connection { get; private set; } = "Searching for adjacent rail";
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Cargo Load (%)")] public float CargoPercent { get; private set; }
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Settings Status")] public string SettingsAction { get; private set; } = "";
    private readonly object settingsGate = new();
    [Serialized, ThreadSafe] private List<StationDepartureCondition>? rules;
    [Serialized] private GameValue<bool>? expression;
    private int settingsRevision;
    [SyncToView] public IEnumerable<IContextValue> ContextProvided=>(GameValueManager.GetContexts(typeof(StationDepartureContext)) ?? Enumerable.Empty<IContextValue>())
        .Concat(GameValueManager.GetContexts(typeof(StationPassengerContext)) ?? Enumerable.Empty<IContextValue>());
    private List<StationDepartureCondition> Rules
    {
        get
        {
            if (this.rules != null) return this.rules;
            this.rules = new();
            this.Comparison = this.RequireAllConditions ? RequiredTrue.All : RequiredTrue.Any;
            if (WaitForTime) this.rules.Add(new(StationConditionKind.WaitSeconds, WaitSeconds));
            if (WaitForMinimumCargo) this.rules.Add(new(StationConditionKind.CargoAtLeastPercent, MinimumCargoPercent));
            if (WaitForMaximumCargo) this.rules.Add(new(StationConditionKind.CargoAtMostPercent, MaximumCargoPercent));
            if (WaitForFullCars) this.rules.Add(new(StationConditionKind.CarsFull, 0));
            if (WaitForEmptyCars) this.rules.Add(new(StationConditionKind.CarsEmpty, 0));
            return this.rules;
        }
    }
    public object PersistentData
    {
        get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/TrainStationComponent.PersistentData.get", this.Parent); lock(settingsGate) return new StationDepartureData { Rules = this.Rules.Select(r => new StationDepartureCondition(r.Kind,r.Threshold)).ToList(), Comparison = this.Comparison, SelectedCarIds = this.SelectedCarIds, Expression=StationNativeConditions.Copy(this.expression),Destinations=DestinationRules.Select(r=>r.Copy()).ToList() }; }
        set
        { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/TrainStationComponent.PersistentData.set", this.Parent);
            if(value is not StationDepartureData data) return;
            lock(settingsGate)
            {
                this.rules = data.Rules?.Where(r => r != null && Enum.IsDefined(r.Kind)).Take(64).Select(r => new StationDepartureCondition(r.Kind,r.Threshold)).ToList();
                this.Comparison = Enum.IsDefined(data.Comparison) ? data.Comparison : RequiredTrue.Any;
                this.SelectedCarIds = data.SelectedCarIds ?? "";
                destinationRules=data.Destinations?.Where(r=>r!=null&&StationNativeConditions.Valid(r.Conditions)).Take(32).Select(r=>r.Copy()).ToList()??new();
                this.expression = StationNativeConditions.Valid(data.Expression) ? StationNativeConditions.Copy(data.Expression) : null;
                if(data.Expression!=null)
                {
                    // Never revive obsolete flat rules when replacing/clearing
                    // a native expression. Invalid saved expressions fail closed.
                    this.rules=new();
                    if(this.expression==null) this.expression=new No();
                }
            }
        }
    }
    private bool CanConfigure(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/CanConfigure", this.Parent); return player != null && Parent != null && !Parent.IsDestroyed && Parent.IsAuthorized(player.User, AccessType.FullAccess)
        && Vector3.Distance(player.User.Position, Parent.Position) <= 6; }
    private void SettingsChanged() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/SettingsChanged", this.Parent); settingsRevision++; this.Changed(nameof(Conditions)); this.Changed(nameof(Comparison)); this.Changed(nameof(CargoCars)); this.Changed(nameof(DestinationRulesSummary)); Parent.SetDirty(); }
    [RPC, Autogen, UITypeName("BigButton")] public void CopySettings(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/CopySettings", this.Parent);
        if (!CanConfigure(player)) return;
        var snapshot = (StationDepartureData)this.PersistentData;
        var clipboard = Clipboards.GetValue(player.User, _ => new SettingsClipboard());
        lock(clipboard) clipboard.Data = snapshot.Copy();
        SettingsAction = "Departure settings copied"; this.Changed(nameof(SettingsAction));
    }
    [RPC, Autogen, UITypeName("BigButton")] public void PasteSettings(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/PasteSettings", this.Parent);
        if (!CanConfigure(player)) return;
        var clipboard = Clipboards.GetValue(player.User, _ => new SettingsClipboard());
        StationDepartureData? snapshot;
        lock(clipboard) snapshot = clipboard.Data?.Copy();
        if (snapshot == null)
        { SettingsAction = "Copy departure settings from a station first"; this.Changed(nameof(SettingsAction)); return; }
        lock(settingsGate) { this.PersistentData = snapshot; SettingsChanged(); }
        SettingsAction = "Departure settings pasted"; this.Changed(nameof(SettingsAction));
    }
    [RPC] public void SetDraftCondition(Player player, StationConditionKind value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/SetDraftCondition", this.Parent); if(!CanConfigure(player) || !Enum.IsDefined(value)) return; lock(settingsGate) { DraftCondition=value; this.Changed(nameof(DraftCondition)); } }
    [RPC] public void SetDraftThreshold(Player player, float value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/SetDraftThreshold", this.Parent); if(!CanConfigure(player) || !float.IsFinite(value)) return; lock(settingsGate) { DraftThreshold=Math.Clamp(value,0,86400); this.Changed(nameof(DraftThreshold)); } }
    [RPC] public void SetDraftRuleNumber(Player player, int value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/SetDraftRuleNumber", this.Parent); if(!CanConfigure(player)) return; lock(settingsGate) { DraftRuleNumber=Math.Clamp(value,1,64); this.Changed(nameof(DraftRuleNumber)); } }
    [RPC] public void AddCondition(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/AddCondition", this.Parent); EditDepartureConditions(player); }
    [RPC] public void EditCondition(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/EditCondition", this.Parent); EditDepartureConditions(player); }
    [RPC, Autogen, UITypeName("BigButton")] public void EditDepartureConditions(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/EditDepartureConditions", this.Parent);
        if(!CanConfigure(player)) return;
        GameValue<bool> draft; int revision;
        lock(settingsGate) { draft=StationNativeConditions.Copy(expression)??StationNativeConditions.FromRules(Rules,Comparison); revision=settingsRevision; }
        OpenNativeEditor(player,draft,revision,value=> { expression=StationNativeConditions.Copy(value); rules=new(); if(expression is SetOfConditions set) Comparison=set.Comparison; });
    }
    private void OpenNativeEditor(Player player,GameValue<bool>? draft,int revision,Action<GameValue<bool>?> commit)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/OpenNativeEditor", this.Parent);
        ViewEditorUtils.PopupUserEditValue(player.User,typeof(GameValue<bool>),Localizer.DoStr("Train station departure conditions"),draft,this,value=>
        {
            if(!CanConfigure(player)) return;
            var condition=StationPassengerConditions.Normalize(value as GameValue<bool>);
            lock(settingsGate)
            {
                if(revision!=settingsRevision) { SettingsAction="Settings changed while the editor was open; reopen it"; this.Changed(nameof(SettingsAction)); return; }
                if(!StationNativeConditions.Valid(condition)) { SettingsAction="Use Train Station conditions with Any/All/None or Not; unsupported or oversized expression was not saved"; this.Changed(nameof(SettingsAction)); return; }
                commit(condition); SettingsChanged(); SettingsAction="Departure conditions saved"; this.Changed(nameof(SettingsAction));
            }
        });
    }
    [RPC] public void RemoveCondition(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/RemoveCondition", this.Parent); if(!CanConfigure(player)) return; lock(settingsGate) { if(expression is SetOfConditions root) { var next=(SetOfConditions)StationNativeConditions.Copy(root)!; if(DraftRuleNumber<1||DraftRuleNumber>next.List.Count) return; next.List.RemoveAt(DraftRuleNumber-1); expression=next; rules=new(); } else if(expression!=null) { expression=null; rules=new(); } else { if(DraftRuleNumber < 1 || DraftRuleNumber > Rules.Count) return; var next=Rules.ToList(); next.RemoveAt(DraftRuleNumber-1); rules=next; } SettingsChanged(); } }
    [RPC] public void SetComparison(Player player, RequiredTrue value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/SetComparison", this.Parent); if(!CanConfigure(player) || !Enum.IsDefined(value)) return; lock(settingsGate) { _ = Rules; Comparison=value; if(expression is SetOfConditions set) { var next=(SetOfConditions)StationNativeConditions.Copy(set)!; next.Comparison=value; expression=next; } SettingsChanged(); } }
    [RPC] public void SetSelectedCarIds(Player player, string value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/SetSelectedCarIds", this.Parent); if(!CanConfigure(player) || value == null || value.Length > 4096) return; lock(settingsGate) { SelectedCarIds=value; SettingsChanged(); } }
    internal RailCell? Cell { get; private set; }
    internal float TrackT { get; private set; }
    public override void PostInitialize() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/PostInitialize", this.Parent); base.PostInitialize(); _ = this.Rules; Stations[this.Parent.ID] = this; StationObjects[Parent.ObjectID]=this; this.Tick(); }
    public override void Destroy() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Destroy", this.Parent); BindTrack(null,0); Stations.TryRemove(this.Parent.ID, out _); StationObjects.TryRemove(Parent.ObjectID,out _); RoutingChanged(); base.Destroy(); }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Tick", this.Parent);
        base.Tick();
        if(Parent.GetComponent<CoasterRailComponent>() is {} ownRail)
        {
            BindTrack(ownRail.Rail.Cell,.5f);
        }
        else if (this.Cell is not { } bound || TrackWorld.Read(bound) == null)
        {
            var rail = new[] { TrackWorld.Nearest(this.Parent.Position, 2.5f), TrackWorld.Nearest(this.Parent.Position, 2.5f, true), TrackWorld.Nearest(this.Parent.Position,2.5f,coaster:true) }
                .Where(x => x.HasValue).OrderBy(x => System.Numerics.Vector3.DistanceSquared(this.Parent.Position, x!.Value.Rail.Point(x.Value.T))).FirstOrDefault();
            BindTrack(rail?.Rail.Cell,rail?.T ?? 0);
        }
        var name=Parent.DisplayName.ToString();
        if(indexedName!=name) {indexedName=name;RoutingChanged();}
        this.Connection = this.Cell != null ? "Connected to rail" : "No adjacent rail";
        this.Changed(nameof(Connection));
    }
    internal static TrainStationComponent? Find(int id) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Find"); return Stations.TryGetValue(id, out var station) && !station.Parent.IsDestroyed ? station : null; }
    internal static TrainStationComponent[] At(RailCell cell) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/At"); return TrackStations.GetValueOrDefault(cell,[]); }
    internal bool HasDepartureConditions {get {lock(settingsGate) return expression!=null || Rules.Count>0;}}
    internal bool ReadyToDepart(RailCouplingComponent train,double elapsed,bool defaultReady=false)
        =>HasDepartureConditions?Ready(train,elapsed):defaultReady;
    internal void NotifyVehicle(Eco.Minecarts.Track.RailEvent action,Guid vehicle)
        =>Parent.GetComponent<RailAutomationComponent>()?.Emit(action,vehicle);
    internal bool Ready(RailCouplingComponent train, double elapsed)
    {
        var context=DepartureContext(train,elapsed);
        lock(settingsGate) return expression!=null ? StationNativeConditions.Ready(expression,context)
            : StationDepartureCondition.Ready(Rules,Comparison,elapsed,context.CargoPercent,context.Full,context.Empty,context.CargoKnown);
    }
    internal StationDepartureContext DepartureContext(RailCouplingComponent train,double elapsed)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Ready", this.Parent);
        var consist=train.Group();
        var ids = (this.SelectedCarIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selected = consist.Where(x => ids.Length > 0 ? ids.Any(id=>Guid.TryParse(id,out var guid)&&guid==x.Parent.ObjectID)
            : !x.Vehicle.RailSpec.Powered && !x.Vehicle.RailSpec.Tender && x.Vehicle.RailSpec.PassengerSeats == 0).ToArray();
        // Missing selected cars never silently satisfy full/empty conditions.
        // Network handles change after reload and can be recycled. Legacy
        // numeric selections fail closed until the owner selects cars again.
        var valid = ids.Length == 0 || ids.All(id => Guid.TryParse(id,out var guid)&&selected.Any(x=>x.Parent.ObjectID==guid));
        var capacity = selected.Sum(x => x.Vehicle.RailSpec.CargoKg);
        this.CargoPercent = capacity > 0 ? (float)Math.Clamp(selected.Sum(x => x.Load.CargoKg) / capacity * 100, 0, 100) : 0;
        this.Changed(nameof(CargoPercent));
        var full = valid && selected.Length > 0 && selected.All(x => x.Load.CargoKg >= x.Vehicle.RailSpec.CargoKg * .999
            || x.Parent.GetComponent<PublicStorageComponent>().Inventory.IsFull);
        var empty = valid && selected.Length > 0 && selected.All(x => x.Parent.GetComponent<PublicStorageComponent>().Inventory.IsEmpty);
        var passengerSeats=0;
        var occupiedSeats=0;
        var passengers=new List<User>();
        foreach(var car in consist)
        {
            var mounts=car.Parent.GetComponent<MountComponent>();
            var seats=Math.Min(car.Vehicle.RailSpec.PassengerSeats,Math.Max(0,mounts.Seats-1));
            passengerSeats+=seats;
            occupiedSeats+=mounts.OccupantIDs.Skip(1).Take(seats).Count(id=>id>=0);
            var passengerIds=mounts.OccupantIDs.Skip(1).Take(seats).ToHashSet();
            passengers.AddRange(mounts.MountedPlayers.Where(p=>passengerIds.Contains(p.ID)).Select(p=>p.User));
        }
        var minimumCondition=consist.Length>0 && consist.All(x=>x.Parent.GetComponent<RailConditionComponent>()!=null)
            ? consist.Min(x=>x.Parent.GetComponent<RailConditionComponent>().ConditionPercent) : double.NaN;
        var engines=consist.Where(x=>x.Vehicle.RailSpec.Powered&&!x.Vehicle.RailSpec.Tram).ToArray();
        var fuelMinutes=engines.Length>0 && engines.All(x=>x.Parent.GetComponent<FuelSupplyComponent>()!=null)
            ? engines.Min(x=>
            {
                var fuel=x.Parent.GetComponent<FuelSupplyComponent>();
                return (fuel.Energy+fuel.EnergyInSupply)/Math.Max(1,RailEconomy.FuelWatts(x.Vehicle.RailSpec)*60);
            }) : double.NaN;
        var fuelEngines=engines.Where(x=>!x.Vehicle.RailSpec.Tram).Select(x=>x.Parent.GetComponent<FuelSupplyComponent>()).ToArray();
        var fuelKnown=fuelEngines.Length>0 && fuelEngines.All(f=>f?.Inventory!=null);
        var fuelFill=fuelKnown ? fuelEngines.Min(f=>f.Inventory.IsEmpty ? 0 : Math.Clamp((double)f.Inventory.FillPerCent*100,0,100)) : double.NaN;
        var fuelAmount=fuelKnown ? fuelEngines.Min(f=>(f.Energy+f.EnergyInSupply)/1000000d) : double.NaN;
        return new StationDepartureContext { Elapsed=elapsed,CargoPercent=valid&&selected.Length>0?this.CargoPercent:double.NaN,CargoKnown=valid&&selected.Length>0,Full=full,Empty=empty,
            CarCount=consist.Length,PassengerSeats=passengerSeats,OccupiedPassengerSeats=occupiedSeats,MinimumConditionPercent=minimumCondition,FuelMinutes=fuelMinutes,
            FuelPercent=fuelFill,FuelMegajoules=fuelAmount,Passengers=passengers.DistinctBy(p=>p.Id).ToArray() };
    }
}
