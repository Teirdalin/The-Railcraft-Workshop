using System.Numerics;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Physics;
using Eco.Minecarts.Track;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

public sealed partial class CoasterStationComponent
{
    [Serialized] public long DepartureSequence {get;set;}
    private void RememberDeparture(RailCouplingComponent front,int direction)
    {
        var forward=front.Parent.Rotation.RotateVector(Vector3.UnitZ)*direction;
        // Starting at the physical endpoint gives the real consist order even
        // if the occupied simulation owner is a backwards-facing middle car.
        foreach(var car in front.Group())
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
