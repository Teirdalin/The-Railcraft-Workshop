using System.Collections.Concurrent;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
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
    [Serialized] private int route;
    public RailSwitchDefinition Definition=>((RailSwitchObject)Parent).Definition;
    [SyncToView,Autogen,PropReadOnly] public string SelectedRoute=>RailSwitchDefinition.RouteName(route);
    internal int SelectedRouteValue=>route;
    [SyncToView,Autogen,PropReadOnly] public string AvailableRoutes=>string.Join(", ",Definition.Routes.Select(RailSwitchDefinition.RouteName));
    [SyncToView,Autogen,PropReadOnly] public string Status {get;private set;}="";
    public object PersistentData {get{ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailSwitchComponent.PersistentData.get", this.Parent); return route; } set { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/RailSwitchComponent.PersistentData.set", this.Parent);route=value is int r && r>=-1&&r<=1?r:0;} }
    internal VoxelRail Rail=>new(anchor,Definition.Profile(route,turns));
    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/PostInitialize", this.Parent);
        base.PostInitialize();
        anchor=new(Parent.Position3i.X,Parent.Position3i.Y,Parent.Position3i.Z);
        var f=Parent.Rotation.RotateVector(Vector3.UnitZ); turns=((int)Math.Round(Math.Atan2(f.X,f.Z)/(Math.PI/2))+4)%4;
        if(!Definition.Supports(route)) route=0;
        var half=Definition.Footprint/2;
        for(var x=-half;x<=half;x++) for(var z=-half;z<=half;z++) {var cell=new RailCell(anchor.X+x,anchor.Y,anchor.Z+z); Index[cell]=this; cells.Add(cell);}
        Publish();
    }
    public override void Destroy() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Destroy", this.Parent);foreach(var cell in cells) if(Index.TryGetValue(cell,out var current)&&current==this) { RailPowerConnectionComponent.RailRemoved(cell); Index.TryRemove(cell,out _); } RailSimulationFrame.Invalidate(); base.Destroy();}
    internal static RailSwitchComponent? At(RailCell cell){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/At"); return Index.TryGetValue(cell,out var result)&&!result.Parent.IsDestroyed?result:null; }
    internal static VoxelRail? Read(RailCell cell){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Read"); return At(cell)?.Rail; }
    // Spring/trailing entry: an unselected branch may merge towards the toe,
    // but an approach from the toe must still obey the selected route.
    internal VoxelRail? TrailingPath(VoxelRail incoming,int exit)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/TrailingPath", this.Parent);
        foreach(var r in Definition.Routes)
        {
            var path=new VoxelRail(anchor,Definition.Profile(r,turns));
            if(incoming.Connects(exit,path,1)) return path;
        }
        return null;
    }
    internal bool AdmitTrailing(VoxelRail path,RailCouplingComponent train)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/AdmitTrailing", this.Parent);
        lock(gate)
        {
            if(path==Rail) return true;
            if(!Definition.Supports(path.Profile.SwitchRoute)
                || path!=new VoxelRail(anchor,Definition.Profile(path.Profile.SwitchRoute,turns))) return false;
            var group=train.Group().Select(x=>x.Parent.ID).ToHashSet();
            if(RailCouplingComponent.OccupiesSwitch(Rail,Definition.Radius,group)) return false;
            route=path.Profile.SwitchRoute; Publish(); Parent.SetDirty();
            return true;
        }
    }
    private void Publish()
    {
        RailSimulationFrame.Invalidate();
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Publish", this.Parent);
        foreach(var r in new[]{-1,0,1})
        {
            var selected=route==r;
            Parent.SetAnimatedState("Route"+RailSwitchDefinition.RouteName(r),selected);
        }
        this.Changed(nameof(SelectedRoute));
    }
    internal bool Select(Player player,int requested,bool remote=false)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Select", this.Parent);
        if(player==null || Parent.IsDestroyed || !Parent.IsAuthorized(player.User,AccessType.FullAccess) || !Definition.Supports(requested)) return false;
        if(!remote && Vector3.Distance(player.User.Position,Parent.Position)>Definition.Radius+4) return false;
        lock(gate)
        {
            if(route==requested) return true;
            // Do not throw the points under any axle or coupled vehicle body.
            if(RailCouplingComponent.OccupiesSwitch(Rail,Definition.Radius)) {Status="Train occupies the switch"; this.Changed(nameof(Status)); return false;}
            route=requested; Publish(); Parent.SetDirty(); Status="Route set to "+SelectedRoute; this.Changed(nameof(Status)); return true;
        }
    }
    internal bool SelectAutomatic(User author,int requested)
    {
        if(author==null || Parent.IsDestroyed || !Parent.IsAuthorized(author,AccessType.FullAccess) || !Definition.Supports(requested))return false;
        lock(gate)
        {
            if(route==requested)return true;
            if(RailCouplingComponent.OccupiesSwitch(Rail,Definition.Radius)){Status="Train occupies the switch";this.Changed(nameof(Status));return false;}
            route=requested;Publish();Parent.SetDirty();Status="Station route set to "+SelectedRoute;this.Changed(nameof(Status));return true;
        }
    }
    [RPC, Autogen, UITypeName("BigButton")] public void Left(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Left", this.Parent); Select(player,-1); }
    [RPC, Autogen, UITypeName("BigButton")] public void Forward(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Forward", this.Parent); Select(player,0); }
    [RPC, Autogen, UITypeName("BigButton")] public void Right(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Switches/Right", this.Parent); Select(player,1); }
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
                    var path=new VoxelRail(points.anchor,points.Definition.Profile(route,points.turns));
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
