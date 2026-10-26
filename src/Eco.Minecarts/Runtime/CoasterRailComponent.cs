using System.Collections.Concurrent;
using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, LocDisplayName("Coaster Rail")]
public sealed class CoasterRailComponent : WorldObjectComponent
{
    private static readonly ConcurrentDictionary<RailCell, CoasterRailComponent> Rails = new();
    private static readonly RailSpatialIndex<CoasterRailComponent> Nearby = new();
    private readonly object gate = new();
    private RailCell? registeredCell;
    private VoxelRail registeredRail;
    private bool destroyed;
    internal VoxelRail Rail
    {
        get
        {
            var direction=Parent.Rotation.RotateVector(Vector3.UnitZ);
            var turns=((int)Math.Round(Math.Atan2(direction.X,direction.Z)/(Math.PI/2))+4)%4;
            var shape=((CoasterRailObject)Parent).PathKey;
            var anchor=Parent.Position+Parent.Rotation.RotateVector(((CoasterRailObject)Parent).RailOffset);
            return new(new((int)MathF.Round(anchor.X),(int)MathF.Round(anchor.Y),(int)MathF.Round(anchor.Z)),
                new(shape,turns,CoasterPath.Find(shape).Chain,Coaster:true));
        }
    }
    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Coaster sections and spatial index/PostInitialize", this.Parent);
        base.PostInitialize();
        ((CoasterRailObject)Parent).SnapOwnership.Attach();
        Register();
    }
    private void Register()
    {
        lock(gate)
        {
            if(destroyed||Parent.IsDestroyed||registeredCell!=null)return;
            registeredRail=Rail;
            var cell=RailPowerConnectionComponent.PhysicalCell(registeredRail.Cell);
            registeredCell=cell;
            Rails[cell]=this;
            Nearby.Add(cell,this);
            RailSimulationFrame.Invalidate();
        }
    }
    private void Unregister(bool destroying=false)
    {
        RailCell? removed=null;
        lock(gate)
        {
            if(destroying)destroyed=true;
            if(registeredCell is {} cell)
            {
                if(Rails.TryRemove(new KeyValuePair<RailCell,CoasterRailComponent>(cell,this)))removed=cell;
                Nearby.Remove(cell,this);
                registeredCell=null;
                RailSimulationFrame.Invalidate();
            }
        }
        // All readers see detachment before power callbacks take connection or
        // drive locks. A replaced component cannot remove its successor.
        if(removed is {} oldCell)RailPowerConnectionComponent.RailRemoved(oldCell);
    }
    public override void OnBeforeObjectMoved()
    {
        base.OnBeforeObjectMoved();
        Unregister();
        ((CoasterRailObject)Parent).SnapOwnership.Detach();
    }
    public override void OnAfterObjectMoved()
    {
        base.OnAfterObjectMoved();
        ((CoasterRailObject)Parent).SnapOwnership.Attach(create:true,checkAccess:true);
        Register();
    }
    private VoxelRail? RegisteredRail()
    {
        lock(gate)
            return !destroyed&&!Parent.IsDestroyed&&registeredCell!=null?registeredRail:null;
    }
    public override void Destroy()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Coaster sections and spatial index/Destroy", this.Parent);
        Unregister(destroying:true);
        ((CoasterRailObject)Parent).SnapOwnership.Detach(destroying:true);
        base.Destroy();
    }
    internal static VoxelRail? Read(RailCell cell)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Coaster sections and spatial index/Read");
        var physical=RailPowerConnectionComponent.PhysicalCell(cell);
        return Rails.TryGetValue(physical,out var component)&&component.RegisteredRail() is {} rail
            &&RailPowerConnectionComponent.PhysicalCell(rail.Cell)==physical?new(cell,rail.Profile):null;
    }
    internal static CoasterRailObject? ObjectAt(RailCell cell)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Coaster sections and spatial index/ObjectAt");
        var physical=RailPowerConnectionComponent.PhysicalCell(cell);
        return Rails.TryGetValue(physical,out var component)&&component.RegisteredRail() is {} rail&&RailPowerConnectionComponent.PhysicalCell(rail.Cell)==physical
            ? (CoasterRailObject)component.Parent : null;
    }
    private static VoxelRail InImage(VoxelRail rail,Vector3 position)
    {
        var size=Eco.World.World.VoxelSize;
        return new(new(rail.Cell.X+(int)Math.Round((position.X-(double)rail.Cell.X)/size.X)*size.X,rail.Cell.Y,
            rail.Cell.Z+(int)Math.Round((position.Z-(double)rail.Cell.Z)/size.Z)*size.Z),rail.Profile);
    }
    internal static IEnumerable<VoxelRail> Near(Vector3 position)
    {
        // Multi-cell pieces are indexed by their saved anchor, not by the
        // occupancy cell under the cart. Include high loop and distant sockets.
        // Store one physical anchor, but return geometry in the caller's
        // continuous world image, as terrain and switches already do.
        var size=Eco.World.World.VoxelSize;
        var query=position;
        query.X-=MathF.Floor(query.X/size.X)*size.X;
        query.Z-=MathF.Floor(query.Z/size.Z)*size.Z;
        var minX=query.X+30>size.X?-1:0;var maxX=query.X<30?1:0;
        var minZ=query.Z+30>size.Z?-1:0;var maxZ=query.Z<30?1:0;
        HashSet<RailCell>? seen=minX!=maxX||minZ!=maxZ?new():null;
        // Ordinary queries visit one neighborhood; seam queries visit only
        // adjacent images. Never enumerate all placed coaster sections.
        for(var x=minX;x<=maxX;x++)for(var z=minZ;z<=maxZ;z++)
        foreach(var component in Nearby.Near(query+new Vector3(x*size.X,0,z*size.Z),30))
        {
            if(component.RegisteredRail() is not {} registered)continue;
            var rail=InImage(registered,position);
            if(Vector3.DistanceSquared(position,rail.Cell.Origin)<900 && (seen==null||seen.Add(rail.Cell)))yield return rail;
        }
    }
}
