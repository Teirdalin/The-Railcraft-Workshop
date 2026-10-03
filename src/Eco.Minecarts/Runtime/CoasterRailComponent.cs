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
    public override void PostInitialize() { base.PostInitialize(); Rails[Rail.Cell]=this; }
    public override void Destroy() { Rails.TryRemove(Rail.Cell,out _); base.Destroy(); }
    internal static VoxelRail? Read(RailCell cell)=>Rails.TryGetValue(cell,out var component)&&!component.Parent.IsDestroyed?component.Rail:null;
    internal static IEnumerable<VoxelRail> Near(Vector3 position)
    {
        // Multi-cell pieces are indexed by their saved anchor, not by the
        // occupancy cell under the cart. Include high loop and distant sockets.
        foreach(var component in Rails.Values)
        {
            if(component.Parent.IsDestroyed)continue;
            var rail=component.Rail;
            if(Vector3.DistanceSquared(position,rail.Cell.Origin)<900)yield return rail;
        }
    }
}
