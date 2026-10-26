using System.Collections.Concurrent;
using Eco.Core.Controller;
using Eco.Gameplay.Aliases;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.Items;

namespace Eco.Minecarts.Runtime;
[Serialized,NoIcon,LocDisplayName("Cargo Unloading")]
public sealed class MinecartDumpRailComponent:WorldObjectComponent
{
    private static readonly ConcurrentDictionary<RailCell,MinecartDumpRailComponent> Rails=new();
    private readonly object gate=new();
    private readonly Dictionary<Guid,DateTime> attempts=new();
    private readonly Dictionary<Guid,DumpCycle> cycles=new();
    private Timer? animationTimer;
    private bool destroyed;
    private sealed class DumpCycle(RailVehicleObject car,DateTime start,int side)
    {internal readonly RailVehicleObject Car=car;internal readonly DateTime Start=start;internal readonly int Side=side;internal bool Committed;internal bool Failed;}
    private RailCell? registeredCell;
    [SyncToView,Autogen,PropReadOnly] public string Status {get;private set;}="Waiting for an enabled vehicle";
    internal static VoxelRail? Read(RailCell cell)
    {
        var physical=RailPowerConnectionComponent.PhysicalCell(cell);
        if(!Rails.TryGetValue(physical,out var dump))return null;
        lock(dump.gate)
        {
            if(dump.destroyed||dump.Parent.IsDestroyed||dump.registeredCell!=physical)return null;
            var forward=dump.Parent.Rotation.RotateVector(System.Numerics.Vector3.UnitZ);
            var turns=((int)Math.Round(Math.Atan2(forward.X,forward.Z)/(Math.PI/2))+4)%4;
            return new(cell,new("Straight",turns,false));
        }
    }
    public override void PostInitialize()
    {
        base.PostInitialize();Register();
    }
    private void Register()
    {
        lock(gate)
        {
            if(destroyed||Parent.IsDestroyed||registeredCell!=null)return;
            var p=Parent.Position3i;registeredCell=RailPowerConnectionComponent.PhysicalCell(new(p.X,p.Y,p.Z));
            Rails[registeredCell.Value]=this;RailSimulationFrame.Invalidate();
        }
    }
    private void Unregister(bool destroying=false)
    {
        var cancelled=false;
        RailCell? removed=null;
        lock(gate)
        {
            if(destroying)destroyed=true;
            if(registeredCell is {} cell&&Rails.TryRemove(new KeyValuePair<RailCell,MinecartDumpRailComponent>(cell,this)))removed=cell;
            registeredCell=null;
            animationTimer?.Dispose();animationTimer=null;
            // EndDump owns only the animation gate. Do not wait for motion:
            // movement can enter this component while holding a vehicle gate.
            foreach(var cycle in cycles.Values)if(!cycle.Car.IsDestroyed)cycle.Car.EndDump(Parent.ObjectID);
            cancelled=cycles.Count>0&&!destroying;cycles.Clear();attempts.Clear();
            if(cancelled)Status="Unloading stopped: unloading rail moved; bucket returned upright";
            RailSimulationFrame.Invalidate();
        }
        // Connection callbacks can enter drive/grid locks and read topology.
        // Notify only after removal and outside the unloading gate.
        if(removed is {} oldCell)RailPowerConnectionComponent.RailRemoved(oldCell);
        if(cancelled)this.Changed(nameof(Status));
    }
    public override void OnBeforeObjectMoved()
    {base.OnBeforeObjectMoved();Unregister();}
    public override void OnAfterObjectMoved()
    {base.OnAfterObjectMoved();Register();}
    public override void Destroy()
    {Unregister(destroying:true);base.Destroy();}
    internal static void TryUnload(RailCell cell,WorldObject car)
    {
        var physical=RailPowerConnectionComponent.PhysicalCell(cell);
        if(Rails.TryGetValue(physical,out var dump)&&!dump.Parent.IsDestroyed)dump.Unload(car,physical);
    }
    private void Unload(WorldObject car,RailCell expectedCell)
    {
        if(car is not RailVehicleObject vehicle||!RailVehicleBalances.AllowsDumping(vehicle.RailSpec.Key))return;
        var source=car.GetComponent<PublicStorageComponent>()?.Inventory;
        if(source==null||!source.NonEmptyStacks.Any())return;
        if(!Monitor.TryEnter(gate))return;
        try
        {
            // A lookup made before relocation may resume after reattachment.
            // Validate its captured physical cell under the same lifecycle gate.
            if(destroyed||registeredCell!=expectedCell||Parent.IsDestroyed||car.IsDestroyed)return;
            var now=DateTime.UtcNow;
            if(cycles.ContainsKey(car.ObjectID)||attempts.TryGetValue(car.ObjectID,out var last)&&(now-last).TotalSeconds<3)return;
            if(attempts.Count>4096)attempts.Clear();
            attempts[car.ObjectID]=now;
            var user=Parent.Owners?.FirstUser()??car.Owners?.FirstUser();
            if(user==null||!car.IsAuthorized(user,AccessType.FullAccess)){Status="Vehicle is not authorized for unloading by this rail's owner";return;}
            if(Transfer(vehicle,false)<=0){Status="Cannot unload: check Put Into links, free storage space, and cargo access";return;}
            if(!vehicle.TryBeginDump(Parent.ObjectID))return;
            // The rail's local right is the designated discharge side, even
            // when a reversed car enters from the other end.
            var side=System.Numerics.Vector3.Dot(Parent.Rotation.RotateVector(System.Numerics.Vector3.UnitX),car.Rotation.RotateVector(System.Numerics.Vector3.UnitX))<0?-1:1;
            cycles.Add(car.ObjectID,new(vehicle,now,side));vehicle.PublishDump(Parent.ObjectID,0,side,false);
            animationTimer??=new Timer(_=>Advance(DateTime.UtcNow),null,50,50);
            Status="Preparing to tip; cargo remains aboard";
        }
        catch(Exception error){Status="Unloading failed: "+error.Message;Eco.Shared.Logging.Log.WriteWarningLine(Localizer.NotLocalizedStr(Status));}
        finally{this.Changed(nameof(Status));Monitor.Exit(gate);}
    }
    private int Transfer(RailVehicleObject car,bool apply)
    {
        var user=Parent.Owners?.FirstUser()??car.Owners?.FirstUser();var source=car.GetComponent<PublicStorageComponent>()?.Inventory;
        if(user==null||source==null||car.IsDestroyed||!car.IsAuthorized(user,AccessType.FullAccess)||!RailVehicleBalances.AllowsDumping(car.RailSpec.Key))return 0;
        // Players normally enable Put Into from the cart's inventory window.
        // Honor those explicit output choices as well as the rail's own links.
        var railOutputs=Parent.GetComponent<LinkComponent>().GetSortedLinkedComponents(user,source:false,target:true);
        var cartOutputs=car.GetComponent<LinkComponent>()?.GetSortedLinkedComponents(user,source:false,target:true)
            ?? Enumerable.Empty<PublicStorageComponent>();
        var outputs=railOutputs.Concat(cartOutputs)
            .Where(s=>s.Parent is not RailVehicleObject&&s.Inventory!=source&&!s.Parent.IsDestroyed).Select(s=>s.Inventory).Distinct().ToArray();
        if(outputs.Length==0)return 0;
        // Native transaction simulation respects partial capacity, authorization
        // and inventory restrictions. Never retain inventory locks across frames.
        using var change=InventoryChangeSet.New(outputs.Prepend(source),user);
        change.InventoryAccessType=InventoryAccessType.Vehicle;
        var moved=0;
        foreach(var cargo in source.NonEmptyStacks.GroupBy(s=>s.Item))
        {
            var remaining=cargo.Sum(s=>s.Quantity);
            foreach(var output in outputs)
            {
                // Capacity queries describe a destination stack's current
                // quantity, not this load's remaining cargo. Let Eco simulate
                // all destination slots, restrictions and permissions together.
                // A full or incompatible output must not poison later outputs.
                var result=change.MoveAllItems(cargo.Key,remaining,source,output,allowPartial:true);
                if(!result.Success||result.Val<=0)continue;
                moved+=result.Val;remaining-=result.Val;
                if(remaining<=0)break;
            }
        }
        if(moved<=0||!change.CanApplyNonDisposing().Success)return 0;
        return !apply||change.TryApply().Success?moved:0;
    }
    private void Advance(DateTime now)
    {
        lock(gate)
        {
            foreach(var pair in cycles.ToArray())
            {
                var cycle=pair.Value;var car=cycle.Car;
                if(car.IsDestroyed){cycles.Remove(pair.Key);continue;}
                var elapsed=Math.Max(0,(now-cycle.Start).TotalSeconds);
                try
                {
                    // Unload at the lip: 45 degrees after 0.65s of visible
                    // preparation. Only a successful commit permits the full tip.
                    if(elapsed>=.65&&!cycle.Committed)
                    {
                        var motion=car.GetComponent<MinecartMotionComponent>();
                        // Movement calls Unload while holding this car's gate.
                        // Never wait for that gate from the animation callback:
                        // a busy car defers discharge to the next observation.
                        if(!MinecartMotionComponent.WithRecoveryLocks([motion],()=>
                        {
                            var contact=!motion.Derailed && motion.BoundRailCell is {} cell
                                && RailPowerConnectionComponent.PhysicalCell(cell)==registeredCell;
                            car.PublishDump(Parent.ObjectID,45,cycle.Side,false);
                            cycle.Committed=true;var moved=contact?Transfer(car,true):0;cycle.Failed=moved<=0;
                            var remaining=moved>0?car.GetComponent<PublicStorageComponent>().Inventory.NonEmptyStacks.Sum(s=>s.Quantity):0;
                            Status=moved>0?remaining>0?$"Unloaded {moved} items; {remaining} remain aboard":$"Unloaded {moved} items; returning bucket":contact
                                ?"Unloading cancelled: cargo or receiving inventory changed"
                                :"Unloading cancelled: vehicle left the unloading rail";
                            if(moved>0)car.SetDirty();this.Changed(nameof(Status));
                            return true;
                        }))continue;
                    }
                    var returning=cycle.Failed||elapsed>=1.05;
                    float Smooth(double t){var v=(float)Math.Clamp(t,0,1);return v*v*(3-2*v);}
                    var angle=cycle.Failed?45*(1-Smooth((elapsed-.65)/.65)):
                        elapsed<.65?45*Smooth(elapsed/.65):elapsed<.9?45+25*Smooth((elapsed-.65)/.25):elapsed<1.05?70:70*(1-Smooth((elapsed-1.05)/.75));
                    car.PublishDump(Parent.ObjectID,angle,cycle.Side,returning);
                    if(elapsed>=(cycle.Failed?1.3:1.8)){car.EndDump(Parent.ObjectID);cycles.Remove(pair.Key);}
                }
                catch(Exception error)
                {car.EndDump(Parent.ObjectID);cycles.Remove(pair.Key);Status="Unloading failed: "+error.Message;this.Changed(nameof(Status));Eco.Shared.Logging.Log.WriteWarningLine(Localizer.NotLocalizedStr(Status));}
            }
            if(cycles.Count==0){animationTimer?.Dispose();animationTimer=null;}
        }
    }
}
