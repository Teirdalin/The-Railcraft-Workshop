using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.UI;
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
    private readonly object directionGate=new();
    // This list has its own revision: ordinary dwell/direction edits need
    // not discard it, but an all -> named lines -> all edit must do so.
    private long linesEditRevision;
    // Pending direction replies belong to this stop's current configuration
    // and placement, even if a later command returns to the original value.
    private long directionRevision;
    // Runtime-only parsed configuration. Keep the original string as the save
    // and UI contract; readers reuse an immutable snapshot until it changes.
    private sealed record ParsedLines(string Source,string[] Names,string Display);
    private ParsedLines? parsedLines;
    [SyncToView,Autogen,PropReadOnly] public string StopName=>stopName;
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Served Lines")] public string Lines=>lines.Length==0?"All lines":CurrentLines.Display;
    private ParsedLines CurrentLines
    {
        get
        {
            var source=lines;var parsed=Volatile.Read(ref parsedLines);
            if(parsed?.Source!=source)
            {
                var names=TramNameList.Read(source);parsed=new(source,names,TramNameList.Format(names));
                Volatile.Write(ref parsedLines,parsed);
            }
            return parsed;
        }
    }
    [SyncToView,Autogen,Eco.Shared.Networking.Range(0,600),LocDisplayName("Default Waiting Time (seconds)"),LocDescription("Enter 0 to 600 seconds. Used only when Station Departure has no configured conditions.")] public float DwellSeconds=>dwellSeconds;
    private static string DirectionName(TramStopDirection value)=>value switch
    {
        TramStopDirection.Both=>"Both directions",TramStopDirection.Forward=>"Forward only",
        TramStopDirection.Reverse=>"Reverse only",_=>"Invalid direction: choose a direction"
    };
    [SyncToView,Autogen,PropReadOnly] public string Direction=>DirectionName(direction);
    [SyncToView,Autogen,PropReadOnly] public string Status=>stopEnabled?"Serving trams":"Closed";
    private bool CanConfigure(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/CanConfigure", this.Parent); return player!=null&&Parent!=null&&!Parent.IsDestroyed&&Parent.IsAuthorized(player.User,AccessType.FullAccess)
        &&RailWorldDistance.Within(player.User.Position,Parent.Position,5); }
    [RPC, Autogen, UITypeName("BigButton")] public void ToggleStop(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/ToggleStop", this.Parent);if(!CanConfigure(player))return;stopEnabled=!stopEnabled;Publish();}
    [RPC] public void SetDwellSeconds(Player player,float value)
    {
        if(!CanConfigure(player)||!float.IsFinite(value))return;
        value=Math.Clamp(value,0,600);if(value==dwellSeconds)return;
        dwellSeconds=value;Publish();
    }
    // Retain old RPCs for already-open views without displaying two extra buttons.
    [RPC] public void IncreaseDwell(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/IncreaseDwell", this.Parent);SetDwellSeconds(player,dwellSeconds+5);}
    [RPC] public void DecreaseDwell(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/DecreaseDwell", this.Parent);SetDwellSeconds(player,dwellSeconds-5);}
    [RPC, Autogen, UITypeName("BigButton")] public void ChooseDirection(Player player)
    {if(CanConfigure(player))_=EditDirection(player);}
    private async Task EditDirection(Player player)
    {
        try
        {
            TramStopDirection original;long revision;
            lock(directionGate){if(!CanConfigure(player))return;original=direction;revision=Volatile.Read(ref directionRevision);}
            var values=Enum.GetValues<TramStopDirection>();
            var labels=values.Select(value=>DirectionName(value)+(value==original?" (current)":"")).ToList();
            var pick=await player.OptionBox(Localizer.DoStr("Choose which travel directions this tram stop serves"),labels);
            if(pick<0||pick>=values.Length||!CanConfigure(player))return;
            lock(directionGate)
            {
                if(!CanConfigure(player))return;
                if(revision!=Volatile.Read(ref directionRevision))
                {player.InfoBoxLoc($"Tram stop changed while this dialog was open; reopen the direction chooser.");return;}
                if(values[pick]==original)return;
                direction=values[pick];Publish();
            }
        }
        catch(TimeoutException) { }
        catch(Exception error){Eco.Shared.Logging.Log.WriteErrorLineLoc($"Tram stop direction selection failed: {error}");}
    }
    // Keep the cycling RPC for already-open views without another button.
    [RPC] public void CycleDirection(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/CycleDirection", this.Parent);
        lock(directionGate)
        {
            if(!CanConfigure(player))return;
            direction=Enum.IsDefined(direction)?(TramStopDirection)(((int)direction+1)%3):TramStopDirection.Both;
            Publish();
        }
    }
    [RPC, Autogen, UITypeName("BigButton")] public void RenameStop(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/RenameStop", this.Parent);if(CanConfigure(player))_=EditName(player);}
    private async Task EditName(Player player)
    {
        var original=stopName;
        var text=await player.InputString(Localizer.DoStr("Tram stop name"),Localizer.DoStr(original));
        if(!CanConfigure(player)||string.IsNullOrWhiteSpace(text)||text.Length>60)return;
        if(stopName!=original){player.InfoBoxLoc($"Stop name changed while this dialog was open; reopen the stop name editor.");return;}
        var replacement=text.Trim();
        // A no-op rename must not invalidate every station-aware journey.
        if(replacement==original)return;
        if(!ReferenceEquals(Interlocked.CompareExchange(ref stopName,replacement,original),original))
        {player.InfoBoxLoc($"Stop name changed while this dialog was open; reopen the stop name editor.");return;}
        Publish();
    }
    [RPC, Autogen, UITypeName("BigButton")] public void SetServedLines(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/SetServedLines", this.Parent);if(CanConfigure(player))_=EditLines(player);}
    private async Task EditLines(Player player)
    {
        string original;long revision;
        lock(directionGate){if(!CanConfigure(player))return;original=lines;revision=linesEditRevision;}
        var text=await player.InputString(Localizer.DoStr("Allowed line names, separated by commas. Quote a name containing commas, like \"Harbor, East\", or \"all\" for a line named all. Enter all for every line."),Localizer.DoStr(original.Length==0?"all":TramNameList.Format(TramNameList.Read(original))));
        if(!CanConfigure(player)||string.IsNullOrWhiteSpace(text)||text.Length>512)return;
        if(!TramNameList.TryParse(text,out var parsed))
        {player.InfoBoxLoc($"Enter line names separated by commas, quote names containing commas, or enter all. Close each quoted name and double any quotes inside it.");return;}
        lock(directionGate)
        {
            if(!CanConfigure(player))return;
            if(revision!=linesEditRevision||lines!=original)
            {player.InfoBoxLoc($"Served lines changed while this dialog was open; reopen the line editor.");return;}
            if(TramNameList.Read(original).SequenceEqual(parsed,StringComparer.OrdinalIgnoreCase))return;
            if(!ReferenceEquals(Interlocked.CompareExchange(ref lines,TramNameList.Write(parsed),original),original))
            {player.InfoBoxLoc($"Served lines changed while this dialog was open; reopen the line editor.");return;}
            linesEditRevision++;Publish();
        }
    }
    internal bool Accepts(TramRouteComponent? tram,int travelDirection)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/Accepts", this.Parent);
        if(tram==null||!tram.Servicing||!tram.Wants(stopName))return false;
        return Compatible(tram,travelDirection);
    }
    internal bool Compatible(TramRouteComponent tram,int travelDirection)
    {
        if(!stopEnabled)return false;
        var allowedDirection=direction;
        if(!Enum.IsDefined(allowedDirection))return false;
        if(allowedDirection==TramStopDirection.Forward&&travelDirection<0||allowedDirection==TramStopDirection.Reverse&&travelDirection>0)return false;
        var source=lines;
        if(source.Length==0)return true;
        var lineName=tram.LineName;
        foreach(var line in CurrentLines.Names)
            if(string.Equals(line,lineName,StringComparison.OrdinalIgnoreCase))return true;
        return false;
    }
    internal bool Ready(double elapsed){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/Ready", this.Parent); return !stopEnabled||elapsed>=dwellSeconds; }
    public override void OnBeforeObjectMoved()
    {
        lock(directionGate)Interlocked.Increment(ref directionRevision);
        base.OnBeforeObjectMoved();
    }
    public override void OnAfterObjectMoved()
    {
        base.OnAfterObjectMoved();
        lock(directionGate)Interlocked.Increment(ref directionRevision);
    }
    private void Publish()
    {
        Interlocked.Increment(ref directionRevision);
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/Publish", this.Parent); TrainStationComponent.RoutingChanged(); this.Changed(nameof(StopName));this.Changed(nameof(Lines));this.Changed(nameof(DwellSeconds));this.Changed(nameof(Direction));this.Changed(nameof(Status));Parent.SetDirty();}
}
