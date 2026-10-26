namespace Eco.Mods.TechTree;

using System.Collections.Concurrent;
using Eco.Core.Controller;
using Eco.Core.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Minecarts.Runtime;
using static Eco.Minecarts.Runtime.PoweredRailDrive;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;

[Serialized, LocDisplayName("Tram Cable Drive"), LocDescription("A mechanical drive that supplies traction to trams through connected Tram Rail. Connect a source of mechanical power; heavier traffic needs more power to keep moving. Crafted at the Railworks Workbench using Industry."), Weight(12000)]
public sealed class TramCableDriveItem : WorldObjectItem<TramCableDriveObject>
{
    // Use the same floor attachment as the other drive cabinets.
    protected override OccupancyContext GetOccupancyContext => new SideAttachedContext(
        DirectionAxisFlags.Down, WorldObject.GetOccupancyInfo(WorldObjectType));
}

[RequiresSkill(typeof(IndustrySkill),3)]
public sealed class TramCableDriveRecipe : MinecartRailRecipeFamily
{
    public TramCableDriveRecipe()=>Configure(MinecartRailRecipes.Make<TramCableDriveItem>("Tram Cable Drive",12,12,woodenGears:2, skillType:typeof(IndustrySkill)),"Tram Cable Drive",typeof(TramCableDriveRecipe),180,3, skillType:typeof(IndustrySkill));
}

