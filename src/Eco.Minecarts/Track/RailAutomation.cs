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
    // Scheduling stays an append. Idle ticks only inspect this cached deadline;
    // the original ordered delivery path runs only when commands become due.
    private double nextDue = double.PositiveInfinity;
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
            nextDue = Math.Min(nextDue, due);
            return true;
        }
    }
    public PendingRailCommand[] TakeDue(double now)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/TakeDue");
        if (!double.IsFinite(now)) return [];
        lock (gate)
        {
            if (now < nextDue) return [];
            return TakeDueLocked(now);
        }
    }
    // The caller holds gate. Keep captured predicates in this helper: C# creates
    // their display class at method entry, even before an early idle return.
    private PendingRailCommand[] TakeDueLocked(double now)
    {
        var ready = pending.Where(p => p.Due <= now).OrderBy(p => p.Due).ThenBy(p => p.Sequence).ToArray();
        pending.RemoveAll(p => p.Due <= now);
        nextDue = double.PositiveInfinity;
        foreach (var command in pending) nextDue = Math.Min(nextDue, command.Due);
        return ready;
    }
    public void Cancel(Guid rule)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Cancel");
        lock (gate)
        {
            if (pending.RemoveAll(p => p.Rule == rule) == 0) return;
            nextDue = double.PositiveInfinity;
            foreach (var command in pending) nextDue = Math.Min(nextDue, command.Due);
        }
    }
    public void Clear()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Clear");
        lock (gate) { pending.Clear(); nextDue = double.PositiveInfinity; }
    }
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
    private Dictionary<Guid, Guid> occupants = [];
    private HashSet<Guid> trains = [];
    private bool initialized;
    public Guid[] Trains { get { lock (gate) return trains.OrderBy(x => x).ToArray(); } }
    public (Guid[] Entered, Guid[] Left) Replace(IEnumerable<(Guid Car, Guid Train)> cars)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Replace");
        var next = new Dictionary<Guid, Guid>();
        foreach (var (car, train) in cars)
            if (car != Guid.Empty && train != Guid.Empty) next[car] = train;
        lock (gate)
        {
            var nextTrains = next.Values.ToHashSet();
            // Coupling, splitting or deleting the identity car can rename an
            // already present consist. Shared car witnesses preserve the
            // physical edge; a rename is not another station passage.
            var continuingNext = new HashSet<Guid>(); var continuingOld = new HashSet<Guid>();
            foreach (var (car, train) in next)
                if (occupants.TryGetValue(car, out var previous))
                { continuingNext.Add(train); continuingOld.Add(previous); }
            var entered = initialized ? nextTrains.Except(trains).Except(continuingNext).OrderBy(x => x).ToArray() : [];
            var left = initialized ? trains.Except(nextTrains).Except(continuingOld).OrderBy(x => x).ToArray() : [];
            occupants = next; trains = nextTrains; initialized = true;
            return (entered, left);
        }
    }
}

public static class RailNetworkSearch
{
    /// <summary>A connection witness with the same bounded prefix as Connected.</summary>
    public static IReadOnlyList<T>? ConnectedPath<T>(T start, T destination,
        Func<T, IEnumerable<T>> neighbors, int limit = 4096) where T : notnull
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/ConnectedPath");
        if (limit < 1) return null;
        var previous = new Dictionary<T,T> { [start] = start };
        var pending = new Queue<T>(); pending.Enqueue(start);
        while (pending.TryDequeue(out var node))
        {
            if (EqualityComparer<T>.Default.Equals(node, destination))
            {
                var path = new List<T> { node };
                while (!EqualityComparer<T>.Default.Equals(node, start))
                { node = previous[node]; path.Add(node); }
                path.Reverse(); return path;
            }
            foreach (var neighbor in neighbors(node))
                if (previous.Count < limit && previous.TryAdd(neighbor, node)) pending.Enqueue(neighbor);
        }
        return null;
    }

    /// <summary>
    /// Match several destinations in one bounded breadth-first traversal. The
    /// accepted prefix is identical to separate Connected calls on stable
    /// topology, including destinations found before the visit limit.
    /// </summary>
    public static HashSet<T> ConnectedTargets<T>(T start, IEnumerable<T> destinations,
        Func<T, IEnumerable<T>> neighbors, int limit = 4096) where T : notnull
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/ConnectedTargets");
        var matches = new HashSet<T>();
        if (limit < 1) return matches;
        var remaining = destinations.ToHashSet();
        if (remaining.Count == 0) return matches;
        // Mark a rail when queued, so converging paths cannot retain duplicate
        // frontier entries. Count dequeues separately: the public limit still
        // bounds expanded rails, not rails discovered ahead of that prefix.
        var seen = new HashSet<T> { start }; var pending = new Queue<T>(); pending.Enqueue(start);
        var visited = 0;
        while (pending.TryDequeue(out var node))
        {
            if (++visited > limit) break;
            if (remaining.Remove(node))
            {
                matches.Add(node);
                if (remaining.Count == 0) break;
            }
            // Nodes discovered after this prefix cannot be visited within the
            // query limit. Still enumerate each expanded node's neighbors.
            foreach (var neighbor in neighbors(node)) if (seen.Count < limit && seen.Add(neighbor)) pending.Enqueue(neighbor);
        }
        return matches;
    }

    /// <summary>Topology only: nearby disconnected parallel tracks are not a network.</summary>
    public static bool Connected<T>(T start, T destination, Func<T, IEnumerable<T>> neighbors, int limit = 4096) where T : notnull
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Geometry and discovery/Connected");
        if (limit < 1) return false;
        // Mark a rail when queued, so converging paths cannot retain duplicate
        // frontier entries. Count dequeues separately: the public limit still
        // bounds expanded rails, not rails discovered ahead of that prefix.
        var seen = new HashSet<T> { start }; var pending = new Queue<T>(); pending.Enqueue(start);
        var visited = 0;
        while (pending.TryDequeue(out var node))
        {
            if (++visited > limit) return false;
            if (EqualityComparer<T>.Default.Equals(node, destination)) return true;
            // Nodes discovered after this prefix cannot be visited within the
            // query limit. Still enumerate each expanded node's neighbors.
            foreach (var neighbor in neighbors(node)) if (seen.Count < limit && seen.Add(neighbor)) pending.Enqueue(neighbor);
        }
        return false;
    }
}
