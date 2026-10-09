namespace Eco.Minecarts.Track;

// One traversal contract for integration, axle sampling and coupled followers.
// Orientation carries reversed authored seams; remaining distance lets coaster
// flight distinguish an open edge from a clipped ordinary cart movement.
public readonly record struct RailPathCursor(VoxelRail Rail,float Progress,int Orientation,double Remaining,bool Valid)
{
    public static RailPathCursor Travel(VoxelRail rail,float progress,double travel,
        Func<VoxelRail,int,(VoxelRail Rail,int End)?> neighbor,int limit=16,
        Func<VoxelRail,int,bool>? enter=null)
    {
        var orientation=1;var distance=progress*rail.Profile.Length+travel;
        for(var crossed=0;crossed<limit&&(distance<0||distance>rail.Profile.Length);crossed++)
        {
            var end=distance<0?0:1;
            var excess=distance<0?-distance:distance-rail.Profile.Length;
            if(neighbor(rail,end) is not {} next)
                return new(rail,end,orientation,Math.CopySign(excess,distance),true);
            if(end==next.End)orientation=-orientation;
            rail=next.Rail;distance=next.End==0?excess:rail.Profile.Length-excess;
            if(enter!=null&&!enter(rail,orientation))
                return new(rail,(float)Math.Clamp(distance/rail.Profile.Length,0,1),orientation,0,false);
        }
        var clipped=Math.Clamp(distance,0,rail.Profile.Length);
        return new(rail,(float)(clipped/rail.Profile.Length),orientation,distance-clipped,true);
    }
}
