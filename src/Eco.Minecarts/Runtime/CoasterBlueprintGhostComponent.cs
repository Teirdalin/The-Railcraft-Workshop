using System.Collections.Concurrent;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Shared.Items;
using Eco.Shared.SharedTypes;
using Eco.Mods.TechTree;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon]
public sealed class CoasterBlueprintGhostComponent : WorldObjectComponent
{
    internal static readonly ConcurrentDictionary<Guid, CoasterBlueprintGhostObject> Live = new();
    private static readonly object RegistryGate = new();
    private static readonly Dictionary<Guid, Dictionary<Guid, CoasterBlueprintGhostObject>> ByStation = new();
    private static long epoch;
    internal static long Epoch => Interlocked.Read(ref epoch);
    internal static CoasterBlueprintGhostObject[] ForStation(Guid station)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ForStation"); lock(RegistryGate) return ByStation.TryGetValue(station,out var owned) ? owned.Values.ToArray() : []; }
    // WorldObject itself does not implement IHasInteractions. Components do,
    // so Eco publishes these actions into its native interaction catalogue.
    // Authorization belongs to the real station and construction plot, not
    // the unowned, empty-occupancy preview used as the click target.
    [Interaction(InteractionTrigger.LeftClick, "Construct planned rail", requiredEnvVars: new[] { "CoasterBlueprintPiece" },
        interactionDistance: 6, priority: 110, authRequired: AccessType.None, flags: InteractionFlags.BlocksOtherInteraction)]
    [Interaction(InteractionTrigger.RightClick, "Construct planned rail", requiredEnvVars: new[] { "CoasterBlueprintPiece" },
        interactionDistance: 6, priority: 110, authRequired: AccessType.None, flags: InteractionFlags.BlocksOtherInteraction)]
    public Task ConstructPreview(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ConstructPreview", this.Parent);
        var ghost = (CoasterBlueprintGhostObject)Parent;
        var editor = CoasterBlueprintComponent.Find(ghost.StationId);
        if (editor != null) return editor.Construct(player, ghost.StepId);
        player.InfoBoxLocStr("This preview's station is no longer available.");
        return Task.CompletedTask;
    }
    [Interaction(InteractionTrigger.InteractKey, "Select planned rail", requiredEnvVars: new[] { "CoasterBlueprintPiece" },
        interactionDistance: 6, priority: 110, authRequired: AccessType.None, flags: InteractionFlags.BlocksOtherInteraction)]
    public void SelectPreview(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/SelectPreview", this.Parent);
        var ghost = (CoasterBlueprintGhostObject)Parent;
        CoasterBlueprintComponent.Find(ghost.StationId)?.SelectGhost(player, ghost.StepId);
    }
    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/PostInitialize", this.Parent);
        base.PostInitialize(); var ghost = (CoasterBlueprintGhostObject)Parent;
        lock(RegistryGate)
        {
            Live[ghost.ObjectID] = ghost;
            if(!ByStation.TryGetValue(ghost.StationId,out var owned)) ByStation[ghost.StationId]=owned=new();
            owned[ghost.ObjectID]=ghost; Interlocked.Increment(ref epoch);
        }
        CoasterBlueprintComponent.Find(ghost.StationId)?.RequestReconcile();
    }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Tick", this.Parent);
        base.Tick();
        // Load order cannot prove orphanhood. Wait for Eco to finish loading
        // all saved stations before cleaning up detached planning visuals.
        if (WorldObjectManager.Init.Initialized && CoasterBlueprintComponent.Find(((CoasterBlueprintGhostObject)Parent).StationId) == null)
            Parent.Destroy();
    }
    public override void Destroy()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Destroy", this.Parent);
        var ghost=(CoasterBlueprintGhostObject)Parent;
        lock(RegistryGate)
        {
            Live.TryRemove(Parent.ObjectID,out _);
            if(ByStation.TryGetValue(ghost.StationId,out var owned))
            { owned.Remove(Parent.ObjectID); if(owned.Count==0) ByStation.Remove(ghost.StationId); }
            Interlocked.Increment(ref epoch);
        }
        CoasterBlueprintComponent.Find(ghost.StationId)?.RequestReconcile(); base.Destroy();
    }
}
