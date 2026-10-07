using System.Numerics;
using Eco.Minecarts.Track;
namespace Eco.Minecarts.Planning;

public sealed record BlueprintScanResult(CoasterBlueprintDocument Document,int Forward,int Behind,string Warning);
public static class CoasterBlueprintScanner
{
    public static BlueprintScanResult Scan(VoxelRail station,
        Func<VoxelRail,int,IEnumerable<(VoxelRail Rail,int End)>> connected,
        Func<Vector3,Vector3,IEnumerable<VoxelRail>> gapCandidates,
        Func<VoxelRail,bool,BlueprintNode?> describe)
    {
        var root=describe(station,false) ?? throw new ArgumentException("Station is not in the blueprint catalogue.");
        var found=new Dictionary<RailCell,BlueprintNode>{{station.Cell,root}};
        var front=new List<RailCell>();var back=new List<RailCell>();var warnings=new List<string>();
        void Warn(int side,string message)=>warnings.Add((side>0?"Front: ":"Behind: ")+message);
        IEnumerable<bool> Walk(int side,List<RailCell> path)
        {
            var rail=station;var exit=side>0?1:0;
            while(found.Count<CoasterBlueprintPlan.MaxPieces)
            {
                var raw=connected(rail,exit).Where(n=>n.Rail.Profile.Coaster).DistinctBy(n=>n.Rail.Cell).Take(3).ToArray();
                if(raw.Length>1){Warn(side,"stopped at an ambiguous track junction.");yield break;}
                if(raw.Length==1 && found.ContainsKey(raw[0].Rail.Cell))yield break;
                (VoxelRail Rail,int End)? next=raw.Length==1?raw[0]:null;var gap=false;
                if(next==null)
                {
                    var point=rail.Point(exit);var outward=rail.Profile.Tangent(exit)*(exit==1?1:-1);
                    var choices=new List<(VoxelRail Rail,int End,float Distance)>();
                    foreach(var candidate in gapCandidates(point,outward).Where(n=>n.Profile.Coaster&&!found.ContainsKey(n.Cell)).DistinctBy(n=>n.Cell))
                    for(var entry=0;entry<2;entry++)
                    {
                        var inward=candidate.Profile.Tangent(entry)*(entry==0?1:-1);
                        var valid=side>0?BlueprintConnections.CanBridge(point,outward,candidate.Point(entry),inward)
                            :BlueprintConnections.CanBridge(candidate.Point(entry),-inward,point,-outward);
                        if(valid)choices.Add((candidate,entry,Vector3.Distance(point,candidate.Point(entry))));
                    }
                    choices.Sort((a,b)=>a.Distance.CompareTo(b.Distance));
                    if(choices.Count>1 && choices[1].Distance-choices[0].Distance<.25f){Warn(side,"stopped at ambiguous jump landing rails.");yield break;}
                    if(choices.Count>0){next=(choices[0].Rail,choices[0].End);gap=true;}
                }
                if(next is not {} target)yield break;
                var reversed=side>0?target.End==1:target.End==0;
                var node=describe(target.Rail,reversed);
                if(node==null){Warn(side,"stopped at an unsupported or out-of-range section.");yield break;}
                if(side>0)node=node with{JumpBefore=gap};
                else if(gap)found[rail.Cell]=found[rail.Cell] with{JumpBefore=true};
                found[target.Rail.Cell]=node;path.Add(target.Rail.Cell);rail=target.Rail;exit=1-target.End;
                yield return true;
            }
        }
        // Alternate successful steps so neither end can spend the whole
        // budget before the other starts. Closed circuits meet without duplicates.
        using var forwards=Walk(1,front).GetEnumerator();using var backwards=Walk(-1,back).GetEnumerator();
        var forwardActive=true;var backwardActive=true;
        while(found.Count<CoasterBlueprintPlan.MaxPieces && (forwardActive||backwardActive))
        {
            if(forwardActive)forwardActive=forwards.MoveNext();
            if(found.Count<CoasterBlueprintPlan.MaxPieces && backwardActive)backwardActive=backwards.MoveNext();
        }
        if(found.Count==CoasterBlueprintPlan.MaxPieces && (forwardActive||backwardActive))
            warnings.Add($"Reached the {CoasterBlueprintPlan.MaxPieces:N0}-piece blueprint limit; both ends were scanned.");
        back.Reverse();
        var nodes=back.Concat(new[]{station.Cell}).Concat(front).Select(c=>found[c]).ToArray();
        return new(new(){Version=2,AnchorId=root.Id,Pieces=nodes},front.Count,back.Count,string.Join(" ",warnings));
    }
}
