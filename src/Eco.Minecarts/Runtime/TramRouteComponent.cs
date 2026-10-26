using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Shared.SharedTypes;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, AutogenClass, CreateComponentTabLoc("Tram Route",true), LocDisplayName("Tram Route")]
public sealed class TramRouteComponent : WorldObjectComponent
{
    [Serialized] private string lineName="City Line";
    [Serialized] private string route="";
    [Serialized] private int nextIndex;
    [Serialized] private bool serviceActive;
    // Route replacement and departure progress share one short critical
    // section. A cursor read or advance must belong to the same itinerary.
    private readonly object routeEditGate=new();
    // An old reply must not overwrite a newer itinerary, including all ->
    // named stops -> all, whose saved string returns to String.Empty.
    private long routeEditRevision;
    private sealed record ParsedRoute(string Source,string[] Stops,string Display);
    private ParsedRoute? parsedRoute;
    private long routingRevision;
    private string? publishedTargetStation;
    internal long RoutingRevision=>Volatile.Read(ref routingRevision);
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Line")] public string LineName=>lineName;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Stops in order")] public string Stops
    {get{lock(routeEditGate)return string.IsNullOrWhiteSpace(route)?"All compatible stops":CurrentRoute.Display;}}
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Next stop")] public string NextStop
    {
        get
        {
            lock(routeEditGate)
            {
                var sequence=Sequence;
                return sequence.Length==0?"Any compatible stop":sequence[CursorIndex(nextIndex,sequence.Length)];
            }
        }
    }
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Service")] public string ServiceStatus=>Servicing?"Running":"Stopped";
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Target Station")] public string TargetStation=>Parent.GetComponent<TrainControllerComponent>().TargetStation;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Track Power")] public string TrackPower {get;private set;}="Place on Tram Rail connected to a powered Tram Cable Drive.";
    private ParsedRoute CurrentRoute
    {
        get
        {
            var source=route;var parsed=Volatile.Read(ref parsedRoute);
            if(parsed?.Source!=source)
            {
                var names=TramNameList.Read(source);parsed=new(source,names,TramNameList.Format(names));
                Volatile.Write(ref parsedRoute,parsed);
            }
            return parsed;
        }
    }
    private string[] Sequence{ get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/TramRouteComponent.Sequence.get", this.Parent); return CurrentRoute.Stops; } }
    private bool CanConfigure(Player player)=>Parent.GetComponent<TrainControllerComponent>().CanConfigure(player);
    [Interaction(InteractionTrigger.InteractKey,"Open tram settings",requiredEnvVars:new[]{"TramSettings"},interactionDistance:2.5f,priority:40,authRequired:AccessType.ConsumerAccess,flags:InteractionFlags.BlocksOtherInteraction)]
    public void OpenSettings(Player player,InteractionTriggerInfo trigger,InteractionTarget target)
    {
        if(player==null || !target.ContainsParameter("TramSettings") || Parent.IsDestroyed || !Parent.IsAuthorized(player.User,AccessType.ConsumerAccess))return;
        var delta=player.User.Position-Parent.Position;var size=Eco.World.World.VoxelSize;
        if(size.X>0)delta.X-=MathF.Round(delta.X/size.X)*size.X;
        if(size.Z>0)delta.Z-=MathF.Round(delta.Z/size.Z)*size.Z;
        if(delta.LengthSquared()>36)return;
        Parent.OpenUI(player);
    }
    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/PostInitialize", this.Parent);
        base.PostInitialize();
        // A tram has a passenger cabin, not a conventional manual driver seat.
        Parent.GetComponent<MountComponent>().MountValidation.Add((seat,player)=>seat>0 && player!=null
            ? Eco.Core.Utils.Result.Succeeded : Eco.Core.Utils.Result.Fail(LocString.Empty));
    }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/Tick", this.Parent);
        base.Tick();
        // Both start controls use the same route. Never restart the controller
        // from this component after an explicit stop or a safety shutdown.
        RefreshService();
        // Observe the displayed label, including mode changes and live
        // station renames/removals, without resending an unchanged value.
        var target=TargetStation;
        if(publishedTargetStation!=target)
        {publishedTargetStation=target;this.Changed(nameof(TargetStation));}
        var cell=Parent.GetComponent<MinecartMotionComponent>().BoundRailCell;
        var rail=cell is {} at?TrackWorld.Read(at):null;
        var power=rail is {} r && r.Profile.Tram?Eco.Mods.TechTree.TramCableDriveObject.PowerFor(r.Cell,Parent.ID):0;
        var status=rail is not {} track || !track.Profile.Tram?"Tram Rail required"
            : power<=.05?"No track power: power a Tram Cable Drive and press Connect"
            : $"Powered tram track ({power*100:0}% traction)";
        if(TrackPower!=status){TrackPower=status;this.Changed(nameof(TrackPower));}
    }
    // Controller mode changes already alter Servicing. Publish that transition
    // with the command, before a route can capture its revision; a later
    // presentation tick must not invalidate an otherwise coherent search.
    internal void RefreshService()
    {
        var active=Servicing;
        if(serviceActive==active)return;
        serviceActive=active;Publish();
    }
    internal void SuspendService() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/SuspendService", this.Parent); if(!serviceActive)return;serviceActive=false;Publish(); }
    [RPC, Autogen, UITypeName("BigButton")] public void StartService(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/StartService", this.Parent);
        if(!CanConfigure(player)) return;
        Parent.GetComponent<TrainControllerComponent>().ApplyTargetCommand((float)(Parent.GetComponent<RailCouplingComponent>().Performance.SpeedLimit*3.6));
        RefreshService();
    }
    [RPC, Autogen, UITypeName("BigButton")] public void StopService(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/StopService", this.Parent);
        if(!CanConfigure(player)) return;
        Parent.GetComponent<TrainControllerComponent>().StopAutomaticMotion();
        RefreshService();
    }
    [RPC, Autogen, UITypeName("BigButton")] public void SetLineName(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/SetLineName", this.Parent); if(CanConfigure(player)) _=EditLineName(player); }
    private async Task EditLineName(Player player)
    {
        var original=lineName;
        var name=await player.InputString(Localizer.DoStr("Tram line name"),Localizer.DoStr(original));
        if(!CanConfigure(player)||string.IsNullOrWhiteSpace(name)||name.Length>60) return;
        if(lineName!=original){player.InfoBoxLoc($"Line name changed while this dialog was open; reopen the line name editor.");return;}
        var replacement=name.Trim();
        // Unchanged labels must not invalidate this tram's cached journeys.
        if(replacement==original)return;
        if(!ReferenceEquals(Interlocked.CompareExchange(ref lineName,replacement,original),original))
        {player.InfoBoxLoc($"Line name changed while this dialog was open; reopen the line name editor.");return;}
        Publish();
    }
    [RPC, Autogen, UITypeName("BigButton")] public void SetRoute(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/SetRoute", this.Parent); if(CanConfigure(player)) _=EditRoute(player); }
    private async Task EditRoute(Player player)
    {
        string original;long revision;
        lock(routeEditGate){if(!CanConfigure(player))return;original=route;revision=routeEditRevision;}
        var names=await player.InputString(Localizer.DoStr("Stop names in travel order, separated by commas. Quote a name containing commas, like \"Harbor, East\", or \"all\" for a stop named all. Enter all to visit any compatible stop."),Localizer.DoStr(original.Length==0?"all":TramNameList.Format(TramNameList.Read(original))));
        if(!CanConfigure(player)||string.IsNullOrWhiteSpace(names)||names.Length>512) return;
        if(!TramNameList.TryParse(names,out var parsed))
        {player.InfoBoxLoc($"Enter stop names separated by commas, quote names containing commas, or enter all. Close each quoted name and double any quotes inside it.");return;}
        if(parsed.Length>32||parsed.Any(x=>x.Length>60)) return;
        lock(routeEditGate)
        {
            if(!CanConfigure(player))return;
            if(revision!=routeEditRevision||route!=original)
            {player.InfoBoxLoc($"Route changed while this dialog was open; reopen the route editor.");return;}
            // Reapplying the same route must not restart service at its first
            // stop or dismiss another editor that still has a current draft.
            if(TramNameList.Read(original).SequenceEqual(parsed,StringComparer.OrdinalIgnoreCase))return;
            if(!ReferenceEquals(Interlocked.CompareExchange(ref route,TramNameList.Write(parsed),original),original))
            {player.InfoBoxLoc($"Route changed while this dialog was open; reopen the route editor.");return;}
            routeEditRevision++;
            nextIndex=0;Publish();
        }
    }
    internal bool Servicing=>Parent.GetComponent<TrainControllerComponent>().Autopilot;
    // Saved cursors can be outside the current itinerary. Preserve positive
    // wraparound and use the same wrap for negative values, without changing
    // persistent state during a view read or a speculative route search.
    private static int CursorIndex(int cursor,int count)
    {
        var index=cursor%count;
        return index<0?index+count:index;
    }
    internal string? NextAfterDeparture(string stopName,out long revisionAfterDeparture)
    {
        lock(routeEditGate)
        {
            revisionAfterDeparture=RoutingRevision;
            var sequence=Sequence;if(sequence.Length==0)return null;
            var index=CursorIndex(nextIndex,sequence.Length);
            var advance=string.Equals(sequence[index],stopName,StringComparison.OrdinalIgnoreCase);
            if(advance)revisionAfterDeparture++;
            return sequence[advance?(index+1)%sequence.Length:index];
        }
    }
    internal bool Wants(string stopName)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/Wants", this.Parent);
        lock(routeEditGate){var sequence=Sequence;return sequence.Length==0||string.Equals(sequence[CursorIndex(nextIndex,sequence.Length)],stopName,StringComparison.OrdinalIgnoreCase);}}
    internal void Departed(string stopName)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/Departed", this.Parent);
        lock(routeEditGate)
        {
            var sequence=Sequence;
            if(sequence.Length==0)return;
            var index=CursorIndex(nextIndex,sequence.Length);
            if(string.Equals(sequence[index],stopName,StringComparison.OrdinalIgnoreCase)) {nextIndex=(index+1)%sequence.Length;Publish();}
        }
    }
    private void Publish()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/Publish", this.Parent); Interlocked.Increment(ref routingRevision); this.Changed(nameof(LineName));this.Changed(nameof(Stops));this.Changed(nameof(NextStop));this.Changed(nameof(ServiceStatus));Parent.SetDirty();}
}
