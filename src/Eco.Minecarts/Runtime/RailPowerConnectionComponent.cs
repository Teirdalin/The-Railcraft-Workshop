using System.Numerics;
using System.ComponentModel;
using Eco.Core.Controller;
using Eco.Core.Plugins.Interfaces;
using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.PowerGrids;
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
        get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailPowerConnectionComponent.SavedState.get", this.Parent); lock(gate) return movingFrom!=null ? new() { Stalled=true } : new() { Members=savedMembers.Select(m=>SavedPowerRail.From(m.Rail,m.Powered)).ToList(),Supports=savedSupports.Select(m=>m.Copy()).ToList(),Stalled=stalled }; }
        set { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailPowerConnectionComponent.SavedState.set", this.Parent); lock(gate) { savedMembers=value?.Members?.Select(m=>SavedPowerRail.From(m.Rail,m.Powered)).ToList() ?? []; savedSupports=value?.Supports?.Select(m=>m.Copy()).ToList() ?? []; stalled=value?.Stalled ?? false; restored=false; } }
    }
    private bool restored;
    private Vector3? movingFrom;
    private bool resumeOn;
    // Native remove/add requests run on Eco's grid worker. A null assignment
    // acknowledges removal; the next non-null assignment acknowledges addition.
    // Count pairs so repeated moves cannot accept an earlier site's allocation.
    private int pendingGridChanges;
    private bool gridRemoved;
    private bool awaitingGridAllocation;
    private bool gridSubscribed;
    private bool destroyed;
    private int gridStateChanged;
    private string publishedStatus = "";
    private int publishedRails = -1;
    [SyncToView, Autogen, PropReadOnly] public string Status { get; private set; } = "Press Connect to scan the adjacent rail network.";
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Connected Rails")] public int ConnectedRails { get; private set; }
    [SyncToView, Autogen, PropReadOnly] public string Instructions => "Connect scans once and turns the drive on. Adjacent support columns can carry power up to their top. Moving the drive or replacing connected rails or supports stops it. Press Connect after moving, repairs or extensions. A power outage keeps the connection.";
    internal bool Connected { get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/RailPowerConnectionComponent.Connected.get", this.Parent); lock(gate) return !destroyed && movingFrom==null && pendingGridChanges==0 && !awaitingGridAllocation && restored && link.Connected; } }
    internal HashSet<RailCell> Cells { get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/RailPowerConnectionComponent.Cells.get", this.Parent); lock(gate) return !destroyed && movingFrom==null && pendingGridChanges==0 && !awaitingGridAllocation && restored && link.Connected ? link.Powered : Empty; } }
    private static readonly HashSet<RailCell> Empty = [];

    [RPC, Autogen, UITypeName("BigButton")] public void Connect(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Connect", this.Parent);
        if(player==null || Parent==null || Parent.IsDestroyed || !Parent.IsAuthorized(player.User,AccessType.FullAccess)
            || !RailWorldDistance.Within(player.User.Position,Parent.Position,5)) return;
        ConnectNetwork();
    }
    internal bool ConnectNetwork()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/ConnectNetwork", this.Parent);
        bool connected;
        lock(gate)
        {
            if(destroyed || movingFrom!=null || Parent.IsDestroyed) return false;
            Unwatch();
            var revision = RailSimulationFrame.Revision;
            var watched = new Dictionary<RailCell,VoxelRail>();
            var supports = new List<SavedPowerSupport>();
            var powered = RailSupportPower.Discover(Parent.Position,Parent is TramCableDriveObject,watched,supports);
            connected = link.Connect(powered,watched);
            stalled = !connected;
            restored = true;
            savedSupports = connected ? supports : [];
            savedMembers = connected ? watched.Values.Select(r=>SavedPowerRail.From(r,powered.Contains(r.Cell))).ToList() : [];
            if(connected) Watch();
            // Discovery runs without change subscriptions. Publish the new
            // watchers before checking its revision, so an edit in that gap
            // cannot leave removed hardware in a saved, powered connection.
            // Later edits reach CheckChanged through the installed watchers.
            if(connected && revision != RailSimulationFrame.Revision)
            {
                Unwatch();
                connected = link.Connect([],[]);
                stalled = true; savedMembers = []; savedSupports = [];
            }
        }
        Parent.GetComponent<OnOffComponent>().On = connected;
        Parent.SetDirty(); Publish();
        Parent.Tick();
        return connected;
    }
    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/PostInitialize", this.Parent);
        base.PostInitialize();
        lock(gate)
        {
            if(!gridSubscribed && !destroyed)
            {
                var grid=Parent.GetComponent<PowerGridComponent>();
                grid.PropertyChanged+=GridChanged;
                grid.OnChanged.Add(GridAllocated);
                gridSubscribed=true;
            }
        }
        Publish();
    }
    private void GridChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(args.PropertyName!=nameof(PowerGridComponent.PowerGrid)) return;
        // This callback runs on the native grid worker. Never enter a drive
        // gate, tick a world object, or mutate the graph from this notification.
        var grid=Parent.GetComponent<PowerGridComponent>();
        bool invalidate=false;
        lock(gate)
        {
            if(destroyed || pendingGridChanges==0) return;
            if(grid.PowerGrid==null) { gridRemoved=true;invalidate=true; }
            else if(gridRemoved)
            {
                gridRemoved=false;
                pendingGridChanges--;
            }
        }
        // Removal is acknowledged on the grid worker, before any new add/tick.
        // Clear the old allocation there. Its next native UpdateEnabled event
        // follows the final grid's supply calculation, even if it rejoins the
        // same grid. Assignment alone can still expose pre-split supply totals.
        if(invalidate) grid.UpdateEnabled(false,false);
    }
    private void GridAllocated()
    {
        lock(gate)
        {
            var grid=Parent.GetComponent<PowerGridComponent>();
            if(!destroyed && pendingGridChanges==0 && awaitingGridAllocation && grid.PowerGrid!=null && grid.Enabled)
            {
                awaitingGridAllocation=false;
                Interlocked.Exchange(ref gridStateChanged,1);
            }
        }
    }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Tick", this.Parent);
        base.Tick();
        if(Interlocked.Exchange(ref gridStateChanged,0)!=0) Publish();
        if (!restored && WorldObjectManager.Init.Initialized)
        {
            bool invalid = false, normalized = false;
            lock(gate)
            {
                if(!restored && movingFrom==null)
                {
                    restored = true;
                    if(savedMembers.Count>0 && !stalled)
                    {
                        // Older saves retain the image used by discovery. Normalize
                        // only after the world has loaded; movement geometry stays
                        // unwrapped. Equivalent duplicate images are one member.
                        var members=savedMembers.Select(m=>new VoxelRail(PhysicalCell(m.Rail.Cell),m.Rail.Profile)).ToArray();
                        var watched=members.DistinctBy(r=>r.Cell).ToDictionary(r=>r.Cell);
                        var powered=savedMembers.Where(m=>m.Powered).Select(m=>PhysicalCell(m.Rail.Cell)).ToHashSet();
                        link.Connect(powered,watched);
                        // Conflicting saved images must fail closed, rather than
                        // selecting whichever old profile happened to come first.
                        if(members.Any(r=>watched[r.Cell]!=r)) {link.Disconnect();invalid=true;}
                        normalized=watched.Count!=savedMembers.Count || savedMembers.Any(m=>m.Rail.Cell!=PhysicalCell(m.Rail.Cell));
                        foreach(var cell in watched.Keys)
                            if(link.Changed(cell,TrackWorld.Read(cell))) { invalid=true; break; }
                        if(link.Connected && savedSupports.Any(s=>!s.Valid())) { link.Disconnect(); invalid=true; }
                        if(link.Connected)
                        {
                            savedMembers=watched.Values.Select(r=>SavedPowerRail.From(r,powered.Contains(r.Cell))).ToList();
                            Watch();
                        }
                        else { stalled=true; savedMembers=[]; savedSupports=[]; invalid=true; }
                    }
                }
            }
            if(invalid) StopDrive();
            if(normalized) Parent.SetDirty();
            Publish();
        }
    }
    public override void OnBeforeObjectMoved()
    {
        base.OnBeforeObjectMoved();
        lock(gate)
        {
            if(destroyed || movingFrom!=null) return;
            movingFrom=Parent.Position;
            resumeOn=Parent.GetComponent<OnOffComponent>().On;
            Unwatch();
        }
        // Tick/traction acquire the drive gate before the connection gate.
        // Publish detachment first, then drain an in-flight drive update.
        StopDrive(); Publish();
    }
    public override void OnAfterObjectMoved()
    {
        base.OnAfterObjectMoved();
        bool resume=false;
        lock(gate)
        {
            if(destroyed || movingFrom is not {} origin) return;
            // Rotation and equivalent horizontal world images do not relocate
            // the cabinet. Still validate hardware changed while detached.
            var sameSite=RailWorldDistance.Squared(origin,Parent.Position)==0;
            if(!sameSite)
            {
                // Keep Eco's spatial index and graph under its existing worker.
                // Queue both while holding our gate, including the pending flag,
                // so completion cannot race a second move or final destruction.
                pendingGridChanges++;
                awaitingGridAllocation=true;
                var grid=Parent.GetComponent<PowerGridComponent>();
                PowerGridManager.QueueComponentRemoved(grid);
                PowerGridManager.QueueComponentAdded(grid);
            }
            if(!sameSite || link.Connected && (link.Watched.Keys.Any(cell=>link.Changed(cell,TrackWorld.Read(cell)))
                || savedSupports.Any(s=>!s.Valid())))
            {
                link.Connect([],[]);savedMembers=[];savedSupports=[];stalled=true;restored=true;
            }
            movingFrom=null;
            if(restored && link.Connected) { Watch(); resume=resumeOn; }
            resumeOn=false;
        }
        // Native grid/component operations stay outside the connection lock.
        if(resume) Parent.GetComponent<OnOffComponent>().On=true;
        Parent.SetDirty();Publish();Parent.Tick();
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
        if(Parent is MinecartChainDriveObject chain) chain.StopPower();
        else if(Parent is TramCableDriveObject cable) cable.StopPower();
    }
    private void Publish()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Publish", this.Parent);
        lock(gate)
        {
            ConnectedRails=movingFrom==null && restored && link.Connected?link.Powered.Count:0;
            Status=movingFrom!=null?"Moving: place the drive, then press Connect.":stalled?"Stalled: no valid rail connection. Repair or place adjacent rails, then press Connect.":ConnectedRails>0
                ?pendingGridChanges>0 || awaitingGridAllocation?"Connected. Updating power grid.":"Connected. Press Connect again to include extensions.":"Press Connect to scan the adjacent rail network.";
            if(publishedStatus!=Status) { this.Changed(nameof(Status)); publishedStatus=Status; }
            if(publishedRails!=ConnectedRails) { this.Changed(nameof(ConnectedRails)); publishedRails=ConnectedRails; }
            Parent.SetAnimatedState("DriveStalled",stalled);
        }
    }
    public override void Destroy()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/Destroy", this.Parent);
        lock(gate)
        {
            if(!destroyed)
            {
                destroyed=true;Unwatch();
                var grid=Parent.GetComponent<PowerGridComponent>();
                if(gridSubscribed) { grid.PropertyChanged-=GridChanged;grid.OnChanged.Remove(GridAllocated);gridSubscribed=false; }
                // Native component destruction also removes itself. This final
                // request follows every add we queued, regardless of component
                // destruction order, so a pending move cannot resurrect it.
                if(pendingGridChanges>0 || awaitingGridAllocation) PowerGridManager.QueueComponentRemoved(grid);
            }
        }
        base.Destroy();
    }
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
