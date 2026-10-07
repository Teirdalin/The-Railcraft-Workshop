using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Eco.Minecarts.Track;

namespace Eco.Minecarts.Planning;

// Pure geometry/data layer: no world mutation, networking, inventory or camera.
// New track definitions enter the editor through the same path catalogue used
// by the physics and asset builders.
public sealed record BlueprintPieceDefinition(string Key, string Name, string Category,
    CoasterPath Path, Vector3 PlacementOffset, bool Terrain)
{
    public string GhostName => "CoasterGhost" + Key + "Object";
    public Vector3 Socket(BlueprintNode node, int end) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/Socket"); return node.Position + Rotate(Path.Point(end) + PlacementOffset, node.Turns) - Vector3.UnitY * .5f; }
    public Vector3 Tangent(BlueprintNode node, int end) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/Tangent"); return Rotate(Path.Tangent(end), node.Turns); }
    public Vector3 Up(BlueprintNode node, int end) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/Up"); return Rotate(Path.Up(end), node.Turns); }
    public static Vector3 Rotate(Vector3 p, int turns) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/Rotate"); return ((turns % 4 + 4) % 4) switch
    { 0 => p, 1 => new(p.Z, p.Y, -p.X), 2 => new(-p.X, p.Y, -p.Z), _ => new(-p.Z, p.Y, p.X) }; }
}

public sealed record BlueprintNode
{
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    [JsonRequired] public string Piece { get; init; } = "";
    [JsonRequired] public int X { get; init; }
    [JsonRequired] public int Y { get; init; }
    [JsonRequired] public int Z { get; init; }
    [JsonRequired] public int Turns { get; init; }
    public bool Built { get; init; }
    public bool Reversed { get; init; }
    public bool JumpBefore { get; init; }
    public Vector3 Position => new(X, Y, Z);
}

public sealed record CoasterBlueprintDocument
{
    [JsonRequired] public int Version { get; init; } = 1;
    public string Name { get; init; } = "Coaster blueprint";
    [JsonRequired] public BlueprintNode[] Pieces { get; init; } = [];
    public Guid AnchorId { get; init; }
}

public static class CoasterBlueprintPlan
{
    public const int MaxPieces = 2048;
    public const int MaxExtent = 2048;
    public const int MaxJsonLength = 512000;
    public const int PreviewPieces = 128;
    private static readonly JsonSerializerOptions SaveOptions = new() { IgnoreReadOnlyProperties = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { MaxDepth = 12 };
    static readonly Lazy<IReadOnlyDictionary<string, BlueprintPieceDefinition>> catalog = new(CreateCatalog);
    public static IReadOnlyDictionary<string, BlueprintPieceDefinition> Catalog => catalog.Value;
    static IReadOnlyDictionary<string, BlueprintPieceDefinition> CreateCatalog()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/CreateCatalog");
        var entries = CoasterTerrainPath.All.Select(p => new BlueprintPieceDefinition(p.Key, HumanName(p.Key),
            p.Path.Chain ? "Chain lift" : p.SourceKey.Contains("Grade") || p.Count > 1 ? "Slopes and transitions" : "Basic rails",
            p.Path, Vector3.Zero, true)).ToList();
        entries.AddRange(CoasterPath.All.Where(p => p.Key != "CoasterStraight").Select(p => new BlueprintPieceDefinition(
            p.Key, HumanName(p.Key), "Complete sections", p, p.PlacementOffset, false)));
        entries.Add(new("CoasterStation", "Station", "Stations", CoasterPath.Find("CoasterTrackStraightSection01"), -Vector3.UnitX, false));
        return entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
    }
    static string HumanName(string key) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/HumanName"); return Regex.Replace(key.Replace("CoasterTrack", "").Replace("Coaster", ""), "([a-z])([A-Z0-9])", "$1 $2"); }

