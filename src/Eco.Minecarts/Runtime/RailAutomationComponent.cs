using System.Diagnostics;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
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
    private bool CanEdit(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/CanEdit", this.Parent); return player!=null && !Parent.IsDestroyed
        && Parent.IsAuthorized(player.User,AccessType.FullAccess) && Vector3.Distance(player.User.Position,Parent.Position)<=5; }
    private void Publish(string status)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/Publish", this.Parent);
        Status=status; this.Changed(nameof(Status)); this.Changed(nameof(Draft)); this.Changed(nameof(Rules));
    }
    private RailCell? Source => Parent.GetComponent<TrainStationComponent>()?.Cell;
    private bool Connected(MinecartChainDriveObject target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/Connected", this.Parent);
        if(Source is not {} start) return false;
        var destination = TrackWorld.Near(target.Position).Where(r=>r.Profile.Chain)
            .Select(r=>(Rail:r,Hit:r.Profile.Nearest(target.Position-r.Cell.Origin)))
            .Where(r=>r.Hit.Distance<=1.6f).OrderBy(r=>r.Hit.Distance).FirstOrDefault();
        return destination.Rail.Profile.Shape!=null
            && RailNetworkSearch.Connected(start,destination.Rail.Cell,TrackWorld.NetworkNeighbors);
    }
    [RPC,Autogen] public void NextChainDrive(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/NextChainDrive", this.Parent);
        if(!CanEdit(player)) return;
        var targets=MinecartChainDriveObject.ActiveDrives.Where(d=>d.IsAuthorized(player.User,AccessType.FullAccess) && Connected(d)).OrderBy(d=>d.ObjectID).ToArray();
        lock(gate)
        {
            if(targets.Length==0) { draftTarget=Guid.Empty; Publish("No authorized chain drive on this connected rail network."); return; }
            var index=Array.FindIndex(targets,d=>d.ObjectID==draftTarget);
            var target=targets[(index+1)%targets.Length]; draftTarget=target.ObjectID;
            Publish($"Selected {target.DisplayName} at {target.Position3i}");
        }
    }
    [RPC,Autogen] public void NextEvent(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/NextEvent", this.Parent);
        if(!CanEdit(player)) return;
        lock(gate) { draftEvent=(RailEvent)(((int)draftEvent+1)%4); Publish("Entry, exit, arrival or dispatch selected."); }
    }
    [RPC,Autogen] public void NextAction(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/NextAction", this.Parent);
        if(!CanEdit(player)) return;
        lock(gate) { draftCommand=(RailCommand)(((int)draftCommand+1)%3); Publish("Command selected."); }
    }
    [RPC,Autogen] public void IncreaseDelay(Player player) {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/IncreaseDelay", this.Parent); if(CanEdit(player)) lock(gate) { draftDelay=Math.Min(86400,draftDelay+5); Publish("Delay increased by five seconds."); } }
    [RPC,Autogen] public void DecreaseDelay(Player player) {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/DecreaseDelay", this.Parent); if(CanEdit(player)) lock(gate) { draftDelay=Math.Max(0,draftDelay-5); Publish("Delay decreased by five seconds."); } }
    [RPC,Autogen] public void AddRule(Player player)
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
    [RPC,Autogen] public void RemoveLastRule(Player player)
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
        foreach(var command in queue.TakeDue(Now))
        {
            var rule=rules.FirstOrDefault(r=>r.Id==command.Rule);
            var author=rule==null?null:UserManager.Users.FirstOrDefault(u=>u.Id==rule.Author);
            var target=MinecartChainDriveObject.ActiveDrives.FirstOrDefault(d=>d.ObjectID==command.Target);
            if(author==null || target==null || !Parent.IsAuthorized(author,AccessType.FullAccess)
                || !target.IsAuthorized(author,AccessType.FullAccess) || !Connected(target))
            { Publish("Command skipped: target, permissions or connected track changed."); continue; }
            var power=target.GetComponent<OnOffComponent>();
            power.On=command.Command switch { RailCommand.Activate=>true,RailCommand.Deactivate=>false,_=>!power.On };
            target.SetDirty(); target.Tick(); Publish($"{command.Command} sent to {target.DisplayName}.");
        }
    }
    public override void Destroy() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Automation and remote controls/Destroy", this.Parent); queue.Clear(); base.Destroy(); }
}
