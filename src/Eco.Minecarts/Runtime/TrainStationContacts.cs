using Eco.Minecarts.Track;

namespace Eco.Minecarts.Runtime;

public sealed partial class TrainStationComponent
{
    // Reverse contacts belong to the existing station index. Updates touch only
    // this consist's current/previous stations, never the global vehicle list.
    private static readonly object ContactGate = new();
    private static readonly Dictionary<Guid, HashSet<TrainStationComponent>> ContactStations = [];
    private readonly Dictionary<Guid, (RailCouplingComponent Car, Guid Train, long Revision)> contactCars = [];
    private RailBlockOccupancy contactOccupancy = new();
    internal static bool HasContactStations => !TrackStations.IsEmpty;

    private void ResetContacts()
    {
        lock (ContactGate)
        {
            foreach (var car in contactCars.Keys)
                if (ContactStations.TryGetValue(car, out var stations))
                {
                    stations.Remove(this);
                    if (stations.Count == 0) ContactStations.Remove(car);
                }
            contactCars.Clear();
            SetTrafficContact(false);
            contactOccupancy = new();
            _ = contactOccupancy.Replace([]);
        }
    }

    internal static void ObserveConsist(RailStationContactFrame frame, bool suppressEnter)
    {
        lock (ContactGate)
        {
            frame.Affected.Clear();
            frame.Affected.UnionWith(frame.Present);
            foreach (var car in frame.Members)
                if (ContactStations.TryGetValue(car.Parent.ObjectID, out var previous))
                    frame.Affected.UnionWith(previous);
            foreach (var station in frame.Affected)
            {
                if (station.Parent.IsDestroyed) continue;
                var changed = station.ReconcileSplitContacts(frame);
                changed |= station.UpdateContactRows(frame);
                if (changed) station.PublishContacts(suppressEnter);
            }
        }
    }

    private bool UpdateContactRows(RailStationContactFrame frame)
    {
        var present = frame.Present.Contains(this); var changed = false;
        foreach (var car in frame.Members)
        {
            var id = car.Parent.ObjectID;
            if (present && !car.Parent.IsDestroyed)
            {
                if (!contactCars.TryGetValue(id, out var old) || old.Train != frame.Train)
                {
                    changed = true;
                    if (!ContactStations.TryGetValue(id, out var stations)) ContactStations[id] = stations = [];
                    stations.Add(this);
                }
                contactCars[id] = (car, frame.Train, frame.Revision);
            }
            else if (contactCars.Remove(id))
            {
                changed = true;
                if (ContactStations.TryGetValue(id, out var stations))
                {
                    stations.Remove(this);
                    if (stations.Count == 0) ContactStations.Remove(id);
                }
            }
        }
        return changed;
    }

    private bool ReconcileSplitContacts(RailStationContactFrame frame)
    {
        HashSet<Guid>? previousTrains = null;
        foreach (var car in frame.Members)
            if (contactCars.TryGetValue(car.Parent.ObjectID, out var old) && old.Revision != frame.Revision)
                (previousTrains ??= []).Add(old.Train);
        if (previousTrains == null) return false;
        var visited = frame.Members.Select(c => c.Parent.ObjectID).ToHashSet();
        var changed = false;
        // A previous span contributed all its cars, including those outside
        // this cell. Recompute detached branches before publishing one complete
        // snapshot, or the split can briefly invent an extra departing train.
        // Only witnesses of the affected prior consist are inspected.
        foreach (var old in contactCars.Values.ToArray())
        {
            if (!previousTrains.Contains(old.Train) || visited.Contains(old.Car.Parent.ObjectID) || old.Car.Parent.IsDestroyed) continue;
            var root = old.Car.Leader(); var complete = true; var ready = true;
            foreach (var car in root.Group())
            {
                visited.Add(car.Parent.ObjectID);
                ready &= car.Parent.Initialized && !car.AwaitingLoad;
                var motion = car.Parent.GetComponent<MinecartMotionComponent>();
                complete &= !motion.Derailed && motion.BoundConsistPose is { } pose && TrackWorld.Contains(pose.Rail);
            }
            if (!ready) continue;
            var detached = new RailStationContactFrame(root.Parent.GetComponent<MinecartMotionComponent>());
            detached.Gather(root, complete);
            changed |= UpdateContactRows(detached);
        }
        return changed;
    }

