namespace Eco.Minecarts.Track;

[Eco.Shared.Serialization.Serialized]
public enum RailEvent { Enter, Leave, Arrive, Dispatch }
[Eco.Shared.Serialization.Serialized]
public enum RailCommand { Activate, Deactivate, Toggle }
public readonly record struct RailControlRule(Guid Id, Guid Target, RailEvent Event, RailCommand Command, double DelaySeconds);
public readonly record struct PendingRailCommand(Guid Rule, Guid Target, Guid Train, RailCommand Command, double Due, long Sequence);

/// <summary>
/// Shared delayed signal scheduler. Object GUIDs, never runtime network IDs.
/// World adapters must revalidate permissions, connectivity and device support
/// when delivering a command. Pending commands deliberately do not survive reload.
/// </summary>
public sealed class RailControlQueue
{
    public const int MaximumPending = 256;
    private readonly object gate = new();
    private readonly List<PendingRailCommand> pending = [];
    private long sequence;
    public int Count { get { lock (gate) return pending.Count; } }
    public bool Schedule(RailControlRule rule, Guid train, double now)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Schedule");
        if (rule.Id == Guid.Empty || rule.Target == Guid.Empty || train == Guid.Empty
            || rule.Event is not (RailEvent.Enter or RailEvent.Leave or RailEvent.Arrive or RailEvent.Dispatch)
            || rule.Command is not (RailCommand.Activate or RailCommand.Deactivate or RailCommand.Toggle)
            || !double.IsFinite(now) || !double.IsFinite(rule.DelaySeconds)
            || rule.DelaySeconds < 0 || rule.DelaySeconds > 86400) return false;
        lock (gate)
        {
            if (pending.Count >= MaximumPending) return false;
            var due = now + rule.DelaySeconds;
            if (!double.IsFinite(due)) return false;
            pending.Add(new(rule.Id, rule.Target, train, rule.Command, due, sequence++));
            return true;
        }
    }
    public PendingRailCommand[] TakeDue(double now)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/TakeDue");
        if (!double.IsFinite(now)) return [];
        lock (gate)
        {
            var ready = pending.Where(p => p.Due <= now).OrderBy(p => p.Due).ThenBy(p => p.Sequence).ToArray();
            pending.RemoveAll(p => p.Due <= now);
            return ready;
        }
    }
    public void Cancel(Guid rule) {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Cancel"); lock (gate) pending.RemoveAll(p => p.Rule == rule); }
    public void Clear() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Clear"); lock (gate) pending.Clear(); }
}

/// <summary>
/// Whole-consist occupancy, suitable for station/detector edges and future block
/// interlocks. Each car contributes independently; a train leaves only after its
/// last car leaves. Replace uses a complete snapshot, so despawned cars cannot
/// permanently occupy a block. First observation after load emits no events.
/// </summary>
public sealed class RailBlockOccupancy
{
    private readonly object gate = new();
    private HashSet<Guid> trains = [];
    private bool initialized;
    public Guid[] Trains { get { lock (gate) return trains.OrderBy(x => x).ToArray(); } }
    public (Guid[] Entered, Guid[] Left) Replace(IEnumerable<(Guid Car, Guid Train)> cars)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Replace");
        var next = cars.Where(c => c.Car != Guid.Empty && c.Train != Guid.Empty).Select(c => c.Train).ToHashSet();
        lock (gate)
        {
            var entered = initialized ? next.Except(trains).OrderBy(x => x).ToArray() : [];
            var left = initialized ? trains.Except(next).OrderBy(x => x).ToArray() : [];
            trains = next; initialized = true;
            return (entered, left);
        }
    }
}

public static class RailNetworkSearch
{
    /// <summary>Topology only: nearby disconnected parallel tracks are not a network.</summary>
    public static bool Connected<T>(T start, T destination, Func<T, IEnumerable<T>> neighbors, int limit = 4096) where T : notnull
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Connected");
        if (limit < 1) return false;
        var seen = new HashSet<T>(); var pending = new Queue<T>(); pending.Enqueue(start);
        while (pending.TryDequeue(out var node))
        {
            if (!seen.Add(node)) continue;
            if (seen.Count > limit) return false;
            if (EqualityComparer<T>.Default.Equals(node, destination)) return true;
            foreach (var neighbor in neighbors(node)) if (!seen.Contains(neighbor)) pending.Enqueue(neighbor);
        }
        return false;
    }
}
