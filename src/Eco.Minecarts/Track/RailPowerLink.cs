namespace Eco.Minecarts.Track;

// Pure connection state; the Eco component supplies persistence and removal events.
internal sealed class RailPowerLink
{
    internal HashSet<RailCell> Powered { get; private set; } = [];
    internal Dictionary<RailCell, VoxelRail> Watched { get; private set; } = [];
    internal bool Connected { get; private set; }
    internal bool Stalled { get; private set; }

    internal bool Connect(HashSet<RailCell> powered, Dictionary<RailCell, VoxelRail> watched)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Connect");
        // A failed reconnect must not leave the previous run secretly powered.
        this.Powered = powered;
        this.Watched = watched;
        this.Connected = powered.Count > 0 && powered.All(watched.ContainsKey);
        this.Stalled = !this.Connected;
        return this.Connected;
    }
    internal bool Changed(RailCell cell, VoxelRail? current)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Changed");
        if (!this.Connected || !this.Watched.TryGetValue(cell, out var saved)) return false;
        // Tram switches conduct all physical branches. Throwing their points
        // changes the chosen route, not the installed hardware or its wiring.
        if (current is { } rail && saved.Cell == rail.Cell
            && saved.Profile with { SwitchRoute = 0 } == rail.Profile with { SwitchRoute = 0 }) return false;
        return this.Removed(cell);
    }
    internal void Disconnect() { Connected=false; Stalled=true; Powered=[]; }
    internal bool Removed(RailCell cell)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Removed");
        if (!this.Connected || !this.Watched.ContainsKey(cell)) return false;
        this.Connected = false;
        this.Stalled = true;
        this.Powered = [];
        return true;
    }
}
