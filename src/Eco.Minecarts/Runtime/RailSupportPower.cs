using System.Numerics;
using Eco.Minecarts.Track;
using Eco.Shared.Math;
namespace Eco.Minecarts.Runtime;

// Only called by Connect. Terrain events validate the saved column afterwards.
internal static class RailSupportPower
{
    internal static bool Matches(SavedPowerSupport saved)
    {
        var type=Eco.World.World.GetBlock(new Vector3i(saved.X,saved.Y,saved.Z))?.GetType();
        return type!=null && RailSupportColumns.TrySupport(type,out var tier,out var part)
            && tier==saved.Tier && Kind(part)==saved.Part;
    }
    private static string Kind(string part)=>part.StartsWith("Base")?"Base":part.StartsWith("Middle")?"Middle":"Top";
    internal static HashSet<RailCell> Discover(Vector3 position,bool tram,Dictionary<RailCell,VoxelRail> watched,List<SavedPowerSupport> supports)
    {
        HashSet<RailCell> Run(Vector3 at,Dictionary<RailCell,VoxelRail> members)=>tram?TrackWorld.TramRun(at,members):TrackWorld.ChainRun(at,members);
        var direct=Run(position,watched);
        if(direct.Count>0)return direct;
        watched.Clear();
        var x=(int)MathF.Round(position.X);var y=(int)MathF.Floor(position.Y);var z=(int)MathF.Round(position.Z);
        for(var dx=-1;dx<=1;dx++)for(var dz=-1;dz<=1;dz++)for(var dy=-1;dy<=1;dy++)
        {
            var column=new List<SavedPowerSupport>();string tier="";
            for(var height=0;height<256;height++)
            {
                var cell=new Vector3i(x+dx,y+dy+height,z+dz);
                var type=Eco.World.World.GetBlock(cell)?.GetType();
                if(type==null || !RailSupportColumns.TrySupport(type,out var found,out var part))break;
                var kind=Kind(part);
                if(height==0) {if(kind!="Base")break;tier=found;}
                else if(found!=tier || kind=="Base")break;
                column.Add(new(){X=cell.X,Y=cell.Y,Z=cell.Z,Tier=tier,Part=kind});
                if(kind!="Top")continue;
                var members=new Dictionary<RailCell,VoxelRail>();
                var powered=Run(new Vector3(cell.X,cell.Y+1,cell.Z),members);
                if(powered.Count>0){foreach(var pair in members)watched.Add(pair.Key,pair.Value);supports.AddRange(column);return powered;}
                break;
            }
        }
        return [];
    }
}
