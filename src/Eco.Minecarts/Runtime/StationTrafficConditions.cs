using Eco.Core.Controller;
using Eco.Core.Utils;
using Eco.Gameplay.Civics.GameValues;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.UI;
using Eco.Minecarts.Track;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.View;

namespace Eco.Minecarts.Runtime;

[Eco, Serialized, LocCategory("Train Station"), LocDisplayName("Train at station"),
 LocDescription("Choose a connected station when applying the conditions. Trains headed there also count. Use Not for an empty station."), RequiredContext(typeof(StationDepartureContext))]
public sealed class StationTrainPresentCondition : GameValue<bool>, IStationTrainCondition
{
    [Serialized, SyncToView] public Guid StationId { get; set; }
    [Eco, PropReadOnly, LocDisplayName("Station")] public string SelectedStation
    {
        get=>TrainStationComponent.Find(StationId)?.StationName ?? "Choose station when applying";
        // Eco's native GameValue clone serializer requires a setter for Eco
        // inputs. The label is derived from the persistent station identity.
        private set { }
    }
    public override LocString Description() => Localizer.Do($"Train at {SelectedStation} (including en route)");
    protected override Eval<bool> Evaluate(Eco.Core.Utils.PropertyScanning.IContextObject action)
    {
        if(action is not StationDepartureContext context || context.StationPresence is not {} presence
            || presence(StationId) is not {} occupied)
            return Eval.MakeFailLoc<bool>($"Station is unavailable or disconnected. Use Sync Now at the departure station.");
        return Eval.Make(Description(),occupied);
    }
}

