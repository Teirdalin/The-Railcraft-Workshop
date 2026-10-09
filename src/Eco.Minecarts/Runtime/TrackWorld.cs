using System.Numerics;
using Eco.Minecarts.Track;
using Eco.Shared.Math;

namespace Eco.Minecarts.Runtime;

internal static class TrackWorld
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type,VoxelTrackProfile?> BlockProfiles = new();
    internal static void RegisterBlockProfile(Type block,VoxelTrackProfile profile)
    {BlockProfiles[block]=profile;RailSimulationFrame.Invalidate();}
    private readonly record struct RailRead(long Revision,VoxelRail? Rail);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<RailCell,RailRead> SharedReads=new();
    // Network membership includes every physical switch branch, independently
    // of its current route. Actual vehicle traversal still follows switch state.
    internal static IEnumerable<RailCell> NetworkNeighbors(RailCell cell)
    {
        if(Read(cell) is not {} seed) yield break;
        var seen=new HashSet<RailCell>();
        foreach(var path in NetworkPaths(seed))
        for(var end=0;end<2;end++)
        foreach(var nearby in Near(path.Point(end)))
        foreach(var candidate in NetworkPaths(nearby))
        for(var otherEnd=0;otherEnd<2;otherEnd++)
            if(path.Connects(end,candidate,otherEnd) && seen.Add(candidate.Cell)) yield return candidate.Cell;
    }
    internal static IEnumerable<VoxelRail> NetworkPaths(VoxelRail rail)=>RailSwitchComponent.At(RailPowerConnectionComponent.PhysicalCell(rail.Cell)) is {} points
        ? points.Definition.Routes.Select(route=>new VoxelRail(rail.Cell,points.Definition.Profile(route,rail.Profile.QuarterTurns)))
        : rail.Profile.Shape=="Crossing" ? new[]{rail,new VoxelRail(rail.Cell,rail.Profile with{QuarterTurns=(rail.Profile.QuarterTurns+1)%4})}
        : new[]{rail};
    public static VoxelRail? Read(RailCell cell)
    {
        RailPowerConnectionObserver.EnsureWorldEvents();
        var cache = RailSimulationFrame.Reads;
        if (cache != null && cache.TryGetValue(cell, out var cached)) return cached;
        var revision=RailSimulationFrame.Revision;
        VoxelRail? result;
        if(SharedReads.TryGetValue(cell,out var shared)&&shared.Revision==revision)result=shared.Rail;
        else
        {
            result=ReadUncached(cell);
            // Bound both positive and negative queries. Existing block/object/
            // switch change events advance Revision and invalidate all readers.
            if(SharedReads.Count>=8192)SharedReads.Clear();
            SharedReads[cell]=new(revision,result);
        }
        if (cache != null && cache.Count < 4096) cache[cell] = result;
        return result;
    }
    private static VoxelRail? ReadUncached(RailCell cell)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Track lookup and connectivity/Read");
        if(CoasterRailComponent.Read(cell) is {} coaster) return coaster;
        if(MinecartDumpRailComponent.Read(cell) is {} dump) return dump;
        if(RailSwitchComponent.Read(cell) is {} points) return points;
#if INDUSTRIAL_TRACKS
        if (IndustrialRailComponent.Read(cell) is { } industrial) return industrial;
