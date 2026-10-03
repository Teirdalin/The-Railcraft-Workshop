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
    public StationDepartureData Copy() => new()
    {
        Rules = Rules?.Where(r => r != null && Enum.IsDefined(r.Kind)).Take(64)
            .Select(r => new StationDepartureCondition(r.Kind,r.Threshold)).ToList(),
        Comparison = Enum.IsDefined(Comparison) ? Comparison : RequiredTrue.Any,
        SelectedCarIds = SelectedCarIds ?? "", Expression=StationNativeConditions.Copy(Expression)
    };
}

[Serialized, NoIcon, AutogenClass, LocDisplayName("Station Departure")]
public sealed class TrainStationComponent : WorldObjectComponent, IPersistentData, IProvidesContext
{
    private static readonly ConcurrentDictionary<int, TrainStationComponent> Stations = new();
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
    [RPC, Autogen] public void SelectCargoCars(Player player) { _ = SelectCargoCarsAsync(player); }
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
    [SyncToView] public IEnumerable<IContextValue> ContextProvided=>GameValueManager.GetContexts(typeof(StationDepartureContext)) ?? Enumerable.Empty<IContextValue>();
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
        get { lock(settingsGate) return new StationDepartureData { Rules = this.Rules.Select(r => new StationDepartureCondition(r.Kind,r.Threshold)).ToList(), Comparison = this.Comparison, SelectedCarIds = this.SelectedCarIds, Expression=StationNativeConditions.Copy(this.expression) }; }
        set
        {
            if(value is not StationDepartureData data) return;
            lock(settingsGate)
            {
                this.rules = data.Rules?.Where(r => r != null && Enum.IsDefined(r.Kind)).Take(64).Select(r => new StationDepartureCondition(r.Kind,r.Threshold)).ToList();
                this.Comparison = Enum.IsDefined(data.Comparison) ? data.Comparison : RequiredTrue.Any;
                this.SelectedCarIds = data.SelectedCarIds ?? "";
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
    private bool CanConfigure(Player player) => player != null && Parent != null && !Parent.IsDestroyed && Parent.IsAuthorized(player.User, AccessType.FullAccess)
        && Vector3.Distance(player.User.Position, Parent.Position) <= 6;
    private void SettingsChanged() { settingsRevision++; this.Changed(nameof(Conditions)); this.Changed(nameof(Comparison)); this.Changed(nameof(CargoCars)); Parent.SetDirty(); }
    [RPC, Autogen] public void CopySettings(Player player)
    {
        if (!CanConfigure(player)) return;
        var snapshot = (StationDepartureData)this.PersistentData;
        var clipboard = Clipboards.GetValue(player.User, _ => new SettingsClipboard());
        lock(clipboard) clipboard.Data = snapshot.Copy();
        SettingsAction = "Departure settings copied"; this.Changed(nameof(SettingsAction));
    }
    [RPC, Autogen] public void PasteSettings(Player player)
    {
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
    { if(!CanConfigure(player) || !Enum.IsDefined(value)) return; lock(settingsGate) { DraftCondition=value; this.Changed(nameof(DraftCondition)); } }
    [RPC] public void SetDraftThreshold(Player player, float value)
    { if(!CanConfigure(player) || !float.IsFinite(value)) return; lock(settingsGate) { DraftThreshold=Math.Clamp(value,0,86400); this.Changed(nameof(DraftThreshold)); } }
    [RPC] public void SetDraftRuleNumber(Player player, int value)
    { if(!CanConfigure(player)) return; lock(settingsGate) { DraftRuleNumber=Math.Clamp(value,1,64); this.Changed(nameof(DraftRuleNumber)); } }
    [RPC] public void AddCondition(Player player)
    { EditDepartureConditions(player); }
    [RPC] public void EditCondition(Player player)
    { EditDepartureConditions(player); }
    [RPC, Autogen] public void EditDepartureConditions(Player player)
    {
        if(!CanConfigure(player)) return;
        GameValue<bool> draft; int revision;
        lock(settingsGate) { draft=StationNativeConditions.Copy(expression)??StationNativeConditions.FromRules(Rules,Comparison); revision=settingsRevision; }
        OpenNativeEditor(player,draft,revision,value=> { expression=StationNativeConditions.Copy(value); rules=new(); if(expression is SetOfConditions set) Comparison=set.Comparison; });
    }
    private void OpenNativeEditor(Player player,GameValue<bool>? draft,int revision,Action<GameValue<bool>?> commit)
    {
        ViewEditorUtils.PopupUserEditValue(player.User,typeof(GameValue<bool>),Localizer.DoStr("Train station departure conditions"),draft,this,value=>
        {
            if(!CanConfigure(player)) return;
            var condition=value as GameValue<bool>;
            lock(settingsGate)
            {
                if(revision!=settingsRevision) { SettingsAction="Settings changed while the editor was open; reopen it"; this.Changed(nameof(SettingsAction)); return; }
                if(!StationNativeConditions.Valid(condition)) { SettingsAction="Use Train Station conditions with Any/All/None or Not; unsupported or oversized expression was not saved"; this.Changed(nameof(SettingsAction)); return; }
                commit(condition); SettingsChanged(); SettingsAction="Departure conditions saved"; this.Changed(nameof(SettingsAction));
            }
        });
    }
    [RPC] public void RemoveCondition(Player player)
    { if(!CanConfigure(player)) return; lock(settingsGate) { if(expression is SetOfConditions root) { var next=(SetOfConditions)StationNativeConditions.Copy(root)!; if(DraftRuleNumber<1||DraftRuleNumber>next.List.Count) return; next.List.RemoveAt(DraftRuleNumber-1); expression=next; rules=new(); } else if(expression!=null) { expression=null; rules=new(); } else { if(DraftRuleNumber < 1 || DraftRuleNumber > Rules.Count) return; var next=Rules.ToList(); next.RemoveAt(DraftRuleNumber-1); rules=next; } SettingsChanged(); } }
    [RPC] public void SetComparison(Player player, RequiredTrue value)
    { if(!CanConfigure(player) || !Enum.IsDefined(value)) return; lock(settingsGate) { _ = Rules; Comparison=value; if(expression is SetOfConditions set) { var next=(SetOfConditions)StationNativeConditions.Copy(set)!; next.Comparison=value; expression=next; } SettingsChanged(); } }
    [RPC] public void SetSelectedCarIds(Player player, string value)
    { if(!CanConfigure(player) || value == null || value.Length > 4096) return; lock(settingsGate) { SelectedCarIds=value; SettingsChanged(); } }
    internal RailCell? Cell { get; private set; }
    internal float TrackT { get; private set; }
    public override void PostInitialize() { base.PostInitialize(); _ = this.Rules; Stations[this.Parent.ID] = this; this.Tick(); }
    public override void Destroy() { Stations.TryRemove(this.Parent.ID, out _); base.Destroy(); }
    public override void Tick()
    {
        base.Tick();
        if(Parent.GetComponent<CoasterRailComponent>() is {} ownRail)
        {
            Cell=ownRail.Rail.Cell;TrackT=.5f;
        }
        else if (this.Cell is not { } bound || TrackWorld.Read(bound) == null)
        {
            var rail = new[] { TrackWorld.Nearest(this.Parent.Position, 2.5f), TrackWorld.Nearest(this.Parent.Position, 2.5f, true), TrackWorld.Nearest(this.Parent.Position,2.5f,coaster:true) }
                .Where(x => x.HasValue).OrderBy(x => System.Numerics.Vector3.DistanceSquared(this.Parent.Position, x!.Value.Rail.Point(x.Value.T))).FirstOrDefault();
            this.Cell = rail?.Rail.Cell; this.TrackT = rail?.T ?? 0;
        }
        this.Connection = this.Cell != null ? "Connected to rail" : "No adjacent rail";
        this.Changed(nameof(Connection));
    }
    internal static TrainStationComponent? Find(int id) => Stations.TryGetValue(id, out var station) && !station.Parent.IsDestroyed ? station : null;
    internal static IEnumerable<TrainStationComponent> At(RailCell cell) => Stations.Values.Where(x => !x.Parent.IsDestroyed && x.Cell == cell).OrderBy(x => x.Parent.ID);
    internal bool HasDepartureConditions {get {lock(settingsGate) return expression!=null || Rules.Count>0;}}
    internal bool Ready(RailCouplingComponent train, double elapsed)
    {
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
        foreach(var car in consist)
        {
            var mounts=car.Parent.GetComponent<MountComponent>();
            var seats=Math.Min(car.Vehicle.RailSpec.PassengerSeats,Math.Max(0,mounts.Seats-1));
            passengerSeats+=seats;
            occupiedSeats+=mounts.OccupantIDs.Skip(1).Take(seats).Count(id=>id>=0);
        }
        var minimumCondition=consist.Length>0 && consist.All(x=>x.Parent.GetComponent<RailConditionComponent>()!=null)
            ? consist.Min(x=>x.Parent.GetComponent<RailConditionComponent>().ConditionPercent) : double.NaN;
        var engines=consist.Where(x=>x.Vehicle.RailSpec.Powered).ToArray();
        var fuelMinutes=engines.Length>0 && engines.All(x=>x.Parent.GetComponent<FuelSupplyComponent>()!=null)
            ? engines.Min(x=>
            {
                var fuel=x.Parent.GetComponent<FuelSupplyComponent>();
                return (fuel.Energy+fuel.EnergyInSupply)/Math.Max(1,RailEconomy.FuelWatts(x.Vehicle.RailSpec)*60);
            }) : double.NaN;
        lock(settingsGate) return expression!=null
            ? StationNativeConditions.Ready(expression,new StationDepartureContext { Elapsed=elapsed,CargoPercent=valid&&selected.Length>0?this.CargoPercent:double.NaN,CargoKnown=valid&&selected.Length>0,Full=full,Empty=empty,
                CarCount=consist.Length,PassengerSeats=passengerSeats,OccupiedPassengerSeats=occupiedSeats,MinimumConditionPercent=minimumCondition,FuelMinutes=fuelMinutes })
            : StationDepartureCondition.Ready(this.Rules, this.Comparison, elapsed, valid && selected.Length > 0 ? this.CargoPercent : double.NaN, full, empty, valid && selected.Length > 0);
    }
}

