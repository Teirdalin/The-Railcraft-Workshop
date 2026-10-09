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
    private sealed record ParsedRoute(string Source,string[] Stops);
    private ParsedRoute? parsedRoute;
    private long routingRevision;
    internal long RoutingRevision=>Volatile.Read(ref routingRevision);
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Line")] public string LineName=>lineName;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Stops in order")] public string Stops=>string.IsNullOrWhiteSpace(route)?"All compatible stops":route;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Next stop")] public string NextStop=>Sequence.Length==0?"Any compatible stop":Sequence[nextIndex%Sequence.Length];
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Service")] public string ServiceStatus=>Servicing?"Running":"Stopped";
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Target Station")] public string TargetStation=>Parent.GetComponent<TrainControllerComponent>().TargetStation;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Track Power")] public string TrackPower {get;private set;}="Place on Tram Rail connected to a powered Tram Cable Drive.";
    private string[] Sequence{ get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/TramRouteComponent.Sequence.get", this.Parent); var source=route; var parsed=Volatile.Read(ref parsedRoute); if(parsed?.Source!=source) {parsed=new(source,source.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries));Volatile.Write(ref parsedRoute,parsed);} return parsed.Stops; } }
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
        var active=Servicing;
        if(serviceActive!=active){serviceActive=active;Publish();}
        this.Changed(nameof(TargetStation));
        var cell=Parent.GetComponent<MinecartMotionComponent>().BoundRailCell;
        var rail=cell is {} at?TrackWorld.Read(at):null;
        var power=rail is {} r && r.Profile.Tram?Eco.Mods.TechTree.TramCableDriveObject.PowerFor(r.Cell,Parent.ID):0;
        var status=rail is not {} track || !track.Profile.Tram?"Tram Rail required"
            : power<=.05?"No track power: power a Tram Cable Drive and press Connect"
            : $"Powered tram track ({power*100:0}% traction)";
        if(TrackPower!=status){TrackPower=status;this.Changed(nameof(TrackPower));}
    }
    internal void SuspendService() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/SuspendService", this.Parent); if(!serviceActive)return;serviceActive=false;Publish(); }
    [RPC, Autogen, UITypeName("BigButton")] public void StartService(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/StartService", this.Parent);
        if(!CanConfigure(player)) return;
        serviceActive=true;Parent.GetComponent<TrainControllerComponent>().ApplyTargetCommand((float)(Parent.GetComponent<RailCouplingComponent>().Performance.SpeedLimit*3.6));Publish();
    }
    [RPC, Autogen, UITypeName("BigButton")] public void StopService(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/StopService", this.Parent);
        if(!CanConfigure(player)) return;
        serviceActive=false;
        Parent.GetComponent<TrainControllerComponent>().StopAutomaticMotion();
        Publish();
    }
    [RPC, Autogen, UITypeName("BigButton")] public void SetLineName(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/SetLineName", this.Parent); if(CanConfigure(player)) _=EditLineName(player); }
    private async Task EditLineName(Player player)
    {
        var name=await player.InputString(Localizer.DoStr("Tram line name"),Localizer.DoStr(lineName));
        if(!CanConfigure(player)||string.IsNullOrWhiteSpace(name)||name.Length>60) return;
        lineName=name.Trim();Publish();
    }
    [RPC, Autogen, UITypeName("BigButton")] public void SetRoute(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/SetRoute", this.Parent); if(CanConfigure(player)) _=EditRoute(player); }
    private async Task EditRoute(Player player)
    {
        var names=await player.InputString(Localizer.DoStr("Stop names in travel order, separated by commas. Enter all to visit any compatible stop."),Localizer.DoStr(route.Length==0?"all":route));
        if(!CanConfigure(player)||string.IsNullOrWhiteSpace(names)||names.Length>512) return;
        var parsed=names.Trim().Equals("all",StringComparison.OrdinalIgnoreCase)?[]:names.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(parsed.Length>32||parsed.Any(x=>x.Length>60)) return;
        route=string.Join(", ",parsed);nextIndex=0;Publish();
    }
    internal bool Servicing=>Parent.GetComponent<TrainControllerComponent>().Autopilot;
    internal string? NextAfterDeparture(string stopName)
    {
        var sequence=Sequence;if(sequence.Length==0)return null;
        return sequence[(nextIndex+(Wants(stopName)?1:0))%sequence.Length];
    }
    internal bool Wants(string stopName)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/Wants", this.Parent); var sequence=Sequence;return sequence.Length==0||string.Equals(sequence[nextIndex%sequence.Length],stopName,StringComparison.OrdinalIgnoreCase); }
    internal void Departed(string stopName)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/Departed", this.Parent);
        var sequence=Sequence;
        if(sequence.Length>0 && Wants(stopName)) {nextIndex=(nextIndex+1)%sequence.Length;Publish();}
    }
    private void Publish()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Tram routing/Publish", this.Parent); Interlocked.Increment(ref routingRevision); this.Changed(nameof(LineName));this.Changed(nameof(Stops));this.Changed(nameof(NextStop));this.Changed(nameof(ServiceStatus));Parent.SetDirty();}
}
