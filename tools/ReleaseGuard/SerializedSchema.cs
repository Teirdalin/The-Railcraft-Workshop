#nullable enable
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal sealed class SerializedSchema : ISignatureTypeProvider<string, object?>
{
    readonly MetadataReader reader;
    SerializedSchema(MetadataReader reader) => this.reader=reader;
    string Name(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => GetTypeFromDefinition(reader,(TypeDefinitionHandle)handle,0),
        HandleKind.TypeReference => GetTypeFromReference(reader,(TypeReferenceHandle)handle,0),
        HandleKind.TypeSpecification => GetTypeFromSpecification(reader,null,(TypeSpecificationHandle)handle,0),
        _ => handle.IsNil ? "" : throw new Exception("Unexpected type handle: "+handle.Kind)
    };
    string AttributeName(CustomAttributeHandle handle)
    {
        var ctor=reader.GetCustomAttribute(handle).Constructor;
        return Name(ctor.Kind==HandleKind.MemberReference
            ?reader.GetMemberReference((MemberReferenceHandle)ctor).Parent
            :reader.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType());
    }
    bool Serialized(CustomAttributeHandleCollection attrs)=>attrs.Any(a=>AttributeName(a).EndsWith(".SerializedAttribute",StringComparison.Ordinal));
    string PersistenceAttributes(CustomAttributeHandleCollection attrs)=>string.Join(";",attrs.Where(a=>AttributeName(a).EndsWith(".SerializedAttribute",StringComparison.Ordinal))
        .Select(a=>AttributeName(a)+":"+Convert.ToHexString(reader.GetBlobBytes(reader.GetCustomAttribute(a).Value))).Order());
    SortedDictionary<string,string> Read()
    {
        var result=new SortedDictionary<string,string>();
        foreach(var handle in reader.TypeDefinitions)
        {
            var type=reader.GetTypeDefinition(handle);var name=Name(handle);
            var fields=type.GetFields().Where(h=>Serialized(reader.GetFieldDefinition(h).GetCustomAttributes())).ToArray();
            var properties=type.GetProperties().Where(h=>Serialized(reader.GetPropertyDefinition(h).GetCustomAttributes())).ToArray();
            if(!Serialized(type.GetCustomAttributes())&&fields.Length==0&&properties.Length==0)continue;
            result[name]=Name(type.BaseType)+"|"+PersistenceAttributes(type.GetCustomAttributes());
            foreach(var fieldHandle in fields)
            {
                var field=reader.GetFieldDefinition(fieldHandle);
                result[name+"::field:"+reader.GetString(field.Name)]=field.DecodeSignature(this,null)+"|"+PersistenceAttributes(field.GetCustomAttributes());
            }
            foreach(var propertyHandle in properties)
            {
                var property=reader.GetPropertyDefinition(propertyHandle);var signature=property.DecodeSignature(this,null);
                result[name+"::property:"+reader.GetString(property.Name)]=Signature(signature)+"|"+PersistenceAttributes(property.GetCustomAttributes());
            }
        }
        return result;
    }
    static SortedDictionary<string,string> Read(string path)
    {
        using var stream=File.OpenRead(path);using var pe=new PEReader(stream);
        return new SerializedSchema(pe.GetMetadataReader()).Read();
    }
    internal static void Compare(string before,string after)
    {
        var old=Read(before);var current=Read(after);
        var differences=old.Keys.Union(current.Keys).Where(k=>!old.TryGetValue(k,out var a)||!current.TryGetValue(k,out var b)||a!=b).ToArray();
        if(differences.Length>0)throw new Exception("Serialized schema changed: "+string.Join(", ",differences));
        Console.WriteLine($"SERIALIZED_SCHEMA_OK: {old.Count} saved type/member definitions unchanged.");
    }
    internal static void CompareCompatible(string before,string after)
    {
        var old=Read(before);var current=Read(after);
        var changed=old.Keys.Where(k=>!current.TryGetValue(k,out var definition)||definition!=old[k]).ToArray();
        if(changed.Length>0)throw new Exception("Existing saved definitions changed: "+string.Join(", ",changed));
        var added=current.Keys.Except(old.Keys).ToArray();
        Console.WriteLine($"SERIALIZED_COMPATIBLE_OK: {old.Count} existing saved definitions unchanged; {added.Length} additive definitions.");
        foreach(var key in added)Console.WriteLine("ADDED: "+key);
    }
    static string Signature(MethodSignature<string> signature)=>signature.ReturnType+"("+string.Join(",",signature.ParameterTypes)+")";
    public string GetTypeFromDefinition(MetadataReader r,TypeDefinitionHandle h,byte raw)
    {
        var t=r.GetTypeDefinition(h);return t.GetDeclaringType().IsNil?r.GetString(t.Namespace)+"."+r.GetString(t.Name):GetTypeFromDefinition(r,t.GetDeclaringType(),raw)+"+"+r.GetString(t.Name);
    }
    public string GetTypeFromReference(MetadataReader r,TypeReferenceHandle h,byte raw)
    {
        var t=r.GetTypeReference(h);return t.ResolutionScope.Kind==HandleKind.TypeReference?GetTypeFromReference(r,(TypeReferenceHandle)t.ResolutionScope,raw)+"+"+r.GetString(t.Name):r.GetString(t.Namespace)+"."+r.GetString(t.Name);
    }
    public string GetTypeFromSpecification(MetadataReader r,object? context,TypeSpecificationHandle h,byte raw)=>r.GetTypeSpecification(h).DecodeSignature(this,context);
    public string GetArrayType(string element,ArrayShape shape)=>element+"[rank:"+shape.Rank+";sizes:"+string.Join(",",shape.Sizes)+";bounds:"+string.Join(",",shape.LowerBounds)+"]";
    public string GetByReferenceType(string type)=>type+"&";
    public string GetFunctionPointerType(MethodSignature<string> signature)=>"function:"+Signature(signature);
    public string GetGenericInstantiation(string type,ImmutableArray<string> args)=>type+"<"+string.Join(",",args)+">";
    public string GetGenericMethodParameter(object? context,int index)=>"!!"+index;
    public string GetGenericTypeParameter(object? context,int index)=>"!"+index;
    public string GetModifiedType(string modifier,string type,bool required)=>type+(required?" modreq:":" modopt:")+modifier;
    public string GetPinnedType(string type)=>type+" pinned";
    public string GetPointerType(string type)=>type+"*";
    public string GetPrimitiveType(PrimitiveTypeCode code)=>code.ToString();
    public string GetSZArrayType(string type)=>type+"[]";
}
