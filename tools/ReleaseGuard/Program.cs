using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if(args.Length==3&&args[0]=="--compare-schema") { SerializedSchema.Compare(args[1],args[2]); return; }
if(args.Length==3&&args[0]=="--compatible-schema") { SerializedSchema.CompareCompatible(args[1],args[2]); return; }
if(args.Length!=1) throw new ArgumentException("Supply the production DLL or a staged release folder, or --compare-schema OLD NEW.");
var path=Path.GetFullPath(args[0]);
if(Directory.Exists(path))
{
    foreach(var file in Directory.GetFiles(path,"*",SearchOption.AllDirectories))
        if(Path.GetFileName(file).Contains("Debug",StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(file).Contains("Probe",StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(file).Contains("BlockAudit",StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".cs",StringComparison.OrdinalIgnoreCase))
            throw new Exception("Developer artifact in release: "+file);
    path=Path.Combine(path,File.Exists(Path.Combine(path,"Railworks.dll"))?"Railworks.dll":"Eco.Minecarts.dll");
}
using var stream=File.OpenRead(path);
using var pe=new PEReader(stream);
var md=pe.GetMetadataReader();
string AttributeName(CustomAttributeHandle handle)
{
    var ctor=md.GetCustomAttribute(handle).Constructor;
    var type=ctor.Kind==HandleKind.MemberReference ? md.GetMemberReference((MemberReferenceHandle)ctor).Parent
        : md.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType();
    return type.Kind==HandleKind.TypeReference ? md.GetString(md.GetTypeReference((TypeReferenceHandle)type).Name)
        : type.Kind==HandleKind.TypeDefinition ? md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)type).Name) : "";
}
foreach(var handle in md.AssemblyReferences)
{
    var name=md.GetString(md.GetAssemblyReference(handle).Name);
    if(name.StartsWith("Eco.Minecarts.") && (name.Contains("Debug")||name.Contains("Audit")||name.Contains("Test")))
        throw new Exception("Production depends on developer assembly: "+name);
}
foreach(var handle in md.TypeDefinitions)
{
    var type=md.GetTypeDefinition(handle);var name=md.GetString(type.Name);
    if(name.StartsWith("CoasterTrack")&&name.Contains("Section")&&!System.Text.RegularExpressions.Regex.IsMatch(name,
        @"^CoasterTrack(?:(?:Chain)?StraightSection01|ChainBrakeSection01|(?:Chain)?Slope(?:Up|Down)Section0[1-4]|(?:Chain)?Steep(?:Up|Down)Section0[1-2]|(?:Chain)?Grade(?:Up|Down)(?:(?:Entry|Exit)Section0[1-2]|Section01)|(?:Chain)?GradeCompact(?:(?:Up|Down)(?:Entry|Exit)|Crest|Dip)Section01|Sharp(?:Left|Right)Section01)(?:R(?:90|180|270))?(?:Block|FormType)$"))
        throw new Exception("Obsolete sliced coaster shape in release: "+name);
    var retiredCoasterShapes=new[]{"Straight","ChainStraight","SlopeUp","SlopeDown","SlopeEntry","SlopeCrest","SlopeDrop","SlopeExit","SteepUp","SteepDown","ChainSteepUp","LiftEntry","LiftCrest","DropEntry","DropExit"};
    if(name.StartsWith("ConvertCoaster") || retiredCoasterShapes.Any(shape=>name=="Coaster"+shape+"Item" || name=="Coaster"+shape+"Object" || name=="Coaster"+shape+"Recipe"))
        throw new Exception("Retired complete-section coaster type in production: "+name);
    if(name.Contains("Probe") || name.Contains("Diagnostic") || name.Contains("Debug")
        || name=="RailMaintenanceCommands" || type.GetCustomAttributes().Any(a=>AttributeName(a)=="ChatCommandHandlerAttribute"))
        throw new Exception("Developer type in production: "+name);
    foreach(var methodHandle in type.GetMethods())
    {
        var method=md.GetMethodDefinition(methodHandle);var methodName=md.GetString(method.Name);
        if(methodName=="TraceControl") throw new Exception("Verbose developer telemetry in production");
        if(name=="MineTrainDrivingComponent" && method.GetCustomAttributes().Any(a=>AttributeName(a) is "RPCAttribute" or "AutogenAttribute"))
            throw new Exception("Menu driving input in production: "+methodName);
    }
}
Console.WriteLine("RELEASE_GUARD_OK: production has no developer types/dependencies or manual-driving menu RPCs.");
