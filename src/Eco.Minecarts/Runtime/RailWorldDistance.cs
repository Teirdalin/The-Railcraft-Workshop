using System.Numerics;

namespace Eco.Minecarts.Runtime;

// Guided vehicles can retain continuous coordinates while users and fixed
// objects have wrapped. Only horizontal axes wrap; height remains physical.
internal static class RailWorldDistance
{
    // Signed shortest displacement from second to first, before local rotation.
    internal static Vector3 Delta(Vector3 first, Vector3 second)
    {
        var delta = first - second;
        var size = Eco.World.World.VoxelSize;
        if (size.X > 0) delta.X -= MathF.Round(delta.X / size.X) * size.X;
        if (size.Z > 0) delta.Z -= MathF.Round(delta.Z / size.Z) * size.Z;
        return delta;
    }

    internal static float Squared(Vector3 first, Vector3 second) => Delta(first, second).LengthSquared();

    internal static int ShoveDirection(Vector3 player, Vector3 cart, Vector3 forward)
        => Eco.Minecarts.Track.RailGuidance.SelectShoveDirection(Delta(player, cart), Vector3.Zero, forward);

    internal static bool Within(Vector3 first, Vector3 second, float radius)
        => Squared(first, second) <= radius * radius;
}
