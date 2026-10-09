using System.Numerics;
using Eco.Core.Controller;
using Eco.Core.Plugins.Interfaces;
using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Math;

namespace Eco.Minecarts.Runtime;

[Serialized]
public sealed class SavedPowerRail
{
    [Serialized] public int X { get; set; }
    [Serialized] public int Y { get; set; }
    [Serialized] public int Z { get; set; }
    [Serialized] public string Shape { get; set; } = "";
    [Serialized] public int Turns { get; set; }
    [Serialized] public bool Chain { get; set; }
    [Serialized] public bool Wooden { get; set; }
    [Serialized] public bool Industrial { get; set; }
    [Serialized] public bool Coaster { get; set; }
    [Serialized] public bool Tram { get; set; }
    [Serialized] public float SwitchRadius { get; set; }
    [Serialized] public bool Powered { get; set; }
    internal VoxelRail Rail => new(new(X,Y,Z),new(Shape,Turns,Chain,Wooden,Industrial,SwitchRadius,Coaster:Coaster,Tram:Tram));
    internal static SavedPowerRail From(VoxelRail rail, bool powered) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/From"); return new()
    { X=rail.Cell.X,Y=rail.Cell.Y,Z=rail.Cell.Z,Shape=rail.Profile.Shape,Turns=rail.Profile.QuarterTurns,
      Chain=rail.Profile.Chain,Wooden=rail.Profile.Wooden,Industrial=rail.Profile.Industrial,
      Coaster=rail.Profile.Coaster,Tram=rail.Profile.Tram,SwitchRadius=rail.Profile.SwitchRadius,Powered=powered }; }
}

[Serialized]
public sealed class SavedPowerSupport
{
    [Serialized] public int X { get; set; }
    [Serialized] public int Y { get; set; }
    [Serialized] public int Z { get; set; }
    [Serialized] public string Tier { get; set; } = "";
    [Serialized] public string Part { get; set; } = "";
    internal RailCell Cell => new(X,Y,Z);
    internal SavedPowerSupport Copy() => new() { X=X,Y=Y,Z=Z,Tier=Tier,Part=Part };
    internal bool Valid() => RailSupportPower.Matches(this);
}

[Serialized]
public sealed class SavedPowerConnection
{
    // Detached entries are supplied atomically by the component's state getter.
    [Serialized, ThreadSafe] public List<SavedPowerRail> Members { get; set; } = [];
    [Serialized, ThreadSafe] public List<SavedPowerSupport> Supports { get; set; } = [];
    [Serialized] public bool Stalled { get; set; }
}

