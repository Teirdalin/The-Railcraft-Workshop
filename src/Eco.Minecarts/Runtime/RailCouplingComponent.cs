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
public sealed class RailCouplingComponent : WorldObjectComponent
{
    private static readonly ConcurrentDictionary<int, RailCouplingComponent> Vehicles = new();
    internal static RailCouplingComponent? NearestTo(Vector3 position, float radius) =>
        Vehicles.Values.Where(c=>!c.Parent.IsDestroyed && Vector3.DistanceSquared(c.Parent.Position,position)<=radius*radius)
            .OrderBy(c=>Vector3.DistanceSquared(c.Parent.Position,position)).FirstOrDefault();
    private static readonly ConcurrentDictionary<Guid, RailCouplingComponent> PersistentVehicles = new();
    private static readonly object LinkGate = new();
    internal static RailCouplingComponent[] LiveVehicles=>Vehicles.Values.Where(c=>!c.Parent.IsDestroyed).ToArray();
    internal bool RearAvailable=>!this.HasPartner(-1);
    internal bool CoupleLoadedCart(RailCouplingComponent added)
    {
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
    private Guid PartnerObjectId(int end) => end > 0 ? this.FrontPartnerObjectId : this.RearPartnerObjectId;
    private bool HasPartner(int end) => this.PartnerObjectId(end) != Guid.Empty || this.PartnerId(end) != 0;
    private int PartnerId(int end) => end > 0 ? this.FrontPartnerId : this.RearPartnerId;
    private int PartnerEnd(int end) => end > 0 ? this.FrontPartnerEnd : this.RearPartnerEnd;
    public Vector3 Connector(int end) => this.Parent.Position + this.Parent.Rotation.RotateVector(new Vector3(0, .27f, end * this.Vehicle.CouplerOffset));
    private void SetPartner(int end, int id, int otherEnd)
    {
        if (end > 0) { this.FrontPartnerId = id; this.FrontPartnerEnd = otherEnd; }
        else { this.RearPartnerId = id; this.RearPartnerEnd = otherEnd; }
        var guid = id != 0 && Vehicles.TryGetValue(id,out var target) ? target.Parent.ObjectID : Guid.Empty;
        if(end>0) this.FrontPartnerObjectId=guid; else this.RearPartnerObjectId=guid;
        this.Parent.SetAnimatedState(end > 0 ? "CoupledFront" : "CoupledRear", id != 0);
        this.Parent.SetDirty();
    }
    private RailCouplingComponent? RawPartner(int end)
    {
        var guid=this.PartnerObjectId(end);
        return guid!=Guid.Empty ? PersistentVehicles.GetValueOrDefault(guid) : Vehicles.GetValueOrDefault(this.PartnerId(end));
    }
    private bool PointsTo(int end,RailCouplingComponent other) => Math.Abs(end)==1 &&
        (this.PartnerObjectId(end)!=Guid.Empty ? this.PartnerObjectId(end)==other.Parent.ObjectID : this.PartnerId(end)==other.Parent.ID);
    private RailCouplingComponent? Partner(int end) => this.RawPartner(end) is {} other && other!=this
        && !other.Parent.IsDestroyed && Math.Abs(this.PartnerEnd(end))==1
        && other.PointsTo(this.PartnerEnd(end),this) && other.PartnerEnd(this.PartnerEnd(end))==end ? other : null;

    public override void PostInitialize()
    {
        base.PostInitialize();
        Vehicles[this.Parent.ID] = this;
        PersistentVehicles[this.Parent.ObjectID] = this;
        this.AwaitingLoad = this.Linked;
        if (this.AwaitingLoad) this.Vehicle.SetRailGuidance(true);
        this.Parent.SetAnimatedState("VehicleCollisionsEnabled", true);
        this.Parent.SetAnimatedState("CoupledFront", this.HasPartner(1));
        this.Parent.SetAnimatedState("CoupledRear", this.HasPartner(-1));
    }
    public override void Tick()
    {
        base.Tick();
        this.ReconcileLoadedLinks(WorldObjectManager.Init.Initialized);
        if (this.AwaitingLoad) return;
        lock (LinkGate)
            foreach (var end in new[] { -1, 1 })
                if (this.Partner(end) is { } partner && (partner.Vehicle.RailSpec.Industrial != this.Vehicle.RailSpec.Industrial || partner.Vehicle.RailSpec.Coaster != this.Vehicle.RailSpec.Coaster)) this.Disconnect(end);
    }
    public override void Destroy()
    {
        lock (LinkGate) { this.Disconnect(1); this.Disconnect(-1); Vehicles.TryRemove(this.Parent.ID, out _); PersistentVehicles.TryRemove(this.Parent.ObjectID,out _); }
        base.Destroy();
    }
    internal void ReconcileLoadedLinks(bool worldReady)
    {
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
        var other = this.Partner(end);
        var otherEnd = this.PartnerEnd(end);
        this.SetPartner(end, 0, 0);
        other?.SetPartner(otherEnd, 0, 0);
        if (!this.Linked) this.AwaitingLoad = false;
        if (other != null && !other.Linked) other.AwaitingLoad = false;
    }

    public RailCouplingComponent[] Group()
    {
        var seen = new HashSet<int>(); var result = new List<RailCouplingComponent>();
        void Visit(RailCouplingComponent node)
        {
            if (!seen.Add(node.Parent.ID)) return;
            result.Add(node);
            foreach (var end in new[] { -1, 1 })
                if (node.Partner(end) is { } next && next.Vehicle.RailSpec.Industrial == this.Vehicle.RailSpec.Industrial && next.Vehicle.RailSpec.Coaster == this.Vehicle.RailSpec.Coaster) Visit(next);
        }
        Visit(this); return result.ToArray();
    }
    public RailCouplingComponent Leader() => this.Group().OrderByDescending(x => x.Parent.GetComponent<MountComponent>().Driver != null)
        .ThenByDescending(x => x.Vehicle.DriverPriority).ThenBy(x => x.Parent.ID).First();
    public bool IsFollower => this.Linked && this.Leader() != this;
    public bool CanBoard => !this.Group().Any(x => x.AwaitingLoad || x != this && x.Parent.GetComponent<MountComponent>().Driver != null);
    internal static bool OccupiesSwitch(VoxelRail rail,float radius,HashSet<int>? exclude=null)=>Vehicles.Values.Any(x=>!x.Parent.IsDestroyed
        && (exclude==null || !exclude.Contains(x.Parent.ID))
        && Math.Abs(x.Parent.Position.Y-rail.Point(0).Y)<2
        && Math.Abs(x.Parent.Position.X-rail.Cell.X)<radius+x.Vehicle.CouplerOffset+.4f
        && Math.Abs(x.Parent.Position.Z-rail.Cell.Z)<radius+x.Vehicle.CouplerOffset+.4f);
    internal TrainLoad Load => new(this.Vehicle.RailSpec,
        this.Parent.GetComponent<Eco.Gameplay.Components.Storage.PublicStorageComponent>().Inventory.NonEmptyStacks.Sum(s => (double)s.Weight) / 1000,
        FuelMass(this.Parent),
        this.Parent.GetComponent<MountComponent>().MountedPlayers.Count());
    private static double FuelMass(WorldObject obj)
    {
        var fuel = obj.GetComponent<Eco.Gameplay.Components.Storage.FuelSupplyComponent>();
        if (fuel == null) return 0;
        var stored = fuel.Inventory.NonEmptyStacks.Sum(s => (double)s.Weight) / 1000;
        return stored + (fuel.CurrentFuel?.Weight ?? 0) / 1000d * Math.Clamp(fuel.Energy / Math.Max(1,fuel.PeakEnergy),0,1);
    }
    internal TrainPerformance Performance => new(this.Group().Select(x => x.Load).ToArray(), this.Leader().Vehicle.RailSpec);
    internal double TrailingMass => this.Group().Where(x => x != this).Sum(x => x.Load.Mass);

    // Handles can occlude the low connector from above. Route the same modified
    // click through their explicit endpoint instead of requiring a pixel-perfect ray.
    [Interaction(InteractionTrigger.LeftClick, "Couple / uncouple", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "MinecartHandle" }, interactionDistance: 3, priority: 100,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ToggleFromHandle(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
        => this.Toggle(player, trigger, target);

    // The broad coaster end target supports both shove and coupling, so it
    // cannot hide the low connector's action when viewed from above.
    [Interaction(InteractionTrigger.LeftClick, "Couple / uncouple", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "CoasterEnd" }, interactionDistance: 3, priority: 100,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void ToggleFromCoasterEnd(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        if (this.Vehicle.RailSpec.Coaster) this.Toggle(player, trigger, target);
    }

    [Interaction(InteractionTrigger.LeftClick, "Couple / uncouple", modifier: InteractionModifier.Shift,
        requiredEnvVars: new[] { "RailCoupler" }, interactionDistance: 3, priority: 100,
        authRequired: AccessType.FullAccess, flags: InteractionFlags.BlocksOtherInteraction)]
    public void Toggle(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
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
        var ids = this.Group().Select(x => x.Parent.ID).ToHashSet();
        return VehicleBounds.TravelFraction(new(from, rotation, this.Vehicle.ContactHalfSize), new(to, nextRotation, this.Vehicle.ContactHalfSize),
            Vehicles.Values.Where(x => !x.Parent.IsDestroyed && !ids.Contains(x.Parent.ID)).Select(x =>
                new VehicleBounds(x.Parent.Position, ToQuaternion(x.Parent), x.Vehicle.ContactHalfSize)));
    }
    internal void BreakConnections()
    {
        lock (LinkGate)
        {
            foreach (var member in this.Group())
            {
                member.Parent.GetComponent<MinecartMotionComponent>().Handbrake = true;
                member.Parent.GetComponent<MinecartMotionComponent>().CommandBrake = true;
                if(member.Parent.GetComponent<TrainControllerComponent>() is {} controller) controller.Autopilot=false;
            }
            this.Disconnect(-1); this.Disconnect(1);
        }
    }
    private static Quaternion ToQuaternion(WorldObject obj) => new(obj.Rotation.x, obj.Rotation.y, obj.Rotation.z, obj.Rotation.w);

    internal double LimitTrainTravel(VoxelRail rail, float parameter, double travel)
    {
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
            var at = node == this ? (Rail: rail, T: parameter) : TrackWorld.Capture(node.Parent.Position,
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
        var motion = this.Parent.GetComponent<MinecartMotionComponent>();
        var poses = new List<Action>();
        var valid = true;
        var seen = new HashSet<int> { this.Parent.ID };
        void Follow(RailCouplingComponent parent, VoxelRail at, float parameter, int orientation, int travelOrientation)
        {
            foreach (var end in new[] { -1, 1 })
            {
                var child = parent.Partner(end);
                if (child == null || !seen.Add(child.Parent.ID)) continue;
                var childEnd = parent.PartnerEnd(end);
                var spacing = parent.Vehicle.CouplerOffset + child.Vehicle.CouplerOffset + .10f;
                var distance = end * orientation * spacing;
                var travelled = parameter * at.Profile.Length + distance;
                var nextRail = at;
                var direction = orientation;
                var travelDirection = travelOrientation;
                var complete = true;
                for (var crossed = 0; crossed < 16 && (travelled < 0 || travelled > nextRail.Profile.Length); crossed++)
                {
                    var exit = travelled < 0 ? 0 : 1;
                    var excess = travelled < 0 ? -travelled : travelled - nextRail.Profile.Length;
                    if (motion.NextRail(nextRail, exit) is not { } next) { complete = false; break; }
                    if (exit == next.End) { direction = -direction; travelDirection = -travelDirection; }
                    nextRail = next.Rail;
                    travelled = next.End == 0 ? excess : nextRail.Profile.Length - excess;
                }
                if (!complete || travelled < 0 || travelled > nextRail.Profile.Length)
                {
                    if (restoring) valid = false;
                    else this.FollowFreeBranch(parent, child, end, childEnd, velocity);
                    continue;
                }
                var nextT = Math.Clamp(travelled / nextRail.Profile.Length, 0, 1);
                var childFacing = direction * -end * childEnd;
                var speed = Vector3.Dot(velocity, rail.Profile.Tangent(t) * facing);
                // A reversed car has the opposite facing, not opposite travel.
                var childVelocity = RailGuidance.AxleTangent(nextRail, nextT, motion.NextRail, child.Vehicle.RailSpec.Wheelbase / 2) * travelDirection * speed;
                var childMotion = child.Parent.GetComponent<MinecartMotionComponent>();
                if (restoring)
                {
                    valid &= childMotion.CanRestoreCoupledPose(nextRail, nextT);
                    var poseRail = nextRail;
                    poses.Add(() => childMotion.AcceptCoupledPose(poseRail, nextT, childFacing, Vector3.Zero));
                }
                else childMotion.AcceptCoupledPose(nextRail, nextT, childFacing, childVelocity);
                Follow(child, nextRail, nextT, childFacing, travelDirection);
            }
        }
        Follow(this, rail, t, facing, facing);
        if (restoring && valid) foreach (var apply in poses) apply();
        return valid;
    }

    private void FollowFreeBranch(RailCouplingComponent parent, RailCouplingComponent child, int end, int childEnd, Vector3 velocity)
    {
        var forward = parent.Parent.Rotation.RotateVector(Vector3.UnitZ);
        var childForward = child.Parent.Rotation.RotateVector(Vector3.UnitZ);
        if (childForward.LengthSquared() < .001f) childForward = forward * -end * childEnd;
        var childUp = child.Parent.Rotation.RotateVector(Vector3.UnitY);
        var position = parent.Connector(end) + forward * end * .10f
            - childForward * childEnd * child.Vehicle.CouplerOffset - childUp * .27f;
        child.Parent.GetComponent<MinecartMotionComponent>().AcceptCoupledFreePose(position, childForward, velocity);
    }

    internal void FollowFree(Vector3 velocity)
    {
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
