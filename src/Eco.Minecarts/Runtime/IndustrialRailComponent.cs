using System.Collections.Concurrent;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized,NoIcon]
public sealed class IndustrialRailComponent : WorldObjectComponent
{
    private static readonly ConcurrentDictionary<RailCell,IndustrialRailComponent> Index=new();
    private readonly List<RailCell> cells=[];
    private RailCell anchor;
    private VoxelTrackProfile profile;
    public override void PostInitialize()
    {
        base.PostInitialize();
        var obj=(IndustrialRailObject)Parent; var d=obj.Definition;
        anchor=new(Parent.Position3i.X,Parent.Position3i.Y,Parent.Position3i.Z);
        var f=Parent.Rotation.RotateVector(Vector3.UnitZ);
        var turns=((int)Math.Round(Math.Atan2(f.X,f.Z)/(Math.PI/2))+4)%4;
        profile=new(d.Shape,turns,d.Chain,false,true);
        var half=d.Bend ? 3 : 1;
        for(int x=-half;x<=half;x++) for(int z=d.Bend ? -half : 0;z<=(d.Bend ? half : 0);z++)
        {
            var p=Parent.Rotation.RotateVector(new Vector3(x,0,z));
            var cell=new RailCell(anchor.X+(int)Math.Round(p.X),anchor.Y,anchor.Z+(int)Math.Round(p.Z));
            Index[cell]=this; cells.Add(cell);
        }
    }
    public override void Destroy()
    {
        foreach(var cell in cells) if(Index.TryGetValue(cell,out var owner)&&owner==this) Index.TryRemove(cell,out _);
        base.Destroy();
    }
    internal static VoxelRail? Read(RailCell cell) => Index.TryGetValue(cell,out var owner)&&!owner.Parent.IsDestroyed ? new VoxelRail(owner.anchor,owner.profile) : null;
}
