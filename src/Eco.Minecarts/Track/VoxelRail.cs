using System.Numerics;

namespace Eco.Minecarts.Track;

public readonly record struct RailCell(int X, int Y, int Z)
{
    // Eco voxel coordinates are cell CENTRES (Vector3 -> Vector3i rounds),
    // not minimum corners. Profile.Point is bottom-centred; exported terrain
    // meshes bake exactly the same -0.5 Y conversion in BakeMesh.
    public Vector3 Origin => new(this.X, this.Y - .5f, this.Z);
}

public readonly record struct VoxelRail(RailCell Cell, VoxelTrackProfile Profile)
{
    public Vector3 Point(float t) => this.Cell.Origin + this.Profile.Point(t);
    public bool Connects(int end, VoxelRail other, int otherEnd) => this.Cell != other.Cell
        && this.Profile.Industrial == other.Profile.Industrial
        && this.Profile.Coaster == other.Profile.Coaster
        && Vector3.DistanceSquared(this.Point(end), other.Point(otherEnd)) < .0001f
        && Vector3.Dot(this.Profile.Tangent(end) * (end == 0 ? -1 : 1),
            other.Profile.Tangent(otherEnd) * (otherEnd == 0 ? -1 : 1)) < -.65f;
}