[Serialized, RequireComponent(typeof(OnOffComponent)), RequireComponent(typeof(PropertyAuthComponent))]
[RequireComponent(typeof(PowerGridComponent)), RequireComponent(typeof(PowerConsumptionComponent))]
[RequireComponent(typeof(RailPowerConnectionComponent))]
public class TramCableDriveObject : WorldObject, IRepresentsItem
{
    public virtual bool Electrical => false;
    private static readonly PoweredRailRegistry<TramCableDriveObject> Drives=new();
    private readonly object gate=new();
    private HashSet<RailCell> cells=[];
    private readonly PoweredRailContacts trams=new(2);
    private float supportedFraction;
    private bool running;
    internal void StopPower()
    {
        lock(gate)
        {
            cells=[];trams.Clear();supportedFraction=0;running=false;
            PoweredRailDrive.Stop(this);
            if(ConnectedRails!=0){ConnectedRails=0;this.Changed(nameof(ConnectedRails));}
            if(ConnectedTrams!=0){ConnectedTrams=0;this.Changed(nameof(ConnectedTrams));}
            if(RequiredPower!=0){RequiredPower=0;this.Changed(nameof(RequiredPower));}
            if(AvailablePower!=0){AvailablePower=0;this.Changed(nameof(AvailablePower));}
            if(Status!="Off"){Status="Off";this.Changed(nameof(Status));}
        }
    }
    private bool OwnsRun=>!Drives.Snapshot.Any(d=>d.ID<ID && !d.IsDestroyed && d.Enabled
        && d.GetComponent<RailPowerConnectionComponent>().Connected && d.cells.Overlaps(cells));
    private float RequiredWatts=>GetComponent<RailPowerConnectionComponent>().Connected && OwnsRun && cells.Count>0 && ReadyExceptPower(GetComponent<PowerGridComponent>())
        ? RailEconomy.TramCableWatts(cells.Count,trams.Count) : 0;
    static TramCableDriveObject()=>AddOccupancyList(typeof(TramCableDriveObject),new BlockOccupancy(Vector3i.Zero,typeof(BuildingWorldObjectBlock)));
    public virtual Type RepresentedItemType=>typeof(TramCableDriveItem);
    public override LocString DisplayName=>Localizer.DoStr("Tram Cable Drive");
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Connected Tram Rails")] public int ConnectedRails {get;private set;}
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Connected Trams")] public int ConnectedTrams {get;private set;}
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Required Power (W)")] public float RequiredPower {get;private set;}
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Allocated Power (W)")] public float AvailablePower {get;private set;}
    [SyncToView,Autogen,PropReadOnly] public string Status {get;private set;}="Waiting for Tram Rail";
    protected override void Initialize()
    {
        base.Initialize();
        PoweredRailDrive.Initialize(this,Electrical);
        Drives.Register(this);
    }
    protected override void OnDestroy()
    { var footprint=WorldOccupancy?.ToArray();var id=ObjectID;Drives.Remove(this);base.OnDestroy();PoweredRailDrive.RemoveFootprint(footprint,id); }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Drive updates/Tick");
        base.Tick();
        lock(gate)
        {
            var now=DateTime.UtcNow;
            var oldRails=ConnectedRails;var oldTrams=ConnectedTrams;var oldRequired=RequiredPower;var oldAvailable=AvailablePower;var oldStatus=Status;
            cells=GetComponent<RailPowerConnectionComponent>().Cells;
            trams.Expire(now);
            var grid=GetComponent<PowerGridComponent>().PowerGrid;
            RequiredPower=RequiredWatts;
            ConnectedRails=cells.Count;ConnectedTrams=trams.Count;
            var other=grid==null?0:grid.Components.Where(c=>c.Parent is not TramCableDriveObject && !c.EnergySelfSupply && ReadyExceptPower(c))
                .Sum(c=>Math.Max(0,c.EnergyDemand));
            // Disconnected cable runs sharing a grid must share its spare power,
            // not each claim the generator's full output independently.
            var totalDemand=grid==null?0:Drives.Values.Where(d=>!d.IsDestroyed
                && d.GetComponent<PowerGridComponent>().PowerGrid==grid).Sum(d=>d.RequiredWatts);
            AvailablePower=grid==null?0:RailEconomy.SharedCableAllocation(grid.EnergySupply,other,RequiredPower,totalDemand);
            supportedFraction=RequiredPower<=0?0:Math.Clamp(AvailablePower/RequiredPower,0,1);
            var demand=RequiredPower>0 && supportedFraction>0 ? Math.Min(RequiredPower,AvailablePower) : 0;
            // Connection invalidation can clear demand between drive ticks.
            var consumption=GetComponent<PowerConsumptionComponent>();
            if(Math.Abs(demand-consumption.JoulesPerSecond)>.01f)
                consumption.OverridePowerConsumption(demand);
            running=OwnsRun && Enabled && cells.Count>0 && grid!=null && GetComponent<PowerGridComponent>().Enabled && supportedFraction>0.05f;
            Status=!Enabled?"Off":cells.Count==0?"No connected Tram Rail":grid==null?(Electrical?"No electrical grid":"No mechanical grid"):
                !running?"Insufficient power":supportedFraction<.99f?"Reduced cable speed":"Powering tram network";
            if(oldRails!=ConnectedRails)this.Changed(nameof(ConnectedRails));
            if(oldTrams!=ConnectedTrams)this.Changed(nameof(ConnectedTrams));
            if(oldRequired!=RequiredPower)this.Changed(nameof(RequiredPower));
            if(oldAvailable!=AvailablePower)this.Changed(nameof(AvailablePower));
            if(oldStatus!=Status)this.Changed(nameof(Status));
            SetAnimatedState("CableRunning",running);
        }
    }
    internal static float PowerFor(RailCell cell,int tramId)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Vehicle propulsion lookup/PowerFor");
        cell=RailPowerConnectionComponent.PhysicalCell(cell);
        foreach(var drive in Drives.Snapshot)
        {
            if(drive.IsDestroyed) continue;
            lock(drive.gate)
            {
                if(!drive.GetComponent<RailPowerConnectionComponent>().Connected || !drive.cells.Contains(cell)) continue;
                // Waiting trams still load the connected cable during an outage
                // or brownout. Dropping them would briefly grant idle-load power.
                drive.trams.Touch(tramId,DateTime.UtcNow);
                var grid=drive.GetComponent<PowerGridComponent>();
                // A switch/grid change can arrive between drive ticks. Contacts
                // stay live, but the previous allocation cannot provide traction.
                if(!drive.running || !drive.GetComponent<OnOffComponent>().On || !drive.Enabled || !grid.Enabled
                    || grid.PowerGrid is not {} network || grid.EnergyDemand<=0 || network.EnergySupply<grid.EnergyDemand) continue;
                return drive.supportedFraction;
            }
        }
        return 0;
    }
}



