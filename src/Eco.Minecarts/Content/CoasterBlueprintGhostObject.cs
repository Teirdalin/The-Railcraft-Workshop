using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.Interactions.Interactors;
using Eco.Minecarts.Runtime;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.SharedTypes;

namespace Eco.Mods.TechTree;

// Ordinary Eco WorldObjects, not bespoke transient NetEntity subclasses. They
// have no item, occupation blocks, storage, vehicle physics or minimap marker.
[Serialized, RequireComponent(typeof(CoasterBlueprintGhostComponent))]
public abstract class CoasterBlueprintGhostObject : WorldObject
{
    [Serialized] public Guid StationId { get; set; }
    [Serialized] public Guid StepId { get; set; }
    public string PieceKey => GetType().Name["CoasterGhost".Length..^"Object".Length];
    public override LocString DisplayName => Localizer.DoStr("Planned coaster rail");
    private string? lastStatus;
    private bool lastSelected;
    internal void Show(string status, bool selected)
    {
        if (lastStatus == status && lastSelected == selected) return;
        lastStatus = status; lastSelected = selected;
        SetAnimatedState("PreviewClear", !selected && status == "Clear");
        SetAnimatedState("PreviewWarning", !selected && status.StartsWith("Warning"));
        SetAnimatedState("PreviewBlocked", !selected && status.StartsWith("Blocked"));
        SetAnimatedState("PreviewSelected", selected);
    }
    public override void Use(Player player, InteractionTarget target, InteractionTriggerInfo triggerInfo, string ui = "WorldObjectUI")
        => CoasterBlueprintComponent.Find(StationId)?.SelectGhost(player, StepId);
    public Task ConstructPreview(Player player, InteractionTriggerInfo trigger, InteractionTarget target)
        => CoasterBlueprintComponent.Find(StationId)?.Construct(player, StepId) ?? Task.CompletedTask;
}
