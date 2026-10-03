using Eco.Core.Plugins.Interfaces;
using Eco.Minecarts.Track;
using Eco.Shared.Math;
using Eco.World.Blocks;
using Eco.Shared.SharedTypes;
namespace Eco.Minecarts.Runtime;

/// <summary>Support columns are terrain blocks, not interactive objects. They remain hammer-snappable.</summary>
public sealed class RailSupportColumns:IModInit
{
    private static int subscribed;
    public static void PostInitialize()
    {if(Interlocked.Exchange(ref subscribed,1)==0)Eco.World.World.OnBlockChanged.Add(Changed);}
    public static bool TrySupport(Type type,out string tier,out string part)
    {
        var name=type.Name;tier="";part="";
        if(!name.StartsWith("RailSupport")||!name.EndsWith("Block")||name.Contains("Stacked"))return false;
        foreach(var candidate in new[]{"Wood","Iron","Steel"})if(name.StartsWith("RailSupport"+candidate))
        {tier=candidate;part=name[(11+candidate.Length)..^5];return part.StartsWith("Base")||part.StartsWith("Middle")||part.StartsWith("Top");}
        return false;
    }
    public static int Reach(string tier)=>tier switch{"Wood"=>6,"Iron"=>12,"Steel"=>18,_=>0};
    public static int GroundedReach(Vector3i top,Func<Vector3i,Type?> read)
    {
        if(read(top) is not {} type||!TrySupport(type,out var tier,out var part)||!part.StartsWith("Top"))return 0;
        for(var y=top.Y-1;y>=0;y--)
        {
            var below=read(new(top.X,y,top.Z));if(below==null)return 0;
            if(!TrySupport(below,out var lowerTier,out var lowerPart))return 0;
            if(lowerTier!=tier)return 0;
            if(lowerPart.StartsWith("Middle"))continue;
            if(!lowerPart.StartsWith("Base")||y==0)return 0;
            var foundation=read(new(top.X,y-1,top.Z));
            return foundation!=null&&foundation!=typeof(EmptyBlock)&&!TrySupport(foundation,out _,out _)
                &&!VoxelTrackProfile.TryParse(foundation.Name,out _)&&foundation.IsDefined(typeof(Solid),true)?Reach(tier):0;
        }
        return 0;
    }
    private static void Changed(WrappedWorldPosition3i p)
    {Adjust(new(p.X,p.Y,p.Z));if(p.Y>0)Adjust(new(p.X,p.Y-1,p.Z));}
    private static void Adjust(Vector3i cell)
    {
        var type=Eco.World.World.GetBlock(cell)?.GetType();
        if(type==null||!TrySupport(type,out var tier,out var part)||!part.StartsWith("Top"))return;
        var suffix=part.Contains("R270")?"R270":part.Contains("R180")?"R180":part.Contains("R90")?"R90":"";
        var shape="Top";
        var rail=Eco.World.World.GetBlock(new(cell.X,cell.Y+1,cell.Z))?.GetType();
        if(rail!=null&&VoxelTrackProfile.TryParse(rail.Name,out var profile)&&!profile.Coaster)
        {
            var phase=profile.Shape.StartsWith("Slope")?profile.Shape[5..]:profile.Shape.StartsWith("RampTop")?profile.Shape[7..]:"";
            if(int.TryParse(phase,out var n)&&n is >=1 and <=4){shape="TopSlope"+n;suffix=profile.QuarterTurns==0?"":"R"+(profile.QuarterTurns*90);}
        }
        var resolved=type.Assembly.GetType("Eco.Mods.TechTree.RailSupport"+tier+shape+suffix+"Block");
        if(resolved!=null&&resolved!=type)Eco.World.World.SetBlock(resolved,cell);
    }
}
