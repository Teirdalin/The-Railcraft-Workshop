using Eco.Core.Controller;
using Eco.Gameplay.Civics.GameValues;
using Eco.Gameplay.Civics.Misc;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.UI;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Minecarts.Track;

namespace Eco.Minecarts.Runtime;

[Serialized]
public sealed class StationDestinationRule
{
    [Serialized] public Guid Id {get;set;}=Guid.NewGuid();
    [Serialized] public GameValue<bool>? Conditions {get;set;}
    [Serialized] public Guid Destination {get;set;}
    [Serialized] public int Author {get;set;}
    public StationDestinationRule Copy()=>new(){Id=Id,Conditions=StationNativeConditions.Copy(Conditions),Destination=Destination,Author=Author};
}

public sealed partial class TrainStationComponent
{
    [Serialized,ThreadSafe] private List<StationDestinationRule>? destinationRules;
    private List<StationDestinationRule> DestinationRules=>destinationRules??=new();
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Conditional Destinations")]
    public string DestinationRulesSummary
    {
        get{lock(settingsGate)return DestinationRules.Count==0?"Optional: no routing rules; follow the normal route"
            :string.Join("\n",DestinationRules.Select((r,i)=>$"{i+1}. IF {r.Conditions?.Description().ToString()??"choose conditions"} THEN {Find(r.Destination)?.StationName??"destination missing or not selected"}"));}
    }
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Routing Status")] public string RoutingStatus {get;private set;}="Normal route";
    internal string StationName=>Parent.GetComponent<TramStopComponent>()?.StopName??Parent.DisplayName.ToString();
    internal int SettingsRevision {get{lock(settingsGate)return settingsRevision;}}
    internal static TrainStationComponent? Find(Guid id)=>id!=Guid.Empty&&StationObjects.TryGetValue(id,out var station)&&!station.Parent.IsDestroyed?station:null;
    internal void ReportRouting(string text){if(RoutingStatus==text)return;RoutingStatus=text;this.Changed(nameof(RoutingStatus));}
    internal StationDestinationRule[] MatchingDestinations(RailCouplingComponent train,double dwell)
    {
        StationDestinationRule[] candidates;lock(settingsGate){if(DestinationRules.Count==0)return [];candidates=DestinationRules.ToArray();}
        var context=DepartureContext(train,dwell);
        return candidates.Where(r=>StationNativeConditions.Ready(r.Conditions,context)).Select(r=>r.Copy()).ToArray();
    }
    [RPC,Autogen,UITypeName("BigButton")]
    public void ConditionalDestinations(Player player)=>ManageConditionalDestinations(player);
    [RPC]
    public void ManageConditionalDestinations(Player player){if(CanConfigure(player))_=ManageDestinations(player);}
    private async Task ManageDestinations(Player player)
    {
        try
        {
            StationDestinationRule[] rows;int revision;
            lock(settingsGate){rows=DestinationRules.Select(r=>r.Copy()).ToArray();revision=settingsRevision;}
            var labels=new List<string>{"Add an IF conditions → THEN destination rule"};
            labels.AddRange(rows.Select((r,i)=>$"{i+1}. {Find(r.Destination)?.StationName??"Destination not selected"}: {r.Conditions?.Description().ToString()??"choose conditions"}"));
            var choice=await player.OptionBox(Localizer.DoStr("Conditional destinations — first successful reachable rule wins. Default: normal route."),labels);
            if(!CanConfigure(player)||choice<0||choice>=labels.Count)return;
            StationDestinationRule selected;
            lock(settingsGate)
            {
                if(revision!=settingsRevision){ReportRouting("Settings changed; reopen routing rules");return;}
                if(choice==0)
                {
                    if(DestinationRules.Count>=32){ReportRouting("Maximum 32 routing rules");return;}
                    selected=new(){Author=player.User.Id,Conditions=new No()};DestinationRules.Add(selected);SettingsChanged();
                }
                else selected=DestinationRules.Single(r=>r.Id==rows[choice-1].Id);
            }
            if(choice==0){if(await ChooseDestination(player,selected.Id))EditRoutingConditions(player,selected.Id);return;}
            var action=await player.OptionBox(Localizer.DoStr("Edit this conditional destination"),new List<string>{"Edit IF conditions","Choose destination station","Move priority up","Move priority down","Delete rule"});
            if(!CanConfigure(player)||action<0)return;
            if(action==0){EditRoutingConditions(player,selected.Id);return;}
            if(action==1){await ChooseDestination(player,selected.Id);return;}
            lock(settingsGate)
            {
                if(revision!=settingsRevision){ReportRouting("Settings changed; reopen routing rules");return;}
                var at=DestinationRules.FindIndex(r=>r.Id==selected.Id);if(at<0)return;
                if(action==4)DestinationRules.RemoveAt(at);
                else if(action is 2 or 3)
                {var to=Math.Clamp(at+(action==2?-1:1),0,DestinationRules.Count-1);(DestinationRules[at],DestinationRules[to])=(DestinationRules[to],DestinationRules[at]);}
                else return;
                SettingsChanged();ReportRouting("Routing rule updated");
            }
        }
        catch(Exception e){Eco.Shared.Logging.Log.WriteErrorLineLoc($"Station routing editor failed: {e}");}
    }
    private void EditRoutingConditions(Player player,Guid id)
    {
        GameValue<bool>? draft;int revision;
        lock(settingsGate){var rule=DestinationRules.FirstOrDefault(r=>r.Id==id);if(rule==null)return;draft=StationNativeConditions.Copy(rule.Conditions)??new SetOfConditions();revision=settingsRevision;}
        OpenNativeEditor(player,draft,revision,value=>
        {var rule=DestinationRules.FirstOrDefault(r=>r.Id==id);if(rule!=null){rule.Conditions=StationNativeConditions.Copy(value);rule.Author=player.User.Id;}});
    }
    private async Task<bool> ChooseDestination(Player player,Guid id)
    {
        if(!CanConfigure(player)||Cell is not {} cell){ReportRouting("Connect this station to rail before choosing a destination");return false;}
        int revision;lock(settingsGate)revision=settingsRevision;
        var connected=new HashSet<RailCell>();var pending=new Queue<RailCell>();pending.Enqueue(RailPowerConnectionComponent.PhysicalCell(cell));
        using(RailSimulationFrame.Begin())
        while(pending.TryDequeue(out var next)&&connected.Count<8192)
        {
            next=RailPowerConnectionComponent.PhysicalCell(next);if(!connected.Add(next))continue;
            foreach(var neighbor in TrackWorld.NetworkNeighbors(next))if(!connected.Contains(RailPowerConnectionComponent.PhysicalCell(neighbor)))pending.Enqueue(neighbor);
        }
        var options=Stations.Values.Where(s=>s!=this&&!s.Parent.IsDestroyed&&s.Cell is {} at&&connected.Contains(RailPowerConnectionComponent.PhysicalCell(at)))
            .OrderBy(s=>s.StationName,StringComparer.OrdinalIgnoreCase).ThenBy(s=>s.Parent.ObjectID).ToArray();
        if(options.Length==0){ReportRouting("No other station found on this connected network");return false;}
        var page=0;
        while(true)
        {
            var entries=options.Skip(page*32).Take(32).Select(s=>(Label:$"{s.StationName} ({s.Parent.Position3i.X}, {s.Parent.Position3i.Y}, {s.Parent.Position3i.Z})",Station:s,Step:0)).ToList();
            if(page>0)entries.Insert(0,("< Previous page",null!,-1));
            if((page+1)*32<options.Length)entries.Add(("Next page >",null!,1));
            var pick=await player.OptionBox(Localizer.DoStr("Choose a station on this rail network. The actual train's clearance and switch access are checked before departure."),entries.Select(e=>e.Label).ToList());
            if(!CanConfigure(player)||pick<0||pick>=entries.Count)return false;
            if(entries[pick].Step!=0){page+=entries[pick].Step;continue;}
            lock(settingsGate)
            {
                if(revision!=settingsRevision){ReportRouting("Settings changed; reopen the station selector");return false;}
                var rule=DestinationRules.FirstOrDefault(r=>r.Id==id);var destination=entries[pick].Station;
                if(rule==null||destination.Parent.IsDestroyed)return false;
                rule.Destination=destination.Parent.ObjectID;rule.Author=player.User.Id;SettingsChanged();ReportRouting("Destination set to "+destination.StationName);return true;
            }
        }
    }
}
