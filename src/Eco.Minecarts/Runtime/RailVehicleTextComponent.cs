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
        ReadOnlySpan<(int X,int Z)> offsets=[(0,0),(-1,0),(1,0),(0,-1),(0,1)];
        var x=(int)MathF.Round(position.X);
        var z=(int)MathF.Round(position.Z);
        var covered=0;
        for(var i=0;i<offsets.Length;i++)
        {
            var offset=offsets[i];
            if(topSolid(new(x+offset.X,z+offset.Z))>=position.Y+3.5f)covered++;
            // Three covered or three uncovered samples settle the same
            // five-column majority without reading the remaining columns.
            if(covered>=3)return true;
            if(covered+offsets.Length-i-1<3)return false;
        }
        return false;
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
