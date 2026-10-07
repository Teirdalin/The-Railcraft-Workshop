using System.Numerics;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Track;
using Eco.Shared.Math;
using Eco.Shared.Networking;
using Eco.World.Blocks;

namespace Eco.Minecarts.Runtime;

internal static class HandleWorld
{
    public static bool Clear(Vector3 position, WorldObject cart)
        { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Collision and ground/Clear"); return TryClear(position, cart, out _); }

    public static bool TryClear(Vector3 position, WorldObject cart, out string reason)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Collision and ground/TryClear");
        // Eco reports the standing player's network origin roughly one voxel
        // below the visible feet on some terrain (confirmed live on
        // RainforestSoilBlock). Lower-body voxel tests therefore mistake the
        // supporting ground for a wall. The native character controller owns
        // foot/terrain collision; this sweep only protects chest/head clearance.
        for (var x = (int)MathF.Floor(position.X - .25f); x <= (int)MathF.Floor(position.X + .25f); x++)
        for (var z = (int)MathF.Floor(position.Z - .25f); z <= (int)MathF.Floor(position.Z + .25f); z++)
        for (var y = (int)MathF.Floor(position.Y + 1f); y <= (int)MathF.Floor(position.Y + 1.7f); y++)
        {
            var cell = new Vector3i(x, y, z);
            var block = Eco.World.World.GetBlock(cell);
            if (block == null)
            {
                reason = $"world data was unavailable at {cell}";
                return false;
            }
            if (block.IsWater())
            {
                reason = $"water occupies the handle space at {cell}";
                return false;
            }
            // Track terrain blocks are registered Solid so hammers can place and
            // target them, but their rendered/collision geometry is only rails and
            // sleepers. Treating the whole voxel as a wall made every handle grab
            // over track release immediately as "obstructed".
            if (VoxelTrackProfile.TryParse(block.GetType().Name, out _)) continue;
            if (block.Is<Solid>())
            {
                reason = $"{block.GetType().Name} blocks the handle path at {cell}";
                return false;
            }
        }

        // Only another physics-driven object is relevant here. The old generic
        // WorldObject radius test treated nearby plants and decorations as walls.
        foreach (var other in NetObjectManager.Default.GetObjectsWithin(position, 2.5f).OfType<WorldObject>())
        {
            if (other == cart || other.IsDestroyed || other is not PhysicsWorldObject
                || Math.Abs(other.Position.Y - position.Y) > 1.8f) continue;
            var difference = other.Position - position;
            if (Math.Abs(difference.X) < .4f && Math.Abs(difference.Z) < .4f)
            {
                reason = $"{other.GetType().Name} occupies the handle space";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }
}
