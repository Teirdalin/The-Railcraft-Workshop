using System.Numerics;
using System.Globalization;
using Eco.Core.Controller;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Minecarts.Physics;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

[Serialized]
public sealed class ChainDriveSpeedData
{
    [Serialized] public float Multiplier { get; set; } = 1;
}

[Serialized, NoIcon, AutogenClass, CreateComponentTabLoc("Chain Drive Speed", true), LocDisplayName("Chain Drive Speed")]
public sealed class ChainDriveSpeedComponent : WorldObjectComponent, IPersistentData
{
    [Serialized] private float multiplier=1;
    private float MaximumSpeed => Parent is Eco.Mods.TechTree.MinecartChainDriveObject drive ? drive.MaximumSpeedMultiplier : 3;
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Requested Speed (x)")] public float SpeedMultiplier => ChainLift.SpeedMultiplier(multiplier,MaximumSpeed);
    [SyncToView] public float PowerMultiplier => EffectiveSpeedMultiplier;
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Available Speed (x)")] public float EffectiveSpeedMultiplier { get; private set; }
    internal void SetEffectiveSpeed(float value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/SetEffectiveSpeed", this.Parent);
        if(EffectiveSpeedMultiplier==value) return;
        EffectiveSpeedMultiplier=value;
        this.Changed(nameof(EffectiveSpeedMultiplier)); this.Changed(nameof(PowerMultiplier));
    }
    public object PersistentData
    {
        get { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/ChainDriveSpeedComponent.PersistentData.get", this.Parent); return new ChainDriveSpeedData { Multiplier=multiplier }; }
        set { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Persistence/Component snapshots/ChainDriveSpeedComponent.PersistentData.set", this.Parent); multiplier=ChainLift.SpeedMultiplier((value as ChainDriveSpeedData)?.Multiplier ?? 1,5); }
    }
    private bool CanAdjust(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/CanAdjust", this.Parent); return player!=null && Parent!=null && !Parent.IsDestroyed
        && Parent.IsAuthorized(player.User,AccessType.FullAccess) && Vector3.Distance(player.User.Position,Parent.Position)<=4; }
    // Autogen treats non-void methods as value editors. A Task-returning
    // action becomes an unusable selector on the stock Eco client.
    // Keep the old RPC for compatibility, but expose only void buttons.
    [RPC, Autogen, UITypeName("BigButton")] public void IncreaseSpeed(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/IncreaseSpeed", this.Parent); SetSpeedMultiplier(player,Math.Min(MaximumSpeed,SpeedMultiplier+.25f)); }
    [RPC, Autogen, UITypeName("BigButton")] public void DecreaseSpeed(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/DecreaseSpeed", this.Parent); SetSpeedMultiplier(player,Math.Max(.25f,SpeedMultiplier-.25f)); }
    [RPC, Autogen, UITypeName("BigButton")] public void ResetSpeed(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/ResetSpeed", this.Parent); SetSpeedMultiplier(player,1); }
    [RPC] public async Task AdjustSpeed(Player player)
    {
        if(!CanAdjust(player)) return;
        var text=await player.InputString(Localizer.DoStr($"Chain speed multiplier (0.25 to {MaximumSpeed}). Power cost scales equally; 1 is the original speed."),Localizer.DoStr(multiplier.ToString(CultureInfo.InvariantCulture)));
        if(string.IsNullOrWhiteSpace(text)) return;
        if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value) || !float.IsFinite(value) || value<.25f || value>MaximumSpeed)
        { player.InfoBoxLoc($"Enter a chain speed multiplier between 0.25 and {MaximumSpeed}. Nothing changed."); return; }
        SetSpeedMultiplier(player,value);
    }
    [RPC] public void SetSpeedMultiplier(Player player,float value)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Mechanical Power/Connections and demand/SetSpeedMultiplier", this.Parent);
        if(!CanAdjust(player) || !float.IsFinite(value) || value<.25f || value>MaximumSpeed) return;
        multiplier=value; Parent.SetDirty(); this.Changed(nameof(SpeedMultiplier)); this.Changed(nameof(PowerMultiplier));
        // Refresh the grid demand before applying the new lift speed.
        Parent.Tick();
    }
}
