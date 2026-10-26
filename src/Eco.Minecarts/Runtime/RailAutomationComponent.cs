using System.Diagnostics;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.UI;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized]
public sealed class RailSignalSetting
{
    [Serialized] public Guid Id { get; set; } = Guid.NewGuid();
    [Serialized] public Guid Target { get; set; }
    [Serialized] public int Author { get; set; }
    [Serialized] public RailEvent Event { get; set; } = RailEvent.Arrive;
    [Serialized] public RailCommand Command { get; set; }
    [Serialized] public double Delay { get; set; }
    internal RailSignalSetting Copy() { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/Copy"); return new() { Id=Id,Target=Target,Author=Author,Event=Event,Command=Command,Delay=Delay }; }
    internal RailControlRule Rule => new(Id,Target,Event,Command,Delay);
}

/// <summary>World adapter for the shared rail signal bus; no client scripts required.</summary>
[Serialized,NoIcon,AutogenClass,LocDisplayName("Rail Network Controls")]
public sealed class RailAutomationComponent : WorldObjectComponent, IPersistentData
{
    private readonly object gate = new();
    private List<RailSignalSetting> rules = [];
    // The serializer receives an independent snapshot, never the live mutable
    // list or its entries while an RPC is editing them.
    [Serialized,ThreadSafe] private List<RailSignalSetting> SavedRules
    {
        get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailAutomationComponent.SavedRules.get", this.Parent); return (List<RailSignalSetting>)PersistentData; }
        set { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailAutomationComponent.SavedRules.set", this.Parent); PersistentData=value; }
    }
    private readonly RailControlQueue queue = new();
    private Guid draftTarget;
    private RailEvent draftEvent = RailEvent.Arrive;
    private RailCommand draftCommand;
    private int draftDelay;
    // A popup belongs to the field revision it displayed, even if another
    // editor changes that selection away and back while the popup is open.
    private long draftTargetRevision, draftEventRevision, draftCommandRevision;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private static string TargetName(Guid id)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/TargetName");
        var target=MinecartChainDriveObject.ActiveDrives.FirstOrDefault(d=>d.ObjectID==id);
        return target!=null ? $"{target.DisplayName} at {target.Position3i}" : id==Guid.Empty ? "no target selected" : "drive no longer available";
    }
    private static string EventName(RailEvent value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/EventName"); return value switch
    { RailEvent.Enter=>"Cart enters", RailEvent.Leave=>"Cart leaves", RailEvent.Arrive=>"Train arrives", RailEvent.Dispatch=>"Train departs", _=>"Unknown event" }; }
    private static string CommandName(RailCommand value) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/CommandName"); return value switch
    { RailCommand.Activate=>"Turn on", RailCommand.Deactivate=>"Turn off", RailCommand.Toggle=>"Toggle", _=>"Unknown action" }; }
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("New Rule")] public string Draft => $"{EventName(draftEvent)}: {CommandName(draftCommand)} {TargetName(draftTarget)}, delay {draftDelay}s";
    [SyncToView,Autogen,Eco.Shared.Networking.Range(0,86400),LocDisplayName("Delay (seconds)")]
    public int DelaySeconds => draftDelay;
    [SyncToView,Autogen,PropReadOnly] public string Rules
    {
        get { lock(gate) return rules.Count==0 ? "No automatic controls" : string.Join("\n",rules.Select((r,i)=>$"{i+1}. {EventName(r.Event)}: {CommandName(r.Command)} {TargetName(r.Target)}, after {r.Delay:0}s")); }
    }
    [SyncToView,Autogen,PropReadOnly] public string Status { get; private set; } = "Select a connected chain drive, event and action, then Add Rule.";
    public object PersistentData
    {
        get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailAutomationComponent.PersistentData.get", this.Parent); lock(gate) return rules.Select(r=>r.Copy()).ToList(); }
        set
        { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailAutomationComponent.PersistentData.set", this.Parent);
            lock(gate)
            {
                rules = (value as List<RailSignalSetting> ?? []).Where(r=>r!=null && r.Id!=Guid.Empty && r.Target!=Guid.Empty
                    && Enum.IsDefined(r.Event) && Enum.IsDefined(r.Command) && double.IsFinite(r.Delay) && r.Delay>=0 && r.Delay<=86400)
                    .DistinctBy(r=>r.Id).Take(32).Select(r=>r.Copy()).ToList();
                queue.Clear();
            }
        }
    }
    private bool CanEdit(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/CanEdit", this.Parent); return player!=null && Parent!=null && !Parent.IsDestroyed
        && Parent.IsAuthorized(player.User,AccessType.FullAccess) && RailWorldDistance.Within(player.User.Position,Parent.Position,5); }
    private void Publish(string status)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/Publish", this.Parent);
        Status=status; this.Changed(nameof(Status)); this.Changed(nameof(Draft)); this.Changed(nameof(Rules));
    }
    private RailCell? Source => Parent.GetComponent<TrainStationComponent>()?.Cell is {} cell
        ? RailPowerConnectionComponent.PhysicalCell(cell) : null;
    // Device identities use physical cells. Keep port matching in the current
    // coordinate image, then fold its results before membership/visit checks.
    // Otherwise crossing a seam misses wrapped targets and revisits each lap.
    private static IEnumerable<RailCell> ConnectedNeighbors(RailCell cell)
        => TrackWorld.NetworkNeighbors(cell).Select(RailPowerConnectionComponent.PhysicalCell);
    private bool Connected(MinecartChainDriveObject target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/Connected", this.Parent);
        // A graph search can overlap construction or station rebinding. Do
        // not deliver a command from a mixture of old and new rail networks.
        var revision=RailSimulationFrame.Revision;
        var targetPosition=target.Position;
        if(Parent.IsDestroyed || target.IsDestroyed || Source is not {} start) return false;
        return TargetCell(target) is {} destination
            && RailNetworkSearch.Connected(start,destination,ConnectedNeighbors)
            && revision==RailSimulationFrame.Revision
            && Source==start
            && target.Position==targetPosition && !Parent.IsDestroyed && !target.IsDestroyed;
    }
    private readonly record struct DeliveryConnection(long Revision,RailCell Source,Guid Target,Vector3 Position);
    private bool ConnectedForDelivery(MinecartChainDriveObject target,Dictionary<DeliveryConnection,bool>? connections)
    {
        if(connections==null)return Connected(target);
        var revision=RailSimulationFrame.Revision;var position=target.Position;
        if(Parent.IsDestroyed||target.IsDestroyed||Source is not {} source)return false;
        var key=new DeliveryConnection(revision,source,target.ObjectID,position);
        if(!connections.TryGetValue(key,out var connected))
        {
            connected=Connected(target);
            // A result from an interrupted query must never enter the batch.
            if(revision!=RailSimulationFrame.Revision||Source!=source||target.Position!=position
                ||Parent.IsDestroyed||target.IsDestroyed)return false;
            connections[key]=connected;
        }
        // Only topology is reused, within this synchronous delivery batch.
        // Permissions are checked anew by the caller for every command.
        return connected&&revision==RailSimulationFrame.Revision&&Source==source&&target.Position==position
            &&!Parent.IsDestroyed&&!target.IsDestroyed;
    }
    private static RailCell? TargetCell(MinecartChainDriveObject target)
    {
        var destination = TrackWorld.Near(target.Position).Where(r=>r.Profile.Chain)
            .Select(r=>(Rail:r,Hit:r.Profile.Nearest(target.Position-r.Cell.Origin)))
            .Where(r=>r.Hit.Distance<=1.6f).OrderBy(r=>r.Hit.Distance).FirstOrDefault();
        return destination.Rail.Profile.Shape!=null ? RailPowerConnectionComponent.PhysicalCell(destination.Rail.Cell) : null;
    }
    // One bounded discovery per chooser, shared with the legacy cycling RPC.
    // Reject a network assembled across construction or source rebinding.
    private MinecartChainDriveObject[]? ConnectedDrives(Player player)
    {
        var revision=RailSimulationFrame.Revision;
        if(Source is not {} start)return [];
        using var frame=RailSimulationFrame.Begin();
        var candidates=MinecartChainDriveObject.ActiveDrives
            .Where(d=>!d.IsDestroyed && d.IsAuthorized(player.User,AccessType.FullAccess))
            .Select(d=>(Drive:d,Position:d.Position,Cell:TargetCell(d))).Where(d=>d.Cell.HasValue).ToArray();
        var connected=RailNetworkSearch.ConnectedTargets(start,candidates.Select(d=>d.Cell!.Value),ConnectedNeighbors);
        var targets=candidates.Where(d=>connected.Contains(d.Cell!.Value) && !d.Drive.IsDestroyed && d.Drive.Position==d.Position
            && d.Drive.IsAuthorized(player.User,AccessType.FullAccess)).Select(d=>d.Drive).ToArray();
        return revision==RailSimulationFrame.Revision && Source==start && !Parent.IsDestroyed?targets:null;
    }
    [RPC, Autogen, UITypeName("BigButton")]
    public void ChooseChainDrive(Player player){if(CanEdit(player))_=ChooseDrive(player);}
    private async Task ChooseDrive(Player player)
    {
        try
        {
            Guid original;long revision;
            lock(gate){if(!CanEdit(player))return;original=draftTarget;revision=draftTargetRevision;}
            var targets=ConnectedDrives(player);
            if(!CanEdit(player))return;
            if(targets==null){lock(gate)Publish("Rail network changed; reopen the drive chooser.");return;}
            var options=targets.OrderBy(d=>d.DisplayName.ToString(),StringComparer.OrdinalIgnoreCase).ThenBy(d=>d.ObjectID).ToArray();
            if(options.Length==0){lock(gate)Publish("No authorized chain drive on this connected rail network.");return;}
            var page=0;
            while(true)
            {
                var entries=options.Skip(page*32).Take(32).Select(d=>(Label:$"{d.DisplayName} at {d.Position3i}"+(d.ObjectID==original?" (current)":""),Drive:d,Step:0)).ToList();
                if(page>0)entries.Insert(0,("< Previous page",null!,-1));
                if((page+1)*32<options.Length)entries.Add(("Next page >",null!,1));
                var pick=await player.OptionBox(Localizer.DoStr("Choose a connected chain drive"),entries.Select(e=>e.Label).ToList());
                if(!CanEdit(player)||pick<0||pick>=entries.Count)return;
                if(entries[pick].Step!=0){page+=entries[pick].Step;continue;}
                lock(gate)
                {
                    if(!CanEdit(player))return;
                    if(draftTargetRevision!=revision||draftTarget!=original){Publish("Drive selection changed; reopen the drive chooser.");return;}
                    var target=entries[pick].Drive;
                    if(target.IsDestroyed||!target.IsAuthorized(player.User,AccessType.FullAccess)||!Connected(target))
                    {Publish("Drive no longer connected or authorized; reopen the drive chooser.");return;}
                    if(draftTarget==target.ObjectID)return;
                    draftTarget=target.ObjectID;draftTargetRevision++;Publish($"Selected {target.DisplayName} at {target.Position3i}");return;
                }
            }
        }
        catch(Exception error){Eco.Shared.Logging.Log.WriteErrorLineLoc($"Rail drive chooser failed: {error}");}
    }
    // Retain the old RPC for already-open views, without another button.
    [RPC] public void NextChainDrive(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/NextChainDrive", this.Parent);
        if(!CanEdit(player)) return;
        var targets=ConnectedDrives(player)?.OrderBy(d=>d.ObjectID).ToArray();
        if(!CanEdit(player)) return;
        lock(gate)
        {
            if(targets==null){Publish("Rail network changed; choose a chain drive again.");return;}
            if(targets.Length==0) { if(draftTarget!=Guid.Empty){draftTarget=Guid.Empty;draftTargetRevision++;} Publish("No authorized chain drive on this connected rail network."); return; }
            var index=Array.FindIndex(targets,d=>d.ObjectID==draftTarget);
            var target=targets[(index+1)%targets.Length];
            if(draftTarget==target.ObjectID)return;
            draftTarget=target.ObjectID;draftTargetRevision++;
            Publish($"Selected {target.DisplayName} at {target.Position3i}");
        }
    }
    [RPC, Autogen, UITypeName("BigButton")] public void ChooseEvent(Player player)
    { if(CanEdit(player)) _=ChooseDraftOption(player,true); }
    [RPC, Autogen, UITypeName("BigButton")] public void ChooseAction(Player player)
    { if(CanEdit(player)) _=ChooseDraftOption(player,false); }
    private async Task ChooseDraftOption(Player player,bool chooseEvent)
    {
        try
        {
            int original;long revision;
            lock(gate)
            {
                if(!CanEdit(player))return;
                original=chooseEvent?(int)draftEvent:(int)draftCommand;
                revision=chooseEvent?draftEventRevision:draftCommandRevision;
            }
            var values=chooseEvent?Enum.GetValues<RailEvent>().Select(v=>(int)v).ToArray()
                :Enum.GetValues<RailCommand>().Select(v=>(int)v).ToArray();
            var labels=values.Select(v=>(chooseEvent?EventName((RailEvent)v):CommandName((RailCommand)v))
                +(v==original?" (current)":"")).ToList();
            var pick=await player.OptionBox(Localizer.DoStr(chooseEvent?"Choose the automatic control event":"Choose the chain drive action"),labels);
            if(pick<0||pick>=values.Length||!CanEdit(player))return;
            lock(gate)
            {
                if(!CanEdit(player))return;
                // Another editor or an already-open legacy view may have
                // changed this field while the native dialog was open.
                if((chooseEvent?draftEventRevision:draftCommandRevision)!=revision
                    ||(chooseEvent?(int)draftEvent:(int)draftCommand)!=original)
                {Publish("Selection changed; reopen the event or action chooser.");return;}
                var selected=values[pick];
                if(selected==original)return;
                if(chooseEvent){draftEvent=(RailEvent)selected;draftEventRevision++;}
                else{draftCommand=(RailCommand)selected;draftCommandRevision++;}
                Publish(chooseEvent?$"Event selected: {EventName(draftEvent)}.":$"Action selected: {CommandName(draftCommand)}.");
            }
        }
        catch(TimeoutException) { }
        catch(Exception error){Eco.Shared.Logging.Log.WriteErrorLineLoc($"Automatic control chooser failed: {error}");}
    }
    // Retain cycling RPCs for already-open views without extra generated buttons.
    [RPC] public void NextEvent(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/NextEvent", this.Parent);
        if(!CanEdit(player)) return;
        lock(gate) { draftEvent=(RailEvent)(((int)draftEvent+1)%4);draftEventRevision++; Publish("Entry, exit, arrival or dispatch selected."); }
    }
    [RPC] public void NextAction(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/NextAction", this.Parent);
        if(!CanEdit(player)) return;
        lock(gate) { draftCommand=(RailCommand)(((int)draftCommand+1)%3);draftCommandRevision++; Publish("Command selected."); }
    }
    [RPC] public void SetDelaySeconds(Player player,int value)
    { if(CanEdit(player)) lock(gate) ChangeDelay(value); }
    private void ChangeDelay(int value)
    {
        value=Math.Clamp(value,0,86400);if(value==draftDelay)return;
        draftDelay=value;this.Changed(nameof(DelaySeconds));Publish($"Delay set to {value} seconds.");
    }
    // Retain old RPCs for already-open views without generating extra buttons.
    [RPC] public void IncreaseDelay(Player player) {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/IncreaseDelay", this.Parent); if(CanEdit(player)) lock(gate) ChangeDelay(draftDelay+5); }
    [RPC] public void DecreaseDelay(Player player) {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/DecreaseDelay", this.Parent); if(CanEdit(player)) lock(gate) ChangeDelay(draftDelay-5); }
    [RPC, Autogen, UITypeName("BigButton")] public void AddRule(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/AddRule", this.Parent);
        if(!CanEdit(player)) return;
        lock(gate)
        {
            var target=MinecartChainDriveObject.ActiveDrives.FirstOrDefault(d=>d.ObjectID==draftTarget);
            if(target==null || !target.IsAuthorized(player.User,AccessType.FullAccess) || !Connected(target) || rules.Count>=32)
            { Publish("No valid target or 32-rule limit reached."); return; }
            rules.Add(new() { Target=draftTarget,Author=player.User.Id,Event=draftEvent,Command=draftCommand,Delay=draftDelay });
            Parent.SetDirty(); Publish("Rule saved.");
        }
    }
    [RPC, Autogen, UITypeName("BigButton")] public void RemoveRule(Player player)
    {
        if(CanEdit(player)) _=RemoveSelectedRule(player);
    }
    private async Task RemoveSelectedRule(Player player)
    {
        try
        {
            RailSignalSetting[] choices;
            lock(gate)
            {
                if(!CanEdit(player))return;
                choices=rules.Select(r=>r.Copy()).ToArray();
                if(choices.Length==0){Publish("No automatic control rules to remove.");return;}
            }
            var labels=choices.Select((r,i)=>$"{i+1}. {EventName(r.Event)}: {CommandName(r.Command)} {TargetName(r.Target)}, after {r.Delay:0}s").ToList();
            var pick=await player.OptionBox(Localizer.DoStr("Remove which automatic control rule? Its queued commands will also be cancelled."),labels);
            if(pick<0 || pick>=choices.Length || !CanEdit(player))return;
            var selected=choices[pick];
            lock(gate)
            {
                if(!CanEdit(player))return;
                // A native dialog can remain open while another editor removes,
                // reorders or replaces rules. Resolve the captured identity,
                // never today's row index, and reject an edited selected rule.
                var index=rules.FindIndex(r=>r.Id==selected.Id);
                if(index<0 || rules[index].Rule!=selected.Rule || rules[index].Author!=selected.Author)
                {Publish("Rule changed or was removed; reopen the rule selector.");return;}
                queue.Cancel(selected.Id);rules.RemoveAt(index);Parent.SetDirty();
                Publish("Rule removed; its queued commands were cancelled.");
            }
        }
        catch(TimeoutException) { }
        catch(Exception error){Eco.Shared.Logging.Log.WriteErrorLineLoc($"Automatic control rule selector failed: {error}");}
    }
    // Preserve the old RPC for already-open views and external controls.
    [RPC] public void RemoveLastRule(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/RemoveLastRule", this.Parent);
        if(!CanEdit(player)) return;
        lock(gate)
        {
            if(rules.Count==0) return;
            queue.Cancel(rules[^1].Id); rules.RemoveAt(rules.Count-1); Parent.SetDirty(); Publish("Last rule removed; its queued commands were cancelled.");
        }
    }
    internal void Emit(RailEvent kind, Guid train)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/Emit", this.Parent);
        lock(gate)
            foreach(var setting in rules.Where(r=>r.Event==kind))
                if(!queue.Schedule(setting.Rule,train,Now)) Publish("Signal queue full; command was not scheduled.");
    }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/Tick", this.Parent);
        base.Tick();
        lock(gate)
        {
            var commands=queue.TakeDue(Now);
            var connections=commands.Length>1?new Dictionary<DeliveryConnection,bool>():null;
            var sent=0;var skipped=0;string? status=null;
            foreach(var command in commands)
            {
                var rule=rules.FirstOrDefault(r=>r.Id==command.Rule);
                var author=rule==null?null:UserManager.Users.FirstOrDefault(u=>u.Id==rule.Author);
                var target=MinecartChainDriveObject.ActiveDrives.FirstOrDefault(d=>d.ObjectID==command.Target);
                if(author==null || target==null || !Parent.IsAuthorized(author,AccessType.FullAccess)
                    || !target.IsAuthorized(author,AccessType.FullAccess) || !ConnectedForDelivery(target,connections))
                { skipped++;status="Command skipped: target, permissions or connected track changed.";continue; }
                var power=target.GetComponent<OnOffComponent>();
                power.On=command.Command switch { RailCommand.Activate=>true,RailCommand.Deactivate=>false,_=>!power.On };
                target.SetDirty();target.Tick();sent++;
                if(commands.Length==1)status=$"{command.Command} sent to {target.DisplayName}.";
            }
            if(commands.Length>1)status=skipped==0?$"{sent} commands sent."
                :$"{sent} commands sent; {skipped} skipped: target, permissions or connected track changed.";
            // Delivery does not edit the rule list or draft. Publish only its
            // final status, keeping partial failures visible after a burst.
            if(status!=null&&Status!=status){Status=status;this.Changed(nameof(Status));}
        }
    }
    public override void Destroy() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/Destroy", this.Parent); queue.Clear(); base.Destroy(); }
}
