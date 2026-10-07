using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eco.Core.Plugins.Interfaces;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Minecarts.Diagnostics;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Utils;
using Eco.World;

namespace Eco.Minecarts.Runtime;

internal static class RailProfile
{
    static readonly ConcurrentDictionary<Type, string> Labels = new();
    internal static string Label(Type type) => Labels.GetOrAdd(type, static t =>
    {
        if (Eco.Minecarts.Track.VoxelTrackProfile.TryParse(t.Name, out var rail))
        {
            var family = rail.Coaster ? "Coaster" : rail.Tram ? "Tram" : rail.Wooden ? "Wooden" : "Standard";
            if (rail.Chain) family += " chain";
            return family + " rail / " + Regex.Replace(rail.Shape, "(?<=[a-z])(?=[A-Z])", " ");
        }
        return Regex.Replace(t.Name.Replace("Object", "").Replace("Block", ""), "(?<=[a-z])(?=[A-Z])", " ");
    });
    internal static RailProfiler.Scope Measure(string system, WorldObject? entity = null) =>
        RailProfiler.Enabled ? RailProfiler.Measure(system, entity == null ? null : Label(entity.GetType())) : default;
}

// Uses Eco's authenticated RPC transport; no additional socket, HTTP endpoint,
// saved component or server configuration is required.
public sealed class RailProfilerService : IModInit
{
    static readonly object Gate = new();
    static readonly Dictionary<string, HashSet<int>> Objects = new();
    static Dictionary<string, int> blockCounts = new();
    static int lastGeneration = -1, subscribed, scanning, censusGeneration;
    static string censusStatus = "Voxel counts not scanned; Scan Counts runs one optional census.";
    static readonly ConcurrentDictionary<int, long> Viewers = new();
    static double lastCpuSeconds;
    static long lastCpuTimestamp;

    public static void PostInitialize()
    {
        if (Interlocked.Exchange(ref subscribed, 1) != 0) return;
        WorldObjectManager.WorldObjectAddedEvent.Add((obj, _) => Added(obj));
        WorldObjectManager.WorldObjectRemovedEvent.Add(Removed);
    }
    static bool Ours(WorldObject obj) => obj.GetType().Assembly == typeof(RailProfilerService).Assembly;
    static void Added(WorldObject obj)
    {
        if (!RailProfiler.Enabled || !Ours(obj)) return;
        lock (Gate)
        {
            var name = RailProfile.Label(obj.GetType());
            if (!Objects.TryGetValue(name, out var ids)) Objects[name] = ids = new();
            ids.Add(obj.ID);
        }
    }
    static void Removed(WorldObject obj)
    {
        if (!RailProfiler.Enabled || !Ours(obj)) return;
        lock (Gate) if (Objects.TryGetValue(RailProfile.Label(obj.GetType()), out var ids)) ids.Remove(obj.ID);
    }

    public static string Poll(User user, bool active, bool scanCounts)
    {
        if (user == null || !user.IsAdmin) return JsonSerializer.Serialize(new ProfileSnapshot { Error = "Rails profiling requires server administrator access." });
        var now = Environment.TickCount64;
        if (Viewers.TryGetValue(user.Id, out var previous) && active && now - previous < 750)
            return JsonSerializer.Serialize(new ProfileSnapshot { Error = "Profiling refresh is limited to once per second." });
        if (!active)
        {
            Viewers.TryRemove(user.Id, out _);
            if (!Viewers.Any(v => now - v.Value < 8000)) RailProfiler.Stop();
            return "{}";
        }
        Viewers[user.Id] = now;
        foreach (var viewer in Viewers.Where(v => now - v.Value > 10000)) Viewers.TryRemove(viewer.Key, out _);
        RailProfiler.RefreshLease();
        if (lastGeneration != RailProfiler.Generation)
        {
            lock (Gate)
            {
                Objects.Clear(); blockCounts.Clear();
                censusStatus = "Voxel counts not scanned; Scan Counts runs one optional census.";
                foreach (var obj in ServiceHolder<IWorldObjectManager>.Obj.All) if (!obj.IsDestroyed) Added(obj);
                lastGeneration = RailProfiler.Generation;
            }
        }
        if (scanCounts && Interlocked.CompareExchange(ref scanning, 1, 0) == 0)
        {
            censusGeneration = RailProfiler.Generation;
            _ = Task.Run(ScanBlocks);
        }
        using var scope = RailProfiler.Measure("Profiler/Snapshot and transport");
        var result = RailProfiler.Snapshot();
        lock (Gate)
        {
            result.Counts = Objects.ToDictionary(p => p.Key, p => p.Value.Count);
            foreach (var pair in blockCounts) result.Counts[pair.Key] = pair.Value;
            result.CensusStatus = censusStatus;
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                var cpu = process.TotalProcessorTime.TotalSeconds;
                var stamp = System.Diagnostics.Stopwatch.GetTimestamp();
                if (lastCpuTimestamp > 0 && stamp > lastCpuTimestamp)
                    result.ServerCpuPercent = Math.Clamp((cpu - lastCpuSeconds) / ((stamp - lastCpuTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency) / Environment.ProcessorCount * 100, 0, 100);
                lastCpuTimestamp = stamp; lastCpuSeconds = cpu;
                result.ServerResidentBytes = process.WorkingSet64;
            }
            catch { /* Native process counters are optional context. */ }
        }
        return JsonSerializer.Serialize(result);
    }

