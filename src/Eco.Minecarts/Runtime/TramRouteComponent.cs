using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
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
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Line")] public string LineName=>lineName;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Stops in order")] public string Stops=>string.IsNullOrWhiteSpace(route)?"All compatible stops":route;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Next stop")] public string NextStop=>Sequence.Length==0?"Any compatible stop":Sequence[nextIndex%Sequence.Length];
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Service")] public string ServiceStatus=>serviceActive?"Running":"Stopped";
    private string[] Sequence=>route.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    private bool CanConfigure(Player player)=>player!=null&&!Parent.IsDestroyed&&Parent.IsAuthorized(player.User,AccessType.FullAccess)
        &&Vector3.Distance(player.User.Position,Parent.Position)<=6;
    public override void PostInitialize()
    {
        base.PostInitialize();
        // A tram has a passenger cabin, not a conventional manual driver seat.
        Parent.GetComponent<MountComponent>().MountValidation.Add((seat,player)=>seat>0 && player!=null
            ? Eco.Core.Utils.Result.Succeeded : Eco.Core.Utils.Result.Fail(LocString.Empty));
    }
    public override void Tick()
    {
        base.Tick();
        // The route must not undo an explicit stop or a safety shutdown.
        if(serviceActive && !Parent.GetComponent<TrainControllerComponent>().Autopilot) SuspendService();
    }
    internal void SuspendService() { if(!serviceActive)return;serviceActive=false;Publish(); }
    [RPC,Autogen] public void StartService(Player player)
    {
        if(!CanConfigure(player)) return;
        serviceActive=true;Parent.GetComponent<TrainControllerComponent>().ApplyTargetCommand((float)(Parent.GetComponent<RailCouplingComponent>().Performance.SpeedLimit*3.6));Publish();
    }
    [RPC,Autogen] public void StopService(Player player)
    {
        if(!CanConfigure(player)) return;
        serviceActive=false;
        var motion=Parent.GetComponent<MinecartMotionComponent>();
        motion.SetDriveCommands(0,motion.ServerDirection,true);
        Parent.GetComponent<TrainControllerComponent>().TakeManualControl();
        Publish();
    }
    [RPC,Autogen] public void SetLineName(Player player)
    { if(CanConfigure(player)) _=EditLineName(player); }
    private async Task EditLineName(Player player)
    {
        var name=await player.InputString(Localizer.DoStr("Tram line name"),Localizer.DoStr(lineName));
        if(!CanConfigure(player)||string.IsNullOrWhiteSpace(name)||name.Length>60) return;
        lineName=name.Trim();Publish();
    }
    [RPC,Autogen] public void SetRoute(Player player)
    { if(CanConfigure(player)) _=EditRoute(player); }
    private async Task EditRoute(Player player)
    {
        var names=await player.InputString(Localizer.DoStr("Stop names in travel order, separated by commas. Enter all to visit any compatible stop."),Localizer.DoStr(route.Length==0?"all":route));
        if(!CanConfigure(player)||string.IsNullOrWhiteSpace(names)||names.Length>512) return;
        var parsed=names.Trim().Equals("all",StringComparison.OrdinalIgnoreCase)?[]:names.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(parsed.Length>32||parsed.Any(x=>x.Length>60)) return;
        route=string.Join(", ",parsed);nextIndex=0;Publish();
    }
    internal bool Servicing=>serviceActive;
    internal bool Wants(string stopName)
    { var sequence=Sequence;return sequence.Length==0||string.Equals(sequence[nextIndex%sequence.Length],stopName,StringComparison.OrdinalIgnoreCase); }
    internal void Departed(string stopName)
    {
        var sequence=Sequence;
        if(sequence.Length>0 && Wants(stopName)) {nextIndex=(nextIndex+1)%sequence.Length;Publish();}
    }
    private void Publish()
    {this.Changed(nameof(LineName));this.Changed(nameof(Stops));this.Changed(nameof(NextStop));this.Changed(nameof(ServiceStatus));Parent.SetDirty();}
}
