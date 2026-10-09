using System.Collections.Concurrent;
using System.Diagnostics;

namespace Eco.Minecarts.Diagnostics;

// Stopwatch measures elapsed server work (including waits), not hardware CPU
// cycles. Only synchronous scopes are supported; never hold one across await/yield.
public static class RailProfiler
{
    const int Window = 30, MaxRows = 4096, MaxDepth = 64;
    static readonly ConcurrentDictionary<(string System, string Entity), Series> Rows = new();
    static readonly object SessionGate = new();
    static long leaseUntil, epoch, started;
    static int nextId;
    [ThreadStatic] static Frame[]? frames;
    [ThreadStatic] static int depth;
    public static bool Enabled => Stopwatch.GetTimestamp() < Volatile.Read(ref leaseUntil);
    public static int Generation => (int)Volatile.Read(ref epoch);

    public static void RefreshLease()
    {
        lock (SessionGate)
        {
            if (!Enabled) { Rows.Clear(); nextId = 0; started = Stopwatch.GetTimestamp(); Interlocked.Increment(ref epoch); }
            Volatile.Write(ref leaseUntil, Stopwatch.GetTimestamp() + Stopwatch.Frequency * 8);
        }
    }
    public static void Stop() => Volatile.Write(ref leaseUntil, 0);

    public static Scope Measure(string system, string? entity = null)
    {
        if (!Enabled) return default;
        var begin = Stopwatch.GetTimestamp();
        var beginAllocated = GC.GetAllocatedBytesForCurrentThread();
        var generation = Volatile.Read(ref epoch);
        frames ??= new Frame[MaxDepth];
        if (depth > 0 && frames[depth - 1].Generation != generation) depth = 0;
        if (depth == MaxDepth) return default;
        entity ??= depth > 0 ? frames[depth - 1].Row.Entity : "Shared rail systems";
        var key = (system, entity);
        if (!Rows.TryGetValue(key, out var row))
        {
            lock (SessionGate)
            {
                if (!Rows.TryGetValue(key, out row))
                {
                    if (Rows.Count >= MaxRows) return default;
                    row = new Series(nextId++, system, entity); Rows[key] = row;
                }
            }
        }
        var index = depth++;
        var startAllocated = GC.GetAllocatedBytesForCurrentThread();
        frames[index] = new Frame { Row = row, Start = Stopwatch.GetTimestamp(), Allocated = startAllocated,
            Generation = generation, Overhead = Stopwatch.GetTimestamp() - begin, OverheadAllocation = startAllocated - beginAllocated };
        return new Scope(index + 1, generation);
    }

    public readonly struct Scope(int token, long generation) : IDisposable
    {
        public void AttributeEntity(string entity)
        {
            if (token == 0 || depth != token || frames == null || frames[token - 1].Generation != generation) return;
            var old = frames[token - 1].Row;
            if (old.Entity == entity) return;
            var key = (old.System, entity);
            if (!Rows.TryGetValue(key, out var row))
            {
                lock (SessionGate)
                {
                    if (!Rows.TryGetValue(key, out row))
                    {
                        if (Rows.Count >= MaxRows) return;
                        row = new Series(nextId++, old.System, entity); Rows[key] = row;
                    }
                }
            }
            frames[token - 1].Row = row;
        }
        public void Dispose()
        {
            if (token == 0 || depth != token || frames == null || frames[token - 1].Generation != generation) return;
            var end = Stopwatch.GetTimestamp(); var allocated = GC.GetAllocatedBytesForCurrentThread();
            var frame = frames[--depth];
            var total = end - frame.Start;
            var bytes = Math.Max(0, allocated - frame.Allocated);
            frame.Row.Record(Math.Max(0, total - frame.ChildTime), Math.Max(0, bytes - frame.ChildAllocation), total);
            var overhead = Stopwatch.GetTimestamp() - end + frame.Overhead;
            frame.Row.AddOverhead(overhead);
            if (depth > 0)
            {
                frames[depth - 1].ChildTime += total + overhead;
                frames[depth - 1].ChildAllocation += bytes + frame.OverheadAllocation + (GC.GetAllocatedBytesForCurrentThread() - allocated);
            }
        }
    }