    public static bool TryLayout(IEnumerable<BlueprintNode> requested, out BlueprintNode[] nodes, out string error, Guid anchorId = default)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/TryLayout");
        var source = requested.Take(MaxPieces + 1).ToArray(); nodes = []; error = "";
        if (source.Length > MaxPieces) { error = $"A plan may contain at most {MaxPieces} pieces."; return false; }
        if (source.Any(n => n == null || n.Id == Guid.Empty || string.IsNullOrWhiteSpace(n.Piece)) || source.Select(n => n.Id).Distinct().Count() != source.Length)
        { error = "Blueprint piece identities are invalid."; return false; }
        if(anchorId!=Guid.Empty) return TryAnchoredLayout(source,anchorId,out nodes,out error);
        var result = new List<BlueprintNode>();
        var socket = new Vector3(0, -.35f, .5f); var tangent = Vector3.UnitZ; var up = Vector3.UnitY;
        foreach (var original in source)
        {
            if (!Catalog.TryGetValue(original.Piece, out var definition)) { error = "Unknown track piece: " + original.Piece; return false; }
            BlueprintNode? fitted = null;
            for (var turns = 0; turns < 4; turns++)
            {
                var candidate = original with { Turns = turns, X = 0, Y = 0, Z = 0 };
                if (Vector3.Dot(tangent, definition.Tangent(candidate, 0)) <= .65f || Vector3.Dot(up, definition.Up(candidate, 0)) <= .65f) continue;
                var p = socket - definition.Socket(candidate, 0);
                var rounded = new Vector3(MathF.Round(p.X), MathF.Round(p.Y), MathF.Round(p.Z));
                if (Vector3.Distance(p, rounded) > .002f) continue;
                candidate = candidate with { X = (int)rounded.X, Y = (int)rounded.Y, Z = (int)rounded.Z };
                if (Math.Abs(candidate.X) > MaxExtent || Math.Abs(candidate.Y) > MaxExtent || Math.Abs(candidate.Z) > MaxExtent) continue;
                fitted = candidate; break;
            }
            if (fitted == null) { error = "The incoming socket of " + definition.Name + " cannot connect here. Choose a matching slope section or transition."; return false; }
            result.Add(fitted); socket = definition.Socket(fitted, 1); tangent = definition.Tangent(fitted, 1); up = definition.Up(fitted, 1);
        }
        nodes = result.ToArray(); return true;
    }

    internal static int Entry(BlueprintNode n){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/Entry"); return n.Reversed?1:0; }
    internal static int Exit(BlueprintNode n){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/Exit"); return n.Reversed?0:1; }
    internal static Vector3 Heading(BlueprintNode n,int end){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/Heading"); return Catalog[n.Piece].Tangent(n,end)*(n.Reversed?-1:1); }
    public static BlueprintNode[] PreviewWindow(CoasterBlueprintDocument plan,int selected)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/PreviewWindow");
        var pending=plan.Pieces.Select((n,i)=>(Node:n,Index:i)).Where(p=>!p.Node.Built).ToArray();
        var centre=selected>=0?selected:Math.Max(0,Array.FindIndex(plan.Pieces,n=>n.Id==plan.AnchorId));
        return pending.Take(8).Concat(pending.TakeLast(8)).Concat(pending.OrderBy(p=>Math.Abs(p.Index-centre)))
            .DistinctBy(p=>p.Node.Id).Take(PreviewPieces).Select(p=>p.Node).ToArray();
    }
    public static bool CanExtend(IReadOnlyList<BlueprintNode> source,string piece,bool behind,Guid anchorId)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/CanExtend");
        if(source.Count>=MaxPieces || !Catalog.TryGetValue(piece,out var definition))return false;
        if(anchorId==Guid.Empty) return !behind && TryLayout(source.Append(new(){Piece=piece}),out _,out _);
        if(source.Count==0)return false;
        var previous=source[behind?0:source.Count-1];var previousDefinition=Catalog[previous.Piece];
        var previousEnd=behind?Entry(previous):Exit(previous);var end=behind?1:0;
        var socket=previousDefinition.Socket(previous,previousEnd);
        for(var turns=0;turns<4;turns++)
        {
            var candidate=new BlueprintNode{Piece=piece,Turns=turns};
            if(Vector3.Dot(Heading(previous,previousEnd),definition.Tangent(candidate,end))<=.65f ||
                Vector3.Dot(previousDefinition.Up(previous,previousEnd),definition.Up(candidate,end))<=.65f)continue;
            var p=socket-definition.Socket(candidate,end);var rounded=new Vector3(MathF.Round(p.X),MathF.Round(p.Y),MathF.Round(p.Z));
            if(Vector3.Distance(p,rounded)<.002f && MathF.Abs(p.X)<=MaxExtent && MathF.Abs(p.Y)<=MaxExtent && MathF.Abs(p.Z)<=MaxExtent)return true;
        }
        return false;
    }
    static bool TryAnchoredLayout(BlueprintNode[] source,Guid anchorId,out BlueprintNode[] nodes,out string error)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/TryAnchoredLayout");
        nodes=[];error="";
        var anchor=Array.FindIndex(source,n=>n.Id==anchorId);
        if(anchor<0 || source[anchor].Piece!="CoasterStation" || source[anchor].Position!=Vector3.UnitX || source[anchor].Turns!=0 || source[anchor].Reversed)
        {error="The blueprint's station anchor is missing or displaced.";return false;}
        if(source.Any(n=>!Catalog.ContainsKey(n.Piece))) {error="Unknown blueprint piece.";return false;}
        var fitted=(BlueprintNode[])source.Clone();
        for(var side=-1;side<=1;side+=2)
        for(var i=anchor+side;i>=0 && i<source.Length;i+=side)
        {
            var previous=fitted[i-side];var previousDefinition=Catalog[previous.Piece];
            var end=side>0?Entry(source[i]):Exit(source[i]);
            var previousEnd=side>0?Exit(previous):Entry(previous);
            var socket=previousDefinition.Socket(previous,previousEnd);
            var definition=Catalog[source[i].Piece];BlueprintNode? match=null;
            var jump=side>0?source[i].JumpBefore:previous.JumpBefore;
            if(jump)
            {
                var left=side>0?previous:source[i];var right=side>0?source[i]:previous;
                if(BlueprintConnections.CanBridge(Catalog[left.Piece].Socket(left,Exit(left)),Heading(left,Exit(left)),
                    Catalog[right.Piece].Socket(right,Entry(right)),Heading(right,Entry(right)))) match=source[i];
            }
            else for(var turns=0;turns<4;turns++)
            {
                var candidate=source[i] with{Turns=turns,X=0,Y=0,Z=0};
                if(Vector3.Dot(Heading(previous,previousEnd),Heading(candidate,end))<=.65f
                    ||Vector3.Dot(previousDefinition.Up(previous,previousEnd),definition.Up(candidate,end))<=.65f)continue;
                var p=socket-definition.Socket(candidate,end);var rounded=new Vector3(MathF.Round(p.X),MathF.Round(p.Y),MathF.Round(p.Z));
                if(Vector3.Distance(p,rounded)>.002f)continue;
                match=candidate with{X=(int)rounded.X,Y=(int)rounded.Y,Z=(int)rounded.Z};break;
            }
            if(match==null || Math.Abs(match.X)>MaxExtent || Math.Abs(match.Y)>MaxExtent || Math.Abs(match.Z)>MaxExtent)
            {error="Cannot fit "+definition.Name+" on this side of the station or jump.";return false;}
            if(source[i].Built && (source[i].Position!=match.Position || source[i].Turns!=match.Turns))
            {error="This would displace a constructed section.";return false;}
            fitted[i]=match;
        }
        nodes=fitted;return true;
    }

    public static string Serialize(CoasterBlueprintDocument document) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/Serialize"); return JsonSerializer.Serialize(document,SaveOptions); }
    public static bool TryRead(string json, out CoasterBlueprintDocument document, out string error, bool reusable = false)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Geometry and scanning/TryRead");
        document = new(); error = "";
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength) { error = "Blueprint text is empty or too large."; return false; }
        try
        {
            var parsed = JsonSerializer.Deserialize<CoasterBlueprintDocument>(json,ReadOptions);
            if (parsed == null || parsed.Version is not (1 or 2) || parsed.Pieces == null || parsed.Name == null || parsed.Name.Length > 80
                ||parsed.Version==1 && (parsed.AnchorId!=Guid.Empty || parsed.Pieces.Any(n=>n!=null&&(n.Reversed||n.JumpBefore)))
                ||parsed.Version==2 && parsed.AnchorId==Guid.Empty)
            { error = "Unsupported or invalid blueprint format."; return false; }
            var input = parsed.Pieces;
            if (!TryLayout(input, out var fitted, out error,parsed.AnchorId)) return false;
            for (int i = 0; i < input.Length; ++i)
                if (input[i].X != fitted[i].X || input[i].Y != fitted[i].Y || input[i].Z != fitted[i].Z || input[i].Turns != fitted[i].Turns)
                { error = "Saved socket geometry differs from the installed track definitions. Rebuild the affected layout."; return false; }
            if(reusable)
            {
                var identities=fitted.ToDictionary(n=>n.Id,_=>Guid.NewGuid());
                document=parsed with{AnchorId=parsed.AnchorId==Guid.Empty?Guid.Empty:identities[parsed.AnchorId],Pieces=fitted.Select(n=>n with{Id=identities[n.Id],Built=false}).ToArray()};
            }
            else document=parsed with{Pieces=fitted};
            return true;
        }
        catch (JsonException) { error = "Invalid blueprint JSON."; return false; }
    }
}
