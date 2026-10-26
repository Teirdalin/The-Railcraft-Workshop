using System.Numerics;
using Eco.Gameplay.Components;
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
        Eco.Shared.Math.Quaternion Rotation,RailCouplingComponent? Tail,VoxelRail StationRail,RailCouplingComponent[] Queue);

    // Load only along a connected, level approach. Extending a queue across a
    // bend could put adjacent car bodies through each other before coupling.
    internal bool TryLoadingPosition(out LoadingPosition? placement,out string error)
        =>PlanLoadingPosition(null,out placement,out error);

    // Replanning after native placement must ignore only the newly placed cart,
    // while retaining every other vehicle and the same clearance/support rules.
    private bool PlanLoadingPosition(RollerCoasterCartObject? placed,out LoadingPosition? placement,out string error)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/TryLoadingPosition");
        placement=null;error="Clear the loading track and extend straight, level coaster rail behind the station.";
        if(Parent.IsDestroyed)return false;
        var revision=RailSimulationFrame.Revision;
        var stationRail=Parent.GetComponent<CoasterRailComponent>().Rail;
        if(!LoadingStationMatches(stationRail))
        {error="Station or loading track changed; try again when it is in place.";return false;}
        var rail=stationRail;
        var forward=rail.Profile.Tangent(.5f);
        var vehicles=RailCouplingComponent.LiveVehicles.Where(c=>c.Parent!=placed).ToArray();
        // Users, carts and the station may have different horizontal world
        // images. Project the live queue into the station's local image.
        var atStation=vehicles.Where(c=>c.Vehicle.RailSpec.Coaster)
            .Select(c=>(Car:c,Hit:rail.Profile.Nearest(RailWorldDistance.Delta(c.Parent.Position,rail.Cell.Origin))))
            .Where(c=>c.Hit.Distance<.35f).OrderByDescending(c=>c.Hit.T).ToArray();
        RailCouplingComponent? tail=null;
        RailCouplingComponent[] queue=[];
        var spec=RailVehicleSpec.Find("RollerCoasterCart");
        var position=rail.Point(.5f);
        if(atStation.Length>0)
        {
            var group=queue=atStation[0].Car.Group();
            if(group.Length>=16){error="The loading queue already has 16 carts.";return false;}
            if(group.Any(c=>!c.Vehicle.RailSpec.Coaster||c.Parent.GetComponent<MinecartMotionComponent>().CurrentRailVelocity.Length()>.05f
                ||Vector3.Dot(c.Parent.Rotation.RotateVector(Vector3.UnitZ),forward)<.99f))
            {error="Stop and align the coaster train before adding a cart.";return false;}
            tail=group.Where(c=>c.RearAvailable).OrderBy(c=>Vector3.Dot(RailWorldDistance.Delta(c.Parent.Position,rail.Point(.5f)),forward)).FirstOrDefault();
            if(tail==null)return false;
            position=rail.Point(.5f)+RailWorldDistance.Delta(tail.Parent.Position,rail.Point(.5f))-forward*(tail.Vehicle.CouplerOffset+spec.Length/2+.10f);
        }
        var distance=Vector3.Dot(RailWorldDistance.Delta(position,rail.Point(0)),forward);
        var seen=new HashSet<RailCell>{rail.Cell};
        // Follow sockets, not a nearest-track search that could jump gaps or
        // switch to another coaster passing beneath the platform.
        for(var i=0;(distance<0||distance>rail.Profile.Length)&&i<40;i++)
        {
            if(TrackWorld.NeighborForVehicle(rail,distance<0?0:1) is not {} previous
                ||!seen.Add(previous.Rail.Cell)||!LevelStraight(previous.Rail,forward))return false;
            rail=previous.Rail;distance=Vector3.Dot(RailWorldDistance.Delta(position,rail.Point(0)),rail.Profile.Tangent(.5f));
        }
        if(distance<0||distance>rail.Profile.Length)return false;
        var t=distance/rail.Profile.Length;
        var facing=Vector3.Dot(rail.Profile.Tangent(.5f),forward)>0?1:-1;
        // Both axles need continuous rail. The body can overhang the short
        // station, just as it can overhang any other single rail block.
        foreach(var sign in new[]{-1,1})
        {
            var end=RailGuidance.Advance(rail,t,sign*facing*(spec.Wheelbase/2+.05f),TrackWorld.NeighborForVehicle);
            if(!LevelStraight(end.Rail,forward)||!RailWorldDistance.Within(end.Rail.Point(end.T),position+forward*sign*(spec.Wheelbase/2+.05f),.03f))return false;
        }
        var rotation=Eco.Shared.Math.Quaternion.LookRotation(forward);
        var bounds=new VehicleBounds(position,new(rotation.x,rotation.y,rotation.z,rotation.w),new(spec.BodyWidth/2,.78f,spec.Length/2-.03f));
        if(vehicles.Any(c=>bounds.Overlaps(c.BoundsNear(position))))
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
        if(!LoadingStationMatches(stationRail)||revision!=RailSimulationFrame.Revision)
        {error="Station or loading track changed; try again when it is in place.";return false;}
        placement=new(rail,t,facing,position,rotation,tail,stationRail,queue);error="";return true;
    }
    private bool LoadingStationMatches(VoxelRail rail)
    {
        if(!Monitor.TryEnter(gate))return false;
        try{return StationRailMatches(rail);}finally{Monitor.Exit(gate);}
    }
    internal static bool LevelStraight(VoxelRail rail,Vector3 forward){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Stations/Detection and departure/LevelStraight"); return rail.Profile.Coaster&&
        Enumerable.Range(0,5).All(i=>Math.Abs(Vector3.Dot(rail.Profile.Tangent(i/4f),forward))>.999f&&rail.Profile.Up(i/4f).Y>.999f); }

    internal async Task PlaceCart(Player player,RollerCoasterCartItem item)
    {
        if(!await loadingGate.WaitAsync(0))return;
        try
        {
            if(Parent.IsDestroyed||!Parent.IsAuthorized(player.User,AccessType.FullAccess)
                ||!RailWorldDistance.Within(player.User.Position,Parent.Position,6))return;
            var inventory=player.User.Inventory;
            var stack=inventory.CarriedItem==item?inventory.Carried.SelectedStack:inventory.Toolbar.SelectedStack;
            if(stack.Item!=item||stack.Quantity<1)return;
            lock(gate)loadingCart=true;
            if(!TryLoadingPosition(out var plan,out var error)){player.InfoBoxLocStr(error);return;}
            if(plan!.Tail?.Group().Any(c=>!c.Parent.IsAuthorized(player.User,AccessType.FullAccess))==true)return;
            var placed=await WorldObjectPlacementUtils.TryPlaceWorldObjectNow(player,item,stack,plan.Position,plan.Rotation,0);
            if(placed is not RollerCoasterCartObject cart)return;
            var motion=cart.GetComponent<MinecartMotionComponent>();
            // Eco has already committed the item transaction. A stale loading
            // plan must neither delete/refund that cart nor snap it to old rail.
            motion.ResetCoupledMotion();motion.CommandBrake=true;
            if(!CompleteLoading(player,plan,cart))
                player.InfoBoxLocStr("Cart placed with its brake on. Loading conditions changed; check the track and couple manually if needed.");
        }
        finally{lock(gate)loadingCart=false;loadingGate.Release();}
    }

    private bool CompleteLoading(Player player,LoadingPosition plan,RollerCoasterCartObject cart)
        =>RailCouplingComponent.WithRecoveryTopology(()=>MinecartMotionComponent.WithRecoveryLocks(
            plan.Queue.Select(c=>c.Parent.GetComponent<MinecartMotionComponent>()).Append(cart.GetComponent<MinecartMotionComponent>()),()=>
        {
            // Simulation may take motion gates before the station gate. Keep
            // completion nonblocking in the opposite acquisition order too.
            if(!Monitor.TryEnter(gate))return false;
            try
            {
                var added=cart.GetComponent<RailCouplingComponent>();
                if(cart.IsDestroyed||added.Linked||cart.GetComponent<MountComponent>().IsMounted
                    ||!RailWorldDistance.Within(cart.Position,plan.Position,.03f)
                    ||Vector3.Dot(cart.Rotation.RotateVector(Vector3.UnitZ),plan.Rotation.RotateVector(Vector3.UnitZ))<.999f
                    ||!StationRailMatches(plan.StationRail)
                    ||!Parent.IsAuthorized(player.User,AccessType.FullAccess)
                    ||!cart.IsAuthorized(player.User,AccessType.FullAccess)
                    ||!RailWorldDistance.Within(player.User.Position,Parent.Position,6)
                    ||plan.Queue.Any(c=>c.Parent.IsDestroyed||!c.Parent.IsAuthorized(player.User,AccessType.FullAccess)))return false;
                if(!PlanLoadingPosition(cart,out var current,out _)||current!.StationRail!=plan.StationRail
                    ||current.Rail!=plan.Rail||Math.Abs(current.T-plan.T)>.001f||current.Facing!=plan.Facing
                    ||current.Tail!=plan.Tail||!current.Queue.SequenceEqual(plan.Queue)
                    ||!RailWorldDistance.Within(current.Position,plan.Position,.03f))return false;
                cart.GetComponent<MinecartMotionComponent>().AcceptCoupledPose(current.Rail,current.T,current.Facing,Vector3.Zero);
                return current.Tail==null||current.Tail.CoupleLoadedCart(added);
            }
            finally{Monitor.Exit(gate);}
        }));
}
