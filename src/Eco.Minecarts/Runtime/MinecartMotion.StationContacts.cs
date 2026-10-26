using Eco.Gameplay.Objects;
using Eco.Minecarts.Track;

namespace Eco.Minecarts.Runtime;

public sealed partial class MinecartMotionComponent
{
    private RailStationContactFrame? stationContactFrame;
    private (VoxelRail Rail, float T, int Facing)? lastStationPose;
    private long lastStationTopology = -1, lastStationRouting = -1, lastStationRailRevision = -1;
    private bool stationContactsObserved, lastStationComplete;

    internal void UpdateStationContacts()
    {
        if (motionStopped || pickupPending || Parent.IsDestroyed || RailVehicle.RailSpec.Coaster
            || !TrainStationComponent.HasContactStations || !WorldObjectManager.Init.Initialized) return;
        var coupling = Parent.GetComponent<RailCouplingComponent>();
        if (coupling.AwaitingLoad || coupling.IsFollower) return;
        var members = coupling.Group();
        var complete = true; var anyContact = false; var suppressEnter = true;
        foreach (var member in members)
        {
            if (!member.Parent.Initialized || member.AwaitingLoad) return;
            var motion = member.Parent.GetComponent<MinecartMotionComponent>();
            var contact = !motion.Derailed && motion.BoundConsistPose is { } at && TrackWorld.Contains(at.Rail);
            complete &= contact; anyContact |= contact;
            suppressEnter &= !motion.stationContactsObserved;
        }
        // A loaded car which has not yet bound its saved rail is not an empty
        // first observation. Wait for binding before suppressing startup edges.
        if (!anyContact && suppressEnter) return;
        var pose = Derailed ? null : BoundConsistPose;
        var topology = RailCouplingComponent.TopologyRevision;
        var routing = TrainStationComponent.RoutingRevision;
        var railRevision = RailSimulationFrame.Revision;
        if (stationContactsObserved && lastStationPose == pose && lastStationTopology == topology
            && lastStationRouting == routing && lastStationRailRevision == railRevision && lastStationComplete == complete
            && (complete || stationContactFrame?.PartialPosesUnchanged() == true)) return;
        stationContactFrame ??= new(this);
        stationContactFrame.Gather(coupling, complete);
        TrainStationComponent.ObserveConsist(stationContactFrame, suppressEnter);
        foreach (var member in members) member.Parent.GetComponent<MinecartMotionComponent>().stationContactsObserved = true;
        lastStationPose = pose; lastStationTopology = topology; lastStationRouting = routing; lastStationRailRevision = railRevision; lastStationComplete = complete;
    }
}