[Serialized, NoIcon, AutogenClass, CreateComponentTabLoc("Rail Connection", true), LocDisplayName("Rail Connection")]
public sealed class RailPowerConnectionComponent : WorldObjectComponent
{
    // Only drives watching this particular cell receive its removal event.
    private static readonly object RegistryGate = new();
    private static readonly Dictionary<RailCell, Dictionary<int, RailPowerConnectionComponent>> Watching = new();
    private readonly object gate = new();
    private readonly RailPowerLink link = new();
    private List<SavedPowerRail> savedMembers = [];
    private List<SavedPowerSupport> savedSupports = [];
    private bool stalled;
    [Serialized, ThreadSafe] private SavedPowerConnection SavedState
    {
        get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailPowerConnectionComponent.SavedState.get", this.Parent); lock(gate) return new() { Members=savedMembers.Select(m=>SavedPowerRail.From(m.Rail,m.Powered)).ToList(),Supports=savedSupports.Select(m=>m.Copy()).ToList(),Stalled=stalled }; }
        set { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailPowerConnectionComponent.SavedState.set", this.Parent); lock(gate) { savedMembers=value?.Members?.Select(m=>SavedPowerRail.From(m.Rail,m.Powered)).ToList() ?? []; savedSupports=value?.Supports?.Select(m=>m.Copy()).ToList() ?? []; stalled=value?.Stalled ?? false; restored=false; } }
    }
    private bool restored;
    private string publishedStatus = "";
    private int publishedRails = -1;
    [SyncToView, Autogen, PropReadOnly] public string Status { get; private set; } = "Press Connect to scan the adjacent rail network.";
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Connected Rails")] public int ConnectedRails { get; private set; }
    [SyncToView, Autogen, PropReadOnly] public string Instructions => "Connect scans once and turns the drive on. Adjacent support columns can carry power up to their top. Removed or replaced connected rails or supports stop the drive. Press Connect again after repairs or extensions. A power outage does not erase the connection.";
    internal bool Connected { get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/RailPowerConnectionComponent.Connected.get", this.Parent); lock(gate) return restored && link.Connected; } }
    internal HashSet<RailCell> Cells { get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/RailPowerConnectionComponent.Cells.get", this.Parent); lock(gate) return restored && link.Connected ? link.Powered : Empty; } }
    private static readonly HashSet<RailCell> Empty = [];

    [RPC, Autogen, UITypeName("BigButton")] public void Connect(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Connect", this.Parent);
        if(player == null || Parent.IsDestroyed || !Parent.IsAuthorized(player.User,AccessType.FullAccess)
            || Vector3.Distance(player.User.Position,Parent.Position)>5) return;
        ConnectNetwork();
    }
    internal bool ConnectNetwork()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/ConnectNetwork", this.Parent);
        bool connected;
        lock(gate)
        {
            Unwatch();
            var watched = new Dictionary<RailCell,VoxelRail>();
            var supports = new List<SavedPowerSupport>();
            var powered = RailSupportPower.Discover(Parent.Position,Parent is TramCableDriveObject,watched,supports);
            connected = link.Connect(powered,watched);
            stalled = !connected;
            restored = true;
            savedSupports = connected ? supports : [];
            savedMembers = connected ? watched.Values.Select(r=>SavedPowerRail.From(r,powered.Contains(r.Cell))).ToList() : [];
            if(connected) Watch();
        }
        Parent.GetComponent<OnOffComponent>().On = connected;
        Parent.SetDirty(); Publish();
        Parent.Tick();
        return connected;
    }
    public override void PostInitialize() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/PostInitialize", this.Parent); base.PostInitialize(); Publish(); }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Tick", this.Parent);
        base.Tick();
        if (!restored && WorldObjectManager.Init.Initialized)
        {
            bool invalid = false;
            lock(gate)
            {
                if(!restored)
                {
                    restored = true;
                    if(savedMembers.Count>0 && !stalled)
                    {
                        var watched=savedMembers.Select(m=>m.Rail).ToDictionary(r=>r.Cell);
                        var powered=savedMembers.Where(m=>m.Powered).Select(m=>m.Rail.Cell).ToHashSet();
                        link.Connect(powered,watched);
                        foreach(var cell in watched.Keys)
                            if(link.Changed(cell,TrackWorld.Read(cell))) { invalid=true; break; }
                        if(link.Connected && savedSupports.Any(s=>!s.Valid())) { link.Disconnect(); invalid=true; }
                        if(link.Connected) Watch();
                        else { stalled=true; savedMembers=[]; savedSupports=[]; invalid=true; }
                    }
                }
            }
            if(invalid) StopDrive();
            Publish();
        }
    }
    private void Watch()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Watch", this.Parent);
        lock(RegistryGate)
        foreach(var cell in link.Watched.Keys.Concat(savedSupports.Select(s=>s.Cell)))
        {
            var key=PhysicalCell(cell);
            if(!Watching.TryGetValue(key,out var subscribers)) Watching[key]=subscribers=new();
            subscribers[Parent.ID]=this;
        }
    }
    private void Unwatch()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Unwatch", this.Parent);
        lock(RegistryGate)
        foreach(var cell in link.Watched.Keys.Concat(savedSupports.Select(s=>s.Cell)))
        {
            var key=PhysicalCell(cell);
            if(Watching.TryGetValue(key,out var subscribers))
            { subscribers.Remove(Parent.ID); if(subscribers.Count==0) Watching.Remove(key); }
        }
        // Registration/removal share one short registry lock, so dropping an
        // empty bucket cannot race another drive connecting to the same cell.
    }
    internal static void BlockChanged(WrappedWorldPosition3i cell)
    {
        RailSimulationFrame.InvalidateCell(new(cell.X,cell.Y,cell.Z));
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/BlockChanged");
        var key=new RailCell(cell.X,cell.Y,cell.Z);
        foreach(var connection in Subscribers(key)) connection.CheckChanged(key,false);
    }
    internal static void RailRemoved(RailCell cell)
    {
        RailSimulationFrame.Invalidate();
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/RailRemoved");
        foreach(var connection in Subscribers(cell)) connection.CheckChanged(cell,true);
    }
    private static RailPowerConnectionComponent[] Subscribers(RailCell cell)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Subscribers"); lock(RegistryGate) return Watching.TryGetValue(PhysicalCell(cell),out var subscribers)?subscribers.Values.ToArray():[]; }
    internal static RailCell PhysicalCell(RailCell cell)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/PhysicalCell");
        // Discovery can cross the world seam using unwrapped neighboring cells,
        // while Eco's terrain event always reports the wrapped physical cell.
        var size=Eco.World.World.VoxelSize;
        return new(((cell.X%size.X)+size.X)%size.X,cell.Y,((cell.Z%size.Z)+size.Z)%size.Z);
    }
    private void CheckChanged(RailCell cell,bool removed)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/CheckChanged", this.Parent);
        bool stopped=false;
        lock(gate)
        {
            var physical=PhysicalCell(cell);
            foreach(var member in link.Watched.Keys)
                if(PhysicalCell(member)==physical && (removed?link.Removed(member):link.Changed(member,TrackWorld.Read(member))))
                { stopped=true; break; }
            if(!stopped && savedSupports.Any(s=>PhysicalCell(s.Cell)==physical && !s.Valid()))
            { link.Disconnect(); stopped=true; }
            if(stopped) { Unwatch(); stalled=true; savedMembers=[]; savedSupports=[]; }
        }
        // Never enter the drive/grid locks while holding the connection lock.
        if(stopped) { StopDrive(); Parent.SetDirty(); Publish(); }
    }
    private void StopDrive()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/StopDrive", this.Parent);
        Parent.GetComponent<OnOffComponent>().On=false;
        Parent.GetComponent<PowerConsumptionComponent>().OverridePowerConsumption(0);
        Parent.SetAnimatedState(Parent is TramCableDriveObject ? "CableRunning" : "ChainRunning",false);
        Parent.GetComponent<ChainDriveSpeedComponent>()?.SetEffectiveSpeed(0);
    }
    private void Publish()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Publish", this.Parent);
        lock(gate)
        {
            ConnectedRails=restored && link.Connected?link.Powered.Count:0;
            Status=stalled?"Stalled: no valid rail connection. Repair or place adjacent rails, then press Connect.":ConnectedRails>0
                ?"Connected. Press Connect again to include extensions.":"Press Connect to scan the adjacent rail network.";
            if(publishedStatus!=Status) { this.Changed(nameof(Status)); publishedStatus=Status; }
            if(publishedRails!=ConnectedRails) { this.Changed(nameof(ConnectedRails)); publishedRails=ConnectedRails; }
            Parent.SetAnimatedState("DriveStalled",stalled);
        }
    }
    public override void Destroy()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Destroy", this.Parent); lock(gate) Unwatch(); base.Destroy(); }
}

public sealed class RailPowerConnectionObserver : IModInit
{
    private static int subscribed;
    public static void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/PostInitialize"); RailSimulationFrame.Invalidate(); EnsureWorldEvents(); }
    internal static void EnsureWorldEvents()
    { if(Volatile.Read(ref subscribed)==0&&Interlocked.Exchange(ref subscribed,1)==0)Eco.World.World.OnBlockChanged.Add(RailPowerConnectionComponent.BlockChanged); }
}