public sealed partial class TrainStationComponent
{
    // Physical contact continues to use the established contact index. This
    // small intent index adds only trains' current next-stop reservations.
    private static readonly object TrafficGate=new();
    private static readonly Dictionary<Guid,(TrainControllerComponent Controller,Guid Station)> Inbound=[];
    private static readonly Dictionary<Guid,HashSet<Guid>> InboundStations=[];
    private bool hasTrafficContact;
    private readonly HashSet<Guid> trafficContactCars=[];
    private sealed record DetectedStation(Guid Id,string Name,RailCell Cell,System.Numerics.Vector3 Position)
    {public StationEditOrigin? Origin {get;init;}}
    private DetectedStation[]? detectedStations;
    private readonly object discoveryGate=new();
    private sealed record ConnectedObservation(RailCell Source,RailCell Target,System.Numerics.Vector3 SourcePosition,
        System.Numerics.Vector3 TargetPosition,long Revision,HashSet<RailCell> Watched,bool Connected)
    {
        // Recency changes under discoveryGate without allocating a new record
        // on every unchanged station-condition check.
        internal long LastUse {get;set;}
    }
    // Editor history and old conditions must not retain a whole explored
    // network per target. Bound both entries and the total path witnesses.
    private const int ConnectionTargetLimit=64,ConnectionCellLimit=8192;
    private readonly Dictionary<Guid,ConnectedObservation> connectionChecks=[];
    private int connectionCells;
    private long connectionUse;
    private void ForgetConnection(Guid id)
    {if(connectionChecks.Remove(id,out var old))connectionCells-=old.Watched.Count;}
    private void RememberConnection(Guid id,ConnectedObservation observation)
    {
        ForgetConnection(id);
        while(connectionChecks.Count>=ConnectionTargetLimit || connectionCells+observation.Watched.Count>ConnectionCellLimit)
            ForgetConnection(connectionChecks.MinBy(p=>p.Value.LastUse).Key);
        observation.LastUse=++connectionUse;
        connectionChecks[id]=observation;
        connectionCells+=observation.Watched.Count;
    }
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Detected Stations")]
    public string DetectedStations => Volatile.Read(ref detectedStations) is {} found
        ? $"{Math.Max(0,found.Length-1)} connected stations. Use Sync Now after adding stations or changing the railroad."
        : "Use Sync Now to detect connected stations";
    [RPC,Autogen,UITypeName("BigButton")]
    public void SyncNow(Player player)
    {
        if(!CanConfigure(player))return;
        if(!RefreshDetectedStations()){SettingsAction="Railroad changed during detection; try Sync Now again";}
        else SettingsAction="Detected station list synchronized";
        this.Changed(nameof(SettingsAction));
    }
    private bool RefreshDetectedStations()
    {
        Tick();
        if(Cell is not {} cell)return false;
        var found=DestinationOptions(cell);if(found==null)return false;
        var next=found.Prepend(this).Where(s=>s.Cell.HasValue&&!s.Parent.IsDestroyed)
            .Select(s=>new DetectedStation(s.Parent.ObjectID,s.StationName,s.Cell!.Value,s.Parent.Position){Origin=s.CaptureEditOrigin()}).ToArray();
        lock(discoveryGate){Volatile.Write(ref detectedStations,next);connectionChecks.Clear();connectionCells=0;}
        this.Changed(nameof(DetectedStations));return true;
    }
    private bool ConnectedForCondition(Guid targetId)
    {
        var target=Find(targetId);
        if(target==null||Cell is not {} source||target.Cell is not {} destination)
        {lock(discoveryGate)ForgetConnection(targetId);return false;}
        if(target==this)return TrackWorld.Read(source)!=null;
        lock(discoveryGate)
        {
            var revision=RailSimulationFrame.Revision;
            var position=Parent.Position;var targetPosition=target.Parent.Position;
            if(connectionChecks.TryGetValue(targetId,out var old)&&old.Source==source&&old.Target==destination
                &&old.SourcePosition==position&&old.TargetPosition==targetPosition)
            {
                if(old.Revision==revision)
                {old.LastUse=++connectionUse;return old.Connected;}
                // A successful connection survives edits outside its proven
                // path. A failed search has an empty frontier: a new rail can
                // reconnect it, so retry that result after any world revision.
                if(old.Connected)
                {
                    var changes=RailSimulationFrame.ChangesAffect(old.Revision,old.Watched);
                    if(!changes.Affected){connectionChecks[targetId]=old with{Revision=changes.Revision,LastUse=++connectionUse};return true;}
                }
            }
            using var frame=RailSimulationFrame.Begin();
            var path=TrackWorld.Read(source)!=null&&TrackWorld.Read(destination)!=null
                ?RailNetworkSearch.ConnectedPath(RailPowerConnectionComponent.PhysicalCell(source),RailPowerConnectionComponent.PhysicalCell(destination),
                    at=>TrackWorld.NetworkNeighbors(at).Select(RailPowerConnectionComponent.PhysicalCell),ConnectionCellLimit):null;
            // A successful topology witness needs only its actual path. New
            // branches cannot break those edges; cuts touch a path endpoint.
            // Failed searches retry after every revision and retain no frontier.
            var watched=path?.ToHashSet()??[];
            var connected=path!=null;
            if(revision!=RailSimulationFrame.Revision||Cell!=source||target.Cell!=destination
                ||Parent.Position!=position||target.Parent.Position!=targetPosition||target.Parent.IsDestroyed)return false;
            RememberConnection(targetId,new(source,destination,position,targetPosition,revision,watched,connected));
            return connected;
        }
    }
    private bool? ObservePresence(Guid targetId,HashSet<Guid> ownCars,bool incomingOnly=false)
    {
        if(!ConnectedForCondition(targetId))return null;
        lock(TrafficGate)return incomingOnly?IncomingLocked(targetId,ownCars):PresenceLocked(targetId,ownCars);
    }
    private static bool PresenceLocked(Guid targetId,HashSet<Guid> ownCars)
        =>Find(targetId) is {} target && (target.hasTrafficContact
            ||InboundStations.TryGetValue(targetId,out var incoming)&&incoming.Any(id=>!ownCars.Contains(id)
                &&Inbound.TryGetValue(id,out var entry)&&!entry.Controller.Parent.IsDestroyed));
    private static bool IncomingLocked(Guid targetId,HashSet<Guid> ownCars)
        =>Find(targetId) is {} target&&InboundStations.TryGetValue(targetId,out var incoming)
            &&incoming.Any(id=>!ownCars.Contains(id)&&!target.trafficContactCars.Contains(id)
                &&Inbound.TryGetValue(id,out var entry)&&!entry.Controller.Parent.IsDestroyed);
    private void SetTrafficContact(bool occupied)
    {
        // Called under ContactGate; publish an identity snapshot under the
        // traffic gate so departure checks never acquire those locks backwards.
        lock(TrafficGate)
        {
            hasTrafficContact=occupied;trafficContactCars.Clear();
            if(occupied)trafficContactCars.UnionWith(contactCars.Keys);
        }
    }
    internal static void SetInbound(TrainControllerComponent controller,Guid target)
    {
        lock(TrafficGate)
        {
            var id=controller.Parent.ObjectID;
            if(controller.Parent.IsDestroyed)target=Guid.Empty;
            if(Inbound.TryGetValue(id,out var old))
            {
                if(old.Station==target)return;
                if(InboundStations.TryGetValue(old.Station,out var previous))
                {previous.Remove(id);if(previous.Count==0)InboundStations.Remove(old.Station);}
                Inbound.Remove(id);
            }
            if(target==Guid.Empty)return;
            Inbound[id]=(controller,target);
            if(!InboundStations.TryGetValue(target,out var entries))InboundStations[target]=entries=[];
            entries.Add(id);
        }
    }
    // Conditions and claiming the selected next stop share this gate. Two
    // simultaneous departures cannot both accept the same empty observation.
    internal static bool ReserveInbound(TrainControllerComponent controller,Guid target,StationDepartureContext? observation)
    {
        lock(TrafficGate)
        {
            // Acquiring the traffic gate can wait after the controller's
            // last validation. Recheck the captured train as well as traffic
            // before publishing a reservation from that departure decision.
            if(observation is {IsCurrent:false})return false;
            SetInbound(controller,target);return true;
        }
    }
    internal bool ReserveDeparture(TrainControllerComponent controller,Guid target,StationDepartureContext? observation,
        int settings,long rails,long stations,long tram)
    {
        lock(TrafficGate)
        {
            // A reservation wait can outlive the rules or the discovered
            // route, including a default tram departure with no observation.
            // Read the revision directly: settingsGate can already be held
            // by a condition evaluator waiting for this traffic gate.
            if(observation is {IsCurrent:false}
                ||Volatile.Read(ref settingsRevision)!=settings
                ||RailSimulationFrame.NetworkRevision!=rails||RoutingRevision!=stations
                ||(controller.Parent.GetComponent<TramRouteComponent>()?.RoutingRevision??0)!=tram)return false;
            SetInbound(controller,target);return true;
        }
    }
    private (Func<Guid,bool?> Observe,Func<Guid,bool?> Incoming,Func<bool> Current) TrafficObservation(RailCouplingComponent[] consist)
    {
        // Connectivity is part of a sampled traffic condition. A later
        // occupancy match cannot validate a sample taken before a rail cut or
        // station rebind. Contexts that never sample traffic remain unaffected.
        var railsRevision=RailSimulationFrame.Revision;
        var stationsRevision=RoutingRevision;
        HashSet<Guid>? ownCars=null;
        Dictionary<(Guid Station,bool Incoming),bool>? witnessed=null;
        bool? Observe(Guid id,bool incomingOnly)
        {
            var key=(id,incomingOnly);
            if(witnessed!=null&&witnessed.TryGetValue(key,out var cached))return cached;
            ownCars??=consist.Select(c=>c.Parent.ObjectID).ToHashSet();
            var value=ObservePresence(id,ownCars,incomingOnly);if(value.HasValue)(witnessed??=[])[key]=value.Value;return value;
        }
        bool Current()
        {
            lock(TrafficGate)return witnessed==null || railsRevision==RailSimulationFrame.Revision
                && stationsRevision==RoutingRevision && witnessed.All(p=>Find(p.Key.Station)!=null
                &&(p.Key.Incoming?IncomingLocked(p.Key.Station,ownCars!):PresenceLocked(p.Key.Station,ownCars!))==p.Value);
        }
        return(id=>Observe(id,false),id=>Observe(id,true),Current);
    }
    private TrainStationComponent[]? CachedDestinationOptions()
    {
        if(Volatile.Read(ref detectedStations)==null&&!RefreshDetectedStations())return null;
        return (Volatile.Read(ref detectedStations)??[]).Where(s=>s.Id!=Parent.ObjectID).Select(s=>Find(s.Id))
            .Where(s=>s!=null).Cast<TrainStationComponent>().ToArray();
    }
    private static IEnumerable<IStationTrainCondition> PresenceConditions(GameValue<bool>? node)
    {
        if(node is IStationTrainCondition station)yield return station;
        else if(node is Not not)foreach(var leaf in PresenceConditions(not.NotValue))yield return leaf;
        else if(node is SetOfConditions set)foreach(var child in set.List)foreach(var leaf in PresenceConditions(child))yield return leaf;
    }
    private static GameValue<bool>? SelectablePresence(GameValue<bool>? node)=>node switch
    {
        GameValueContext<bool> context when context.Name==nameof(StationDepartureContext.TrainAtStation)=>new StationTrainPresentCondition(),
        Not not=>new Not{NotValue=SelectablePresence(not.NotValue)!},
        SetOfConditions set=>PresenceSet(set),
        _=>node
    };
    private static SetOfConditions PresenceSet(SetOfConditions set)
    {var copy=new SetOfConditions{Comparison=set.Comparison};foreach(var child in set.List)copy.List.Add(SelectablePresence(child)!);return copy;}
    private async Task CompleteStationEditor(Player player,GameValue<bool>? condition,int revision,
        Func<GameValue<bool>?,bool> commit,string saved)
    {
        try
        {
            if(!StationNativeConditions.Valid(condition))return;
            condition=SelectablePresence(condition);
            var leaves=PresenceConditions(condition).ToArray();
            if(leaves.Length>0)Tick();
            var selections=new Dictionary<IStationTrainCondition,DetectedStation>(ReferenceEqualityComparer.Instance);
            var origin=CaptureEditOrigin();
            void Status(string text){SettingsAction=text;this.Changed(nameof(SettingsAction));}
            bool Current()
            {
                if(!CanConfigure(player))return false;
                lock(settingsGate)if(settingsRevision!=revision)
                {Status("Settings changed; reopen conditions");return false;}
                if(!EditOriginCurrent(origin))
                {Status("Station moved; reopen conditions");return false;}
                return true;
            }
            async Task<bool> Choose(IStationTrainCondition leaf)
            {
                if(!Current())return false;
                if(Volatile.Read(ref detectedStations)==null&&!RefreshDetectedStations())return false;
                // Retain the exact options shown across page replies. Sync Now
                // or a moving target must not remap an older numeric reply.
                var options=Volatile.Read(ref detectedStations)??[];
                var page=Math.Max(0,Array.FindIndex(options,s=>s.Id==leaf.StationId))/32;
                while(true)
                {
                    var rows=options.Skip(page*32).Take(32).Select(s=>(Label:s.Name+(s.Id==leaf.StationId?" (current)":"")+$" ({s.Position.X:0}, {s.Position.Y:0}, {s.Position.Z:0})",Id:s.Id,Step:0)).ToList();
                    if(page>0)rows.Add(("< Previous page",Guid.Empty,-1));
                    if((page+1)*32<options.Length)rows.Add(("Next page >",Guid.Empty,1));
                    var prompt=leaf is StationTrainIncomingCondition
                        ?"Choose the station expecting a train. For a replacement service, choose this station. Parked trains do not count. Cancel keeps the saved conditions."
                        :"Choose the station to check. Trains en route also count. Cancel keeps the saved conditions. Use Sync Now to discover added stations.";
                    var pick=await player.OptionBox(Localizer.DoStr(prompt),rows.Select(r=>r.Label).ToList());
                    if(!Current()||pick<0||pick>=rows.Count)return false;
                    if(rows[pick].Step!=0){page+=rows[pick].Step;continue;}
                    var target=options.First(s=>s.Id==rows[pick].Id);
                    if(Find(target.Id) is not {} current||!current.EditOriginCurrent(target.Origin)||current.Cell!=target.Cell||current.Parent.Position!=target.Position||!ConnectedForCondition(target.Id))
                    {Status("Station or railroad changed; use Sync Now and reopen conditions");return false;}
                    leaf.StationId=target.Id;selections[leaf]=target;return true;
                }
            }
            if(leaves.Length>0)
            {
                // Existing identities need no discovery or repeated target
                // picker. New/missing choices still require an explicit target.
                var review=leaves.Any(leaf=>leaf.StationId!=Guid.Empty);
                foreach(var leaf in leaves)
                {
                    if(Find(leaf.StationId) is {} target&&target.Cell is {} cell)
                        selections[leaf]=new(target.Parent.ObjectID,target.StationName,cell,target.Parent.Position){Origin=target.CaptureEditOrigin()};
                    else if(!await Choose(leaf))return;
                }
                if(review)
                {
                    var page=0;
                    while(true)
                    {
                        var rows=new List<(string Label,int Leaf,int Step)>{("Apply station choices",-1,0)};
                        rows.AddRange(leaves.Skip(page*32).Take(32).Select((leaf,index)=>
                        {
                            var target=selections[leaf];
                            var kind=leaf is StationTrainIncomingCondition?"Train on the way to":"Train at";
                            return ($"Change {page*32+index+1}: {kind} {target.Name} ({target.Position.X:0}, {target.Position.Y:0}, {target.Position.Z:0})",page*32+index,0);
                        }));
                        if(page>0)rows.Add(("< Previous page",-1,-1));
                        if((page+1)*32<leaves.Length)rows.Add(("Next page >",-1,1));
                        var pick=await player.OptionBox(Localizer.DoStr("Review stations used by these conditions. Apply keeps these choices; select a condition to change its station. Cancel keeps the saved conditions."),rows.Select(r=>r.Label).ToList());
                        if(!Current()||pick<0||pick>=rows.Count)return;
                        if(rows[pick].Step!=0){page+=rows[pick].Step;continue;}
                        if(rows[pick].Leaf<0)break;
                        if(!await Choose(leaves[rows[pick].Leaf]))return;
                    }
                }
            }
            lock(settingsGate)
            {
                if(!Current()||!StationNativeConditions.Valid(condition))return;
                var railsRevision=RailSimulationFrame.Revision;
                var stationsRevision=RoutingRevision;
                if(selections.Values.Any(target=>Find(target.Id) is not {} current||!current.EditOriginCurrent(target.Origin)||current.Cell!=target.Cell
                        ||current.Parent.Position!=target.Position||!ConnectedForCondition(target.Id))
                    ||leaves.Length>0&&(railsRevision!=RailSimulationFrame.Revision||stationsRevision!=RoutingRevision))
                {Status("Station or railroad changed; use Sync Now and reopen conditions");return;}
                if(!commit(condition))return;
                SettingsChanged();SettingsAction=saved;this.Changed(nameof(SettingsAction));
            }
        }
        catch(Exception error){Eco.Shared.Logging.Log.WriteErrorLineLoc($"Station condition selection failed: {error}");}
    }
}