    struct Frame
    {
        public Series Row;
        public long Start, Allocated, Generation, ChildTime, ChildAllocation, Overhead, OverheadAllocation;
    }
    sealed class Series(int id, string system, string entity)
    {
        public readonly int Id = id;
        public readonly string System = system, Entity = entity;
        readonly object gate = new();
        readonly Bucket[] buckets = new Bucket[Window];
        long overhead;
        public void AddOverhead(long ticks) => Interlocked.Add(ref overhead, ticks);
        public void Record(long time, long bytes, long inclusive)
        {
            var second = Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            lock (gate)
            {
                ref var b = ref buckets[(int)(second % Window)];
                if (b.Second != second) b = new Bucket { Second = second };
                b.Time += time; b.Allocations += bytes; b.Calls++; b.PeakCall = Math.Max(b.PeakCall, inclusive);
            }
        }
        public ProfileRow Snapshot(long now, double seconds)
        {
            var completed = now - 1; long total = 0, bytes = 0, calls = 0, current = 0, peak = 0, peakCall = 0;
            var samples = new double[Window];
            lock (gate)
            {
                foreach (var b in buckets)
                {
                    if (b.Second < now - Window + 1 || b.Second > now) continue;
                    total += b.Time; bytes += b.Allocations; calls += b.Calls;
                    samples[(int)(b.Second - (now - Window + 1))] = b.Time * (1000d / Stopwatch.Frequency);
                    peak = Math.Max(peak, b.Time); peakCall = Math.Max(peakCall, b.PeakCall);
                    if (b.Second == completed) current = b.Time;
                }
            }
            var scale = 1000d / Stopwatch.Frequency;
            return new ProfileRow { Id = Id, System = System, Entity = Entity, CurrentMs = current * scale,
                AverageMs = total * scale / seconds, PeakMs = peak * scale, PeakCallMs = peakCall * scale,
                CallsPerSecond = calls / seconds, AllocatedBytesPerSecond = bytes / seconds,
                WindowMs = samples, ProfilerMs = Interlocked.Read(ref overhead) * scale / Math.Max(1, (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency) };
        }
    }
    struct Bucket { public long Second, Time, Allocations, Calls, PeakCall; }
    public static ProfileSnapshot Snapshot()
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = Math.Clamp((now - started) / (double)Stopwatch.Frequency, 1, Window - 1 + (now % Stopwatch.Frequency) / (double)Stopwatch.Frequency);
        var rows = Rows.Values.Select(x => x.Snapshot(now / Stopwatch.Frequency, seconds)).OrderByDescending(x => x.AverageMs).ToArray();
        return new ProfileSnapshot { Generation = Generation, WindowSeconds = seconds, Rows = rows,
            MeasuredMsPerSecond = rows.Sum(x => x.AverageMs), ProfilerMsPerSecond = rows.Sum(x => x.ProfilerMs) };
    }
}

// Shared wire contract. Unknown memory/wire bytes deliberately stay null.
public sealed class ProfileRow
{
    public int Id { get; set; }
    public string System { get; set; } = "";
    public string Entity { get; set; } = "";
    public double CurrentMs { get; set; }
    public double AverageMs { get; set; }
    public double PeakMs { get; set; }
    public double PeakCallMs { get; set; }
    public double CallsPerSecond { get; set; }
    public double AllocatedBytesPerSecond { get; set; }
    public double ProfilerMs { get; set; }
    public double[] WindowMs { get; set; } = [];
}
public sealed class ProfileSnapshot
{
    public int Generation { get; set; }
    public double WindowSeconds { get; set; }
    public double MeasuredMsPerSecond { get; set; }
    public double ProfilerMsPerSecond { get; set; }
    public ProfileRow[] Rows { get; set; } = [];
    public Dictionary<string, int> Counts { get; set; } = new();
    public string CensusStatus { get; set; } = "";
    public long? RetainedMemoryBytes { get; set; }
    public long? WireBytes { get; set; }
    public double? ServerCpuPercent { get; set; }
    public long? ServerResidentBytes { get; set; }
    public double? ServerGcPauseMsTotal { get; set; }
    public string? ServerModVersion {get;set;}
    public double? ServerGcPauseMsSinceLastPoll { get; set; }
    public double? ServerGcPeakPollPauseMs { get; set; }
    public int[]? ServerGcCollectionsSinceLastPoll { get; set; }
    public long? ServerThreadPoolPending { get; set; }
    public string Error { get; set; } = "";
}
