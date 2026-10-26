using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Minecarts.Physics;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized,NoIcon,AutogenClass,LocDisplayName("Railroad Crossing")]
public sealed class RailCrossingComponent:WorldObjectComponent,IPersistentData
{
    [Serialized] private int armLength=4;
    private readonly object gate=new();
    private readonly RailCrossingClearance clearance=new();
    private bool detached;
    private int publishedLength;
    private bool? publishedClosed;
    public object PersistentData {get{lock(gate)return ArmLength;}set{lock(gate)armLength=value is int length?Math.Clamp(length,1,10):4;}}
    [SyncToView,Autogen,Eco.Shared.Networking.Range(1,10),LocDisplayName("Arm length (blocks)")]
    public int ArmLength=>Math.Clamp(armLength,1,10);
    [SyncToView,Autogen,PropReadOnly] public string Status {get;private set;}="Road open";
    private bool CanConfigure(Player player)=>player!=null&&!Parent.IsDestroyed&&!detached
        &&Parent.IsAuthorized(player.User,AccessType.FullAccess)&&RailWorldDistance.Within(player.User.Position,Parent.Position,5);
    [RPC] public void SetArmLength(Player player,int value)
    {
        lock(gate)
        {
            if(!CanConfigure(player)||value<1||value>10||value==ArmLength)return;
            if(clearance.Closed){player.InfoBoxLoc($"Wait until the crossing is clear before changing the arm length.");return;}
            armLength=value;Parent.SetDirty();this.Changed(nameof(ArmLength));Evaluate(Environment.TickCount64);
        }
    }
    public override void PostInitialize()
    {
        base.PostInitialize();
        lock(gate){armLength=Math.Clamp(armLength,1,10);Evaluate(Environment.TickCount64);}
    }
    public override void Tick()
    {
        base.Tick();
        using var profile=RailProfile.Measure("Rail Network/Road crossings/Detect trains",Parent);
        lock(gate)if(!detached&&!Parent.IsDestroyed)Evaluate(Environment.TickCount64);
    }
    private void Evaluate(long now)
    {
        var right=Parent.Rotation.RotateVector(Vector3.UnitX);
        var ahead=Parent.Rotation.RotateVector(Vector3.UnitZ);
        var hazard=false;
        foreach(var coupling in RailCouplingComponent.RegisteredVehicles)
        {
            if(coupling.Parent.IsDestroyed||!coupling.Parent.Initialized)continue;
            var car=(RailVehicleObject)coupling.Parent;
            var relative=RailWorldDistance.Delta(car.Position,Parent.Position);
            // Conservative broad phase precedes component/motion reads.
            if(Math.Abs(relative.Y)>12)continue;
            var velocity=car.GetComponent<MinecartMotionComponent>()?.CurrentRailVelocity??Vector3.Zero;
            if(relative.LengthSquared()>MathF.Pow(18+car.CouplerOffset+velocity.Length()*6,2))continue;
            Vector3 Local(Vector3 v)=>new(Vector3.Dot(v,right),v.Y,Vector3.Dot(v,ahead));
            var half=car.ContactHalfSize;half.Z=Math.Max(half.Z,car.CouplerOffset);
            if(RailCrossingRules.Hazard(ArmLength,Local(relative),half,Local(car.Rotation.RotateVector(Vector3.UnitZ)),Local(velocity))
                ||ApproachingOnTrack(car,relative,right,ahead,half))
            {hazard=true;break;}
        }
        var closed=clearance.Update(hazard,now);
        if(publishedLength!=ArmLength)
        {
            for(var n=1;n<=10;n++)Parent.SetAnimatedState("CrossingLength"+n,n==ArmLength);
            publishedLength=ArmLength;
        }
        if(publishedClosed!=closed)
        {
            Parent.SetAnimatedState("CrossingPose",closed?"GateDown":"GateUp");
            Parent.SetAnimatedState("CrossingAlert",closed);
            publishedClosed=closed;
        }
        var status=hazard?"Train approaching or occupying crossing":closed?"Waiting for the last carriage to clear":"Road open";
        if(Status!=status){Status=status;this.Changed(nameof(Status));}
    }
    private bool ApproachingOnTrack(RailVehicleObject car,Vector3 relative,Vector3 right,Vector3 ahead,Vector3 half)
    {
        if(car.GetComponent<MinecartMotionComponent>()?.CrossingTrackPose is not {} pose||Math.Abs(pose.Speed)<.05)return false;
        var rail=pose.Rail;var t=pose.T;var direction=pose.Speed>0?1:-1;
        var remaining=Math.Abs(pose.Speed)*RailCrossingRules.LookaheadSeconds;
        var original=rail.Point(t);var revision=RailSimulationFrame.Revision;
        using var frame=RailSimulationFrame.Begin();
        for(var count=0;count<256&&remaining>=0;count++)
        {
            var available=(direction>0?1-t:t)*rail.Profile.Length;
            var distance=Math.Min(remaining,available);
            // Sampling each half metre also covers a bend entering the road
            // between rail ports; it does not assume a straight approach.
            var samples=Math.Max(1,(int)Math.Ceiling(distance/.5));
            for(var sample=0;sample<=samples;sample++)
            {
                var at=(float)(t+direction*(distance*sample/samples)/rail.Profile.Length);
                var body=relative+rail.Point(at)-original;
                var facing=rail.Profile.Tangent(at);
                Vector3 Local(Vector3 v)=>new(Vector3.Dot(v,right),v.Y,Vector3.Dot(v,ahead));
                if(RailCrossingRules.Hazard(ArmLength,Local(body),half,Local(facing),Vector3.Zero))return true;
            }
            if(remaining<=available||rail.Profile.Shape=="Stopper")break;
            remaining-=available;
            if(TrackWorld.Neighbor(rail,direction>0?1:0) is not {} next)break;
            rail=next.Rail;t=next.End;direction=next.End==0?1:-1;
        }
        // A concurrently edited approach is checked again on the next tick.
        return revision!=RailSimulationFrame.Revision;
    }
    public override void OnBeforeObjectMoved()
    {lock(gate){detached=true;clearance.Reset();publishedClosed=null;publishedLength=0;}base.OnBeforeObjectMoved();}
    public override void OnAfterObjectMoved()
    {base.OnAfterObjectMoved();lock(gate){detached=false;Evaluate(Environment.TickCount64);}}
    public override void Destroy()
    {lock(gate){detached=true;}base.Destroy();}
}
