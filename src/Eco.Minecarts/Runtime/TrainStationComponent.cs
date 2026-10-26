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
using Eco.Shared.IoC;
using Eco.Shared.Networking;
using Eco.Shared.Items;
using Eco.Gameplay.Players;
using Eco.Gameplay.Items;
using Eco.Gameplay.UI;
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
    internal static bool RegistrationPending(Guid id,TrainStationComponent? station)
    {
        if(id==Guid.Empty)return false;
        // Loading and movement have gaps between the authoritative object and
        // its routing registration. Neither gap proves that a saved stop died.
        if(!WorldObjectManager.Init.Initialized)return true;
        station??=ServiceHolder<IWorldObjectManager>.Obj.GetFromID(id)?.GetComponent<TrainStationComponent>();
        if(station==null)return false;
        lock(TrackStationGate)
            return !station.removed && (!station.Parent.Initialized || station.detached || station.bindingObservation==null
                || !StationObjects.TryGetValue(id,out var registered) || !ReferenceEquals(registered,station));
    }
    private string? indexedName;
    private Vector3? indexedPosition;
    // One immutable observation prevents overlapping ticks from committing an
    // older discovery. It is runtime scratch, never saved station state.
    private sealed record BindingObservation(Vector3 Position, VoxelRail? Rail, long Revision, HashSet<RailCell> Watched);
    private BindingObservation? bindingObservation;
    private bool removed;
    private bool detached;
    private long movementRevision;
    // Dialogs must retain a station's movement history as well as its current
    // coordinates: moving away and back or rotating can restore the same cell.
    // Capture under the existing binding gate; never hold it across UI awaits
    // or rail discovery. This state is temporary and is not serialized.
    private readonly record struct StationEditOrigin(Vector3 Position,RailCell? Cell,float T,long Movement);
    private StationEditOrigin? CaptureEditOrigin()
    {
        lock(TrackStationGate)
            return removed||detached||Parent.IsDestroyed ? null : new(Parent.Position,Cell,TrackT,movementRevision);
    }
    private bool EditOriginCurrent(StationEditOrigin? origin)=>origin.HasValue&&CaptureEditOrigin()==origin;
    private static HashSet<RailCell> BindingCells(Vector3 position)
    {
        // Match TrackWorld.Near's block-query cube, including empty cells where
        // construction could introduce a nearer line. Object rail registration
        // already publishes an unknown change and refreshes conservatively.
        var x=(int)MathF.Floor(position.X+.5f);var y=(int)MathF.Floor(position.Y+.5f);var z=(int)MathF.Floor(position.Z+.5f);
        var cells=new HashSet<RailCell>();
        for(var dx=-2;dx<=2;dx++)for(var dy=-2;dy<=2;dy++)for(var dz=-2;dz<=2;dz++)
            cells.Add(RailPowerConnectionComponent.PhysicalCell(new(x+dx,y+dy,z+dz)));
        return cells;
    }
    private VoxelTrackProfile? boundProfile;
    private void BindTrack(RailCell? cell, float parameter, VoxelTrackProfile? profile=null)
    {
        // Station identity follows physical hardware. Vehicle path geometry
        // remains continuous when either position crosses the torus seam.
        if(cell is {} image)cell=RailPowerConnectionComponent.PhysicalCell(image);
        lock(TrackStationGate)
        {
            if((removed || detached) && cell.HasValue)return;
            if(Cell!=cell)
            {
                ResetContacts();
                if(Cell is {} old && TrackStations.TryGetValue(old,out var entries))
                {
                    var kept=entries.Where(s=>s!=this).ToArray();
                    if(kept.Length==0)TrackStations.TryRemove(old,out _); else TrackStations[old]=kept;
                }
                if(cell is {} next)
                    TrackStations[next]=TrackStations.GetValueOrDefault(next,[]).Where(s=>s!=this)
                        .Append(this).OrderBy(s=>s.Parent.ID).ToArray();
            }
            var changed=Cell!=cell || TrackT!=parameter || boundProfile!=profile;
            Cell=cell; TrackT=parameter; boundProfile=profile;
            if(changed)RoutingChanged();
        }
    }
    private sealed class SettingsClipboard { public StationDepartureData? Data; }
    private static readonly ConditionalWeakTable<object, SettingsClipboard> Clipboards = new();
    // Native inventory notifications invalidate departure samples without
    // rescanning stacks at each route/reservation check. Weak keys and a
    // callback that owns only its counter do not retain removed inventories.
    private sealed class InventoryChanges
    {
        internal long Revision;
        internal bool Subscribed;
        internal void Changed(User _) => Interlocked.Increment(ref Revision);
    }
    private static readonly ConditionalWeakTable<Inventory,InventoryChanges> InventoryRevisions = new();
    private static InventoryChanges WatchInventory(Inventory inventory)
    {
        // Keep GetValue's factory side-effect free: concurrent factories may
        // run more than once, but only the published counter may subscribe.
        var changes=InventoryRevisions.GetValue(inventory,_=>new());
        lock(changes)
            if(!changes.Subscribed)
            {inventory.OnChanged.Add(changes.Changed);changes.Subscribed=true;}
        return changes;
    }
    private sealed class PartChanges
    {
        internal long Revision;
        internal bool Subscribed;
        internal void Changed(object? _,System.ComponentModel.PropertyChangedEventArgs change)
        {
            if(string.IsNullOrEmpty(change.PropertyName) || change.PropertyName is nameof(PartItem.Durability) or nameof(PartItem.IntegrityAmount))
                Interlocked.Increment(ref Revision);
        }
    }
    private static readonly ConditionalWeakTable<PartItem,PartChanges> PartRevisions = new();
    private static PartChanges WatchPart(PartItem part)
    {
        // A part's native wear/repair notifications include changes that do not
        // replace its inventory stack. Do not retain the part from the counter.
        var changes=PartRevisions.GetValue(part,_=>new());
        lock(changes)
            if(!changes.Subscribed)
            {part.PropertyChanged+=changes.Changed;changes.Subscribed=true;}
        return changes;
    }
    [Serialized] public bool WaitForTime { get; set; }
    [Serialized] public float WaitSeconds { get; set; }
    [Serialized] public bool WaitForMinimumCargo { get; set; }
    [Serialized] public float MinimumCargoPercent { get; set; } = 90;
    [Serialized] public bool WaitForMaximumCargo { get; set; }
    [Serialized] public float MaximumCargoPercent { get; set; } = 10;
    [Serialized] public bool WaitForFullCars { get; set; }
    [Serialized] public bool WaitForEmptyCars { get; set; }
    [Serialized] public bool RequireAllConditions { get; set; }
    // These are structured identities, not player-authored display text. Eco
    // associates an explicit SetX RPC with X's property-level input policy.
    [Serialized, EcoTextLimit(0,4096,4096), Profanity(Filter=Eco.Shared.Utils.ProfanityFilterType.NoFilter)]
    public string SelectedCarIds { get; set; } = "";
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
            int revision;string original;
            lock(settingsGate){revision=settingsRevision;original=SelectedCarIds;}
            var position=Parent.Position;
            var origin=CaptureEditOrigin();
            var train=RailCouplingComponent.NearestTo(Parent.Position,12);
            var members=train?.Group() ?? [];
            var cars=members.Where(c=>!c.Vehicle.RailSpec.Powered && !c.Vehicle.RailSpec.Tender && c.Vehicle.RailSpec.PassengerSeats==0).ToArray();
            var originalIds=new HashSet<Guid>();var originalValid=true;
            foreach(var id in original.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
                if(Guid.TryParse(id,out var parsed))originalIds.Add(parsed);else originalValid=false;
            var selected=cars.Where(c=>originalIds.Contains(c.Parent.ObjectID)).Select(c=>c.Parent.ObjectID).ToHashSet();
            var missingSelection=!originalValid||originalIds.Except(selected).Any();
            // Compare this train's membership, not the global topology stamp:
            // coupling on an unrelated line must not dismiss an open picker.
            bool CurrentTrain()
            {
                if(!EditOriginCurrent(origin))return false;
                var current=RailCouplingComponent.NearestTo(position,12)?.Group() ?? [];
                return current.Length==members.Length && current.ToHashSet().SetEquals(members)
                    && members.All(c=>!c.Parent.IsDestroyed);
            }
            void Status(string text){SettingsAction=text;this.Changed(nameof(SettingsAction));}
            var page=0;
            while(true)
            {
                var entries=new List<(string Label,int Car,int Step)>{("Use all cargo cars"+(string.IsNullOrWhiteSpace(original)?" (current)":""),-1,0)};
                for(var i=page*32;i<Math.Min(cars.Length,(page+1)*32);i++)
                {
                    var car=cars[i];var at=car.Parent.Position3i;
                    entries.Add(($"[{(selected.Contains(car.Parent.ObjectID)?"x":" ")}] {i+1}. {car.Parent.DisplayName} ({at.X}, {at.Y}, {at.Z})",i,0));
                }
                if(page>0)entries.Add(("< Previous page",-1,-1));
                if((page+1)*32<cars.Length)entries.Add(("Next page >",-1,1));
                if(selected.Count>0 && selected.Count<=124)entries.Add(($"Apply {selected.Count} selected cars",-2,0));
                var title=cars.Length==0?"No cargo cars on the nearby train. Use all cargo cars for automatic selection."
                    :"Toggle cargo cars, then Apply. Use all cargo cars for automatic selection. Cancel keeps the current setting.";
                if(missingSelection)
                    title+=" Some saved cars are unavailable; Apply replaces the selection.";
                var pick=await player.OptionBox(Localizer.DoStr(title),entries.Select(e=>e.Label).ToList());
                if(pick<0||pick>=entries.Count)return;
                lock(settingsGate)
                {
                    if(!CanConfigure(player))return;
                    if(revision!=settingsRevision){Status("Station settings changed; reopen cargo car selection");return;}
                    if(!CurrentTrain()){Status("Nearby train changed; reopen cargo car selection");return;}
                    var entry=entries[pick];
                    if(entry.Step!=0){page+=entry.Step;continue;}
                    if(entry.Car>=0)
                    {
                        var id=cars[entry.Car].Parent.ObjectID;
                        if(!selected.Remove(id))
                        {
                            // 124 N-format GUIDs plus commas fit the existing
                            // 4,096-character save/RPC contract.
                            if(selected.Count>=124)Status("Select at most 124 cargo cars");
                            else selected.Add(id);
                        }
                        continue;
                    }
                    var all=entry.Car==-1;
                    if(all?string.IsNullOrWhiteSpace(original):originalValid&&originalIds.SetEquals(selected))
                    {Status("Cargo car selection unchanged");return;}
                    SelectedCarIds=all?"":string.Join(",",cars.Where(c=>selected.Contains(c.Parent.ObjectID)).Select(c=>c.Parent.ObjectID.ToString("N")));
                    SettingsChanged();Status("Cargo car selection saved");return;
                }
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
    { get { lock(settingsGate) return this.expression!=null ? StationNativeConditions.Describe(this.expression) : this.Rules.Count == 0 ? "No conditions: train waits" : string.Join("\n", this.Rules.Select((r, i) => $"{i + 1}. {StationNativeConditions.FromRule(r).Description()}")); } }
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
                // Replacing saved state also invalidates in-flight evaluations
                // and editor replies; it need not publish a view during load.
                settingsRevision++;
            }
        }
    }
    private bool CanConfigure(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/CanConfigure", this.Parent); return player != null && Parent != null && !Parent.IsDestroyed && Parent.IsAuthorized(player.User, AccessType.FullAccess)
        && RailWorldDistance.Within(player.User.Position, Parent.Position, 6); }
    private void SettingsChanged() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/SettingsChanged", this.Parent); settingsRevision++; this.Changed(nameof(Conditions)); this.Changed(nameof(Comparison)); this.Changed(nameof(CargoCars)); RefreshDestinationRulesSummary(); Parent.SetDirty(); }
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
        OpenNativeEditor(player,draft,revision,value=> { expression=StationNativeConditions.Copy(value); rules=new(); if(expression is SetOfConditions set) Comparison=set.Comparison; return true; });
    }
    private void OpenNativeEditor(Player player,GameValue<bool>? draft,int revision,Func<GameValue<bool>?,bool> commit,
        string title="Train station departure conditions",string saved="Departure conditions saved")
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/OpenNativeEditor", this.Parent);
        var origin=CaptureEditOrigin();
        bool CurrentOrigin()
        {
            if(EditOriginCurrent(origin))return true;
            SettingsAction="Station moved or reconnected; reopen conditions";this.Changed(nameof(SettingsAction));return false;
        }
        ViewEditorUtils.PopupUserEditValue(player.User,typeof(GameValue<bool>),Localizer.DoStr(title),draft,this,value=>
        {
            if(!CanConfigure(player)||!CurrentOrigin()) return;
            var submitted=value as GameValue<bool>;
            if(!StationPassengerConditions.CanNormalize(submitted))
            {SettingsAction="Unsupported or oversized conditions were not saved; reopen the editor";this.Changed(nameof(SettingsAction));return;}
            var condition=StationPassengerConditions.Normalize(submitted);
            lock(settingsGate)
            {
                if(revision!=settingsRevision) { SettingsAction="Settings changed while the editor was open; reopen it"; this.Changed(nameof(SettingsAction)); return; }
                if(!StationNativeConditions.Valid(condition)) { SettingsAction="Use Train Station conditions with Any/All/None or Not; unsupported or oversized expression was not saved"; this.Changed(nameof(SettingsAction)); return; }
            }
            _=CompleteStationEditor(player,condition,revision,value=>CurrentOrigin()&&commit(value),saved);
        });
    }
    [RPC] public void RemoveCondition(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/RemoveCondition", this.Parent); if(!CanConfigure(player)) return; lock(settingsGate) { if(expression is SetOfConditions root && StationNativeConditions.Valid(root)) { var next=(SetOfConditions)StationNativeConditions.Copy(root)!; if(DraftRuleNumber<1||DraftRuleNumber>next.List.Count) return; next.List.RemoveAt(DraftRuleNumber-1); expression=next; rules=new(); } else if(expression!=null) { expression=null; rules=new(); } else { if(DraftRuleNumber < 1 || DraftRuleNumber > Rules.Count) return; var next=Rules.ToList(); next.RemoveAt(DraftRuleNumber-1); rules=next; } SettingsChanged(); } }
    [RPC] public void SetComparison(Player player, RequiredTrue value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/SetComparison", this.Parent); if(!CanConfigure(player) || !Enum.IsDefined(value)) return; lock(settingsGate) { _ = Rules; Comparison=value; if(expression is SetOfConditions set && StationNativeConditions.Valid(set)) { var next=(SetOfConditions)StationNativeConditions.Copy(set)!; next.Comparison=value; expression=next; } SettingsChanged(); } }
    [RPC] public void SetSelectedCarIds(Player player, [EcoTextLimit(0,4096,4096)] string value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/SetSelectedCarIds", this.Parent); if(!CanConfigure(player) || value == null || value.Length > 4096) return; lock(settingsGate) { SelectedCarIds=value; SettingsChanged(); } }
    internal RailCell? Cell { get; private set; }
    internal float TrackT { get; private set; }
    // A crossing has two paths in one physical cell. Its stop parameter belongs
    // only to the path chosen by binding, including when queried in another
    // continuous world image. The cell index still serves physical occupancy.
    internal bool MatchesTrack(VoxelRail rail)
    {
        var cell=RailPowerConnectionComponent.PhysicalCell(rail.Cell);
        lock(TrackStationGate)
            return !removed && !detached && !Parent.IsDestroyed && Cell==cell && boundProfile==rail.Profile;
    }
    // Sample both stop coordinates under the gate used by movement/binding.
    // Controllers keep this value only for the lifetime of an unsaved hold.
    internal (RailCell Cell,float T,VoxelTrackProfile Profile)? BoundStop
    {
        get
        {
            lock(TrackStationGate)
                return !removed && !detached && !Parent.IsDestroyed && Cell is {} cell && boundProfile is {} profile ? (cell,TrackT,profile) : null;
        }
    }
    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/PostInitialize", this.Parent);
        base.PostInitialize(); _ = this.Rules;
        lock(TrackStationGate)
        {
            if(removed)return;
            Stations[this.Parent.ID]=this; StationObjects[Parent.ObjectID]=this;
        }
        this.Tick();
    }
    public override void Destroy()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Destroy", this.Parent);
        lock(TrackStationGate)
        {
            removed=true; BindTrack(null,0); Volatile.Write(ref bindingObservation,null);
            Stations.TryRemove(this.Parent.ID,out _); StationObjects.TryRemove(Parent.ObjectID,out _); RoutingChanged();
        }
        base.Destroy();
    }
    public override void OnBeforeObjectMoved()
    {
        base.OnBeforeObjectMoved();
        lock(TrackStationGate)
        {
            if(removed || Parent.IsDestroyed || detached)return;
            // Publish detachment before clearing the index and contacts. A
            // concurrent ordinary tick must not restore this old stop/source.
            detached=true;movementRevision++;
            BindTrack(null,0);Volatile.Write(ref bindingObservation,null);
            if(Connection!="Station is being moved")
            {Connection="Station is being moved";this.Changed(nameof(Connection));}
        }
    }
    public override void OnAfterObjectMoved()
    {
        base.OnAfterObjectMoved();
        lock(TrackStationGate)
        {
            if(removed || Parent.IsDestroyed)return;
            detached=false;
        }
        // Refresh only this station, through the existing bounded discovery.
        // Controllers and automation see the new binding before move returns.
        Tick();
    }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Tick", this.Parent);
        base.Tick();
        var movement=Volatile.Read(ref movementRevision);
        if(Parent.IsDestroyed || Volatile.Read(ref detached))return;
        var observation=Volatile.Read(ref bindingObservation);
        var revision=RailSimulationFrame.Revision;
        var position=Parent.Position;
        var ownRail=Parent.GetComponent<CoasterRailComponent>()?.Rail;
        var current=ownRail ?? (Cell is {} bound ? TrackWorld.Read(bound) : null);
        // A failed discovery is an observation too. Its watched empty cells
        // detect construction; unknown changes and journal overflow also retry.
        var refresh=observation==null || observation.Position!=position || observation.Rail!=current
            || ownRail==null && observation.Revision!=revision && RailSimulationFrame.ChangesAffect(observation.Revision,observation.Watched).Affected;
        var watched=observation!=null && observation.Position==position ? observation.Watched : BindingCells(position);
        (VoxelRail Rail,float T)? rail=null;
        if(refresh)
        {
            rail=ownRail is {} own ? (own,.5f)
                : new[] { TrackWorld.Nearest(position,2.5f), TrackWorld.Nearest(position,2.5f,true), TrackWorld.Nearest(position,2.5f,coaster:true) }
                    .Where(x=>x.HasValue).OrderBy(x=>Vector3.DistanceSquared(position,x!.Value.Rail.Point(x.Value.T))).FirstOrDefault();
        }
        // A crossing may select its alternate directed path. Cache the physical
        // cell's profile so that valid selection does not rediscover every tick.
        var observedRail=refresh ? ownRail ?? (rail is {} selected ? TrackWorld.Read(RailPowerConnectionComponent.PhysicalCell(selected.Rail.Cell)) : null) : current;
        lock(TrackStationGate)
        {
            // Discovery performs world reads outside the index gate. An edit,
            // movement, newer tick or teardown makes that result obsolete.
            if(removed || detached || movement!=movementRevision || Parent.IsDestroyed || revision!=RailSimulationFrame.Revision
                || position!=Parent.Position || !ReferenceEquals(observation,bindingObservation))return;
            if(refresh)
            {
                BindTrack(rail?.Rail.Cell,rail?.T ?? 0,rail?.Rail.Profile);
            }
            // Advance an unaffected observation too, so distant edits neither
            // force discovery nor eventually exhaust this reader's journal.
            if(refresh || observation!.Revision!=revision)
                Volatile.Write(ref bindingObservation,new(position,observedRail,revision,watched));
            var name=StationName;
            // Position also labels disconnected destinations. Their movement
            // may leave the bound cell/parameter unchanged (both are empty).
            if(indexedName!=name || indexedPosition!=position)
            {indexedName=name;indexedPosition=position;RoutingChanged();}
            var connection=Cell!=null ? "Connected to rail" : "No adjacent rail";
            if(Connection!=connection) {Connection=connection;this.Changed(nameof(Connection));}
        }
        // Never take settingsGate while holding the binding/index gate.
        RefreshDestinationRulesSummary();
    }
    internal static TrainStationComponent? Find(int id) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Find"); return Stations.TryGetValue(id, out var station) && !station.Parent.IsDestroyed ? station : null; }
    internal static TrainStationComponent[] At(RailCell cell) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/At"); return TrackStations.GetValueOrDefault(RailPowerConnectionComponent.PhysicalCell(cell),[]); }
    internal bool HasDepartureConditions {get {lock(settingsGate) return expression!=null || Rules.Count>0;}}
    internal bool ReadyToDepart(RailCouplingComponent train,double elapsed,bool defaultReady=false)
        =>!train.RepairRequired && (HasDepartureConditions?Ready(train,elapsed):defaultReady);
    internal void NotifyVehicle(Eco.Minecarts.Track.RailEvent action,Guid vehicle)
        =>Parent.GetComponent<RailAutomationComponent>()?.Emit(action,vehicle);
    internal bool Ready(RailCouplingComponent train, double elapsed)=>Ready(train,elapsed,out _);
    internal bool Ready(RailCouplingComponent train,double elapsed,out StationDepartureContext observation)
    {
        int revision; string selected;
        lock(settingsGate) { revision=settingsRevision; selected=SelectedCarIds; }
        var context=DepartureContextFor(train,elapsed,selected);
        observation=context;
        lock(settingsGate)
        {
            // An editor can change the selected cars while live inventory and
            // passenger data are being sampled. Do not evaluate newer settings
            // against an older selection; the next departure poll will retry.
            if(revision!=settingsRevision) return false;
            var ready=expression!=null ? StationNativeConditions.Ready(expression,context)
                : StationDepartureCondition.Ready(Rules,Comparison,elapsed,context.CargoPercent,context.Full,context.Empty,context.CargoKnown);
            return revision==settingsRevision && ready && context.IsCurrent;
        }
    }
    internal StationDepartureContext DepartureContext(RailCouplingComponent train,double elapsed)
    {
        string selected; lock(settingsGate) selected=SelectedCarIds;
        return DepartureContextFor(train,elapsed,selected);
    }
    private StationDepartureContext DepartureContextFor(RailCouplingComponent train,double elapsed,string selectedCarIds)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/Ready", this.Parent);
        var consist=train.Group();
        var ids = (selectedCarIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        HashSet<Guid>? requested = ids.Length>0 ? [] : null;
        var valid=true;
        foreach(var id in ids)
            if(Guid.TryParse(id,out var guid)) requested!.Add(guid); else valid=false;
        var count=0; var capacity=0d; var cargo=0d; var full=true; var empty=true;
        List<(RailCouplingComponent Car,Inventory Inventory,InventoryChanges Changes,long Revision)>? observedCargo=null;
        foreach(var car in consist)
        {
            var spec=car.Vehicle.RailSpec;
            if(requested!=null ? !requested.Contains(car.Parent.ObjectID)
                : spec.Powered || spec.Tender || spec.PassengerSeats!=0) continue;
            var inventory=car.Parent.GetComponent<PublicStorageComponent>().Inventory;
            var changes=WatchInventory(inventory);
            // Observe before sampling: a completed native edit during any of
            // the reads must invalidate the whole departure observation.
            (observedCargo??=[]).Add((car,inventory,changes,Volatile.Read(ref changes.Revision)));
            // Cargo conditions need cargo only. A full physics Load also scans
            // burner fuel and mounted players, and used to be read twice here.
            var weight=inventory.NonEmptyStacks.Sum(s=>(double)s.Weight)/1000;
            count++; capacity+=spec.CargoKg; cargo+=weight;
            full = full && (weight>=spec.CargoKg*.999 || inventory.IsFull);
            empty = empty && inventory.IsEmpty;
        }
        // Missing selected cars never silently satisfy full/empty conditions.
        // Network handles change after reload and can be recycled. Legacy
        // numeric selections fail closed until the owner selects cars again.
        valid &= count>0 && (requested==null || count==requested.Count);
        var percent=capacity>0 ? (float)Math.Clamp(cargo/capacity*100,0,100) : 0;
        if(!this.CargoPercent.Equals(percent)) { this.CargoPercent=percent; this.Changed(nameof(CargoPercent)); }
        full &= valid; empty &= valid;
        var passengerSeats=0;
        var occupiedSeats=0;
        var passengers=new List<User>();
        var observedSeats=new List<(MountComponent Mounts,int Seats,int[] Occupants)>();
        HashSet<int>? passengerIds=null;
        HashSet<int>? passengerUsers=null;
        foreach(var car in consist)
        {
            var mounts=car.Parent.GetComponent<MountComponent>();
            var seats=Math.Min(car.Vehicle.RailSpec.PassengerSeats,Math.Max(0,mounts.Seats-1));
            passengerSeats+=seats;
            if(seats==0)continue;
            // Count and resolve the same native seat snapshot. OccupantIDs
            // allocates an array; rereading it also mixed boarding states.
            var occupants=mounts.OccupantIDs;
            observedSeats.Add((mounts,seats,occupants));
            // Reuse local lookup scratch across cars; empty cars need neither
            // a lookup nor a second native occupant enumeration. Keep the
            // captured IDs for the same fail-closed freshness check below.
            passengerIds?.Clear();
            for(var seat=1;seat<occupants.Length && seat<=seats;seat++)
            {
                var id=occupants[seat];if(id<0)continue;
                occupiedSeats++;(passengerIds??=[]).Add(id);
            }
            if(passengerIds is not {Count:>0})continue;
            foreach(var player in mounts.MountedPlayers)
                if(passengerIds.Contains(player.ID) && (passengerUsers??=[]).Add(player.User.Id))passengers.Add(player.User);
        }
        // Capture each burner's inventory marker and published energy before
        // sampling fuel measures. Reuse this observation for all three measures;
        // freshness checks below only read identities, counters and scalars.
        var observedFuel=new List<(FuelSupplyComponent Fuel,Inventory Inventory,InventoryChanges Changes,long Revision,float Energy,float Supply)>();
        var engines=0;var fuelKnown=true;
        var fuelMinutes=double.PositiveInfinity;var fuelFill=double.PositiveInfinity;var fuelAmount=double.PositiveInfinity;
        foreach(var car in consist)
        {
            var spec=car.Vehicle.RailSpec;
            if(!spec.Powered || spec.Tram)continue;
            engines++;
            var fuel=car.Parent.GetComponent<FuelSupplyComponent>();
            if(fuel?.Inventory is not {} inventory){fuelKnown=false;continue;}
            var changes=WatchInventory(inventory);
            var revision=Volatile.Read(ref changes.Revision);
            var energy=fuel.Energy;var supply=fuel.EnergyInSupply;
            observedFuel.Add((fuel,inventory,changes,revision,energy,supply));
            var amount=energy+supply;
            fuelMinutes=Math.Min(fuelMinutes,amount/Math.Max(1,RailEconomy.FuelWatts(spec)*60));
            fuelFill=Math.Min(fuelFill,inventory.IsEmpty?0:Math.Clamp((double)inventory.FillPerCent*100,0,100));
            fuelAmount=Math.Min(fuelAmount,amount/1000000d);
        }
        if(engines==0 || !fuelKnown)fuelMinutes=fuelFill=fuelAmount=double.NaN;
        var observedConditions=new List<(RailCouplingComponent Car,RailConditionComponent? Condition,PartsComponent? Parts,Inventory? Inventory,InventoryChanges? Changes,long Revision,bool Working,float Percent)>(consist.Length);
        var observedParts=new List<(PartChanges Changes,long Revision)>();
        var minimumCondition=double.PositiveInfinity;var conditionKnown=consist.Length>0;
        foreach(var car in consist)
        {
            var condition=car.Parent.GetComponent<RailConditionComponent>();
            var parts=car.Parent.GetComponent<PartsComponent>();
            var inventory=parts?.Parts;
            var changes=inventory==null?null:WatchInventory(inventory);
            var revision=changes==null?0:Volatile.Read(ref changes.Revision);
            var working=parts?.AllPartsWorking??false;
            // Subscribe and capture markers before reading the native minimum.
            // Later checks use only identities, flags and counters, including
            // wear/repair roundtrips, without scanning Parts under TrafficGate.
            if(inventory!=null)
                foreach(var stack in inventory.NonEmptyStacks)
                    if(stack.Item is PartItem part)
                    {
                        var marker=WatchPart(part);
                        observedParts.Add((marker,Volatile.Read(ref marker.Revision)));
                    }
            var conditionPercent=condition?.ConditionPercent??float.NaN;
            observedConditions.Add((car,condition,parts,inventory,changes,revision,working,conditionPercent));
            conditionKnown &= condition!=null;
            minimumCondition=Math.Min(minimumCondition,conditionPercent);
        }
        if(!conditionKnown)minimumCondition=double.NaN;
        bool ObservationCurrent()
        {
            // Compare this consist, rather than the global topology revision:
            // coupling on an unrelated line must not cancel this departure.
            if(train.Parent.IsDestroyed)return false;
            var currentConsist=train.Group();
            if(!ReferenceEquals(currentConsist,consist) && !currentConsist.AsSpan().SequenceEqual(consist))return false;
            if(observedCargo!=null)
                foreach(var observed in observedCargo)
                    if(observed.Car.Parent.IsDestroyed
                        || !ReferenceEquals(observed.Car.Parent.GetComponent<PublicStorageComponent>().Inventory,observed.Inventory)
                        || Volatile.Read(ref observed.Changes.Revision)!=observed.Revision)return false;
            foreach(var observed in observedSeats)
            {
                if(observed.Mounts.Parent.IsDestroyed)return false;
                var current=observed.Mounts.OccupantIDs;
                if(observed.Occupants.Length<observed.Seats+1 || current.Length<observed.Seats+1 || !current.AsSpan(1,observed.Seats)
                    .SequenceEqual(observed.Occupants.AsSpan(1,observed.Seats)))return false;
            }
            foreach(var observed in observedFuel)
                if(observed.Fuel.Parent.IsDestroyed
                    || !ReferenceEquals(observed.Fuel.Inventory,observed.Inventory)
                    || Volatile.Read(ref observed.Changes.Revision)!=observed.Revision
                    || !observed.Fuel.Energy.Equals(observed.Energy)
                    || !observed.Fuel.EnergyInSupply.Equals(observed.Supply))return false;
            foreach(var observed in observedConditions)
                if(observed.Car.Parent.IsDestroyed
                    || !ReferenceEquals(observed.Car.Parent.GetComponent<RailConditionComponent>(),observed.Condition)
                    || !ReferenceEquals(observed.Car.Parent.GetComponent<PartsComponent>(),observed.Parts)
                    || !ReferenceEquals(observed.Parts?.Parts,observed.Inventory)
                    || (observed.Changes!=null && Volatile.Read(ref observed.Changes.Revision)!=observed.Revision)
                    || (observed.Parts!=null && observed.Parts.AllPartsWorking!=observed.Working)
                    || (observed.Inventory==null && observed.Condition!=null && !observed.Condition.ConditionPercent.Equals(observed.Percent)))return false;
            foreach(var observed in observedParts)
                if(Volatile.Read(ref observed.Changes.Revision)!=observed.Revision)return false;
            return true;
        }
        var traffic=TrafficObservation(consist);
        return new StationDepartureContext { Elapsed=elapsed,CargoPercent=valid?percent:double.NaN,CargoKnown=valid,Full=full,Empty=empty,
            CarCount=consist.Length,PassengerSeats=passengerSeats,OccupiedPassengerSeats=occupiedSeats,MinimumConditionPercent=minimumCondition,FuelMinutes=fuelMinutes,
            FuelPercent=fuelFill,FuelMegajoules=fuelAmount,Passengers=passengers.ToArray(),ObservationCurrent=ObservationCurrent,
            StationPresence=traffic.Observe,StationIncoming=traffic.Incoming,StationPresenceCurrent=traffic.Current };
    }
}

