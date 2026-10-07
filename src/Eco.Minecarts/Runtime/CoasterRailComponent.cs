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
    private RailCell registeredCell;
    private VoxelRail registeredRail;
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
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Coaster sections and spatial index/PostInitialize", this.Parent); base.PostInitialize(); registeredRail=Rail; registeredCell=registeredRail.Cell; Rails[registeredCell]=this; Nearby.Add(registeredCell,this); RailSimulationFrame.Invalidate(); }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Coaster sections and spatial index/Tick", this.Parent);
        base.Tick();
        var rail=Rail; var cell=rail.Cell;
        if(rail==registeredRail) return;
        // Handles an administrative move of an otherwise stationary rail object.
        RailPowerConnectionComponent.RailRemoved(registeredCell);
        Rails.TryRemove(new KeyValuePair<RailCell,CoasterRailComponent>(registeredCell,this));
        Nearby.Remove(registeredCell,this);
        registeredRail=rail; registeredCell=cell; Rails[cell]=this; Nearby.Add(cell,this); RailSimulationFrame.Invalidate();
    }
    public override void Destroy()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Coaster sections and spatial index/Destroy", this.Parent);
        RailPowerConnectionComponent.RailRemoved(registeredCell);
        Rails.TryRemove(new KeyValuePair<RailCell,CoasterRailComponent>(registeredCell,this));
        Nearby.Remove(registeredCell,this); RailSimulationFrame.Invalidate(); base.Destroy();
    }
    internal static VoxelRail? Read(RailCell cell){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Coaster sections and spatial index/Read"); return Rails.TryGetValue(cell,out var component)&&!component.Parent.IsDestroyed?component.Rail:null; }
    internal static CoasterRailObject? ObjectAt(RailCell cell){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Coaster sections and spatial index/ObjectAt"); return Rails.TryGetValue(cell,out var component)&&!component.Parent.IsDestroyed?(CoasterRailObject)component.Parent:null; }
    internal static IEnumerable<VoxelRail> Near(Vector3 position)
    {
        // Multi-cell pieces are indexed by their saved anchor, not by the
        // occupancy cell under the cart. Include high loop and distant sockets.
        foreach(var component in Nearby.Near(position,30))
        {
            if(component.Parent.IsDestroyed)continue;
            var rail=component.Rail;
            if(Vector3.DistanceSquared(position,rail.Cell.Origin)<900)yield return rail;
        }
    }
}
