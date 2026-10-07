using System.Numerics;
using Eco.Minecarts.Track;

namespace Eco.Minecarts.Runtime;

// Loads and world reads are scratch data for one synchronous update. Physical
// connection results may survive updates until topology changes or one second
// elapses. Thread-local ownership keeps callback caches independent.
internal static class RailSimulationFrame
{
    private static long revision;
    private static readonly object changeGate=new();
    private static readonly Queue<(long Revision,RailCell Cell)> changes=new(4096);
    private static long unknownRevision,droppedThrough;
    internal static long Revision => Volatile.Read(ref revision);
    [ThreadStatic] private static State? state;
    private sealed class State
    {
        internal int Depth;
        internal long Revision;
        internal long NeighborExpiry;
        internal readonly Dictionary<RailCell, VoxelRail?> Reads = new();
        internal readonly Dictionary<(Vector3, bool?), VoxelRail[]> Nearby = new();
        internal readonly Dictionary<(VoxelRail, int, bool, bool), (VoxelRail Rail, int End)?> Neighbors = new();
        internal readonly Dictionary<object, object> Values = new();
        internal void ClearScratch() { Reads.Clear(); Nearby.Clear(); Values.Clear(); }
        internal void Clear() { ClearScratch(); Neighbors.Clear(); }
    }
    internal static Scope Begin()
    {
        state ??= new State();
        if (state.Depth++ == 0)
        {
            state.ClearScratch();
            var current=Revision;
            var now=Environment.TickCount64;
            if(state.Revision!=current || now>=state.NeighborExpiry)
            { state.Neighbors.Clear(); state.Revision=current; state.NeighborExpiry=now+1000; }
        }
        return new Scope();
    }
    internal static void Invalidate()
    { lock(changeGate)unknownRevision=Interlocked.Increment(ref revision); }
    internal static void InvalidateCell(RailCell cell)
    {
        lock(changeGate)
        {
            var next=Interlocked.Increment(ref revision);
            if(changes.Count>=4096)droppedThrough=changes.Dequeue().Revision;
            changes.Enqueue((next,cell));
        }
    }
    // A fixed journey survives unrelated terrain edits. Unknown changes and
    // a reader that fell behind the bounded journal always require replanning.
    internal static (bool Affected,long Revision) ChangesAffect(long previous,IReadOnlySet<RailCell> watched)
    {
        lock(changeGate)
        {
            if(previous<unknownRevision || previous<droppedThrough)return (true,Revision);
            foreach(var change in changes)
                if(change.Revision>previous && watched.Contains(change.Cell))return (true,Revision);
            return (false,Revision);
        }
    }
    private static State? Current
    {
        get
        {
            if (state is not { Depth: > 0 } active) return null;
            var current = Volatile.Read(ref revision);
            if (active.Revision != current)
            { active.Clear(); active.Revision = current; active.NeighborExpiry=Environment.TickCount64+1000; }
            return active;
        }
    }
    internal static Dictionary<RailCell,VoxelRail?>? Reads => Current?.Reads;
    internal static Dictionary<(Vector3,bool?),VoxelRail[]>? Nearby => Current?.Nearby;
    internal static Dictionary<(VoxelRail,int,bool,bool),(VoxelRail Rail,int End)?>? Neighbors => Current?.Neighbors;
    internal static T? Get<T>(object key) where T : class => Current?.Values.GetValueOrDefault(key) as T;
    internal static void Set(object key, object value)
    {
        if (Current is { } active && active.Values.Count < 4096) active.Values[key] = value;
    }
    internal readonly struct Scope : IDisposable
    {
        public void Dispose()
        {
            if (state is { Depth: > 0 } active && --active.Depth == 0) active.ClearScratch();
        }
    }
}
