using System.Reflection;

namespace Eco.Minecarts.Physics;

// Implement this in Mods/UserCode. These are balance providers, never replacement
// saved Item/WorldObject types, so editing them does not change save identities.
public sealed record RailVehicleBalance
{
    public int? CarriedItemWeightGrams { get; init; }
    public double? EmptyMassKg { get; init; }
    public double? CargoCapacityKg { get; init; }
    public int? StorageSlots { get; init; }
    public double? MaximumSpeedMetresPerSecond { get; init; }
    public double? PowerWatts { get; init; }
    public double? TractionNewtons { get; init; }
    public double? BrakingNewtons { get; init; }
    public double? IntendedTrainMassKg { get; init; }
    public double? DurabilityHours { get; init; }
}

public static class RailVehicleBalances
{
    private static readonly object Gate=new();
    private static Dictionary<string,RailVehicleSpec>? resolved;
    private static Dictionary<string,RailVehicleBalance>? settings;
    private static bool dirty;
    static RailVehicleBalances()
    {
        AppDomain.CurrentDomain.AssemblyLoad+=(_,_)=> { lock(Gate){dirty=true;} };
    }
    private static Dictionary<string,RailVehicleBalance> Discover()
    {
        var found=new Dictionary<string,RailVehicleBalance>(StringComparer.Ordinal);
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types=assembly.GetTypes(); }
            catch(ReflectionTypeLoadException e) { types=e.Types.Where(t=>t!=null).Cast<Type>().ToArray(); }
            foreach(var type in types.Where(t=>t.Namespace=="RailworksWorkshop.VehicleSettings" && t.Name.EndsWith("Settings",StringComparison.Ordinal)))
            {
                T? Read<T>(string name) where T:struct
                {
                    var field=type.GetField(name,BindingFlags.Public|BindingFlags.Static);
                    if(field==null)return null;
                    if(!field.IsLiteral)throw new InvalidOperationException(type.Name+"."+name+" must be a const value.");
                    return (T)Convert.ChangeType(field.GetRawConstantValue()!,typeof(T),System.Globalization.CultureInfo.InvariantCulture);
                }
                var key=type.GetField("VehicleKey",BindingFlags.Public|BindingFlags.Static)?.GetRawConstantValue() as string
                    ??throw new InvalidOperationException("Missing VehicleKey in "+type.Name);
                var balance=new RailVehicleBalance {
                    CarriedItemWeightGrams=Read<int>("CarriedItemWeightGrams"),EmptyMassKg=Read<double>("EmptyMassKg"),
                    CargoCapacityKg=Read<double>("CargoCapacityKg"),StorageSlots=Read<int>("StorageSlots"),
                    MaximumSpeedMetresPerSecond=Read<double>("MaximumSpeedMetresPerSecond"),PowerWatts=Read<double>("PowerWatts"),
                    TractionNewtons=Read<double>("TractionNewtons"),BrakingNewtons=Read<double>("BrakingNewtons"),
                    IntendedTrainMassKg=Read<double>("IntendedTrainMassKg"),DurabilityHours=Read<double>("DurabilityHours")
                };
                if(!found.TryAdd(key,balance))throw new InvalidOperationException("Duplicate Railworks settings for "+key+". Keep one .cs file for each vehicle.");
            }
        }
        return found;
    }
    public static void InitializeStorage(Eco.Gameplay.Components.Storage.PublicStorageComponent storage,RailVehicleSpec spec)
    {
        storage.Initialize(spec.Slots,(int)(spec.CargoKg*1000));
        if(storage.Inventory is Eco.Gameplay.Items.LimitedInventory inventory)
        {
            inventory.EnsureStacks(spec.Slots);
            // Native ShrinkTo relocates occupied slots and refuses a reduction
            // when cargo would be lost. Retry naturally at the next initialization.
            if(inventory.Stacks.Count()>spec.Slots)inventory.ShrinkTo(spec.Slots);
        }
    }
    internal static void ApplyItemWeight(string key)
    {
        lock(Gate)
        {
            settings??=Discover();
            if(!settings.TryGetValue(key,out var balance) || balance.CarriedItemWeightGrams is not {} grams)return;
            if(grams<0)throw new InvalidOperationException("Invalid carried item weight in "+key+".cs");
            var type=typeof(RailVehicleSpec).Assembly.GetType("Eco.Mods.TechTree."+key+"Item",true)!;
            var attr=Eco.Gameplay.Items.ItemAttribute.Get<Eco.Gameplay.Items.WeightAttribute>(type)
                ??throw new InvalidOperationException("Missing native vehicle weight attribute for "+key);
            typeof(Eco.Gameplay.Items.WeightAttribute).GetProperty("Weight")!.SetValue(attr,grams);
        }
    }
    internal static RailVehicleSpec Resolve(RailVehicleSpec original)
    {
        lock(Gate)
        {
            if(dirty){resolved=null;settings=null;dirty=false;}
            resolved??=new(StringComparer.Ordinal);
            if(resolved.TryGetValue(original.Key,out var cached))return cached;
            settings??=Discover();
            if(!settings.TryGetValue(original.Key,out var balance))return resolved[original.Key]=original;
            var result=original with {
                EmptyKg=balance.EmptyMassKg??original.EmptyKg,
                CargoKg=balance.CargoCapacityKg??original.CargoKg,
                Slots=balance.StorageSlots??original.Slots,
                MaximumSpeed=balance.MaximumSpeedMetresPerSecond??original.MaximumSpeed,
                PowerWatts=balance.PowerWatts??original.PowerWatts,
                TractionN=balance.TractionNewtons??original.TractionN,
                BrakeN=balance.BrakingNewtons??original.BrakeN,
                IntendedTrainKg=balance.IntendedTrainMassKg??original.IntendedTrainKg,
                DurabilityHours=balance.DurabilityHours??original.DurabilityHours
            };
            if(balance.CarriedItemWeightGrams is < 0 || !double.IsFinite(result.EmptyKg)||result.EmptyKg<=0||!double.IsFinite(result.CargoKg)||result.CargoKg<0||result.CargoKg>int.MaxValue/1000d
                ||result.Slots<1||result.Slots>1024||!double.IsFinite(result.MaximumSpeed)||result.MaximumSpeed<=0
                ||!double.IsFinite(result.PowerWatts)||result.PowerWatts<0||!double.IsFinite(result.TractionN)||result.TractionN<0
                ||!double.IsFinite(result.BrakeN)||result.BrakeN<0||!double.IsFinite(result.IntendedTrainKg)||result.IntendedTrainKg<0
                ||!double.IsFinite(result.DurabilityHours)||result.DurabilityHours<=0)
                throw new InvalidOperationException("Invalid Railworks balance values in "+original.Key+".cs. Weights/capacities must be finite and nonnegative, empty mass/speed/durability positive, storage slots 1-1024.");
            return resolved[original.Key]=result;
        }
    }
}

public sealed class RailVehicleBalanceInitialization : Eco.Core.Plugins.Interfaces.IModInit
{
    public static void PostInitialize()
    {
        // Eco explicitly uses the cached ItemAttribute for weight queries.
        // Update its native Weight property once after item initialization;
        // no item/object types or persistent item data are replaced.
        foreach(var spec in RailVehicleSpec.Additional.Append(RailVehicleSpec.Minecart).Append(RailVehicleSpec.MineTrain))
            RailVehicleBalances.ApplyItemWeight(spec.Key);
    }
}
