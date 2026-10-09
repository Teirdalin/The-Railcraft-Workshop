using System.Numerics;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

public sealed partial class CoasterStationComponent
{
    [Serialized] public long DepartureSequence {get;set;}
    private void RememberDeparture(RailCouplingComponent train)
    {
        var forward=train.Parent.Rotation.RotateVector(Vector3.UnitZ);
        foreach(var car in train.Group().OrderByDescending(c=>Vector3.Dot(c.Parent.Position,forward)))
        {
            var motion=car.Parent.GetComponent<MinecartMotionComponent>();
            motion.CoasterHomeStation=Parent.ObjectID;
            motion.CoasterDepartureOrder=++DepartureSequence;
            motion.CoasterRecoveryFacing=Vector3.Dot(car.Parent.Rotation.RotateVector(Vector3.UnitZ),forward)<0?-1:1;
            car.Parent.SetDirty();
        }
        Parent.SetDirty();
    }

}
