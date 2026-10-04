using System.Numerics;
using Eco.Core.Controller;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;

namespace Eco.Minecarts.Runtime;

// Eco's stock AuthComponent.OpenDeed opens PropertyDeed (the map). Provide
// direct owner-only controls over the SAME native access list, without replacing
// existing deeds, group permissions, owners or the native authorization checks.
[Serialized, NoIcon, AutogenClass, CreateComponentTabLoc("Vehicle Access", true), LocDisplayName("Vehicle Access")]
public sealed class RailVehicleAccessComponent : WorldObjectComponent
{
    private StandaloneAuthComponent Auth => Parent.GetComponent<StandaloneAuthComponent>();
    [SyncToView, Autogen, PropReadOnly, LocDisplayName("Authorized Operators")]
    public string Operators => Auth?.Deed == null ? "" : string.Join(", ",((System.Collections.Generic.IEnumerable<Eco.Gameplay.Aliases.IAlias>)Auth.Deed.Accessors).Select(a=>a.Name));
    [SyncToView, Autogen, LocDisplayName("Grant Operator Access"), LocDescription("Owner only. Enter an exact username to grant vehicle access without opening the property map.")]
    public string GrantOperator => "";
    [SyncToView, Autogen, LocDisplayName("Revoke Operator Access"), LocDescription("Owner only. Enter an exact username to remove their direct access. Ownership and group permissions stay unchanged.")]
    public string RevokeOperator => "";
    [RPC] public void SetGrantOperator(Player player,string value) => Change(player,value,true);
    [RPC] public void SetRevokeOperator(Player player,string value) => Change(player,value,false);
    private void Change(Player player,string value,bool add)
    {
        if(player==null || Parent.IsDestroyed || Auth?.Deed==null
            || !Parent.IsAuthorized(player.User,AccessType.OwnerAccess)
            || Vector3.Distance(player.User.Position,Parent.Position)>((RailVehicleObject)Parent).CouplerOffset+4)return;
        var user=UserManager.Users.FirstOrDefault(u=>string.Equals(u.Name,value?.Trim(),StringComparison.OrdinalIgnoreCase));
        if(user==null){player.InfoBoxLoc($"Enter the exact name of an existing player.");return;}
        if(add)Auth.Deed.Accessors.Add(user);else Auth.Deed.Accessors.Remove(user);
        Parent.SetDirty();this.Changed(nameof(Operators));this.Changed(add?nameof(GrantOperator):nameof(RevokeOperator));
        if(add)player.InfoBoxLoc($"Vehicle access granted to {user.Name}.");
        else player.InfoBoxLoc($"Direct vehicle access removed from {user.Name}. Ownership and group access are unchanged.");
    }
}
