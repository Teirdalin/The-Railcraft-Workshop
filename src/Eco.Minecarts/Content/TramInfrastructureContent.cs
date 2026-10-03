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
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;

[Serialized, LocDisplayName("Tram Cable Drive"), LocDescription("Connect to a mechanically powered grid beside Tram Rail. The connected rail network conducts traction power to automated trams. Available power determines supported tram load and speed."), Weight(12000)]
public sealed class TramCableDriveItem : WorldObjectItem<TramCableDriveObject> { }

[RequiresSkill(typeof(BasicEngineeringSkill),3)]
public sealed class TramCableDriveRecipe : MinecartRailRecipeFamily
{
    public TramCableDriveRecipe()=>Configure(MinecartRailRecipes.Make<TramCableDriveItem>("Tram Cable Drive",12,12,woodenGears:2),"Tram Cable Drive",typeof(TramCableDriveRecipe),180,6);
}

[Serialized, RequireComponent(typeof(OnOffComponent)), RequireComponent(typeof(PropertyAuthComponent))]
[RequireComponent(typeof(PowerGridComponent)), RequireComponent(typeof(PowerConsumptionComponent))]
public sealed class TramCableDriveObject : WorldObject, IRepresentsItem
{
    private static readonly ConcurrentDictionary<int,TramCableDriveObject> Drives=new();
    private readonly object gate=new();
    private HashSet<RailCell> cells=[];
    private readonly Dictionary<int,DateTime> trams=[];
    private DateTime nextScan;
    private float lastDemand=-1;
    private float supportedFraction;
    private bool running;
    private bool OwnsRun=>!Drives.Values.Any(d=>d.ID<ID && !d.IsDestroyed && d.Enabled && d.cells.Overlaps(cells));
    private static bool ReadyExceptPower(PowerGridComponent grid)=>!grid.Parent.IsDestroyed &&
        grid.Parent.Components.All(c=>c==grid || c.Enabled && (c is not IOperatingWorldObjectComponent op || op.Operating));
    private float RequiredWatts=>OwnsRun && cells.Count>0 && ReadyExceptPower(GetComponent<PowerGridComponent>())
        ? RailEconomy.TramCableWatts(cells.Count,trams.Count) : 0;
    static TramCableDriveObject()=>AddOccupancyList(typeof(TramCableDriveObject),new BlockOccupancy(Vector3i.Zero,typeof(BuildingWorldObjectBlock)));
    public Type RepresentedItemType=>typeof(TramCableDriveItem);
    public override LocString DisplayName=>Localizer.DoStr("Tram Cable Drive");
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Connected Tram Rails")] public int ConnectedRails {get;private set;}
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Connected Trams")] public int ConnectedTrams {get;private set;}
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Required Mechanical Power (W)")] public float RequiredPower {get;private set;}
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Allocated Mechanical Power (W)")] public float AvailablePower {get;private set;}
    [SyncToView,Autogen,PropReadOnly] public string Status {get;private set;}="Waiting for Tram Rail";
    protected override void Initialize()
    {
        base.Initialize();
        GetComponent<PowerConsumptionComponent>().Initialize(0);
        GetComponent<PowerGridComponent>().Initialize(5,new MechanicalPower());
        Drives[ID]=this;
    }
    protected override void OnDestroy()
    {
        var occupied=WorldOccupancy?.ToArray();var id=ObjectID;
        Drives.TryRemove(ID,out _);
        base.OnDestroy();
        if(occupied!=null) foreach(var cell in occupied)
            if(Eco.World.World.GetBlock(cell) is WorldObjectBlock block && block.WorldObjectHandle.Id==id)
                Eco.World.World.DeleteBlock(cell);
    }
    public override void Tick()
    {
        base.Tick();
        lock(gate)
        {
            var now=DateTime.UtcNow;
            if(now>=nextScan) { cells=TrackWorld.TramRun(Position);nextScan=now.AddSeconds(1); }
            foreach(var stale in trams.Where(x=>(now-x.Value).TotalSeconds>2).Select(x=>x.Key).ToArray()) trams.Remove(stale);
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
            Status=!Enabled?"Off":cells.Count==0?"No connected Tram Rail":grid==null?"No mechanical grid":
                !running?"Insufficient power":supportedFraction<.99f?"Reduced cable speed":"Powering tram network";
            this.Changed(nameof(ConnectedRails));this.Changed(nameof(ConnectedTrams));this.Changed(nameof(RequiredPower));this.Changed(nameof(AvailablePower));this.Changed(nameof(Status));
            SetAnimatedState("CableRunning",running);
        }
    }
    internal static float PowerFor(RailCell cell,int tramId)
    {
        foreach(var drive in Drives.Values.OrderBy(d=>d.ID))
        {
            if(drive.IsDestroyed) {Drives.TryRemove(drive.ID,out _);continue;}
            lock(drive.gate)
            {
                if(!drive.cells.Contains(cell)||!drive.running) continue;
                drive.trams[tramId]=DateTime.UtcNow;
                return drive.supportedFraction;
            }
        }
        return 0;
    }
}
