using System.Numerics;
namespace Eco.Minecarts.Planning;

// A gap describes layout geometry, never a promise that the current cart speed
// clears it. Broad limits bound discovery; headings avoid neighboring circuits.
public static class BlueprintConnections
{
    public const float MaximumJumpDistance=32;
    public const float MaximumJumpHeight=16;
    public static bool CanBridge(Vector3 from,Vector3 departure,Vector3 to,Vector3 arrival)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/CanBridge");
        var horizontal=new Vector3(departure.X,0,departure.Z);
        if(horizontal.LengthSquared()<.01f)return false;
        horizontal=Vector3.Normalize(horizontal);
        var delta=to-from;var distance=Vector3.Dot(delta,horizontal);
        var lateral=(new Vector3(delta.X,0,delta.Z)-distance*horizontal).Length();
        var landing=new Vector3(arrival.X,0,arrival.Z);
        return distance>.05f && distance<=MaximumJumpDistance && Math.Abs(delta.Y)<=MaximumJumpHeight && lateral<.35f
            && landing.LengthSquared()>.01f && Vector3.Dot(Vector3.Normalize(landing),horizontal)>.85f;
    }
    public static float? EstimatedLaunchSpeed(Vector3 from,Vector3 departure,Vector3 to)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/EstimatedLaunchSpeed");
        var horizontal=new Vector3(departure.X,0,departure.Z);var length=horizontal.Length();
        if(length<.01f)return null;
        var distance=Vector3.Dot(to-from,horizontal/length);
        var rise=distance*departure.Y/length-(to.Y-from.Y);
        return rise>.01f?MathF.Sqrt(9.80665f*distance*distance/(2*rise))/length:null;
    }
}
