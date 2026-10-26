using Eco.Gameplay.Components;
using Eco.Gameplay.Items;
using Eco.Minecarts.Physics;
using Eco.Mods.TechTree;

namespace Eco.Minecarts.Runtime;

// Ordinary native PartItems: native inventories, replacement permissions,
// repair skills, bounties, integrity and saved durability remain authoritative.
internal static class RailVehicleMaintenance
{
    internal static PartsComponent.PartInfo[] RequiredParts(RailVehicleSpec spec)
    {
        static PartsComponent.PartInfo Part<T>(int count)=>new(){TypeName=typeof(T).Name,Quantity=count};
        if(spec.Tier=="Wood")return [Part<WoodenWheelItem>(2)];
        if(spec.Key=="LargeTrainEngine")return [Part<IronWheelItem>(4),Part<PortableSteamEngineItem>(2),Part<IronGearItem>(1),Part<LubricantItem>(2)];
        if(spec.Tier=="Industry")return [Part<IronWheelItem>(4),Part<LubricantItem>(2)];
        if(spec.Powered&&!spec.Tram&&!spec.HumanPowered)return [Part<IronWheelItem>(2),Part<PortableSteamEngineItem>(1),Part<LubricantItem>(1)];
        if(spec.Coaster||spec.Tram)return [Part<IronWheelItem>(2),Part<SteelGearItem>(1),Part<LubricantItem>(1)];
        if(spec.HumanPowered)return [Part<IronWheelItem>(2),Part<IronGearItem>(1),Part<LubricantItem>(1)];
        return [Part<IronWheelItem>(2),Part<LubricantItem>(1)];
    }
    internal static bool MigrateTrainParts(RailVehicleSpec spec,PartsComponent parts)
    {
        // Only an installed legacy large-engine gear changes material. Leave
        // loose/cargo steel, other vehicles and deliberately missing parts alone.
        if(spec.Key!="LargeTrainEngine" || parts.Parts==null)return false;
        var changed=false;
        foreach(var stack in parts.Parts.NonEmptyStacks.ToArray())
        {
            if(stack.Item is not SteelGearItem old)continue;
            var replacement=new IronGearItem();
            var scale=replacement.OriginalMaxDurability/old.OriginalMaxDurability;
            replacement.IntegrityAmount=old.IntegrityAmount*scale;
            replacement.SetDurability(old.Durability*scale);
            // PostInitialize runs after native Parts has attached its inventory
            // callbacks. The native notification refreshes status and inventory
            // caches without minting health, quantity or an absent gear.
            stack.ReplaceStack(replacement,stack.Quantity,notifyParentInventory:true);
            changed=true;
        }
        return changed;
    }
}
