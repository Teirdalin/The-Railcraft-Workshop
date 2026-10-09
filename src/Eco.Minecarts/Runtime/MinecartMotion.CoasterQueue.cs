using System.Numerics;
using Eco.Minecarts.Track;

namespace Eco.Minecarts.Runtime;

public sealed partial class MinecartMotionComponent
{
    private bool coasterQueueFeed;
    private CoasterStationComponent? queueStation;
    private RailCell? queueCheckedCell;
    private long queueCheckedRevision=-1;
    private int queueDirection;
    internal void ArmStationQueue(){coasterQueueFeed=true;queueCheckedCell=null;WakeMotion();}
    private int QueueDirection()
    {
        if(!coasterQueueFeed||!RailVehicle.RailSpec.Coaster||current is not {} rail
            ||Parent.GetComponent<RailCouplingComponent>().IsFollower)return 0;
        if(queueStation==null||queueStation.Parent.IsDestroyed||queueStation.Parent.ObjectID!=CoasterHomeStation)
            queueStation=CoasterStationComponent.ForHome(CoasterHomeStation);
        if(queueStation==null)return 0;
        var target=queueStation.Parent.GetComponent<CoasterRailComponent>().Rail;
        if(rail.Cell==target.Cell){coasterQueueFeed=false;return 0;}
        if(queueCheckedCell==rail.Cell&&queueCheckedRevision==RailSimulationFrame.Revision)return queueDirection;
        queueCheckedCell=rail.Cell;queueCheckedRevision=RailSimulationFrame.Revision;queueDirection=0;
        var forward=target.Profile.Tangent(.5f);var delta=target.Point(.5f)-rail.Point(t);
        var distance=Vector3.Dot(delta,forward);
        if(distance<=0||distance>128||Vector3.Distance(delta,forward*distance)>.04f)return 0;
        var cursor=rail;var seen=new HashSet<RailCell>();
        for(var i=0;i<256;i++){
            if(!seen.Add(cursor.Cell)||!CoasterStationComponent.LevelStraight(cursor,forward))return 0;
            if(cursor.Cell==target.Cell){queueDirection=Vector3.Dot(rail.Profile.Tangent(t),forward)>0?1:-1;return queueDirection;}
            var end=Vector3.Dot(cursor.Profile.Tangent(.5f),forward)>0?1:0;
            var next=TrackWorld.NeighborForVehicle(cursor,end);if(next==null)return 0;cursor=next.Value.Rail;
        }
        return 0;
    }
    private bool QueueCanAdvance()
    {
        var direction=QueueDirection();
        return direction!=0&&current is {} rail&&!Derailed&&!pickupPending
            &&Math.Abs(Parent.GetComponent<RailCouplingComponent>().LimitTrainTravel(rail,t,direction*.10))>.02;
    }
    private bool QueueControl(out (double Force,bool Brake) input)
    {
        input=default;if(!coasterQueueFeed)return false;
        var direction=QueueDirection();if(!coasterQueueFeed)return false;
        if(direction==0){input=(0,true);return true;}
        var mass=Parent.GetComponent<RailCouplingComponent>().Performance.Mass;
        // Existing integration and collision limiting own the low-speed feed.
        input=(mass*Math.Clamp((.8-state.Speed*direction)*1.5,0,1.2)*direction,false);
        return true;
    }
}
