using System.Collections.Concurrent;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;

namespace Eco.Minecarts.Runtime;

public sealed partial class CoasterStationComponent
{
    // Deliberately process-local: saves and component reinitialization cannot
    // replenish a station's use. A server restart does.
    private static readonly ConcurrentDictionary<Guid,byte> ReturnedStations=new();
    [SyncToView,Autogen,PropReadOnly,LocDisplayName("Cart Return")]
    public string CartReturnStatus {get;private set;}="Available once per server restart";

    [RPC,Autogen,UITypeName("BigButton")]
    public void ReturnCartsToStation(Player player)
    {
        if(Parent.IsDestroyed||!Parent.IsAuthorized(player.User,AccessType.FullAccess)
            ||Vector3.Distance(player.User.Position,Parent.Position)>6)return;
        TryReturnCarts(car=>car.IsAuthorized(player.User,AccessType.FullAccess),out var message);
        player.InfoBoxLocStr(message);
    }

    private sealed record ReturnPosition(RailCouplingComponent Car,VoxelRail Rail,float Parameter,int Facing,Vector3 Position);
    internal bool TryReturnCarts(Func<WorldObject,bool> authorized,out string message)
    {
        var result="Carts are busy; try again when their motion update finishes.";
        message=result;
        if(!loadingGate.Wait(0))return false;
        try
        {
            var success=RailCouplingComponent.WithRecoveryTopology(()=>
            {
                if(Parent.IsDestroyed||!authorized(Parent)){result="Station access denied.";return false;}
                if(ReturnedStations.ContainsKey(Parent.ObjectID)){result="Already used at this station; available after the next server restart.";return false;}
                var cars=RailCouplingComponent.LiveVehicles.Where(c=>c.Vehicle.RailSpec.Coaster
                    &&c.Parent.GetComponent<MinecartMotionComponent>().CoasterHomeStation==Parent.ObjectID)
                    .OrderBy(c=>c.Parent.GetComponent<MinecartMotionComponent>().CoasterDepartureOrder).ThenBy(c=>c.Parent.ID).ToArray();
                if(cars.Length==0){result="No departed carts belong to this station. The return is still available.";return false;}
                if(cars.Length>256){result="Too many carts for one return queue.";return false;}
                var ids=cars.Select(c=>c.Parent.ID).ToHashSet();
                if(cars.Any(c=>c.Group().Any(m=>!ids.Contains(m.Parent.ID))))
                {result="A cart is coupled to a vehicle from another station. Uncouple it before returning.";return false;}
                return MinecartMotionComponent.WithRecoveryLocks(cars.Select(c=>c.Parent.GetComponent<MinecartMotionComponent>()),()=>
                {
                    if(cars.Any(c=>c.Parent.IsDestroyed||!authorized(c.Parent)))
                    {result="Full access to every returned cart is required.";return false;}
                    if(cars.Any(c=>c.Parent.GetComponent<MountComponent>().IsMounted))
                    {result="Empty the coaster seats before returning carts.";return false;}
                    var plans=new List<ReturnPosition>();var visited=new HashSet<int>();
                    var stationRail=Parent.GetComponent<CoasterRailComponent>().Rail;
                    var forward=stationRail.Profile.Tangent(.5f);var distance=0f;RailCouplingComponent? preceding=null;
                    foreach(var candidate in cars)
                    {
                        if(visited.Contains(candidate.Parent.ID))continue;
                        var group=candidate.RecoveryOrder();
                        if(group.Length==0){result="The coupled carts do not form a valid linear train.";return false;}
                        var first=group[0];var firstFacing=first.EndAvailable(1)?1:-1;
                        foreach(var car in group)
                        {
                            visited.Add(car.Parent.ID);
                            if(preceding!=null)distance+=preceding.Vehicle.CouplerOffset+car.Vehicle.CouplerOffset+.10f;
                            if(distance>128){result="The return queue exceeds 128 metres; split it between stations.";return false;}
                            var target=stationRail.Point(.5f)-forward*distance;
                            var rail=stationRail;var seen=new HashSet<RailCell>();
                            var d=Vector3.Dot(target-rail.Point(0),rail.Profile.Tangent(.5f));
                            while(d<0||d>rail.Profile.Length)
                            {
                                if(!seen.Add(rail.Cell)||seen.Count>256||TrackWorld.NeighborForVehicle(rail,d<0?0:1) is not {} next)
                                {result="Extend straight, level coaster rail behind the station to fit all returned carts.";return false;}
                                rail=next.Rail;
                                if(!LevelStraight(rail,forward)||At(rail.Cell) is {} other&&other!=this)
                                {result="The return queue needs continuous straight, level rail behind this station.";return false;}
                                d=Vector3.Dot(target-rail.Point(0),rail.Profile.Tangent(.5f));
                            }
                            var t=d/rail.Profile.Length;
                            foreach(var sign in new[]{-1,1})
                            {
                                var support=RailGuidance.Advance(rail,t,sign*(Vector3.Dot(rail.Profile.Tangent(t),forward)>0?1:-1)*(car.Vehicle.RailSpec.Wheelbase/2+.05f),TrackWorld.NeighborForVehicle);
                                if(!LevelStraight(support.Rail,forward)||Vector3.Distance(support.Rail.Point(support.T),target+forward*sign*(car.Vehicle.RailSpec.Wheelbase/2+.05f))>.03f)
                                {result="Both axles of every returned cart need continuous straight, level rail.";return false;}
                            }
                            var facing=firstFacing*first.FacingRelativeTo(car);
                            var rotation=Eco.Shared.Math.Quaternion.LookRotation(forward*facing);
                            var bounds=new VehicleBounds(target,new(rotation.x,rotation.y,rotation.z,rotation.w),car.Vehicle.ContactHalfSize);
                            if(RailCouplingComponent.LiveVehicles.Any(c=>!ids.Contains(c.Parent.ID)
                                &&bounds.Overlaps(new(c.Parent.Position,new(c.Parent.Rotation.x,c.Parent.Rotation.y,c.Parent.Rotation.z,c.Parent.Rotation.w),c.Vehicle.ContactHalfSize))))
                            {result="Another vehicle blocks the return queue. Clear it and try again.";return false;}
                            if(!ReturnTrackClear(target,rotation))
                            {result="A solid block obstructs the return queue. Clear it and try again.";return false;}
                            plans.Add(new(car,rail,t,facing*(Vector3.Dot(rail.Profile.Tangent(t),forward)>0?1:-1),target));preceding=car;
                        }
                    }
                    // Never wait on the station gate while holding car gates:
                    // normal simulation takes these gates in the opposite order.
                    if(!Monitor.TryEnter(gate))return false;
                    try
                    {
                        if(!ReturnedStations.TryAdd(Parent.ObjectID,0))return false;
                        loadingCart=true;
                        arrivals.Clear();dispatched.Clear();securing.Clear();trains.Clear();
                        foreach(var plan in plans)
                        {
                            plan.Car.ResetRecoveryTrail();
                            plan.Car.Parent.GetComponent<MinecartMotionComponent>().RecoverAtStation(plan.Rail,plan.Parameter,plan.Facing);
                            ((RailVehicleObject)plan.Car.Parent).SetRestraints(false);
                        }
                        CartReturnStatus="Used; available after the next server restart";
                        Parent.SetDirty();result=$"Returned {plans.Count} carts in departure order. Available again after the next server restart.";
                        return true;
                    }
                    finally{loadingCart=false;Monitor.Exit(gate);}
                });
            });
            message=result;return success;
        }
        finally{loadingGate.Release();}
    }

    private static bool ReturnTrackClear(Vector3 position,Eco.Shared.Math.Quaternion rotation)
    {
        for(var z=-.8f;z<=.81f;z+=.2f)for(var x=-.5f;x<=.51f;x+=.25f)for(var y=.5f;y<=1.51f;y+=.25f)
        {
            var p=position+rotation.RotateVector(new(x,y,z));
            var cell=new Eco.Shared.Math.Vector3i((int)MathF.Round(p.X),(int)MathF.Floor(p.Y),(int)MathF.Round(p.Z));
            var block=Eco.World.World.GetBlock(cell);
            if(block==null||!block.GetType().IsDefined(typeof(Eco.World.Blocks.Solid),true))continue;
            if(block is Eco.Mods.TechTree.CoasterTrackBlock)continue;
            if(block is WorldObjectBlock own&&own.WorldObjectHandle.Object is Eco.Mods.TechTree.CoasterRailObject)continue;
            return false;
        }
        return true;
    }
}
