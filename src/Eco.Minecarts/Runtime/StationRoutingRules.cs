using System.Numerics;
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
        get
        {
            lock(settingsGate)
            {
                if(DestinationRules.Count==0)return "Optional: no routing rules; follow the normal route";
                var destinations=DestinationRuleLabels(DestinationRules,"destination missing or not selected");
                return string.Join("\n",DestinationRules.Select((r,i)=>$"{i+1}. IF {StationNativeConditions.Describe(r.Conditions)} THEN {destinations[i]}"));
            }
        }
    }
    // View publication is runtime scratch. Remote destination edits do not
    // change this station's settings, but an open panel still needs fresh text.
    private long destinationSummaryRevision=-1;
    private int destinationSummarySettings=-1;
    private string? publishedDestinationSummary;
    private void RefreshDestinationRulesSummary()
    {
        lock(settingsGate)
        {
            var revision=RoutingRevision;
            if(destinationSummaryRevision==revision && destinationSummarySettings==settingsRevision)return;
            var summary=DestinationRulesSummary;
            // Do not acknowledge a revision that changed while reading targets.
            if(revision!=RoutingRevision)return;
            destinationSummaryRevision=revision;destinationSummarySettings=settingsRevision;
            if(publishedDestinationSummary==summary)return;
            publishedDestinationSummary=summary;
            this.Changed(nameof(DestinationRulesSummary));
        }
    }
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Routing Status")] public string RoutingStatus {get;private set;}="Normal route";
    internal string StationName=>Parent.GetComponent<TramStopComponent>()?.StopName??Parent.DisplayName.ToString();
    internal int SettingsRevision {get{lock(settingsGate)return settingsRevision;}}
    internal static TrainStationComponent? Find(Guid id)=>id!=Guid.Empty&&StationObjects.TryGetValue(id,out var station)&&!station.Parent.IsDestroyed?station:null;
    internal void ReportRouting(string text){if(RoutingStatus==text)return;RoutingStatus=text;this.Changed(nameof(RoutingStatus));}
    internal StationDestinationRule[] MatchingDestinations(RailCouplingComponent train,double dwell,out StationDepartureContext? observation)
    {
        observation=null;
        StationDestinationRule[] candidates; int revision; string selected;
        lock(settingsGate)
        {
            if(DestinationRules.Count==0)return [];
            candidates=DestinationRules.ToArray();revision=settingsRevision;selected=SelectedCarIds;
        }
        var context=DepartureContextFor(train,dwell,selected);
        observation=context;
        lock(settingsGate)
        {
            if(revision!=settingsRevision)return [];
            var matches=candidates.Where(r=>StationNativeConditions.Ready(r.Conditions,context)).Select(r=>r.Copy()).ToArray();
            return revision==settingsRevision?matches:[];
        }
    }
    [RPC,Autogen,UITypeName("BigButton")]
    public void ConditionalDestinations(Player player)=>ManageConditionalDestinations(player);
    [RPC]
    public void ManageConditionalDestinations(Player player){if(CanConfigure(player))_=ManageDestinations(player);}
    // Distinct stations can have the same name, including the default train
    // station label. Disambiguate only those destinations in this rule list;
    // repeated rules for one target and unique names stay compact. Snapshot
    // names/positions once without discovering or scanning the rail network.
    private static string[] DestinationRuleLabels(IReadOnlyList<StationDestinationRule> rules,string missing)
    {
        var targets=rules.Select(r=>Find(r.Destination) is {} target
            ?(Id:target.Parent.ObjectID,Name:target.StationName,Position:target.Parent.Position)
            :(Id:Guid.Empty,Name:missing,Position:Vector3.Zero)).ToArray();
        var ambiguous=targets.Where(t=>t.Id!=Guid.Empty).GroupBy(t=>t.Name,StringComparer.OrdinalIgnoreCase)
            .Where(group=>group.Select(t=>t.Id).Distinct().Skip(1).Any()).Select(group=>group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return targets.Select(t=>t.Id!=Guid.Empty&&ambiguous.Contains(t.Name)
            ?$"{t.Name} ({t.Position.X:0}, {t.Position.Y:0}, {t.Position.Z:0})":t.Name).ToArray();
    }
    private async Task ManageDestinations(Player player)
    {
        try
        {
            var origin=CaptureEditOrigin();
            StationDestinationRule[] rows;int revision;
            lock(settingsGate){rows=DestinationRules.Select(r=>r.Copy()).ToArray();revision=settingsRevision;}
            bool Current()
            {
                if(!CanConfigure(player))return false;
                lock(settingsGate)if(revision!=settingsRevision)
                {ReportRouting("Settings changed; reopen routing rules");return false;}
                if(!EditOriginCurrent(origin))
                {ReportRouting("Station moved; reopen routing rules");return false;}
                return true;
            }
            if(!Current())return;
            // An empty list has only one action. Go straight to its destination
            // picker; the draft remains detached until the native editor saves it.
            if(rows.Length==0)
            {await ChooseDestination(player,new(){Author=player.User.Id,Conditions=new No()},revision,adding:true);return;}
            var canAdd=rows.Length<32;
            var destinations=DestinationRuleLabels(rows,"Destination not selected");
            var labels=rows.Select((r,i)=>$"{i+1}. {destinations[i]}: {StationNativeConditions.Describe(r.Conditions)}").ToList();
            if(canAdd)labels.Insert(0,"Add an IF conditions → THEN destination rule");
            var choice=await player.OptionBox(Localizer.DoStr("Conditional destinations — first successful reachable rule wins. Default: normal route."),labels);
            if(!Current()||choice<0||choice>=labels.Count)return;
            var adding=canAdd&&choice==0;
            var row=choice-(canAdd?1:0);
            StationDestinationRule selected;
            lock(settingsGate)
            {
                if(!Current())return;
                if(adding)
                {
                    if(DestinationRules.Count>=32){ReportRouting("Maximum 32 routing rules");return;}
                    // Keep a new rule detached until both dialogs are applied.
                    // Cancelled or unsuccessful selection must not fill the saved list.
                    selected=new(){Author=player.User.Id,Conditions=new No()};
                }
                else selected=rows[row];
            }
            if(adding){await ChooseDestination(player,selected,revision,adding:true);return;}
            var actions=new List<(string Label,int Action)>{("Edit IF conditions",0),("Choose destination station",1)};
            if(row>0)actions.Add(("Move priority up",2));
            if(row<rows.Length-1)actions.Add(("Move priority down",3));
            actions.Add(("Delete rule",4));
            var target=Find(selected.Destination);
            var destination=target==null?"Destination missing or not selected":$"{target.StationName} ({target.Parent.Position.X:0}, {target.Parent.Position.Y:0}, {target.Parent.Position.Z:0})";
            var actionChoice=await player.OptionBox(Localizer.DoStr($"Conditional destination {row+1}: {destination}. Choose an action."),actions.Select(a=>a.Label).ToList());
            if(!Current()||actionChoice<0||actionChoice>=actions.Count)return;
            var action=actions[actionChoice].Action;
            if(action==0){EditRoutingConditions(player,selected,revision,adding:false);return;}
            if(action==1){await ChooseDestination(player,selected,revision,adding:false);return;}
            lock(settingsGate)
            {
                if(!Current())return;
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
    private sealed record DestinationEdit(StationEditOrigin? Source,DetectedStation Target);
    private bool DestinationStillCurrent(Player player,DestinationEdit edit)
    {
        var target=Find(edit.Target.Id);
        if(edit.Source is not {Cell:{} source})
        {ReportRouting("Source station disconnected; reopen the routing rule");return false;}
        bool EndpointsCurrent()=>CanConfigure(player)&&EditOriginCurrent(edit.Source)
            &&target!=null&&ReferenceEquals(Find(edit.Target.Id),target)&&target.Cell==edit.Target.Cell
            &&target.Parent.Position==edit.Target.Position&&target.EditOriginCurrent(edit.Target.Origin);
        var railsRevision=RailSimulationFrame.Revision;
        var stationsRevision=RoutingRevision;
        using var frame=RailSimulationFrame.Begin();
        if(!EndpointsCurrent()||TrackWorld.Read(source)==null||TrackWorld.Read(edit.Target.Cell)==null
            ||!RailNetworkSearch.Connected(RailPowerConnectionComponent.PhysicalCell(source),RailPowerConnectionComponent.PhysicalCell(edit.Target.Cell),
                at=>TrackWorld.NetworkNeighbors(at).Select(RailPowerConnectionComponent.PhysicalCell),8192)
            ||railsRevision!=RailSimulationFrame.Revision||stationsRevision!=RoutingRevision||!EndpointsCurrent())
        {ReportRouting("Destination or rail network changed; reopen the routing rule");return false;}
        return true;
    }
    private void EditRoutingConditions(Player player,StationDestinationRule draft,int revision,bool adding,DestinationEdit? edit=null)
    {
        if(!CanConfigure(player))return;
        lock(settingsGate)if(revision!=settingsRevision)
        {ReportRouting("Settings changed; reopen routing rules");return;}
        if(edit==null)
        {
            if(Cell is not {} source||Find(draft.Destination) is not {} target||target.Cell is not {} cell)
            {ReportRouting("Connect the source and destination to rail before editing this rule");return;}
            edit=new(CaptureEditOrigin(),new(target.Parent.ObjectID,target.StationName,cell,target.Parent.Position){Origin=target.CaptureEditOrigin()});
        }
        // A new rule spans the picker and native condition editor. Retain the
        // original endpoints until the final commit, including traffic reviews.
        OpenNativeEditor(player,StationNativeConditions.Copy(draft.Conditions)??new SetOfConditions(),revision,
            value=>DestinationStillCurrent(player,edit)&&SaveRoutingRule(player,draft,value,adding),"Conditional destination conditions","Routing conditions saved");
    }
    // Called under settingsGate after the native editor/selector checks its revision.
    private bool SaveRoutingRule(Player player,StationDestinationRule draft,GameValue<bool>? conditions,bool adding)
    {
        if(Find(draft.Destination) is not {} target || target==this)
        {ReportRouting("Destination was removed or not selected; rule not saved");return false;}
        var at=DestinationRules.FindIndex(r=>r.Id==draft.Id);
        if(adding && (DestinationRules.Count>=32 || at>=0))
        {ReportRouting("Routing rules changed; reopen routing rules");return false;}
        if(!adding && at<0){ReportRouting("Routing rule was removed; reopen routing rules");return false;}
        var saved=new StationDestinationRule{Id=draft.Id,Destination=draft.Destination,
            Conditions=StationNativeConditions.Copy(conditions),Author=player.User.Id};
        if(adding)DestinationRules.Add(saved);else DestinationRules[at]=saved;
        ReportRouting("Destination set to "+target.StationName);return true;
    }
    private TrainStationComponent[]? DestinationOptions(RailCell source)
    {
        var revision=RailSimulationFrame.Revision;
        var stationsRevision=RoutingRevision;
        var position=Parent.Position;
        var candidates=Stations.Values.Where(s=>s!=this&&!s.Parent.IsDestroyed)
            .Select(s=>(Station:s,Position:s.Parent.Position,Cell:s.Cell)).Where(s=>s.Cell.HasValue)
            .Select(s=>(s.Station,s.Position,Cell:RailPowerConnectionComponent.PhysicalCell(s.Cell!.Value))).ToArray();
        // This is a one-dialog discovery, never a connectivity cache. Keep the
        // previous 8,192-rail prefix but stop once every candidate is found.
        // Empty candidate lists need no world traversal at all.
        using var frame=RailSimulationFrame.Begin();
        var connected=RailNetworkSearch.ConnectedTargets(RailPowerConnectionComponent.PhysicalCell(source),candidates.Select(s=>s.Cell),
            cell=>TrackWorld.NetworkNeighbors(cell).Select(RailPowerConnectionComponent.PhysicalCell),8192);
        var options=candidates.Where(s=>!s.Station.Parent.IsDestroyed&&s.Station.Parent.Position==s.Position&&s.Station.Cell is {} at
                && RailPowerConnectionComponent.PhysicalCell(at)==s.Cell&&connected.Contains(s.Cell))
            .Select(s=>s.Station).OrderBy(s=>s.StationName,StringComparer.OrdinalIgnoreCase).ThenBy(s=>s.Parent.ObjectID).ToArray();
        // A traversal across construction or station rebinding is not a
        // connected-network observation. Let the user reopen a fresh chooser.
        return revision==RailSimulationFrame.Revision&&stationsRevision==RoutingRevision
            && Cell==source&&Parent.Position==position&&!Parent.IsDestroyed?options:null;
    }
    private async Task<bool> ChooseDestination(Player player,StationDestinationRule draft,int revision,bool adding)
    {
        if(!CanConfigure(player)||Cell is not {} cell){ReportRouting("Connect this station to rail before choosing a destination");return false;}
        var origin=CaptureEditOrigin();
        var discovered=CachedDestinationOptions();
        if(discovered==null){ReportRouting("Rail network changed; reopen the station selector");return false;}
        var options=discovered.Select(s=>(Id:s.Parent.ObjectID,Name:s.StationName,Cell:s.Cell,Position:s.Parent.Position,Origin:s.CaptureEditOrigin()))
            .Where(s=>s.Cell.HasValue).Select(s=>new DetectedStation(s.Id,s.Name,s.Cell!.Value,s.Position){Origin=s.Origin}).ToArray();
        if(options.Length==0){ReportRouting("No other station found on this connected network");return false;}
        var page=Math.Max(0,Array.FindIndex(options,s=>s.Id==draft.Destination))/32;
        while(true)
        {
            var entries=options.Skip(page*32).Take(32).Select(s=>(Label:s.Name+(s.Id==draft.Destination?" (current)":"")+$" ({s.Position.X:0}, {s.Position.Y:0}, {s.Position.Z:0})",Option:s,Step:0)).ToList();
            if(page>0)entries.Insert(0,("< Previous page",default!,-1));
            if((page+1)*32<options.Length)entries.Add(("Next page >",default!,1));
            var pick=await player.OptionBox(Localizer.DoStr("Choose a station on this rail network. Cancel keeps the saved rule. Train clearance and switch access are checked before departure."),entries.Select(e=>e.Label).ToList());
            if(!CanConfigure(player)||pick<0||pick>=entries.Count)return false;
            lock(settingsGate)if(revision!=settingsRevision)
            {ReportRouting("Settings changed; reopen the station selector");return false;}
            if(!EditOriginCurrent(origin))
            {ReportRouting("Station moved; reopen the station selector");return false;}
            if(entries[pick].Step!=0){page+=entries[pick].Step;continue;}
            lock(settingsGate)
            {
                if(revision!=settingsRevision){ReportRouting("Settings changed; reopen the station selector");return false;}
                var option=entries[pick].Option;
                var edit=new DestinationEdit(origin,option);
                if(!DestinationStillCurrent(player,edit))return false;
                draft.Destination=option.Id;
                if(!adding)
                {
                    if(!SaveRoutingRule(player,draft,draft.Conditions,adding:false))return false;
                    SettingsChanged();return true;
                }
            }
            EditRoutingConditions(player,draft,revision,adding:true,new(origin,entries[pick].Option));return true;
        }
    }
}
