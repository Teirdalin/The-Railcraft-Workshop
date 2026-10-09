using System.Numerics;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Placement;
using Eco.Gameplay.Players;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.Items;

namespace Eco.Minecarts.Runtime;

public sealed partial class CoasterStationComponent
{
    private static readonly SemaphoreSlim loadingGate=new(1,1);
    private bool loadingCart;
    internal sealed record LoadingPosition(VoxelRail Rail,float T,int Facing,Vector3 Position,
        Eco.Shared.Math.Quaternion Rotation,RailCouplingComponent? Tail);

    // Load only along a connected, level approach. Extending a queue across a
    // bend could put adjacent car bodies through each other before coupling.
    internal bool TryLoadingPosition(out LoadingPosition? placement,out string error)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/TryLoadingPosition");
        placement=null;error="Clear the loading track and extend straight, level coaster rail behind the station.";
        if(Parent.IsDestroyed)return false;
        var rail=Parent.GetComponent<CoasterRailComponent>().Rail;
        var forward=rail.Profile.Tangent(.5f);
        var vehicles=RailCouplingComponent.LiveVehicles;
        var atStation=vehicles.Where(c=>c.Vehicle.RailSpec.Coaster)
            .Select(c=>(Car:c,Hit:rail.Profile.Nearest(c.Parent.Position-rail.Cell.Origin)))
            .Where(c=>c.Hit.Distance<.35f).OrderByDescending(c=>c.Hit.T).ToArray();
        RailCouplingComponent? tail=null;
        var spec=RailVehicleSpec.Find("RollerCoasterCart");
        var position=rail.Point(.5f);
        if(atStation.Length>0)
        {
            var group=atStation[0].Car.Group();
            if(group.Length>=16){error="The loading queue already has 16 carts.";return false;}
            if(group.Any(c=>!c.Vehicle.RailSpec.Coaster||c.Parent.GetComponent<MinecartMotionComponent>().CurrentRailVelocity.Length()>.05f
                ||Vector3.Dot(c.Parent.Rotation.RotateVector(Vector3.UnitZ),forward)<.99f))
            {error="Stop and align the coaster train before adding a cart.";return false;}
            tail=group.Where(c=>c.RearAvailable).OrderBy(c=>Vector3.Dot(c.Parent.Position,forward)).FirstOrDefault();
            if(tail==null)return false;
            position=tail.Parent.Position-forward*(tail.Vehicle.CouplerOffset+spec.Length/2+.10f);
        }
        var distance=Vector3.Dot(position-rail.Point(0),forward);
        var seen=new HashSet<RailCell>{rail.Cell};
        // Follow sockets, not a nearest-track search that could jump gaps or
        // switch to another coaster passing beneath the platform.
        for(var i=0;(distance<0||distance>rail.Profile.Length)&&i<40;i++)
        {
            if(TrackWorld.NeighborForVehicle(rail,distance<0?0:1) is not {} previous
                ||!seen.Add(previous.Rail.Cell)||!LevelStraight(previous.Rail,forward))return false;
            rail=previous.Rail;distance=Vector3.Dot(position-rail.Point(0),rail.Profile.Tangent(.5f));
        }
        if(distance<0||distance>rail.Profile.Length)return false;
        var t=distance/rail.Profile.Length;
        var facing=Vector3.Dot(rail.Profile.Tangent(.5f),forward)>0?1:-1;
        // Both axles need continuous rail. The body can overhang the short
        // station, just as it can overhang any other single rail block.
        foreach(var sign in new[]{-1,1})
        {
            var end=RailGuidance.Advance(rail,t,sign*facing*(spec.Wheelbase/2+.05f),TrackWorld.NeighborForVehicle);
            if(!LevelStraight(end.Rail,forward)||Vector3.Distance(end.Rail.Point(end.T),position+forward*sign*(spec.Wheelbase/2+.05f))>.03f)return false;
        }
        var rotation=Eco.Shared.Math.Quaternion.LookRotation(forward);
        var bounds=new VehicleBounds(position,new(rotation.x,rotation.y,rotation.z,rotation.w),new(spec.BodyWidth/2,.78f,spec.Length/2-.03f));
        if(vehicles.Any(c=>bounds.Overlaps(new(c.Parent.Position,new(c.Parent.Rotation.x,c.Parent.Rotation.y,c.Parent.Rotation.z,c.Parent.Rotation.w),c.Vehicle.ContactHalfSize))))
        {error="Another vehicle blocks the next loading position.";return false;}
        // Occupancy is empty for movable vehicles, so explicitly reject solid
        // scenery above the rail instead of relying on native occupied cells.
        for(var z=-.8f;z<=.81f;z+=.2f)for(var x=-.5f;x<=.51f;x+=.25f)for(var y=.5f;y<=1.51f;y+=.25f)
        {
            var p=position+rotation.RotateVector(new(x,y,z));
            var cell=new Eco.Shared.Math.Vector3i((int)MathF.Round(p.X),(int)MathF.Floor(p.Y),(int)MathF.Round(p.Z));
            var block=Eco.World.World.GetBlock(cell);
            if(block==null||!block.GetType().IsDefined(typeof(Eco.World.Blocks.Solid),true))continue;
            if(block is CoasterTrackBlock)continue;
            if(block is Eco.Gameplay.Objects.WorldObjectBlock objectBlock&&objectBlock.WorldObjectHandle.Object is CoasterRailObject)continue;
            error="A block obstructs the cart's loading position.";return false;
        }
        placement=new(rail,t,facing,position,rotation,tail);error="";return true;
    }
    internal static bool LevelStraight(VoxelRail rail,Vector3 forward){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/LevelStraight"); return rail.Profile.Coaster&&
        Enumerable.Range(0,5).All(i=>Math.Abs(Vector3.Dot(rail.Profile.Tangent(i/4f),forward))>.999f&&rail.Profile.Up(i/4f).Y>.999f); }

    internal async Task PlaceCart(Player player,RollerCoasterCartItem item)
    {
        if(!await loadingGate.WaitAsync(0))return;
        try
        {
            if(Parent.IsDestroyed||!Parent.IsAuthorized(player.User,AccessType.FullAccess)
                ||Vector3.Distance(player.User.Position,Parent.Position)>6)return;
            var inventory=player.User.Inventory;
            var stack=inventory.CarriedItem==item?inventory.Carried.SelectedStack:inventory.Toolbar.SelectedStack;
            if(stack.Item!=item||stack.Quantity<1)return;
            lock(gate)loadingCart=true;
            if(!TryLoadingPosition(out var plan,out var error)){player.InfoBoxLocStr(error);return;}
            if(plan!.Tail?.Group().Any(c=>!c.Parent.IsAuthorized(player.User,AccessType.FullAccess))==true)return;
            var placed=await WorldObjectPlacementUtils.TryPlaceWorldObjectNow(player,item,stack,plan.Position,plan.Rotation,0);
            if(placed is not RollerCoasterCartObject cart)return;
            var motion=cart.GetComponent<MinecartMotionComponent>();
            motion.ResetCoupledMotion();motion.AcceptCoupledPose(plan.Rail,plan.T,plan.Facing,Vector3.Zero);
            if(plan.Tail!=null&&!plan.Tail.CoupleLoadedCart(cart.GetComponent<RailCouplingComponent>()))
                player.InfoBoxLocStr("Cart placed with its brake on. The train moved; couple it manually when stopped.");
        }
        finally{lock(gate)loadingCart=false;loadingGate.Release();}
    }
}
