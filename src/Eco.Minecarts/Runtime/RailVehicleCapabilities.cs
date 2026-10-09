using System.Numerics;

namespace Eco.Minecarts.Runtime;

// Runtime capabilities are not save-schema types. Concrete definitions choose
// their modules; a manual cart never acquires a train controller through inheritance.
[Flags]
public enum RailVehicleCapabilities
{
    None=0, ManualHandle=1, FueledMotor=2, CableMotor=4, HumanMotor=8,
    StationRouting=16, Coaster=32, NativeGroundPhysics=64
}

public sealed record RailVehiclePoints(Vector3 FrontCoupler,Vector3 RearCoupler)
{
    public static RailVehiclePoints Standard(float offset)=>new(new(0,.27f,offset),new(0,.27f,-offset));
    public Vector3 Coupler(int end)=>end>0?FrontCoupler:RearCoupler;
}
