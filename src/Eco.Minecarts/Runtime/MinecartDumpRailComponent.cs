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
    private sealed class DumpCycle(RailVehicleObject car,DateTime start,int side)
    {internal readonly RailVehicleObject Car=car;internal readonly DateTime Start=start;internal readonly int Side=side;internal bool Committed;internal bool Failed;}
    private RailCell registeredCell;
    [SyncToView,Autogen,PropReadOnly] public string Status {get;private set;}="Waiting for an enabled vehicle";
    internal static VoxelRail? Read(RailCell cell)
    {
        if(!Rails.TryGetValue(cell,out var dump)||dump.Parent.IsDestroyed)return null;
        var forward=dump.Parent.Rotation.RotateVector(System.Numerics.Vector3.UnitZ);
        var turns=((int)Math.Round(Math.Atan2(forward.X,forward.Z)/(Math.PI/2))+4)%4;
        return new(cell,new("Straight",turns,false));
    }
    public override void PostInitialize()
    {
        base.PostInitialize();var p=Parent.Position3i;registeredCell=new(p.X,p.Y,p.Z);
        Rails[registeredCell]=this;RailSimulationFrame.Invalidate();
    }
    public override void Destroy()
    {lock(gate){animationTimer?.Dispose();animationTimer=null;foreach(var cycle in cycles.Values)if(!cycle.Car.IsDestroyed)cycle.Car.EndDump(Parent.ObjectID);cycles.Clear();}
        Rails.TryRemove(new KeyValuePair<RailCell,MinecartDumpRailComponent>(registeredCell,this));RailSimulationFrame.Invalidate();base.Destroy();}
    internal static void TryUnload(RailCell cell,WorldObject car)
    {
        if(Rails.TryGetValue(cell,out var dump)&&!dump.Parent.IsDestroyed)dump.Unload(car);
    }
    private void Unload(WorldObject car)
    {
        if(car is not RailVehicleObject vehicle||!RailVehicleBalances.AllowsDumping(vehicle.RailSpec.Key))return;
        var source=car.GetComponent<PublicStorageComponent>()?.Inventory;
        if(source==null||!source.NonEmptyStacks.Any())return;
        if(!Monitor.TryEnter(gate))return;
        try
        {
            var now=DateTime.UtcNow;
            if(cycles.ContainsKey(car.ObjectID)||attempts.TryGetValue(car.ObjectID,out var last)&&(now-last).TotalSeconds<3)return;
            if(attempts.Count>4096)attempts.Clear();
            attempts[car.ObjectID]=now;
            var user=Parent.Owners?.FirstUser();
            if(user==null||!car.IsAuthorized(user,AccessType.FullAccess)){Status="Vehicle is not authorized for unloading by this rail's owner";return;}
            if(Transfer(vehicle,false)<=0){Status="No linked output inventory accepts this cargo";return;}
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
        var user=Parent.Owners?.FirstUser();var source=car.GetComponent<PublicStorageComponent>()?.Inventory;
        if(user==null||source==null||car.IsDestroyed||!car.IsAuthorized(user,AccessType.FullAccess)||!RailVehicleBalances.AllowsDumping(car.RailSpec.Key))return 0;
        var outputs=Parent.GetComponent<LinkComponent>().GetSortedLinkedComponents(user,source:false,target:true)
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
                var quantity=Math.Min(remaining,Math.Min(source.GetMaxPickup(cargo.Key,remaining,change,output).Val,
                    Math.Min(output.GetMaxAcceptedVal(cargo.Key,remaining,user,source),output.GetMaxAccepted(cargo.Key,remaining,change,source).Val)));
                if(quantity<=0)continue;
                change.MoveItems(cargo.Key,quantity,source,output);moved+=quantity;remaining-=quantity;
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
                        car.PublishDump(Parent.ObjectID,45,cycle.Side,false);
                        cycle.Committed=true;var moved=Transfer(car,true);cycle.Failed=moved<=0;
                        Status=moved>0?$"Unloaded {moved} items; returning bucket":"Unloading cancelled: cargo or receiving inventory changed";
                        if(moved>0)car.SetDirty();this.Changed(nameof(Status));
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
