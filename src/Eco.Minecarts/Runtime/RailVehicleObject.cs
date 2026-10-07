using System.Numerics;
using Eco.Gameplay.Objects;

namespace Eco.Minecarts.Runtime;

// Shared network ownership and rail-pose contract for carts and locomotives.
// Existing concrete saved object names remain unchanged.
[RequireComponent(typeof(RailVehicleAccessComponent))]
[RequireComponent(typeof(RailVehicleTextComponent))]
public abstract class RailVehicleObject : PhysicsWorldObject
{
    public virtual Eco.Minecarts.Physics.RailVehicleSpec RailSpec => Eco.Minecarts.Physics.RailVehicleSpec.Minecart;
    public virtual float CouplerOffset => .806f;
    public virtual double RailMassKg => this.RailSpec.EmptyKg;
    public virtual int DriverPriority => 0;
    public virtual Vector3 ContactHalfSize => new(.40f, .40f, .80f);
    internal bool ServerOnlyPhysics => !this.RailSpec.Pullable && !this.RailSpec.HumanPowered;
    protected override void CreateEntity() { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/CreateEntity"); this.netEntity = new MinecartNetPhysicsEntity(this.GetType().Name, this); }
    internal void SetRailGuidance(bool active) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/SetRailGuidance"); this.SetAnimatedState("RailGuidedPhysics",active||this.ServerOnlyPhysics); ((MinecartNetPhysicsEntity)this.netEntity).SetGuided(active); }
    internal void PublishRailPose(Vector3 velocity) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/PublishRailPose"); ((MinecartNetPhysicsEntity)this.netEntity).PublishGuidedPose(velocity); }
    internal void ReleaseRailPose(Vector3 velocity) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Pose synchronization/ReleaseRailPose"); this.SetAnimatedState("RailGuidedPhysics",this.ServerOnlyPhysics); ((MinecartNetPhysicsEntity)this.netEntity).ReleaseGuidedPose(velocity); }
}
