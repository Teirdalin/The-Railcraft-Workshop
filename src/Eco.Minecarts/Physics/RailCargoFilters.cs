using Eco.Gameplay.Items;
using Eco.Mods.TechTree;
namespace Eco.Minecarts.Physics;

// A native accepting restriction. Previously stored cargo remains withdrawable.
public sealed class RailCargoTagRestriction(params string[] tags):TagRestriction(tags);
// Match the steam locomotives' native fuel category. This is an accepting
// restriction, so pre-existing non-fuel cargo can still be withdrawn.
public sealed class RailTenderFuelRestriction():TagRestriction("Burnable Fuel");

// Inventory-local stack sizes; the native weight and access restrictions still
// apply, and no shared Item/Tag/MaxStackSize metadata is changed.
public sealed class RailBulkStackRestriction(int multiplier):StackLimitRestriction(1,staticLimit:true)
{
 public override int MaxAccepted(Item item)
 {
  var normal=item.MaxStackSize;
  if(!item.IsStackable||!IsBulk(item))return normal;
  return (int)Math.Min(int.MaxValue-64L,(long)normal*multiplier);
 }
 private static bool IsBulk(Item item)=>item is CrushedIronOreItem or CrushedCopperOreItem or CrushedGoldOreItem
  ||item.Tags().Any(tag=>tag.Name is "Rock" or "Ore" or "CrushedRock" or "CrushedOre");
 // The current native crushed metal ores have no Ore/CrushedRock tag.
 // Include those three native types without broadening to all Excavatable items.
}
internal static class RailCargoFilters
{
 internal static void Apply(Inventory inventory,string key)
 {
  inventory.RemoveAllRestrictions(r=>r is RailCargoTagRestriction or RailTenderFuelRestriction or RailBulkStackRestriction);
  var tender=key is "CoalTender" or "LargeCoalTender";
  if(tender)inventory.AddInvRestriction(new RailTenderFuelRestriction());
  var tags=RailVehicleBalances.CargoFilterTags(key);
  if(tags.Length>0&&!(tender&&tags.Length==1&&tags[0]=="Burnable Fuel"))inventory.AddInvRestriction(new RailCargoTagRestriction(tags));
  var multiplier=RailVehicleBalances.BulkCargoStackMultiplier(key);
  // Respect a separately installed mod's existing explicit stack limit.
  if(multiplier>1&&!inventory.Restrictions.OfType<StackLimitRestriction>().Any(r=>r.Enabled))
   inventory.AddInvRestriction(new RailBulkStackRestriction(multiplier));
 }
}
