namespace Eco.Minecarts.Track;

// Extensions register their own block type against the existing common rail
// representation; no extra per-block Tick, network or propulsion controller.
public static class RailTrackDefinitions
{
    public static void RegisterBlock<TBlock>(VoxelTrackProfile profile) where TBlock:Eco.World.Blocks.Block
        =>Eco.Minecarts.Runtime.TrackWorld.RegisterBlockProfile(typeof(TBlock),profile);
}
