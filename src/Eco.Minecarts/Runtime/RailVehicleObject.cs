using System.Numerics;
using Eco.Gameplay.Objects;

namespace Eco.Minecarts.Runtime;

// Shared network ownership and rail-pose contract for carts and locomotives.
// Existing concrete saved object names remain unchanged.
public abstract class RailVehicleObject : PhysicsWorldObject
{
    public virtual Eco.Minecarts.Physics.RailVehicleSpec RailSpec => Eco.Minecarts.Physics.RailVehicleSpec.Minecart;
    public virtual float CouplerOffset => .806f;
    public virtual double RailMassKg => 280;
    public virtual int DriverPriority => 0;
    public virtual Vector3 ContactHalfSize => new(.40f, .40f, .80f);
    protected override void CreateEntity() => this.netEntity = new MinecartNetPhysicsEntity(this.GetType().Name, this);
    internal void SetRailGuidance(bool active) => ((MinecartNetPhysicsEntity)this.netEntity).SetGuided(active);
    internal void PublishRailPose(Vector3 velocity) => ((MinecartNetPhysicsEntity)this.netEntity).PublishGuidedPose(velocity);
    internal void ReleaseRailPose(Vector3 velocity) => ((MinecartNetPhysicsEntity)this.netEntity).ReleaseGuidedPose(velocity);
}
