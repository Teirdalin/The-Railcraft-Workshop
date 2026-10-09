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

[Serialized, LocDisplayName("Tram Cable Drive"), LocDescription("Connect to a mechanically powered grid beside Tram Rail. The connected rail network conducts traction power to automated trams. Available power determines supported tram load and speed. Crafted at the Railworks Workbench using Industry."), Weight(12000)]
public sealed class TramCableDriveItem : WorldObjectItem<TramCableDriveObject> { }

[RequiresSkill(typeof(IndustrySkill),3)]
public sealed class TramCableDriveRecipe : MinecartRailRecipeFamily
{
    public TramCableDriveRecipe()=>Configure(MinecartRailRecipes.Make<TramCableDriveItem>("Tram Cable Drive",12,12,woodenGears:2, skillType:typeof(IndustrySkill)),"Tram Cable Drive",typeof(TramCableDriveRecipe),180,6, skillType:typeof(IndustrySkill));
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
    private float lastDemand=-1;
    private float supportedFraction;
    private bool running;
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
            if(Math.Abs(demand-lastDemand)>.01f)
            { GetComponent<PowerConsumptionComponent>().OverridePowerConsumption(demand);lastDemand=demand; }
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
        foreach(var drive in Drives.Snapshot)
        {
            if(drive.IsDestroyed) continue;
            lock(drive.gate)
            {
                if(!drive.GetComponent<RailPowerConnectionComponent>().Connected || !drive.cells.Contains(cell)||!drive.running) continue;
                drive.trams.Touch(tramId,DateTime.UtcNow);
                return drive.supportedFraction;
            }
        }
        return 0;
    }
}
