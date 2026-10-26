using System.Numerics;

namespace Eco.Minecarts.Physics;

// Local X is the arm's reach. Local +Z points towards the railway behind it.
// Geometry is independent of rail routing: each physical carriage is observed,
// including reversing, stationary, manually moved and detached rolling stock.
public static class RailCrossingRules
{
    public const double LookaheadSeconds=6;
    public const long ClearDelayMilliseconds=2500;
    public static bool Hazard(int length,Vector3 center,Vector3 halfSize,Vector3 forward,Vector3 velocity)
    {
        if(!Finite(center)||!Finite(halfSize)||!Finite(forward)||!Finite(velocity))return true;
        length=Math.Clamp(length,1,10);
        // Do not let trains on a bridge or a tunnel close a road-level gate.
        if(Math.Abs(center.Y) > halfSize.Y+1.25f)return false;
        var right=new Vector3(forward.Z,0,-forward.X);
        var ex=Math.Abs(right.X)*halfSize.X+Math.Abs(forward.X)*halfSize.Z+.35f;
        var ez=Math.Abs(right.Z)*halfSize.X+Math.Abs(forward.Z)*halfSize.Z+.35f;
        double enter=0,leave=LookaheadSeconds;
        return Slab(center.X,velocity.X,-.5-ex,length+.5+ex,ref enter,ref leave)
            && Slab(center.Z,velocity.Z,-1.5-ez,6+ez,ref enter,ref leave);
    }
    private static bool Slab(double p,double v,double minimum,double maximum,ref double enter,ref double leave)
    {
        if(Math.Abs(v)<.0001)return p>=minimum&&p<=maximum;
        var a=(minimum-p)/v;var b=(maximum-p)/v;
        enter=Math.Max(enter,Math.Min(a,b));leave=Math.Min(leave,Math.Max(a,b));
        return enter<=leave;
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}

public sealed class RailCrossingClearance
{
    private long? clearSince;
    public bool Closed {get;private set;}
    public bool Update(bool hazard,long now)
    {
        if(hazard){Closed=true;clearSince=null;}
        else if(Closed)
        {
            if(clearSince==null||now<clearSince)clearSince=now;
            if(now-clearSince>=RailCrossingRules.ClearDelayMilliseconds)Closed=false;
        }
        return Closed;
    }
    public void Reset(){Closed=false;clearSince=null;}
}
