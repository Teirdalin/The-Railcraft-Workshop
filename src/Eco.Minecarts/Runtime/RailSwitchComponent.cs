using System.Collections.Concurrent;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Gameplay.UI;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;

namespace Eco.Minecarts.Runtime;

[Serialized,NoIcon,AutogenClass,LocDisplayName("Track Switch")]
public sealed class RailSwitchComponent : WorldObjectComponent,IPersistentData
{
    private static readonly ConcurrentDictionary<RailCell,RailSwitchComponent> Index=new();
    private readonly object gate=new();
    private readonly List<RailCell> cells=new();
    private RailCell anchor;
    private int turns;
    private long selectorRevision;
    [Serialized] private int route;
    public RailSwitchDefinition Definition=>((RailSwitchObject)Parent).Definition;
    [SyncToView,Autogen,PropReadOnly] public string SelectedRoute=>RailSwitchDefinition.RouteName(route);
    internal int SelectedRouteValue=>route;
    [SyncToView,PropReadOnly] public string AvailableRoutes=>string.Join(", ",Definition.Routes.Select(RailSwitchDefinition.RouteName));
    [SyncToView,Autogen,PropReadOnly] public string Status {get;private set;}="";
    public object PersistentData {get{ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailSwitchComponent.PersistentData.get", this.Parent); return route; } set { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailSwitchComponent.PersistentData.set", this.Parent);route=value is int r && r>=-1&&r<=1?r:0;} }
    internal VoxelRail Rail=>RailInImage(anchor,route);
    // The registry uses physical cells; geometry stays in the caller's image
    // so ports still meet during continuous travel across a toroidal seam.
    private VoxelRail RailInImage(RailCell cell,int selected)
    {
        var size=Eco.World.World.VoxelSize;
        var image=new RailCell(anchor.X+(int)Math.Round((cell.X-(double)anchor.X)/size.X)*size.X,anchor.Y,
            anchor.Z+(int)Math.Round((cell.Z-(double)anchor.Z)/size.Z)*size.Z);
        return new(image,Definition.Profile(selected,turns));
    }
    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/PostInitialize", this.Parent);
        base.PostInitialize();
        lock(gate) Register();
    }
    private void Register()
    {
        anchor=RailPowerConnectionComponent.PhysicalCell(new(Parent.Position3i.X,Parent.Position3i.Y,Parent.Position3i.Z));
        var f=Parent.Rotation.RotateVector(Vector3.UnitZ); turns=((int)Math.Round(Math.Atan2(f.X,f.Z)/(Math.PI/2))+4)%4;
        if(!Definition.Supports(route)) route=0;
        var half=Definition.Footprint/2;
        for(var x=-half;x<=half;x++) for(var z=-half;z<=half;z++) {var cell=RailPowerConnectionComponent.PhysicalCell(new(anchor.X+x,anchor.Y,anchor.Z+z)); Index[cell]=this; cells.Add(cell);}
        Publish();
    }
    private void Unregister()
    {
        RailCell[] removed;
        lock(gate)
        {
            removed=cells.Where(cell=>Index.TryRemove(new KeyValuePair<RailCell,RailSwitchComponent>(cell,this))).ToArray();
            cells.Clear();
        }
        // Remove the whole footprint before notifying power/topology readers.
        // Those callbacks must not run while holding the switch command gate.
        foreach(var cell in removed)RailPowerConnectionComponent.RailRemoved(cell);
    }
    public override void OnBeforeObjectMoved()
    {
        base.OnBeforeObjectMoved();
        Unregister();
    }
    public override void OnAfterObjectMoved()
    {
        base.OnAfterObjectMoved();
        lock(gate)if(!Parent.IsDestroyed)Register();
    }
    public override void Destroy()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Destroy", this.Parent);
        Unregister();RailSimulationFrame.Invalidate();base.Destroy();
    }
    internal static RailSwitchComponent? At(RailCell cell){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/At"); return Index.TryGetValue(RailPowerConnectionComponent.PhysicalCell(cell),out var result)&&!result.Parent.IsDestroyed?result:null; }
    internal static VoxelRail? Read(RailCell cell){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Read"); var points=At(cell); return points?.RailInImage(cell,points.route); }
    // Spring/trailing entry: an unselected branch may merge towards the toe,
    // but an approach from the toe must still obey the selected route.
    internal VoxelRail? TrailingPath(VoxelRail incoming,int exit)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/TrailingPath", this.Parent);
        foreach(var r in Definition.Routes)
        {
            var path=RailInImage(incoming.Cell,r);
            if(incoming.Connects(exit,path,1)) return path;
        }
        return null;
    }
    internal bool AdmitTrailing(VoxelRail path,RailCouplingComponent train)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/AdmitTrailing", this.Parent);
        lock(gate)
        {
            if(Parent.IsDestroyed||cells.Count==0)return false;
            if(path==RailInImage(path.Cell,route)) return true;
            if(!Definition.Supports(path.Profile.SwitchRoute)
                || path!=RailInImage(path.Cell,path.Profile.SwitchRoute)) return false;
            if(train.OtherOccupiesSwitch(Rail,Definition.Radius)) return false;
            route=path.Profile.SwitchRoute; Publish(); Parent.SetDirty(); ReportStatus("Route set to "+SelectedRoute);
            return true;
        }
    }
    private void Publish()
    {
        // Route changes and re-registration invalidate pending local dialogs,
        // including a move or route change that later returns to its old value.
        selectorRevision++;
        RailSimulationFrame.InvalidateCells(cells);
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Publish", this.Parent);
        foreach(var r in new[]{-1,0,1})
        {
            var selected=route==r;
            Parent.SetAnimatedState("Route"+RailSwitchDefinition.RouteName(r),selected);
        }
        this.Changed(nameof(SelectedRoute));
    }
    private void ReportStatus(string value)
    {
        // Occupied automatic points can be retried every solver substep.
        // Keep checking live occupancy, but only publish a changed message.
        if(Status==value)return;
        Status=value;this.Changed(nameof(Status));
    }
    private bool CanSelect(Player player,bool remote=false)
    {
        return player!=null && !Parent.IsDestroyed && Parent.IsAuthorized(player.User,AccessType.FullAccess)
            && (remote || RailWorldDistance.Within(player.User.Position,Parent.Position,Definition.Radius+4));
    }
    internal bool Select(Player player,int requested,bool remote=false)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Select", this.Parent);
        if(!CanSelect(player,remote) || !Definition.Supports(requested))return false;
        lock(gate)
        {
            // Access and proximity may change while this command waits for the gate.
            if(!CanSelect(player,remote)||cells.Count==0)return false;
            if(route==requested) return true;
            // Do not throw the points under any axle or coupled vehicle body.
            if(RailCouplingComponent.OccupiesSwitch(Rail,Definition.Radius)) {ReportStatus("Train occupies the switch"); return false;}
            route=requested; Publish(); Parent.SetDirty(); ReportStatus("Route set to "+SelectedRoute); return true;
        }
    }
    internal bool SelectAutomatic(User author,int requested)
    {
        if(author==null || Parent.IsDestroyed || !Parent.IsAuthorized(author,AccessType.FullAccess) || !Definition.Supports(requested))return false;
        lock(gate)
        {
            // Delayed station commands must still belong to an authorized author.
            if(Parent.IsDestroyed||cells.Count==0||!Parent.IsAuthorized(author,AccessType.FullAccess))return false;
            if(route==requested)return true;
            if(RailCouplingComponent.OccupiesSwitch(Rail,Definition.Radius)){ReportStatus("Train occupies the switch");return false;}
            route=requested;Publish();Parent.SetDirty();ReportStatus("Station route set to "+SelectedRoute);return true;
        }
    }
    [RPC, Autogen, UITypeName("BigButton")]
    public void SetRoute(Player player){if(CanSelect(player))_=EditRoute(player);}
    private async Task EditRoute(Player player)
    {
        try
        {
            int original;int[] routes;long revision;
            lock(gate){original=route;routes=Definition.Routes;revision=selectorRevision;}
            var labels=routes.Select(r=>RailSwitchDefinition.RouteName(r)+(r==original?" (selected)":"")).ToList();
            var choice=await player.OptionBox(Localizer.DoStr("Select switch route"),labels);
            if(!CanSelect(player)||choice<0||choice>=routes.Length)return;
            lock(gate)
            {
                if(route!=original){ReportStatus("Route changed; reopen the route selector");return;}
                if(selectorRevision!=revision){ReportStatus("Switch changed; reopen the route selector");return;}
                Select(player,routes[choice]);
            }
        }
        catch(Exception error){Eco.Shared.Logging.Log.WriteErrorLineLoc($"Switch route selector failed: {error}");}
    }
    // Retain old RPCs for already-open views and external controls.
    [RPC] public void Left(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Left", this.Parent); Select(player,-1); }
    [RPC] public void Forward(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Forward", this.Parent); Select(player,0); }
    [RPC] public void Right(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Right", this.Parent); Select(player,1); }
    [Interaction(InteractionTrigger.InteractKey,"Set switch route",requiredEnvVars:new[]{"RailSwitch"},interactionDistance:4,authRequired:AccessType.FullAccess)]
    public void Open(Player player,InteractionTriggerInfo trigger,InteractionTarget target) {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Open", this.Parent);if(!Parent.IsDestroyed&&Parent.IsAuthorized(player.User,AccessType.FullAccess)) Parent.OpenUI(player);}
    // Deterministic selected-path traversal; each multi-cell object counts once.
    internal sealed record CommandTarget(RailSwitchComponent Switch,int Route);
    internal static CommandTarget? Ahead(VoxelRail start,int exit,int requested)
        { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Ahead"); return AheadCore(start,exit,(points,command,physicalRoute)=>command==requested ? physicalRoute : (int?)null); }

    internal static CommandTarget? AheadStep(VoxelRail start,int exit,int step)
        { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/AheadStep"); return AheadCore(start,exit,(points,_,__) =>
        {
            var next=Math.Clamp(points.route+step,-1,1);
            return points.Definition.Supports(next)?next:(int?)null;
        }); }

    private static CommandTarget? AheadCore(VoxelRail start,int exit,Func<RailSwitchComponent,int,int,int?> resolve)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/AheadCore");
        var rail=start; var seen=new HashSet<RailCell>();
        for(int pieces=0;pieces<15;pieces++)
        {
            if(!seen.Add(rail.Cell)) return null;
            // Examine physical ports before selected-path traversal. This also
            // permits a command from the trailing side of an unselected branch.
            foreach(var candidate in TrackWorld.Near(rail.Point(exit)))
            {
                if(candidate.Cell==rail.Cell || At(candidate.Cell) is not {} points) continue;
                foreach(var route in points.Definition.Routes)
                foreach(var entry in new[]{0,1})
                {
                    var path=points.RailInImage(rail.Cell,route);
                    if(!rail.Connects(exit,path,entry)) continue;
                    var incoming=rail.Profile.Tangent(exit)*(exit==1?1:-1);
                    var outgoing=path.Profile.Tangent(1-entry)*(entry==0?1:-1);
                    var side=Vector3.Dot(Vector3.Cross(incoming,outgoing),Vector3.UnitY);
                    var command=Math.Abs(side)<.25f?0:side>0?1:-1;
                    if(resolve(points,command,route) is {} targetRoute) return new(points,targetRoute);
                }
            }
            if(TrackWorld.Neighbor(rail,exit) is not {} next) return null;
            rail=next.Rail; exit=1-next.End;
        }
        return null;
    }
}
