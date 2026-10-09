using System.Collections.Concurrent;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, LocDisplayName("Rail Couplings")]
public sealed partial class RailCouplingComponent : WorldObjectComponent
{
    private static readonly ConcurrentDictionary<int, RailCouplingComponent> Vehicles = new();
    internal static RailCouplingComponent? NearestTo(Vector3 position, float radius) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/NearestTo"); return Vehicles.Values.Where(c=>!c.Parent.IsDestroyed && Vector3.DistanceSquared(c.Parent.Position,position)<=radius*radius)
            .OrderBy(c=>Vector3.DistanceSquared(c.Parent.Position,position)).FirstOrDefault(); }
    private static readonly ConcurrentDictionary<Guid, RailCouplingComponent> PersistentVehicles = new();
    private static readonly object LinkGate = new();
    private static long topologyRevision;
    internal static long TopologyRevision=>Volatile.Read(ref topologyRevision);
    private sealed record ConsistSnapshot(long Revision, RailCouplingComponent[] Members);
    private ConsistSnapshot? consist;
    private readonly object leaderCacheKey = new();
    private static void InvalidateConsists()
    { Interlocked.Increment(ref topologyRevision); RailSimulationFrame.Invalidate(); }
    internal static RailCouplingComponent[] LiveVehicles=>Vehicles.Values.Where(c=>!c.Parent.IsDestroyed).ToArray();
    internal bool RearAvailable=>!this.HasPartner(-1);
    internal bool EndAvailable(int end) => !this.HasPartner(end);
    // Rotating the symmetric cart exchanges LOCAL ends, not physical partners.
    internal void ReverseEnds()
    {
        lock (LinkGate)
        {
            var front = this.Partner(1);
            var rear = this.Partner(-1);
            var frontEnd = this.FrontPartnerEnd;
            var rearEnd = this.RearPartnerEnd;
            if (front != null) front.SetPartner(frontEnd, this.Parent.ID, -1);
            if (rear != null) rear.SetPartner(rearEnd, this.Parent.ID, 1);
            this.SetPartner(1, rear?.Parent.ID ?? 0, rearEnd);
            this.SetPartner(-1, front?.Parent.ID ?? 0, frontEnd);
        }
    }
    internal bool CoupleLoadedCart(RailCouplingComponent added)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/CoupleLoadedCart", this.Parent);
        lock(LinkGate)
        {
            if(Parent.IsDestroyed||added.Parent.IsDestroyed||!RearAvailable||added.Linked
                ||!Vehicle.RailSpec.Coaster||!added.Vehicle.RailSpec.Coaster
                ||Vector3.Distance(Connector(-1),added.Connector(1))>.3f)return false;
            SetPartner(-1,added.Parent.ID,1);added.SetPartner(1,Parent.ID,-1);
            return true;
        }
    }
    internal bool AwaitingLoad { get; private set; }
    internal bool LoadMembersReady => this.Group().All(node => node.Parent.Initialized &&
        new[] { -1, 1 }.All(end => !node.HasPartner(end) ||
            (node.PartnerObjectId(end) != Guid.Empty && Math.Abs(node.PartnerEnd(end)) == 1 && node.Partner(end) is { } partner &&
             partner.PartnerEnd(node.PartnerEnd(end)) == end &&
             partner.Vehicle.RailSpec.Industrial == node.Vehicle.RailSpec.Industrial && partner.Vehicle.RailSpec.Coaster == node.Vehicle.RailSpec.Coaster)));
    internal void CompleteLoad()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/CompleteLoad", this.Parent);
        foreach (var node in this.Group()) node.AwaitingLoad = false;
    }
    [Serialized] public int FrontPartnerId { get; set; }
    [Serialized] public int RearPartnerId { get; set; }
    [Serialized] public int FrontPartnerEnd { get; set; }
    [Serialized] public int RearPartnerEnd { get; set; }
    // ObjectID is persisted by Eco; ID is a newly allocated network handle.
    // Retain the old integer fields only for conservative legacy migration.
    [Serialized] public Guid FrontPartnerObjectId { get; set; }
    [Serialized] public Guid RearPartnerObjectId { get; set; }
    public RailVehicleObject Vehicle => (RailVehicleObject)this.Parent;
    public bool Linked => this.HasPartner(1) || this.HasPartner(-1);
    private Guid PartnerObjectId(int end) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/PartnerObjectId", this.Parent); return end > 0 ? this.FrontPartnerObjectId : this.RearPartnerObjectId; }
    private bool HasPartner(int end) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/HasPartner", this.Parent); return this.PartnerObjectId(end) != Guid.Empty || this.PartnerId(end) != 0; }
    private int PartnerId(int end) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/PartnerId", this.Parent); return end > 0 ? this.FrontPartnerId : this.RearPartnerId; }
    private int PartnerEnd(int end) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/PartnerEnd", this.Parent); return end > 0 ? this.FrontPartnerEnd : this.RearPartnerEnd; }
    public Vector3 Connector(int end) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/Connector", this.Parent); return this.Parent.Position + this.Parent.Rotation.RotateVector(this.Vehicle.ConnectionPoints.Coupler(end)); }
    private void SetPartner(int end, int id, int otherEnd)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/SetPartner", this.Parent);
        if (end > 0) { this.FrontPartnerId = id; this.FrontPartnerEnd = otherEnd; }
        else { this.RearPartnerId = id; this.RearPartnerEnd = otherEnd; }
        var guid = id != 0 && Vehicles.TryGetValue(id,out var target) ? target.Parent.ObjectID : Guid.Empty;
        if(end>0) this.FrontPartnerObjectId=guid; else this.RearPartnerObjectId=guid;
        InvalidateConsists();
        this.Parent.SetAnimatedState(end > 0 ? "CoupledFront" : "CoupledRear", id != 0);
        this.Parent.GetComponent<MinecartMotionComponent>()?.Changed(nameof(MinecartMotionComponent.VehicleCoupled));
        this.Parent.SetDirty();
    }
    private RailCouplingComponent? RawPartner(int end)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/RawPartner", this.Parent);
        var guid=this.PartnerObjectId(end);
        return guid!=Guid.Empty ? PersistentVehicles.GetValueOrDefault(guid) : Vehicles.GetValueOrDefault(this.PartnerId(end));
    }
    private bool PointsTo(int end,RailCouplingComponent other) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/PointsTo", this.Parent); return Math.Abs(end)==1 &&
        (this.PartnerObjectId(end)!=Guid.Empty ? this.PartnerObjectId(end)==other.Parent.ObjectID : this.PartnerId(end)==other.Parent.ID); }
    private RailCouplingComponent? Partner(int end) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/Partner", this.Parent); return this.RawPartner(end) is {} other && other!=this
        && !other.Parent.IsDestroyed && Math.Abs(this.PartnerEnd(end))==1
        && other.PointsTo(this.PartnerEnd(end),this) && other.PartnerEnd(this.PartnerEnd(end))==end ? other : null; }

    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/PostInitialize", this.Parent);
        base.PostInitialize();
        Vehicles[this.Parent.ID] = this;
        PersistentVehicles[this.Parent.ObjectID] = this;
        InvalidateConsists();
        this.AwaitingLoad = this.Linked;
        if (this.AwaitingLoad) this.Vehicle.SetRailGuidance(true);
        this.Parent.SetAnimatedState("VehicleCollisionsEnabled", true);
        this.Parent.SetAnimatedState("CoupledFront", this.HasPartner(1));
        this.Parent.SetAnimatedState("CoupledRear", this.HasPartner(-1));
    }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/Tick", this.Parent);
        base.Tick();
        this.ReconcileLoadedLinks(WorldObjectManager.Init.Initialized);
        if (this.AwaitingLoad) return;
        lock (LinkGate)
            foreach (var end in new[] { -1, 1 })
                if (this.Partner(end) is { } partner && (partner.Vehicle.RailSpec.Industrial != this.Vehicle.RailSpec.Industrial || partner.Vehicle.RailSpec.Coaster != this.Vehicle.RailSpec.Coaster)) this.Disconnect(end);
    }
    public override void Destroy()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/Destroy", this.Parent);
        lock (LinkGate) { this.Disconnect(1); this.Disconnect(-1); Vehicles.TryRemove(this.Parent.ID, out _); PersistentVehicles.TryRemove(this.Parent.ObjectID,out _); InvalidateConsists(); }
        base.Destroy();
    }
    internal void ReconcileLoadedLinks(bool worldReady)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/ReconcileLoadedLinks", this.Parent);
        // Eco completes every loaded object's PostInitialize before marking
        // WorldObjectManager.Init initialized. Never use a timeout as deletion evidence.
        if(!worldReady || !this.Parent.Initialized || !this.AwaitingLoad) return;
        var reset=new HashSet<RailCouplingComponent>();
        lock(LinkGate)
        {
            foreach(var end in new[]{-1,1})
            {
                if(!this.HasPartner(end)) continue;
                var raw=this.RawPartner(end);
                if(raw!=null && !raw.Parent.IsDestroyed && !raw.Parent.Initialized) continue;
                var partner=this.Partner(end);
                var legacy=this.PartnerObjectId(end)==Guid.Empty;
                var valid=partner!=null && partner.Vehicle.RailSpec.Industrial==this.Vehicle.RailSpec.Industrial && partner.Vehicle.RailSpec.Coaster==this.Vehicle.RailSpec.Coaster;
                // Old saves cannot safely identify a distant car by a reused ID.
                // Only migrate reciprocal, physically adjacent legacy connectors.
                if(valid && legacy) valid=Vector3.Distance(this.Connector(end),partner!.Connector(this.PartnerEnd(end)))<=.75f;
                if(valid)
                {
                    var otherEnd=this.PartnerEnd(end);
                    if(legacy || this.PartnerId(end)!=partner!.Parent.ID) this.SetPartner(end,partner!.Parent.ID,otherEnd);
                    if(partner!.PartnerObjectId(otherEnd)==Guid.Empty || partner.PartnerId(otherEnd)!=this.Parent.ID)
                        partner.SetPartner(otherEnd,this.Parent.ID,end);
                }
                else
                {
                    foreach(var node in this.Group()) reset.Add(node);
                    if(partner!=null) reset.Add(partner);
                    this.Disconnect(end);
                }
            }
            if(!this.Linked) this.AwaitingLoad=false;
        }
        // Never acquire a motion lock while holding the link lock.
        foreach(var node in reset) if(!node.Parent.IsDestroyed) node.Parent.GetComponent<MinecartMotionComponent>().ResetCoupledMotion();
    }
    private void Disconnect(int end)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/Disconnect", this.Parent);
        var other = this.Partner(end);
        var otherEnd = this.PartnerEnd(end);
        this.SetPartner(end, 0, 0);
        other?.SetPartner(otherEnd, 0, 0);
        if (!this.Linked) this.AwaitingLoad = false;
        if (other != null && !other.Linked) other.AwaitingLoad = false;
    }

    public RailCouplingComponent[] Group()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/Group", this.Parent);
        var cached = Volatile.Read(ref this.consist);
        if (cached?.Revision == Volatile.Read(ref topologyRevision)) return cached.Members;
        lock (LinkGate)
        {
            var version = Volatile.Read(ref topologyRevision);
            cached = this.consist;
            if (cached?.Revision == version) return cached.Members;
            var seen = new HashSet<int>(); var result = new List<RailCouplingComponent>();
            void Visit(RailCouplingComponent node)
            {
                if (!seen.Add(node.Parent.ID)) return;
                result.Add(node);
                for (var end = -1; end <= 1; end += 2)
                    if (node.Partner(end) is { } next && next.Vehicle.RailSpec.Industrial == this.Vehicle.RailSpec.Industrial && next.Vehicle.RailSpec.Coaster == this.Vehicle.RailSpec.Coaster) Visit(next);
            }
            Visit(this);
            var members = result.ToArray();
            Volatile.Write(ref this.consist, new ConsistSnapshot(version, members));
            return members;
        }
    }
    internal int FacingRelativeTo(RailCouplingComponent member)
    {
        // Coupler endpoints carry orientation even through a tight bend, where
        // comparing two cars' world-space forward vectors would be ambiguous.
        lock (LinkGate)
        {
            var seen = new HashSet<int>();
            var pending = new Queue<(RailCouplingComponent Car, int Facing)>();
            pending.Enqueue((this, 1));
            while (pending.TryDequeue(out var node))
            {
                if (!seen.Add(node.Car.Parent.ID)) continue;
                if (node.Car == member) return node.Facing;
                for (var end = -1; end <= 1; end += 2)
                    if (node.Car.Partner(end) is { } next)
                        pending.Enqueue((next, node.Facing * -end * node.Car.PartnerEnd(end)));
            }
            return 0; // The touched car was disconnected before the shove.
        }
    }
    private static RailCouplingComponent SelectLeader(RailCouplingComponent[] group)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/SelectLeader");
        var best=group[0];
        foreach(var candidate in group)
        {
            var occupied=candidate.Parent.GetComponent<MountComponent>().Driver!=null||candidate.Parent.GetComponent<MinecartMotionComponent>()?.ManualCart?.Holder!=null;
            var bestOccupied=best.Parent.GetComponent<MountComponent>().Driver!=null||best.Parent.GetComponent<MinecartMotionComponent>()?.ManualCart?.Holder!=null;
            var manual=candidate.Parent.GetComponent<MinecartMotionComponent>()?.ManualCart!=null;
            var bestManual=best.Parent.GetComponent<MinecartMotionComponent>()?.ManualCart!=null;
            if(occupied&&!bestOccupied || occupied==bestOccupied
                && (candidate.Vehicle.DriverPriority>best.Vehicle.DriverPriority
                    || candidate.Vehicle.DriverPriority==best.Vehicle.DriverPriority &&
                        (manual&&!bestManual||manual==bestManual&&candidate.Parent.ID<best.Parent.ID))) best=candidate;
        }
        return best;
    }
    public RailCouplingComponent Leader()
    {
        using var scope = RailProfile.Measure("Vehicle Simulation/Coupling and consists/Leader", this.Parent);
        if (RailSimulationFrame.Get<RailCouplingComponent>(leaderCacheKey) is { } cached) return cached;
        var leader = SelectLeader(this.Group()); RailSimulationFrame.Set(leaderCacheKey, leader); return leader;
    }
    public bool IsFollower => this.Linked && this.Leader() != this;
    public bool CanBoard => !this.Group().Any(x => x.AwaitingLoad || x != this && (x.Parent.GetComponent<MountComponent>().Driver != null || x.Parent.GetComponent<MinecartMotionComponent>()?.ManualCart?.Holder!=null));
    internal static bool OccupiesSwitch(VoxelRail rail,float radius,HashSet<int>? exclude=null){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/OccupiesSwitch"); return Vehicles.Values.Any(x=>!x.Parent.IsDestroyed
        && (exclude==null || !exclude.Contains(x.Parent.ID))
        && Math.Abs(x.Parent.Position.Y-rail.Point(0).Y)<2
        && Math.Abs(x.Parent.Position.X-rail.Cell.X)<radius+x.Vehicle.CouplerOffset+.4f
        && Math.Abs(x.Parent.Position.Z-rail.Cell.Z)<radius+x.Vehicle.CouplerOffset+.4f); }
    internal TrainLoad Load { get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/RailCouplingComponent.Load.get", this.Parent); return new(this.Vehicle.RailSpec,
        this.Parent.GetComponent<Eco.Gameplay.Components.Storage.PublicStorageComponent>().Inventory.NonEmptyStacks.Sum(s => (double)s.Weight) / 1000,
        FuelMass(this.Parent),
        this.Parent.GetComponent<MountComponent>().MountedPlayers.Count()); } }
    private static double FuelMass(WorldObject obj)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/FuelMass");
        var fuel = obj.GetComponent<Eco.Gameplay.Components.Storage.FuelSupplyComponent>();
        if (fuel == null) return 0;
        var stored = fuel.Inventory.NonEmptyStacks.Sum(s => (double)s.Weight) / 1000;
        return stored + (fuel.CurrentFuel?.Weight ?? 0) / 1000d * Math.Clamp(fuel.Energy / Math.Max(1,fuel.PeakEnergy),0,1);
    }
    internal TrainPerformance Performance
    {
        get
        { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/RailCouplingComponent.Performance.get", this.Parent);
            if (RailSimulationFrame.Get<TrainPerformance>(this) is { } cached) return cached;
            var group=this.Group();
            var loads=new TrainLoad[group.Length];
            for(var i=0;i<group.Length;i++) loads[i]=group[i].Load;
            var performance = new TrainPerformance(loads,this.Leader().Vehicle.RailSpec);
            RailSimulationFrame.Set(this, performance); return performance;
        }
    }
    internal double TrailingMass => this.Group().Where(x => x != this).Sum(x => x.Load.Mass);
    private readonly object gradeCacheKey = new();
    internal (double Force,float Multiplier) ChainTraction(VoxelRail rootRail,int rootFacing,double speed,float grade)
    {
        var cells=new HashSet<RailCell>();var direction=0;
        foreach(var car in Group())
        {
            var motion=car.Parent.GetComponent<MinecartMotionComponent>();
            var pose=motion.BoundConsistPose ?? (car==this?(rootRail,0f,rootFacing):((VoxelRail,float,int)?)null);
            if(pose is not {} at)continue;
            // The last axle still engages the lift after the car's centre has
            // crossed the crest. Centre-only detection released traction early
            // while that axle's slope continued to pull the consist backwards.
            foreach(var offset in new[]{-car.Vehicle.RailSpec.Wheelbase/2,0,car.Vehicle.RailSpec.Wheelbase/2})
            {
                var contact=RailGuidance.Advance(at.Item1,at.Item2,offset,motion.NextRail,out var orientation);
                if(!contact.Rail.Profile.Chain||contact.Rail.Profile.Coaster!=Vehicle.RailSpec.Coaster||!TrackWorld.Contains(contact.Rail))continue;
                var sign=at.Item3*orientation*FacingRelativeTo(car)*rootFacing;
                if(sign==0)continue;
                if(direction==0)direction=sign;
                if(sign==direction)cells.Add(contact.Rail.Cell);
            }
        }
        if(cells.Count==0)return (0,1);
        var power=Vehicle.RailSpec.Coaster
            ?Eco.Mods.TechTree.MinecartChainDriveObject.CoasterLiftForConsist(cells,Parent.ID)
            :Eco.Mods.TechTree.MinecartChainDriveObject.LiftForConsist(cells,Parent.ID);
        return (direction*ChainLift.Force(Performance.Mass,Math.Max(0,grade*direction),speed*direction,power.Watts,ChainLift.TargetSpeed(power.Multiplier,Vehicle.RailSpec.Coaster)),power.Multiplier);
    }
    private sealed record GradeSample(float Value);
    internal float ConsistGrade(float rootGrade, int rootFacing)
    {
        if (!this.Linked) return rootGrade;
        if (RailSimulationFrame.Get<GradeSample>(gradeCacheKey) is {} cached) return cached.Value;
        var members=this.Group();var performance=this.Performance;
        var orientations=new Dictionary<RailCouplingComponent,int>();
        void Visit(RailCouplingComponent car,int orientation)
        {
            if(!orientations.TryAdd(car,orientation))return;
            for(var end=-1;end<=1;end+=2)
                if(car.Partner(end) is {} next)Visit(next,orientation*-end*car.PartnerEnd(end));
        }
        lock(LinkGate)Visit(this,1);
        double weighted=0;
        for(var i=0;i<members.Length;i++)
        {
            var car=members[i];var grade=rootGrade;
            if(car!=this && car.Parent.GetComponent<MinecartMotionComponent>() is {} motion
                && motion.BoundConsistPose is {} pose && orientations.TryGetValue(car,out var orientation))
                grade=RailGuidance.AxleTangent(pose.Rail,pose.T,motion.NextRail,car.Vehicle.RailSpec.Wheelbase/2).Y
                    *pose.Facing*orientation*rootFacing;
            weighted+=performance.Cars[i].Mass*grade;
        }
        var value=(float)(weighted/Math.Max(1,performance.Mass));
        RailSimulationFrame.Set(gradeCacheKey,new GradeSample(value));return value;
    }

    // Handles can occlude the low connector from above. Route the same modified
    // click through their explicit endpoint instead of requiring a pixel-perfect ray.
    [Interaction(InteractionTrigger.LeftClick, "Couple / uncouple", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "MinecartHandle" }, interactionDistance: 3, priority: 100,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ToggleFromHandle(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
        { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/ToggleFromHandle", this.Parent); this.Toggle(player, trigger, target); }

    // The broad coaster end target supports both shove and coupling, so it
    // cannot hide the low connector's action when viewed from above.
    [Interaction(InteractionTrigger.LeftClick, "Couple / uncouple", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "CoasterEnd" }, interactionDistance: 3, priority: 100,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ToggleFromCoasterEnd(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/ToggleFromCoasterEnd", this.Parent);
        if (this.Vehicle.RailSpec.Coaster) this.Toggle(player, trigger, target);
    }

    [Interaction(InteractionTrigger.LeftClick, "Couple / uncouple", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "RailCoupler" }, interactionDistance: 3, priority: 100,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void Toggle(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/Toggle", this.Parent);
        if ((!target.TryGetParameter("RailCoupler", out var raw) && !target.TryGetParameter("MinecartHandle", out raw)
            && !target.TryGetParameter("CoasterEnd", out raw))
            || !int.TryParse(raw?.ToString(), out var end) || Math.Abs(end) != 1
            || !this.Parent.IsAuthorized(player.User, AccessType.FullAccess) || Vector3.Distance(player.User.Position, this.Connector(end)) > 3) return;
        RailCouplingComponent[] changed;
        lock (LinkGate)
        {
            if (this.HasPartner(end))
            {
                var old = this.Partner(end);
                if (old != null && !old.Parent.IsAuthorized(player.User, AccessType.FullAccess)) return;
                changed = this.Group(); this.Disconnect(end);
            }
            else
            {
                var group = this.Group();
                if (group.Any(x => x.Parent.GetComponent<MountComponent>().Driver != null)) return;
                var candidate = Vehicles.Values.Where(x => !x.Parent.IsDestroyed && !group.Contains(x)
                    && x.Vehicle.RailSpec.Industrial == this.Vehicle.RailSpec.Industrial && x.Vehicle.RailSpec.Coaster == this.Vehicle.RailSpec.Coaster)
                    .SelectMany(x => new[] { -1, 1 }.Select(e => (Other: x, End: e, Distance: Vector3.Distance(this.Connector(end), x.Connector(e)))))
                    .Where(x => x.Distance <= .45f && !x.Other.HasPartner(x.End) && x.Other.Parent.IsAuthorized(player.User, AccessType.FullAccess)
                        && Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ) * end, x.Other.Parent.Rotation.RotateVector(Vector3.UnitZ) * x.End) < -.7f)
                    .OrderBy(x => x.Distance).FirstOrDefault();
                if (candidate.Other == null || candidate.Other.Group().Any(x => x.Parent.GetComponent<MountComponent>().Driver != null)) return;
                this.SetPartner(end, candidate.Other.Parent.ID, candidate.End);
                candidate.Other.SetPartner(candidate.End, this.Parent.ID, end);
                changed = this.Group();
            }
        }
        // Park detached cars; followers surrender their own native/server solver.
        foreach (var node in changed) node.Parent.GetComponent<MinecartMotionComponent>().ResetCoupledMotion();
    }

    internal float ContactFraction(Vector3 from, Quaternion rotation, Vector3 to, Quaternion nextRotation)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Collision/ContactFraction", this.Parent);
        var ids = this.Group().Select(x => x.Parent.ID).ToHashSet();
        return VehicleBounds.TravelFraction(new(from, rotation, this.Vehicle.ContactHalfSize), new(to, nextRotation, this.Vehicle.ContactHalfSize),
            Vehicles.Values.Where(x => !x.Parent.IsDestroyed && !ids.Contains(x.Parent.ID)).Select(x =>
                new VehicleBounds(x.Parent.Position, ToQuaternion(x.Parent), x.Vehicle.ContactHalfSize)));
    }
    internal void BreakConnections()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/BreakConnections", this.Parent);
        RailCouplingComponent[] members;
        lock (LinkGate)
        {
            members=this.Group();
            this.Disconnect(-1); this.Disconnect(1);
        }
        // Motion ticks acquire their motion gate before LinkGate. Brake setters
        // wake sleeping components, so never invoke them in the inverse order.
        foreach (var member in members)
        {
            member.Parent.GetComponent<MinecartMotionComponent>().Handbrake=true;
            member.Parent.GetComponent<MinecartMotionComponent>().CommandBrake=true;
            if(member.Parent.GetComponent<TrainControllerComponent>() is {} controller) controller.Autopilot=false;
        }
    }
    private static Quaternion ToQuaternion(WorldObject obj) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/ToQuaternion"); return new(obj.Rotation.x, obj.Rotation.y, obj.Rotation.z, obj.Rotation.w); }

    internal double LimitTrainTravel(VoxelRail rail, float parameter, double travel)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/LimitTrainTravel", this.Parent);
        var motion = this.Parent.GetComponent<MinecartMotionComponent>();
        var rootFacing = Vector3.Dot(this.Parent.Rotation.RotateVector(Vector3.UnitZ), rail.Profile.Tangent(parameter)) < 0 ? -1 : 1;
        // Track parameter direction and the body's facing are separate. Walk
        // coupler endpoints to carry the root's forward direction through cars
        // that were coupled rear-to-rear or front-to-front.
        var orientations = new Dictionary<int, int> { [this.Parent.ID] = 1 };
        void Orient(RailCouplingComponent parent)
        {
            foreach (var end in new[] { -1, 1 })
                if (parent.Partner(end) is { } child
                    && orientations.TryAdd(child.Parent.ID, orientations[parent.Parent.ID] * -end * parent.PartnerEnd(end)))
                    Orient(child);
        }
        RailCouplingComponent[] members;
        lock (LinkGate) { Orient(this); members=this.Group(); }
        var fraction = 1d;
        foreach (var node in members)
        {
            var bound=node.Parent.GetComponent<MinecartMotionComponent>().BoundConsistPose;
            var at = node == this ? (Rail: rail, T: parameter) : bound is {} pose&&TrackWorld.Contains(pose.Rail)?(pose.Rail,pose.T):TrackWorld.Capture(node.Parent.Position,
                node.Parent.Rotation.RotateVector(Vector3.UnitZ), .65f, .75f, node.Vehicle.RailSpec.Industrial, coaster: node.Vehicle.RailSpec.Coaster);
            if (at == null) continue;
            var here = at.Value;
            var facing = Vector3.Dot(node.Parent.Rotation.RotateVector(Vector3.UnitZ), here.Rail.Profile.Tangent(here.T)) < 0 ? -1 : 1;
            var memberTravel = travel * rootFacing * orientations[node.Parent.ID] * facing;
            var allowed = RailGuidance.LimitBufferTravel(here.Rail, here.T, memberTravel, motion.NextRail, node.Vehicle.CouplerOffset + .194f);
            if (Math.Abs(memberTravel) > .000001) fraction = Math.Min(fraction, Math.Abs(allowed / memberTravel));
            var next = RailGuidance.Advance(here.Rail, here.T, allowed, motion.NextRail, out var nextOrientation);
            var halfWheelbase = node.Vehicle.RailSpec.Wheelbase / 2;
            var from = RailGuidance.PosePosition(here.Rail, here.T, motion.NextRail, halfWheelbase);
            var to = RailGuidance.PosePosition(next.Rail, next.T, motion.NextRail, halfWheelbase);
            var q = motion.RailRotation(next.Rail, next.T,
                RailGuidance.AxleTangent(next.Rail, next.T, motion.NextRail, halfWheelbase) * facing * nextOrientation);
            var bufferFraction = Math.Abs(memberTravel) > .000001 ? Math.Abs(allowed / memberTravel) : 1;
            fraction = Math.Min(fraction, bufferFraction * node.ContactFraction(from, ToQuaternion(node.Parent), to, new(q.x, q.y, q.z, q.w)));
        }
        return travel * fraction;
    }

    internal bool FollowTrain(VoxelRail rail, float t, int facing, Vector3 velocity, bool restoring = false)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/FollowTrain", this.Parent);
        this.RecordMotion(rail,t,rail.Point(t),velocity);
        if(!restoring && (this.motionTrail?.RecentFlight(this.Group().Sum(c=>c.Vehicle.RailSpec.Length+.10f)) == true
            || this.Vehicle.RailSpec.Coaster && this.Group().Any(c=>c.Parent.GetComponent<MinecartMotionComponent>().AwaitingCoupledLanding)))
        { this.FollowMotionTrajectory(velocity); return true; }
        var motion = this.Parent.GetComponent<MinecartMotionComponent>();
        var poses = new List<Action>();
        var valid = true;
        var trajectoryHandled=false;
        var seen = new HashSet<int> { this.Parent.ID };
        void FollowAirborne(RailCouplingComponent parent, RailCouplingComponent child, int end, int childEnd)
        {
            this.FollowFreeBranch(parent, child, end, childEnd, velocity, groundedLeader:true);
            foreach (var nextEnd in new[] { -1, 1 })
                if (child.Partner(nextEnd) is { } next && seen.Add(next.Parent.ID))
                    FollowAirborne(child, next, nextEnd, child.PartnerEnd(nextEnd));
        }
        void Follow(RailCouplingComponent parent, VoxelRail at, float parameter, int orientation, int travelOrientation)
        {
            if(trajectoryHandled)return;
            foreach (var end in new[] { -1, 1 })
            {
                var child = parent.Partner(end);
                if (child == null || !seen.Add(child.Parent.ID)) continue;
                var childEnd = parent.PartnerEnd(end);
                var spacing = parent.Vehicle.CouplerOffset + child.Vehicle.CouplerOffset + .10f;
                var distance = end * orientation * spacing;
                var cursor=RailPathCursor.Travel(at,parameter,distance,motion.NextRail);
                if(cursor.Remaining!=0)
                {
                    if(!restoring && this.Vehicle.RailSpec.Coaster){trajectoryHandled=true;this.FollowMotionTrajectory(velocity);return;}
                    if(restoring)valid=false;
                    else FollowAirborne(parent,child,end,childEnd);
                    continue;
                }
                var nextRail=cursor.Rail;var nextT=cursor.Progress;
                var direction=orientation*cursor.Orientation;
                var travelDirection=travelOrientation*cursor.Orientation;
                var childFacing = direction * -end * childEnd;
                var speed = Vector3.Dot(velocity, RailGuidance.AxleTangent(rail,t,motion.NextRail,this.Vehicle.RailSpec.Wheelbase/2) * facing);
                // A reversed car has the opposite facing, not opposite travel.
                var childVelocity = RailGuidance.AxleTangent(nextRail, nextT, motion.NextRail, child.Vehicle.RailSpec.Wheelbase / 2) * travelDirection * speed;
                var childMotion = child.Parent.GetComponent<MinecartMotionComponent>();
                if (!restoring && childMotion.AwaitingCoupledLanding
                    && !childMotion.CanLandCoupledAt(nextRail, nextT, FreeFollowerPosition(parent, child, end, childEnd),childEnd))
                {
                    FollowAirborne(parent, child, end, childEnd);
                    continue;
                }
                if (restoring)
                {
                    valid &= childMotion.CanRestoreCoupledPose(nextRail, nextT);
                    var poseRail = nextRail;
                    poses.Add(() => childMotion.AcceptFollowerPose(Parent.ID,poseRail, nextT, childFacing, Vector3.Zero));
                }
                else childMotion.AcceptContactFollowerPose(Parent.ID,nextRail, nextT, childFacing, childVelocity,childEnd);
                Follow(child, nextRail, nextT, childFacing, travelDirection);
            }
        }
        Follow(this, rail, t, facing, facing);
        if (restoring && valid) foreach (var apply in poses) apply();
        return valid;
    }

    private void FollowFreeBranch(RailCouplingComponent parent, RailCouplingComponent child, int end, int childEnd, Vector3 velocity, bool groundedLeader=false)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/FollowFreeBranch", this.Parent);
        var forward = parent.Parent.Rotation.RotateVector(Vector3.UnitZ);
        var childForward = child.Parent.Rotation.RotateVector(Vector3.UnitZ);
        if (childForward.LengthSquared() < .001f) childForward = forward * -end * childEnd;
        var position = FreeFollowerPosition(parent, child, end, childEnd);
        var motion=child.Parent.GetComponent<MinecartMotionComponent>();
        if(groundedLeader)motion.AcceptLandingFollowerPose(Parent.ID,position,childForward,velocity,childEnd);
        else motion.AcceptFreeFollowerPose(Parent.ID,position,childForward,velocity,childEnd);
    }

    private static Vector3 FreeFollowerPosition(RailCouplingComponent parent, RailCouplingComponent child, int end, int childEnd)
    {
        var forward = parent.Parent.Rotation.RotateVector(Vector3.UnitZ);
        return parent.Connector(end) + forward * end * .10f
            - child.Parent.Rotation.RotateVector(child.Vehicle.ConnectionPoints.Coupler(childEnd));
    }

    internal void FollowFree(Vector3 velocity)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Coupling and consists/FollowFree", this.Parent);
        if(this.Vehicle.RailSpec.Coaster && this.Linked)
        {
            this.RecordMotion(null,0,this.Parent.Position,velocity);
            this.FollowMotionTrajectory(velocity);
            return;
        }
        var seen = new HashSet<int> { this.Parent.ID };
        void Visit(RailCouplingComponent parent)
        {
            foreach (var end in new[] { -1, 1 })
                if (parent.Partner(end) is { } child && seen.Add(child.Parent.ID))
                {
                    this.FollowFreeBranch(parent, child, end, parent.PartnerEnd(end), velocity);
                    Visit(child);
                }
        }
        Visit(this);
    }
}
