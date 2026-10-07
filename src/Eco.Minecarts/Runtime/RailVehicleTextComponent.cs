using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

// Reuse native saved text. Only the derived presentation state is replicated;
// no additional saved fields or client scripts are needed.
[Serialized]
public sealed class RailVehicleTextComponent : WorldObjectComponent
{
    private bool? visible;
    public override void PostInitialize() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Text updates/PostInitialize", this.Parent); base.PostInitialize(); Refresh(); }
    public override void Tick() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Text updates/Tick", this.Parent); base.Tick(); Refresh(); }
    private void Refresh()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Text updates/Refresh", this.Parent);
        if (Parent.IsDestroyed) return;
        var next = !string.IsNullOrWhiteSpace(Parent.GetComponent<CustomTextComponent>()?.TextData?.Text);
        if (visible == next) return;
        Parent.SetAnimatedState("VehicleTextVisible", next);
        visible = next;
    }
}
