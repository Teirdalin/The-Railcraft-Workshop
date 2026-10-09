using Eco.Gameplay.Components;
using Eco.Gameplay.Objects;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

// Reuse native saved text. Only the derived presentation state is replicated;
// no additional saved fields or client scripts are needed.
[Serialized, Eco.Core.Controller.NoIcon]
public sealed class RailVehicleTextComponent : WorldObjectComponent
{
    private bool? visible;
    internal static bool NightLights(float hour) => float.IsFinite(hour) && (hour < 6 || hour >= 18);
    internal static bool AutomaticLights(float hour,System.Numerics.Vector3 position,Func<Eco.Shared.Math.Vector2i,int> topSolid)
    {
        if(NightLights(hour))return true;
        // Native cached columns include terrain and constructed roofs. Sample
        // above the cab itself so its roof does not trigger its own lights.
        var covered=0;
        foreach(var offset in new[]{(0,0),(-1,0),(1,0),(0,-1),(0,1)})
            if(topSolid(new((int)MathF.Round(position.X)+offset.Item1,(int)MathF.Round(position.Z)+offset.Item2))>=position.Y+3.5f)covered++;
        return covered>=3;
    }
    public override void PostInitialize() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Text updates/PostInitialize", this.Parent); base.PostInitialize(); Refresh(); }
    public override void Tick() {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Text updates/Tick", this.Parent); base.Tick(); Refresh(); }
    private void Refresh()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Network/Text updates/Refresh", this.Parent);
        if (Parent.IsDestroyed) return;
        if (Parent is RailVehicleObject designVehicle)
        {
            var modern=Eco.Minecarts.Physics.RailVehicleBalances.UsesNewDesign(designVehicle.RailSpec.Key);
            Parent.SetAnimatedState("RailLegacyDesign", !modern);
            Parent.SetAnimatedState("RailDesignPose", modern?"ModernDesign":"LegacyDesign");
        }
        if (Parent is RailVehicleObject vehicle && (vehicle.Capabilities.HasFlag(RailVehicleCapabilities.CableMotor)||vehicle.Capabilities.HasFlag(RailVehicleCapabilities.FueledMotor)))
        {
            var lights=AutomaticLights(Eco.Simulation.Time.WorldTime.SkyTimeOfDay,Parent.Position,Eco.World.World.GetTopSolidBlockY);
            Parent.SetAnimatedState(vehicle.Capabilities.HasFlag(RailVehicleCapabilities.CableMotor)?"TramNightLights":"RailAutomaticLights",lights);
        }
        var next = !string.IsNullOrWhiteSpace(Parent.GetComponent<CustomTextComponent>()?.TextData?.Text);
        if (visible == next) return;
        Parent.SetAnimatedState("VehicleTextVisible", next);
        visible = next;
    }
}
