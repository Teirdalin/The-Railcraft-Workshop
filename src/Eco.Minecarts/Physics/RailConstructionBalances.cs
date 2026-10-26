using System.Reflection;
namespace Eco.Minecarts.Physics;

public sealed record RailRecipeBalance
{
 public int? MetalBars {get;init;}
 public int? WoodBoards {get;init;}
 public int? HewnLogs {get;init;}
 public int? Wood {get;init;}
 public int? WoodenGears {get;init;}
 public int? SteelGears {get;init;}
 public int? Lubricant {get;init;}
 public int? Fabric {get;init;}
 public int? OutputCount {get;init;}
 public float? LaborCalories {get;init;}
 public float? CraftMinutes {get;init;}
 public double? ChainLiftWattsPerBlock {get;init;}
 public double? ChainTractionNewtons {get;init;}
 public double? MaximumSupportedKg {get;init;}
}
// Providers change construction budgets, never saved block/item/form identities.
public static class RailConstructionBalances
{
 private static readonly object Gate=new();
 private static Dictionary<string,RailRecipeBalance>? settings;
 private static volatile bool dirty;
 static RailConstructionBalances(){AppDomain.CurrentDomain.AssemblyLoad+=(_,_)=>dirty=true;}
 public static RailRecipeBalance? Find(string key)
 {
  lock(Gate)
  {
   if(dirty){settings=null;dirty=false;}
   settings??=Discover();return settings.GetValueOrDefault(key);
  }
 }
 public static double WoodenMaximumSupportedKg=>Find("WoodenTrack")?.MaximumSupportedKg??900;
 public static double ChainLiftWattsPerBlock=>Find("MinecartChain")?.ChainLiftWattsPerBlock??2500;
 public static double ChainTractionNewtons=>Find("MinecartChain")?.ChainTractionNewtons??110000;
 public static string Key(Type type)=>type.Name.EndsWith("Item",StringComparison.Ordinal)?type.Name[..^4]:type.Name.EndsWith("Recipe",StringComparison.Ordinal)?type.Name[..^6]:type.Name;
 private static Dictionary<string,RailRecipeBalance> Discover()
 {
  var found=new Dictionary<string,RailRecipeBalance>(StringComparer.Ordinal);
  foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
  {
   Type[] types;try{types=assembly.GetTypes();}catch(ReflectionTypeLoadException e){types=e.Types.Where(t=>t!=null).Cast<Type>().ToArray();}
   foreach(var type in types.Where(t=>t.Namespace=="RailworksWorkshop.RailSettings"&&t.Name.EndsWith("Settings",StringComparison.Ordinal)))
   {
    T? Read<T>(string name) where T:struct
    {
     var field=type.GetField(name,BindingFlags.Public|BindingFlags.Static);if(field==null)return null;
     if(!field.IsLiteral)throw new InvalidOperationException(type.Name+"."+name+" must be const.");
     return (T)Convert.ChangeType(field.GetRawConstantValue()!,typeof(T),System.Globalization.CultureInfo.InvariantCulture);
    }
    var key=type.GetField("RailKey",BindingFlags.Public|BindingFlags.Static)?.GetRawConstantValue() as string??throw new InvalidOperationException("Missing RailKey in "+type.Name);
    var value=new RailRecipeBalance{MetalBars=Read<int>("MetalBars"),WoodBoards=Read<int>("WoodBoards"),HewnLogs=Read<int>("HewnLogs"),Wood=Read<int>("Wood"),WoodenGears=Read<int>("WoodenGears"),SteelGears=Read<int>("SteelGears"),Lubricant=Read<int>("Lubricant"),Fabric=Read<int>("Fabric"),OutputCount=Read<int>("OutputCount"),LaborCalories=Read<float>("LaborCalories"),CraftMinutes=Read<float>("CraftMinutes"),MaximumSupportedKg=Read<double>("MaximumSupportedKg"),ChainLiftWattsPerBlock=Read<double>("ChainLiftWattsPerBlock"),ChainTractionNewtons=Read<double>("ChainTractionNewtons")};
    if(new[]{value.MetalBars,value.WoodBoards,value.HewnLogs,value.Wood,value.WoodenGears,value.SteelGears,value.Lubricant,value.Fabric}.Any(n=>n is <0)||value.OutputCount is <=0
      ||value.LaborCalories is {} labor&&(!float.IsFinite(labor)||labor<0)
      ||value.CraftMinutes is {} minutes&&(!float.IsFinite(minutes)||minutes<=0)
      ||value.ChainLiftWattsPerBlock is {} watts&&(!double.IsFinite(watts)||watts<=0)
      ||value.ChainTractionNewtons is {} force&&(!double.IsFinite(force)||force<=0)
      ||value.MaximumSupportedKg is {} mass&&(!double.IsFinite(mass)||mass<=0))throw new InvalidOperationException("Invalid rail budget in "+type.Name+". Material/labor costs must be nonnegative; output, time and supported mass must be positive and finite.");
    if(!found.TryAdd(key,value))throw new InvalidOperationException("Duplicate Railworks rail settings for "+key);
   }
  }
  return found;
 }
}
