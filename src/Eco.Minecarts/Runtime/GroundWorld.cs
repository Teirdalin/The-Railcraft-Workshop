using System.Numerics;
using Eco.Gameplay.Objects;
using Eco.Minecarts.Track;
using Eco.Shared.Math;
using Eco.Shared.Networking;
using Eco.World.Blocks;

namespace Eco.Minecarts.Runtime;

internal static class GroundWorld
{
    // Conservative voxel collision. Does not drive through walls or unsupported
    // cliff edges. Custom non-rail ramp/collision shapes are not inferred as flat.
    public static Vector3? Resolve(Vector3 position, System.Numerics.Quaternion rotation, WorldObject cart)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Collision and ground/Resolve");
        var heights = new List<float>();
        var spec = (cart as RailVehicleObject)?.RailSpec ?? Eco.Minecarts.Physics.RailVehicleSpec.Minecart;
        foreach (var x in new[] { -spec.HalfGauge, spec.HalfGauge })
        foreach (var z in new[] { -spec.Wheelbase / 2, spec.Wheelbase / 2 })
        {
            var wheel = position + Vector3.Transform(new Vector3(x, 0, z), rotation);
            float? height = null;
            for (var y = (int)MathF.Floor(position.Y + .25f); y >= (int)MathF.Floor(position.Y - .45f); y--)
            {
                var cell = new Vector3i((int)MathF.Floor(wheel.X), y, (int)MathF.Floor(wheel.Z));
                var block = Eco.World.World.GetBlock(cell);
                if (block == null || !block.Is<Solid>() || block.IsWater()) continue;
                var top = y + 1f;
                if (VoxelTrackProfile.TryParse(block.GetType().Name, out var rail))
                    top = y + rail.Nearest(wheel - new Vector3(cell.x + .5f, y, cell.z + .5f)).Point.Y;
                if (top > position.Y + .26f || top < position.Y - .4f) continue;
                height = top;
                break;
            }
            if (height == null) return null;
            heights.Add(height.Value);
        }
        if (heights.Max() - heights.Min() > .26f) return null;
        position.Y = heights.Max();
        var right = Vector3.Transform(Vector3.UnitX, rotation);
        var forward = Vector3.Transform(Vector3.UnitZ, rotation);
        var extent = Vector3.Abs(right) * .40f + Vector3.Abs(forward) * .82f;
        for (var x = (int)MathF.Floor(position.X - extent.X); x <= (int)MathF.Floor(position.X + extent.X); x++)
        for (var z = (int)MathF.Floor(position.Z - extent.Z); z <= (int)MathF.Floor(position.Z + extent.Z); z++)
        for (var y = (int)MathF.Floor(position.Y + .04f); y <= (int)MathF.Floor(position.Y + .85f); y++)
        {
            var block = Eco.World.World.GetBlock(new Vector3i(x, y, z));
            if (block == null) return null;
            if (!block.Is<Solid>()) continue;
            if (VoxelTrackProfile.TryParse(block.GetType().Name, out var rail) && rail.Shape != "Stopper") continue;
            return null;
        }
        foreach (var other in NetObjectManager.Default.GetObjectsWithin(position, 2.5f).OfType<WorldObject>())
        {
            if (other == cart || other.IsDestroyed || Math.Abs(other.Position.Y - position.Y) > 1) continue;
            var local = Vector3.Transform(other.Position - position, System.Numerics.Quaternion.Inverse(rotation));
            if (Math.Abs(local.X) < .75f && Math.Abs(local.Z) < 1.2f) return null;
        }
        return position;
    }
}
