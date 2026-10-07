using System.Collections.Concurrent;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.Math;

namespace Eco.Minecarts.Runtime;

internal static class RailInfrastructure
{
    private static readonly ConcurrentDictionary<RailCell, WorldObject> WideTurns = new();
    private static readonly object BreakGate = new();
    private static RailCell Cell(WorldObject obj) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Support and block events/Cell"); return new(obj.Position3i.X, obj.Position3i.Y, obj.Position3i.Z); }
    internal static void Register(WorldObject obj) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Support and block events/Register"); WideTurns[Cell(obj)] = obj; RailSimulationFrame.Invalidate(); }
    internal static void Remove(WorldObject obj)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Support and block events/Remove"); RailPowerConnectionComponent.RailRemoved(Cell(obj)); WideTurns.TryRemove(new KeyValuePair<RailCell,WorldObject>(Cell(obj),obj)); RailSimulationFrame.Invalidate(); }
    internal static VoxelRail? TryRead(RailCell cell)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Support and block events/TryRead");
        if (!WideTurns.TryGetValue(cell, out var obj) || obj.IsDestroyed) return null;
        var forward = obj.Rotation.RotateVector(System.Numerics.Vector3.UnitZ);
        var turns = ((int)Math.Round(Math.Atan2(forward.X, forward.Z) / (Math.PI / 2)) + 4) % 4;
        return new VoxelRail(cell, new("WideBend", turns, false,Tram:obj is TramWideRailTurnObject));
    }
    internal static bool Supports(VoxelRail rail, double mass)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Entities/Support and block events/Supports");
        if (mass <= rail.Profile.MaximumSupportedKg) return true;
        lock (BreakGate)
        {
            if (TrackWorld.Read(rail.Cell) == rail)
            {
                var position = new Vector3i(rail.Cell.X, rail.Cell.Y, rail.Cell.Z);
                Eco.World.World.DeleteBlock(position);
                WorldObjectManager.ForceAdd(typeof(BrokenWoodenTrackObject), null!, position,
                    Quaternion.LookRotation(System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ,
                        System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, rail.Profile.QuarterTurns * MathF.PI / 2))), validatePlacement: false);
            }
        }
        return false;
    }
}
