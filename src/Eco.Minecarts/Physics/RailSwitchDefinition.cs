using Eco.Minecarts.Track;

namespace Eco.Minecarts.Physics;

public sealed record RailSwitchDefinition(string Key,string Name,float Radius,bool Left,bool Right,bool Industrial=false,bool Tram=false)
{
    public int Footprint=>(int)(2*Radius);
    public int[] Routes=>new[]{-1,0,1}.Where(Supports).ToArray();
    public bool Supports(int route)=>route==0 || route==-1&&Left || route==1&&Right;
    public VoxelTrackProfile Profile(int route,int turns=0)=>Supports(route)
        ? new("Switch",turns,false,false,Industrial,Radius,route,Tram:Tram) : throw new ArgumentOutOfRangeException(nameof(route));
    public static string RouteName(int route)=>route<0?"Left":route>0?"Right":"Forward";
    public static readonly RailSwitchDefinition[] All = new[]{("RailSwitch","Standard Rail",.5f,false,false),("WideRailSwitch","Standard Rail Wide-Turn",2.5f,false,false),("IndustrialRailSwitch","Industrial Railway",5.5f,true,false),("TramRailSwitch","Tram Rail",.5f,false,true),("TramWideRailSwitch","Tram Rail Wide-Turn",2.5f,false,true)}
        .SelectMany(size=>new[]{("Left",true,false),("Right",false,true),("ThreeWay",true,true)}.Select(kind=>new RailSwitchDefinition(size.Item1+kind.Item1,size.Item2+" "+(kind.Item1=="ThreeWay"?"Three-Way":kind.Item1)+" Switch",size.Item3,kind.Item2,kind.Item3,size.Item4,size.Item5))).ToArray();
    public static RailSwitchDefinition Find(string key)=>All.Single(x=>x.Key==key);
}
