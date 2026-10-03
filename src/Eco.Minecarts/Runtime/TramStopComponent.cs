using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized] public enum TramStopDirection { Both, Forward, Reverse }

[Serialized,NoIcon,AutogenClass,CreateComponentTabLoc("Tram Stop",true),LocDisplayName("Tram Stop")]
public sealed class TramStopComponent : WorldObjectComponent
{
    [Serialized] private string stopName="Tram Stop";
    [Serialized] private string lines="";
    [Serialized] private float dwellSeconds=15;
    [Serialized] private bool stopEnabled=true;
    [Serialized] private TramStopDirection direction;
    [SyncToView,Autogen,PropReadOnly] public string StopName=>stopName;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Served Lines")] public string Lines=>lines.Length==0?"All lines":lines;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Waiting Time (seconds)")] public float DwellSeconds=>dwellSeconds;
    [SyncToView,Autogen,PropReadOnly] public string Direction=>direction==TramStopDirection.Both?"Both directions":direction==TramStopDirection.Forward?"Forward only":"Reverse only";
    [SyncToView,Autogen,PropReadOnly] public string Status=>stopEnabled?"Serving trams":"Closed";
    private bool CanConfigure(Player player)=>player!=null&&!Parent.IsDestroyed&&Parent.IsAuthorized(player.User,AccessType.FullAccess)
        &&Vector3.Distance(player.User.Position,Parent.Position)<=5;
    [RPC,Autogen] public void ToggleStop(Player player)
    {if(!CanConfigure(player))return;stopEnabled=!stopEnabled;Publish();}
    [RPC,Autogen] public void IncreaseDwell(Player player)
    {if(!CanConfigure(player))return;dwellSeconds=Math.Min(600,dwellSeconds+5);Publish();}
    [RPC,Autogen] public void DecreaseDwell(Player player)
    {if(!CanConfigure(player))return;dwellSeconds=Math.Max(0,dwellSeconds-5);Publish();}
    [RPC,Autogen] public void CycleDirection(Player player)
    {if(!CanConfigure(player))return;direction=(TramStopDirection)(((int)direction+1)%3);Publish();}
    [RPC,Autogen] public void RenameStop(Player player)
    {if(CanConfigure(player))_=EditName(player);}
    private async Task EditName(Player player)
    {
        var text=await player.InputString(Localizer.DoStr("Tram stop name"),Localizer.DoStr(stopName));
        if(!CanConfigure(player)||string.IsNullOrWhiteSpace(text)||text.Length>60)return;
        stopName=text.Trim();Publish();
    }
    [RPC,Autogen] public void SetServedLines(Player player)
    {if(CanConfigure(player))_=EditLines(player);}
    private async Task EditLines(Player player)
    {
        var text=await player.InputString(Localizer.DoStr("Allowed line names, separated by commas. Enter all for every line."),Localizer.DoStr(lines.Length==0?"all":lines));
        if(!CanConfigure(player)||string.IsNullOrWhiteSpace(text)||text.Length>512)return;
        lines=text.Trim().Equals("all",StringComparison.OrdinalIgnoreCase)?"":string.Join(", ",text.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries));
        Publish();
    }
    internal bool Accepts(TramRouteComponent? tram,int travelDirection)
    {
        if(!stopEnabled||tram==null||!tram.Servicing||!tram.Wants(stopName))return false;
        if(direction==TramStopDirection.Forward&&travelDirection<0||direction==TramStopDirection.Reverse&&travelDirection>0)return false;
        return lines.Length==0||lines.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
            .Any(line=>string.Equals(line,tram.LineName,StringComparison.OrdinalIgnoreCase));
    }
    internal bool Ready(double elapsed)=>!stopEnabled||elapsed>=dwellSeconds;
    private void Publish()
    {this.Changed(nameof(StopName));this.Changed(nameof(Lines));this.Changed(nameof(DwellSeconds));this.Changed(nameof(Direction));this.Changed(nameof(Status));Parent.SetDirty();}
}