    // Called from motion destruction, including pickup. Other cars still retain
    // the block; removing its identity car cannot create an entry/exit pair.
    internal static void ForgetContact(Guid car)
    {
        lock (ContactGate)
        {
            if (!ContactStations.Remove(car, out var stations)) return;
            foreach (var station in stations)
                if (station.contactCars.Remove(car) && !station.Parent.IsDestroyed)
                    station.PublishContacts(false);
        }
    }

    private void PublishContacts(bool suppressEnter)
    {
        SetTrafficContact(contactCars.Count>0);
        var edges = contactOccupancy.Replace(contactCars.Select(p => (p.Key, p.Value.Train)));
        if (!suppressEnter)
            foreach (var train in edges.Entered) NotifyVehicle(RailEvent.Enter, train);
        foreach (var train in edges.Left) NotifyVehicle(RailEvent.Leave, train);
    }
}

// Reused scratch data under one motion gate. Shared path traversal respects
// curves, slopes, switches and authored seams instead of world-space proximity.
internal sealed class RailStationContactFrame
{
    private readonly Func<VoxelRail, int, (VoxelRail Rail, int End)?> next;
    private readonly Func<VoxelRail, int, bool> visit;
    internal readonly HashSet<TrainStationComponent> Present = [];
    internal readonly HashSet<TrainStationComponent> Affected = [];
    internal RailCouplingComponent[] Members = [];
    internal Guid Train;
    internal long Revision;
    private (VoxelRail Rail, float T, int Facing)?[] partialPoses = [];

    internal RailStationContactFrame(MinecartMotionComponent motion)
    { next = motion.NextRail; visit = Visit; }

    // An incomplete consist uses each surviving car's contact, rather than a
    // span derived from its owner. A stationary/unbound owner does not imply
    // that those cars stayed put. Compare the exact poses used by Gather so
    // a later follower publication remains visible to the next observation.
    internal bool PartialPosesUnchanged()
    {
        if (partialPoses.Length != Members.Length) return false;
        for (var i = 0; i < Members.Length; i++)
        {
            var motion = Members[i].Parent.GetComponent<MinecartMotionComponent>();
            if (partialPoses[i] != (motion.Derailed ? null : motion.BoundConsistPose)) return false;
        }
        return true;
    }

    private bool Visit(VoxelRail rail, int orientation)
    {
        foreach (var station in TrainStationComponent.At(rail.Cell))
            if (!station.Parent.IsDestroyed && station.Parent.GetComponent<CoasterStationComponent>() == null)
                Present.Add(station);
        return true;
    }

    private void Span(VoxelRail rail, float parameter, double first, double last)
    {
        Visit(rail, 1);
        // Same topology bound as remote network connectivity. The ordinary
        // simulation already bounds per-car traversal; no world graph scan.
        _ = RailPathCursor.Travel(rail, parameter, first, next, 4096, visit);
        _ = RailPathCursor.Travel(rail, parameter, last, next, 4096, visit);
    }

    internal void Gather(RailCouplingComponent coupling, bool complete)
    {
        Present.Clear();
        Members = coupling.Group();
        Revision = RailCouplingComponent.TopologyRevision;
        Train = Members[0].Parent.ObjectID;
        foreach (var member in Members)
            if (member.Parent.ObjectID.CompareTo(Train) < 0) Train = member.Parent.ObjectID;
        var root = coupling.Parent.GetComponent<MinecartMotionComponent>().BoundConsistPose;
        if (complete && root is { } pose)
        {
            var span = coupling.StationSpan;
            Span(pose.Rail, pose.T, span.Min * pose.Facing, span.Max * pose.Facing);
        }
        else
        {
            if (partialPoses.Length != Members.Length) partialPoses = new (VoxelRail Rail, float T, int Facing)?[Members.Length];
            for (var i = 0; i < Members.Length; i++)
            {
                var member = Members[i];
                var motion = member.Parent.GetComponent<MinecartMotionComponent>();
                var observed = motion.Derailed ? null : motion.BoundConsistPose;
                partialPoses[i] = observed;
                if (observed is not { } memberPose || !TrackWorld.Contains(memberPose.Rail)) continue;
                Span(memberPose.Rail, memberPose.T, (member.Vehicle.ConnectionPoints.Coupler(-1).Z - .05) * memberPose.Facing,
                    (member.Vehicle.ConnectionPoints.Coupler(1).Z + .05) * memberPose.Facing);
            }
        }
    }
}
