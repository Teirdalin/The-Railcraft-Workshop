using System.Numerics;
using Eco.Minecarts.Track;
using Eco.Shared.Math;
using Eco.World.Blocks;
using Eco.Gameplay.Blocks;

namespace Eco.Minecarts.Runtime;

internal static class FlightWorld
{
    // Called in <=5 cm swept steps. Eco cells are centred at integer XYZ.
    public static float? LandingHeight(Vector3 previous, Vector3 next, System.Numerics.Quaternion rotation, RailCell? departed)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Collision and ground/LandingHeight");
        if (next.Y > previous.Y) return null;
        float? highest = null;
        foreach (var offset in new[] { Vector3.Zero, new Vector3(-.3f, 0, -.41f), new Vector3(.3f, 0, -.41f),
            new Vector3(-.3f, 0, .41f), new Vector3(.3f, 0, .41f) })
        {
            var wheel = next + Vector3.Transform(offset, rotation);
            var x = (int)MathF.Floor(wheel.X + .5f); var z = (int)MathF.Floor(wheel.Z + .5f);
            for (var y = (int)MathF.Floor(previous.Y + .5f); y >= (int)MathF.Floor(next.Y - .5f); y--)
            {
                var cell = new RailCell(x, y, z);
                var block = Eco.World.World.GetBlock(new Vector3i(x, y, z));
                if (block == null || !block.Is<Solid>() || block.IsWater()) continue;
                // Rail contact is resolved by TryBindRail at the cart's own
                // pose. A rotated corner/wheel sample is not a valid body landing
                // height and must not park a cart above a slope. This also covers
                // whole rail sections represented by WorldObjectBlock.
                if (TrackWorld.Read(cell) is { } track && track.Profile.Shape != "Stopper") continue;
                var top = SurfaceHeight(block, cell, wheel);
                if (top <= previous.Y + .001f && top >= next.Y - .001f)
                    highest = highest == null ? top : Math.Max(highest.Value, top);
            }
        }
        return highest;
    }

    public static bool BodyBlocked(Vector3 point, System.Numerics.Quaternion rotation)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Collision and ground/BodyBlocked");
        foreach (var x in new[] { -.3f, 0, .3f })
        foreach (var z in new[] { -.7f, 0, .7f })
        foreach (var y in new[] { .3f, .65f })
        {
            var sample = point + Vector3.Transform(new Vector3(x, y, z), rotation);
            var cell = new RailCell((int)MathF.Floor(sample.X + .5f),
                (int)MathF.Floor(sample.Y + .5f), (int)MathF.Floor(sample.Z + .5f));
            var block = Eco.World.World.GetBlock(new Vector3i(cell.X, cell.Y, cell.Z));
            if (block != null && block.Is<Solid>() && !block.IsWater()
                && !(TrackWorld.Read(cell) is { } track && track.Profile.Shape != "Stopper")
                && sample.Y < SurfaceHeight(block, cell, sample) - .001f) return true;
        }
        return false;
    }

    private static float SurfaceHeight(Block block, RailCell cell, Vector3 sample)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Vehicle Simulation/Collision and ground/SurfaceHeight");
        // Vanilla road/support quarter ramps must not become full-cube walls
        // during launch. Use their registered form rotation, not a material name.
        var form = BlockFormManager.GetFormForBlock(block.GetType());
        var phase = form?.FormType.Name switch { "RampA" => 0, "RampB" => 1, "RampC" => 2, "RampD" => 3, _ => -1 };
        if (phase < 0 || form == null) return cell.Y + .5f;
        var rotation = Array.IndexOf(form.BlockTypes, block.GetType());
        var direction = (rotation % 4) switch { 0 => -Vector3.UnitX, 1 => Vector3.UnitZ, 2 => Vector3.UnitX, _ => -Vector3.UnitZ };
        var progress = Math.Clamp(Vector3.Dot(sample - new Vector3(cell.X, cell.Y, cell.Z), direction) + .5f, 0, 1);
        return cell.Y - .5f + (phase + progress) * .25f;
    }
}