#endif
        if (RailInfrastructure.TryRead(cell) is { } infrastructure) return infrastructure;
        var block = Eco.World.World.GetBlock(new Vector3i(cell.X, cell.Y, cell.Z));
        if(block==null) return null;
        var profile=BlockProfiles.GetOrAdd(block.GetType(),static type=>VoxelTrackProfile.TryParse(type.Name,out var parsed)?parsed:null);
        if (Eco.Minecarts.Diagnostics.RailProfiler.Enabled)
            _railProfileScope.AttributeEntity(profile.HasValue ? RailProfile.Label(block.GetType()) : "Empty or non-rail cell queries");
        return profile is {} parsed ? new VoxelRail(cell,parsed) : null;
    }

    public static bool Contains(VoxelRail rail)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Track lookup and connectivity/Contains");
        if(Read(rail.Cell) is not { } current) return false;
        return current==rail || current.Profile.Shape=="Crossing" && rail.Profile.Shape=="Crossing"
            && rail.Profile.Tram==current.Profile.Tram && rail.Profile.QuarterTurns==(current.Profile.QuarterTurns+1)%4;
    }

    public static IEnumerable<VoxelRail> Near(Vector3 position, bool? coaster = null)
    {
        var cache = RailSimulationFrame.Nearby;
        if (cache == null) return NearUncached(position, coaster);
        var key = (position, coaster);
        if (cache.TryGetValue(key, out var cached)) return cached;
        // Keep retained scratch capacity bounded even on a long route.
        if (cache.Count >= 256) return NearUncached(position, coaster);
        var result = NearUncached(position, coaster).ToArray();
        cache[key] = result;
        return result;
    }
    private static IEnumerable<VoxelRail> NearUncached(Vector3 position, bool? coaster)
    {
        var seen = new HashSet<RailCell>();
        // Standard rolling stock cannot use complete coaster sections.
        if (coaster != false)
            foreach(var rail in CoasterRailComponent.Near(position))
                if(seen.Add(rail.Cell)) yield return rail;
        var x = (int)MathF.Floor(position.X + .5f);
        var y = (int)MathF.Floor(position.Y + .5f);
        var z = (int)MathF.Floor(position.Z + .5f);
        for (var dx = -2; dx <= 2; dx++)
        for (var dy = -2; dy <= 2; dy++)
        for (var dz = -2; dz <= 2; dz++)
            if (Read(new(x + dx, y + dy, z + dz)) is { } rail && (coaster == null || rail.Profile.Coaster == coaster) && seen.Add(rail.Cell))
            {
                yield return rail;
                if(rail.Profile.Shape=="Crossing")yield return new VoxelRail(rail.Cell,rail.Profile with{QuarterTurns=(rail.Profile.QuarterTurns+1)%4});
            }
    }

    public static (VoxelRail Rail, float T)? Nearest(Vector3 position, float maxDistance, bool industrial = false, bool coaster = false)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Track lookup and connectivity/Nearest");
        (VoxelRail Rail, float T)? result = null;
        foreach (var rail in Near(position, coaster))
        {
            if (rail.Profile.Industrial != industrial || rail.Profile.Coaster != coaster) continue;
            var nearest = rail.Profile.Nearest(position - rail.Cell.Origin);
            if (nearest.Distance >= maxDistance) continue;
            maxDistance = nearest.Distance;
            result = (rail, nearest.T);
        }
        return result;
    }

    public static (VoxelRail Rail, float T)? Capture(Vector3 position, Vector3 forward, float horizontal = .24f, float vertical = .32f, bool industrial = false, bool coaster = false)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Track lookup and connectivity/Capture");
        (VoxelRail Rail, float T)? result = null;
        var best = float.MaxValue;
        foreach (var rail in Near(position, coaster))
        {
            if (rail.Profile.Industrial != industrial || rail.Profile.Coaster != coaster) continue;
            var nearest = rail.Profile.Nearest(position - rail.Cell.Origin);
            var offset = position - rail.Point(nearest.T);
            if (new System.Numerics.Vector2(offset.X, offset.Z).Length() > horizontal || Math.Abs(offset.Y) > vertical) continue;
            // Prefer the track along the cart, rather than an adjacent crossing.
            var alignment = Math.Abs(Vector3.Dot(forward, rail.Profile.Tangent(nearest.T)));
            var score = nearest.Distance + .06f * (1 - alignment);
            if (score >= best) continue;
            best = score;
            result = (rail, nearest.T);
        }
        return result;
    }

    internal static (VoxelRail Rail,float T)? CaptureContact(Vector3 position,System.Numerics.Quaternion rotation,Vector3 velocity,float halfWheelbase,float halfWidth,float maxPenetration=.45f)
    {
        (VoxelRail Rail,float T)? result=null;var best=float.MaxValue;
        foreach(var rail in Near(position,true))
        {
            var nearest=rail.Profile.Nearest(position-rail.Cell.Origin);
            var up=rail.Profile.Up(nearest.T);var offset=position-rail.Point(nearest.T);
            var normal=Vector3.Dot(offset,up);
            if((offset-up*normal).LengthSquared()>.24f*.24f || Vector3.Dot(velocity,up)>.05f)continue;
            var forward=rail.Profile.Tangent(nearest.T);
            if(Vector3.Dot(Vector3.Transform(Vector3.UnitZ,rotation),forward)<0)forward=-forward;
            var x=Vector3.Normalize(Vector3.Cross(up,forward));var y=Vector3.Cross(forward,x);
            var guided=System.Numerics.Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,forward.X,forward.Y,forward.Z,0,0,0,0,1));
            var contact=normal-Eco.Minecarts.Physics.AirMotion.LandingClearance(rotation,guided,up,halfWheelbase,halfWidth);
            if(contact>.05f || contact< -maxPenetration)continue;
            var error=Math.Abs(contact)+(offset-up*normal).Length();
            if(error<best){best=error;result=(rail,nearest.T);}
        }
        return result;
    }

    public static (VoxelRail Rail, int End)? Neighbor(VoxelRail rail, int end, bool chainOnly = false)
    {
        var cache=RailSimulationFrame.Neighbors;
        var key=(rail,end,chainOnly,false);
        if(cache!=null && cache.TryGetValue(key,out var cached)) return cached;
        var revision=RailSimulationFrame.Revision;
        var result=NeighborUncached(rail,end,chainOnly);
        if(cache!=null && revision==RailSimulationFrame.Revision)
        { if(cache.Count>=4096)cache.Clear(); cache[key]=result; }
        return result;
    }
    private static (VoxelRail Rail, int End)? NeighborUncached(VoxelRail rail, int end, bool chainOnly)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Track lookup and connectivity/Neighbor");
        (VoxelRail Rail, int End)? result = null;
        foreach (var candidate in Near(rail.Point(end), rail.Profile.Coaster))
        {
            if (chainOnly && !candidate.Profile.Chain) continue;
            var connected = candidate;
            if(RailSwitchComponent.At(candidate.Cell) is {} points && points.TrailingPath(rail,end) is {} trailing)
                connected = trailing;
            for (var otherEnd = 0; otherEnd <= 1; otherEnd++)
            {
                if (!rail.Connects(end, connected, otherEnd)) continue;
                // Ambiguous junctions are not safe to traverse automatically.
                if (result != null) return null;
                result = (connected, otherEnd);
            }
        }
        return result;
    }

    // Manual driving must resolve an actual route at ambiguous visual joins.
    // A nearby parallel rail or crossing can make the conservative Neighbor
    // query return null; that used to make the integrator launch the train
    // straight off the endpoint. Choose the connected rail whose tangent
    // continues the approach instead, while still honoring a switch's
    // selected trailing path.
    public static (VoxelRail Rail, int End)? NeighborForVehicle(VoxelRail rail, int end)
    {
        var cache=RailSimulationFrame.Neighbors;
        var key=(rail,end,false,true);
        if(cache!=null && cache.TryGetValue(key,out var cached)) return cached;
        var revision=RailSimulationFrame.Revision;
        var result=NeighborForVehicleUncached(rail,end);
        if(cache!=null && revision==RailSimulationFrame.Revision)
        { if(cache.Count>=4096)cache.Clear(); cache[key]=result; }
        return result;
    }
    private static (VoxelRail Rail, int End)? NeighborForVehicleUncached(VoxelRail rail, int end)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Track lookup and connectivity/NeighborForVehicle");
        var incoming = rail.Profile.Tangent(end) * (end == 1 ? 1 : -1);
        (VoxelRail Rail, int End)? best = null;
        var bestScore = float.NegativeInfinity;
        foreach (var candidate in Near(rail.Point(end), rail.Profile.Coaster))
        {
            if (candidate.Cell == rail.Cell) continue;
            var connected = candidate;
            if (RailSwitchComponent.At(candidate.Cell) is { } points
                && points.TrailingPath(rail, end) is { } trailing) connected = trailing;
            for (var otherEnd = 0; otherEnd <= 1; otherEnd++)
            {
                if (!rail.Connects(end, connected, otherEnd)) continue;
                var outgoing = connected.Profile.Tangent(otherEnd) * (otherEnd == 0 ? 1 : -1);
                var score = Vector3.Dot(incoming, outgoing);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (connected, otherEnd);
                }
            }
        }
        return best;
    }

    public static IEnumerable<(VoxelRail Rail, int End)> Neighbors(VoxelRail rail, int end)
    {
        foreach (var candidate in Near(rail.Point(end), rail.Profile.Coaster))
            for (var entry = 0; entry < 2; entry++)
                if (rail.Connects(end, candidate, entry)) yield return (candidate, entry);
    }

    public static (VoxelRail Rail, float T)? UnderCart(Vector3 position, bool industrial = false, bool coaster = false)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Rail Network/Track lookup and connectivity/UnderCart");
        (VoxelRail Rail, float T)? result = null;
        var best = float.MaxValue;
        foreach (var rail in Near(position, coaster))
        {
            if (rail.Profile.Industrial != industrial || rail.Profile.Coaster != coaster) continue;
            var projected = rail.Profile.Nearest(position - rail.Cell.Origin);
            var delta = position - rail.Point(projected.T);
            if (delta.Y < 0 || delta.Y > 1.15f || new System.Numerics.Vector2(delta.X, delta.Z).Length() > .35f || projected.Distance >= best) continue;
            best = projected.Distance;
            result = (rail, projected.T);
        }
        return result;
    }

    public static HashSet<RailCell> ChainRun(Vector3 drivePosition, Dictionary<RailCell,VoxelRail>? watched = null)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/One-time network discovery/ChainRun");
        var powered = new HashSet<RailCell>();
        var visited = new HashSet<RailCell>();
        static bool Conducts(VoxelRail rail) => rail.Profile.Chain ||
            (rail.Profile.Coaster && CoasterStationComponent.At(rail.Cell) != null);
        // Connecting beside the station must work even when the nearest chain
        // lies beyond its other socket. Seed discovery from conductive hardware.
        var seed = Near(drivePosition).Where(Conducts)
            .OrderBy(r => r.Profile.Nearest(drivePosition - r.Cell.Origin).Distance).FirstOrDefault();
        if (seed.Profile.Shape == null || seed.Profile.Nearest(drivePosition - seed.Cell.Origin).Distance > 1.6f) return powered;
        // A coaster station joins its two rail sockets into one conductive run,
        // but it has no lift chain and must not consume power or pull a cart.
        var pending = new Queue<VoxelRail>();
        pending.Enqueue(seed);
        while (pending.TryDequeue(out var rail))
        {
            if (!visited.Add(rail.Cell)) continue;
            watched?.TryAdd(rail.Cell,rail);
            if (rail.Profile.Chain) powered.Add(rail.Cell);
            // Fail closed rather than powering a silently truncated long chain.
            if (powered.Count > 64) return [];
            for (var end = 0; end < 2; end++)
            {
                // Ambiguous conductive branches remain unpowered until the
                // topology is made explicit; an ordinary rail is never a wire.
                var next = Neighbors(rail, end).Where(n => Conducts(n.Rail) && !visited.Contains(n.Rail.Cell))
                    .DistinctBy(n => n.Rail.Cell).Take(2).ToArray();
                if (next.Length == 1) pending.Enqueue(next[0].Rail);
            }
        }
        return powered;
    }

    public static HashSet<RailCell> TramRun(Vector3 drivePosition, Dictionary<RailCell,VoxelRail>? watched = null)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/One-time network discovery/TramRun");
        var seed=Near(drivePosition).Where(r=>r.Profile.Tram)
            .OrderBy(r=>r.Profile.Nearest(drivePosition-r.Cell.Origin).Distance).FirstOrDefault();
        if(seed.Profile.Shape==null || seed.Profile.Nearest(drivePosition-seed.Cell.Origin).Distance>2.5f) return [];
        var connected=new HashSet<RailCell>();
        var pending=new Queue<RailCell>();
        pending.Enqueue(seed.Cell);
        while(pending.TryDequeue(out var cell))
        {
            if(!connected.Add(cell)) continue;
            if(Read(cell) is {} member) watched?.TryAdd(cell,member);
            // A bounded network is easier to diagnose than an accidentally
            // truncated one whose far end appears powered for no reason.
            if(connected.Count>4096) return [];
            foreach(var neighbor in NetworkNeighbors(cell))
                if(!connected.Contains(neighbor) && Read(neighbor) is { } rail && rail.Profile.Tram)
                    pending.Enqueue(neighbor);
        }
        return connected;
    }
}
