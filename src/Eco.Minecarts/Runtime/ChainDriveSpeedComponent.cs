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
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Requested Speed (x)")] public float SpeedMultiplier => ChainLift.SpeedMultiplier(multiplier);
    [SyncToView] public float PowerMultiplier => EffectiveSpeedMultiplier;
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Available Speed (x)")] public float EffectiveSpeedMultiplier { get; private set; }
    internal void SetEffectiveSpeed(float value)
    {
        if(EffectiveSpeedMultiplier==value) return;
        EffectiveSpeedMultiplier=value;
        this.Changed(nameof(EffectiveSpeedMultiplier)); this.Changed(nameof(PowerMultiplier));
    }
    public object PersistentData
    {
        get => new ChainDriveSpeedData { Multiplier=multiplier };
        set => multiplier=ChainLift.SpeedMultiplier((value as ChainDriveSpeedData)?.Multiplier ?? 1);
    }
    private bool CanAdjust(Player player) => player!=null && Parent!=null && !Parent.IsDestroyed
        && Parent.IsAuthorized(player.User,AccessType.FullAccess) && Vector3.Distance(player.User.Position,Parent.Position)<=4;
    // Autogen treats non-void methods as value editors. A Task-returning
    // action becomes an unusable selector on the stock Eco client.
    // Keep the old RPC for compatibility, but expose only void buttons.
    [RPC, Autogen] public void IncreaseSpeed(Player player) => SetSpeedMultiplier(player,Math.Min(3,SpeedMultiplier+.25f));
    [RPC, Autogen] public void DecreaseSpeed(Player player) => SetSpeedMultiplier(player,Math.Max(.25f,SpeedMultiplier-.25f));
    [RPC, Autogen] public void ResetSpeed(Player player) => SetSpeedMultiplier(player,1);
    [RPC] public async Task AdjustSpeed(Player player)
    {
        if(!CanAdjust(player)) return;
        var text=await player.InputString(Localizer.DoStr("Chain speed multiplier (0.25 to 3). Mechanical power cost scales equally; 1 is the original speed."),Localizer.DoStr(multiplier.ToString(CultureInfo.InvariantCulture)));
        if(string.IsNullOrWhiteSpace(text)) return;
        if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value) || !float.IsFinite(value) || value<.25f || value>3)
        { player.InfoBoxLoc($"Enter a chain speed multiplier between 0.25 and 3. Nothing changed."); return; }
        SetSpeedMultiplier(player,value);
    }
    [RPC] public void SetSpeedMultiplier(Player player,float value)
    {
        if(!CanAdjust(player) || !float.IsFinite(value) || value<.25f || value>3) return;
        multiplier=value; Parent.SetDirty(); this.Changed(nameof(SpeedMultiplier)); this.Changed(nameof(PowerMultiplier));
        // Refresh the grid demand before applying the new lift speed.
        Parent.Tick();
    }
}
