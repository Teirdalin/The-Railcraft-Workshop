using System.Numerics;
namespace Eco.Minecarts.Track;

/// <summary>Explicit modular rail profiles. Numbered slopes rise one block per sequence.</summary>
public sealed record CoasterTerrainPath(string Key,string SourceKey,int Section,int Count,int X,int Y,int Z,CoasterPath Path)
{
    public string? NextKey { get; init; }
    public int NextHeight { get; init; }
    private static readonly Lazy<CoasterTerrainPath[]> Data=new(Build);
    private static readonly Lazy<Dictionary<string,CoasterTerrainPath>> Lookup=new(()=>All.ToDictionary(p=>p.Key));
    public static CoasterTerrainPath[] All=>Data.Value;
    public static string MenuGroup(string source)=>source.Contains("Chain")?"CoasterChain":source.Contains("Steep")||source.Contains("Grade")?"CoasterSteep":source.Contains("Slope")?"CoasterSlope":"CoasterBasic";
    public static CoasterTerrainPath Find(string key)=>Lookup.Value.TryGetValue(key,out var path)?path:throw new ArgumentException("Unknown modular coaster rail: "+key);
    public static bool TryProfile(string name,out VoxelTrackProfile profile)
    {
        profile=default;
        if(!name.StartsWith("CoasterTrack",StringComparison.Ordinal)||!name.EndsWith("Block",StringComparison.Ordinal)||name.Contains("Stacked"))return false;
        var key=name[..^5];var turns=0;
        if(key=="CoasterTrack")key="CoasterTrackStraightSection01";
        foreach(var degrees in new[]{270,180,90})
            if(key.EndsWith("R"+degrees,StringComparison.Ordinal)){turns=degrees/90;key=key[..^(degrees.ToString().Length+1)];break;}
        if(!Lookup.Value.TryGetValue(key,out var section))return false;
        profile=new(key,turns,section.Path.Chain,Coaster:true);return true;
    }
    private static CoasterTerrainPath[] Build()
    {
        var result=new List<CoasterTerrainPath>();
        string Key(string source,int phase)=>"CoasterTrack"+source[7..]+"Section"+phase.ToString("D2");
        void Add(string source,int phase,int count,Func<float,Vector3> point,Func<float,Vector3> tangent,bool chain=false,string? nextKey=null,int nextHeight=0)
        {
            var key=Key(source,phase);
            var path=CoasterPath.Section(key,t=>{
                var forward=Vector3.Normalize(tangent(t));
                var up=Vector3.Normalize(Vector3.UnitY-forward*Vector3.Dot(Vector3.UnitY,forward));
                return(point(t),up,forward);
            },chain);
            result.Add(new(key,source,phase,count,0,0,phase-1,path){
                NextKey=nextKey??(count>1?Key(source,phase%count+1):null),
                NextHeight=nextKey!=null?nextHeight:phase==count&&count>1?(source.EndsWith("Down",StringComparison.Ordinal)?-1:1):0
            });
        }
        foreach(var chain in new[]{false,true})
        {
            var prefix=chain?"CoasterChain":"Coaster";
            Add(prefix+"Straight",1,1,t=>new(0,.15f,t-.5f),t=>Vector3.UnitZ,chain);
            foreach(var count in new[]{4,2})
            foreach(var direction in new[]{1,-1})
            for(var phase=1;phase<=count;phase++)
            {
                var start=direction>0?(phase-1f)/count:1-(phase-1f)/count;
                var rise=direction/(float)count;
                var source=prefix+(count==4?"Slope":"Steep")+(direction>0?"Up":"Down");
                Add(source,phase,count,t=>new(0,.15f+start+rise*t,t-.5f),t=>new(0,rise,1),chain);
            }
            // A 45-degree grade rises one block per cell. Two eased cells on
            // either side distribute the pitch change (0, .5, 1), preserving
            // both tangent and height at each grid-face socket.
            var grade=prefix+"Grade";
            Add(grade+"UpEntry",1,2,t=>new(0,.15f+.25f*t*t,t-.5f),t=>new(0,.5f*t,1),chain,Key(grade+"UpEntry",2),0);
            Add(grade+"UpEntry",2,2,t=>new(0,.15f+.25f+.5f*t+.25f*t*t,t-.5f),t=>new(0,.5f+.5f*t,1),chain,Key(grade+"Up",1),1);
            Add(grade+"Up",1,1,t=>new(0,.15f+t,t-.5f),t=>new(0,1,1),chain,Key(grade+"Up",1),1);
            Add(grade+"UpExit",1,2,t=>new(0,.15f+t-.25f*t*t,t-.5f),t=>new(0,1-.5f*t,1),chain,Key(grade+"UpExit",2),0);
            Add(grade+"UpExit",2,2,t=>new(0,.15f+.75f+.5f*t-.25f*t*t,t-.5f),t=>new(0,.5f-.5f*t,1),chain,Key(prefix+"Straight",1),1);
            Add(grade+"DownEntry",1,2,t=>new(0,1.15f-.25f*t*t,t-.5f),t=>new(0,-.5f*t,1),chain,Key(grade+"DownEntry",2),0);
            Add(grade+"DownEntry",2,2,t=>new(0,.90f-.5f*t-.25f*t*t,t-.5f),t=>new(0,-.5f-.5f*t,1),chain,Key(grade+"Down",1),-1);
            Add(grade+"Down",1,1,t=>new(0,1.15f-t,t-.5f),t=>new(0,-1,1),chain,Key(grade+"Down",1),-1);
            Add(grade+"DownExit",1,2,t=>new(0,1.15f-t+.25f*t*t,t-.5f),t=>new(0,-1+.5f*t,1),chain,Key(grade+"DownExit",2),0);
            Add(grade+"DownExit",2,2,t=>new(0,.40f-.5f*t+.25f*t*t,t-.5f),t=>new(0,-.5f+.5f*t,1),chain,Key(prefix+"Straight",1),0);
        }
        foreach(var side in new[]{-1,1})
            Add(side<0?"CoasterSharpLeft":"CoasterSharpRight",1,1,
                t=>new(side*.5f*(1-MathF.Cos(t*MathF.PI/2)),.15f,-.5f+.5f*MathF.Sin(t*MathF.PI/2)),
                t=>new(side*MathF.Sin(t*MathF.PI/2),0,MathF.Cos(t*MathF.PI/2)));
        return result.ToArray();
    }
}
