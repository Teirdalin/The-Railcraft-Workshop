using System.Numerics;

namespace Eco.Minecarts.Physics;

public static class StandingCab
{
    public static (float Width,float Depth,float Floor,float Z) Layout(RailVehicleSpec spec) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/Layout"); return spec.Key=="MineTrain" ? (1.65f,1.25f,.42f,-.49f) :
        (1.65f,Math.Max(1.25f,spec.Length*.31f),spec.Length>3 ? .695f : .495f,-spec.Length*.31f); }
    public static bool Contains(RailVehicleSpec spec, Vector3 local)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Physics/Contains");
        if(!spec.Powered || !float.IsFinite(local.X) || !float.IsFinite(local.Y) || !float.IsFinite(local.Z)) return false;
        var cab=Layout(spec);
        return Math.Abs(local.X)<=cab.Width/2+.10f
            && Math.Abs(local.Z-cab.Z)<=cab.Depth/2+.10f && local.Y>=cab.Floor-.3f && local.Y<=cab.Floor+2.1f;
    }
}
