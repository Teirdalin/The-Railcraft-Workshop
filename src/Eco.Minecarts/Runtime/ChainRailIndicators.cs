using System.Numerics;
using Eco.Core.Plugins.Interfaces;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.Networking;
using Eco.Shared.Logging;

namespace Eco.Minecarts.Runtime;

// Transient network visuals only: never registered as world objects or saved.
public sealed class ChainRailIndicators : IThreadedPlugin
{
    private readonly ManualResetEventSlim stopped = new(false);
    private readonly object gate = new();
    private readonly Dictionary<Player, Dictionary<RailCell, ChainRailIndicator>> viewers = new();
    private DateTime nextWarning;
    internal const float Radius = 6;
    private const int MaxArrows = 72;
    public string GetCategory() => "Transport";
    public string GetStatus() => "Chain direction and power arrows while holding a hammer";
    public override string ToString() => "Rail Chain Indicators";

    public void Run()
    {
        while (!stopped.Wait(1000))
        {
            lock (gate)
            {
                if (stopped.IsSet) return;
                try { Refresh(); }
                catch (Exception error)
                {
                    Clear();
                    if (DateTime.UtcNow < nextWarning) continue;
                    nextWarning = DateTime.UtcNow.AddMinutes(1);
                    Log.WriteWarningLineLocStr("Chain rail indicators could not refresh: " + error.Message);
                }
            }
        }
    }

    public Task ShutdownAsync()
    {
        stopped.Set();
        lock (gate) Clear();
        return Task.CompletedTask;
    }

    private void Clear()
    {
        foreach (var arrows in viewers.Values)
            foreach (var arrow in arrows.Values) arrow.Destroy();
        viewers.Clear();
    }

    private void Refresh()
    {
        var players = UserManager.Users.Where(u => u.IsOnline).Select(u => u.Player)
            .Where(p => p != null && HoldingHammer(p)).Cast<Player>().ToHashSet();
        foreach (var player in viewers.Keys.Where(p => !players.Contains(p)).ToArray())
        {
            foreach (var arrow in viewers[player].Values) arrow.Destroy();
            viewers.Remove(player);
        }
        if (players.Count == 0) return;
        var power = MinecartChainDriveObject.ReadPowerIndicators();
        foreach (var player in players)
        {
            if (!viewers.TryGetValue(player, out var arrows)) viewers[player] = arrows = new();
            var rails = Nearby(player.User.Position).OrderBy(r => Vector3.DistanceSquared(r.Point(.5f), player.User.Position))
                .Take(MaxArrows).ToDictionary(r => r.Cell);
            foreach (var cell in arrows.Keys.Where(c => !rails.ContainsKey(c)).ToArray())
            { arrows[cell].Destroy(); arrows.Remove(cell); }
            foreach (var rail in rails.Values)
            {
                var powered = power.GetValueOrDefault(rail.Cell);
                if (arrows.TryGetValue(rail.Cell, out var existing))
                {
                    if (existing.Rail == rail && existing.Powered == powered) continue;
                    existing.Destroy(); arrows.Remove(rail.Cell);
                }
                arrows[rail.Cell] = new ChainRailIndicator(player, rail, powered);
            }
        }
    }

    internal static bool HoldingHammer(Player player) => player.User.Player == player
        && player.User.Inventory.Toolbar.SelectedItem is HammerItem;

    private static IEnumerable<VoxelRail> Nearby(Vector3 position)
    {
        var rails = new Dictionary<RailCell, VoxelRail>();
        void Add(VoxelRail rail)
        {
            if (rail.Profile.Chain && Vector3.DistanceSquared(position, rail.Point(.5f)) <= Radius * Radius)
                rails.TryAdd(rail.Cell, rail);
        }
        // Include complete-section rails whose anchor may be outside the cube.
        foreach (var rail in CoasterRailComponent.Near(position)) Add(rail);
        var x = (int)MathF.Round(position.X); var y = (int)MathF.Round(position.Y); var z = (int)MathF.Round(position.Z);
        for (var dx = -6; dx <= 6; dx++)
        for (var dy = -6; dy <= 6; dy++)
        for (var dz = -6; dz <= 6; dz++)
            if (TrackWorld.Read(new(x + dx, y + dy, z + dz)) is {} rail) Add(rail);
        return rails.Values;
    }
}

internal sealed class ChainRailIndicator : NetEntity
{
    private readonly Player viewer;
    internal VoxelRail Rail { get; }
    internal bool Powered { get; }
    internal ChainRailIndicator(Player viewer, VoxelRail rail, bool powered)
        : base(powered ? "RailChainPoweredIndicator" : "RailChainUnpoweredIndicator")
    {
        this.viewer = viewer; Rail = rail; Powered = powered;
        var z = rail.Profile.Tangent(.5f);
        var up = rail.Profile.Up(.5f);
        var x = Vector3.Normalize(Vector3.Cross(up, z)); var y = Vector3.Cross(z, x);
        var q = Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1));
        Position = rail.Point(.5f) + up * .12f;
        Rotation = new(q.X, q.Y, q.Z, q.W);
        SetActiveAndCreate();
    }
    public override bool IsRelevant(INetObjectViewer candidate) => ReferenceEquals(candidate.Client, viewer.Client)
        && ChainRailIndicators.HoldingHammer(viewer)
        && Vector3.DistanceSquared(viewer.User.Position, Position) <= 49;
    public override bool IsNotRelevant(INetObjectViewer candidate) => !IsRelevant(candidate);
    // These display objects have no client-authoritative state.
    public override void ReceiveUpdate(Eco.Shared.Serialization.BSONObject data) { }
    public override void ReceiveInitialState(Eco.Shared.Serialization.BSONObject data) { }
}