    static async Task ScanBlocks()
    {
        try
        {
            var counts = new Dictionary<string, int>(); int visited = 0;
            foreach (var chunk in Eco.World.World.Chunks)
            {
                if (!RailProfiler.Enabled || censusGeneration != RailProfiler.Generation) return;
                // Scope ends before the throttle await; no thread-crossing timing.
                using (RailProfiler.Measure("Profiler/Optional voxel census"))
                {
                    CountChunk(chunk, counts);
                    lock (Gate)
                    {
                        if (censusGeneration != RailProfiler.Generation) return;
                        // Publish progress at most once per 128 chunks, rather
                        // than copying all counts for every chunk in the world.
                        if (++visited % 128 == 0) { blockCounts = new(counts); censusStatus = $"Voxel census: {visited} chunks scanned (snapshot counts)."; }
                    }
                }
                await Task.Delay(2);
            }
            lock (Gate)
            {
                if (censusGeneration != RailProfiler.Generation) return;
                blockCounts = counts;
                censusStatus = $"Voxel census complete ({visited} chunks); snapshot counts. Rescan after track edits.";
            }
        }
        catch (Exception e) { lock (Gate) censusStatus = "Voxel census unavailable: " + e.GetType().Name; }
        finally { Interlocked.Exchange(ref scanning, 0); }
    }

    static void CountChunk(PersistentChunk chunk, Dictionary<string, int> counts)
    {
        var blocks = chunk.Blocks;
        if (blocks != null)
        {
            foreach (var block in blocks)
            {
                if (block == null || block.GetType().Assembly != typeof(RailProfilerService).Assembly) continue;
                var name = RailProfile.Label(block.GetType());
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
            return;
        }
        // Frozen chunks expose their already cached native compressed packet.
        // Decode a temporary snapshot instead of Thaw(), retaining Eco's memory
        // savings and avoiding any terrain/persistence mutation.
        var packed = chunk.PackIntoBson(); BSONObject? decoded = null;
        try
        {
            var packet = packed;
            if (packed.TryGetValue("compressed", out var compressed))
                packet = decoded = compressed.ByteSpanValue.ToArray().Decompress(SimpleBSON.Load);
            if (!packet.TryGetValue("b", out var types)) return;
            foreach (var id in types.UShortSpanValue)
            {
                var type = BlockManager.FromId(id);
                if (type.Assembly != typeof(RailProfilerService).Assembly) continue;
                var name = RailProfile.Label(type);
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }
        finally { decoded?.Recycle(); packed.Recycle(); }
    }
}

public static class RailProfilerRPC
{
    [RPC(AccessType.None)]
    public static string RailcraftProfile(this User target, User requester, bool active, bool scanCounts)
    {
        // The requester comes from Eco's authenticated observer conversion;
        // the target view and identity can never grant another user admin rights.
        if (target == null || requester != target)
            return JsonSerializer.Serialize(new ProfileSnapshot { Error = "Profile your own authenticated user view." });
        return RailProfilerService.Poll(requester, active, scanCounts);
    }
}
