using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
using Eco.Core.Controller;
using Eco.Core.Plugins.Interfaces;
using Eco.Gameplay.Auth;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Placement;
using Eco.Gameplay.Players;
using Eco.Gameplay.Property;
using Eco.Gameplay.UI;
using Eco.Minecarts.Planning;
using Eco.Minecarts.Track;
using Eco.Mods.TechTree;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Networking;
using Eco.Shared.Serialization;
using Eco.Shared.Voxel;
using Eco.World.Blocks;
using Eco.World.Water;
using NVector = System.Numerics.Vector3;
using EQuaternion = Eco.Shared.Math.Quaternion;

namespace Eco.Minecarts.Runtime;

[Serialized, NoIcon, AutogenClass, CreateComponentTabLoc("Coaster Blueprint", true), LocDisplayName("Coaster Blueprint")]
public sealed class CoasterBlueprintComponent : WorldObjectComponent
{
    private static readonly ConcurrentDictionary<Guid, CoasterBlueprintComponent> Editors = new();
    private static readonly ConcurrentDictionary<string, Lazy<Type?>> ContentTypes = new();
    private static readonly object EditorListGate = new();
    private static CoasterBlueprintComponent[] editorSnapshot = [];
    private static readonly object PreviewCreationGate = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<Guid, CoasterBlueprintGhostObject> ghosts = new();
    private readonly Dictionary<Guid, string> validation = new();
    private readonly Stack<string> undo = new(), redo = new();
    private CoasterBlueprintDocument? document;
    private int revision;
    private int validationDirty = 1;
    private int reconcileRequested = 1;
    private HashSet<RailCell> watchedCells = [];
    private long lastOpeningSync;
    private long previewLimitEpoch = -1;
    private string layoutText = "";
    private string publishedStatus = "";
    private string nextPiece = "CoasterTrackStraightSection01";
    [Serialized] public string PlanJson { get; private set; } = "";
    [Serialized] public string LibraryJson { get; private set; } = "{}";
    [Serialized] public bool ShowPreview { get; private set; }
    [Serialized] public bool Editing { get; private set; }
    [Serialized] public int SelectedIndex { get; private set; } = -1;
    [Serialized] public bool ExtendBehind { get; private set; }
    [SyncToView, PropReadOnly] public string BuildEnd => ExtendBehind ? "Behind the station" : "In front of the station";
    [SyncToView, Autogen, PropReadOnly, UITypeName("StringTitle"), LocDisplayName("Coaster designer")]
    public string RouteSummary { get; private set; } = "Open this page to discover your route.";
    [SyncToView, Autogen, PropReadOnly, UITypeName("StringDisplay"), LocDisplayName("Quick guide")]
    public string Instructions => "Build chooses a rail and which end to extend. Add Next repeats it. Click a preview with a hammer and carried rail to build it; complete sections need their crafted item. Select a preview with E, then Edit. White = selected, green = clear, amber = needs support, red = blocked.";
    [SyncToView, Autogen, PropReadOnly, UITypeName("StringDisplay"), LocDisplayName("Selected rail")] public string CurrentPiece { get; private set; } = "No blueprint";
    [SyncToView, PropReadOnly] public string NextPiece { get; private set; } = "Straight";
    [SyncToView, Autogen, PropReadOnly, UITypeName("StringDisplay"), LocDisplayName("Around the selection")] public string Layout { get { SyncOnOpening(); return layoutText; } }
    [SyncToView, Autogen, PropReadOnly] public string Status { get; private set; } = "";
    [SyncToView, PropReadOnly] public string SavedLayouts => string.Join(", ", ReadLibrary().Keys);
    private CoasterBlueprintDocument Plan => document ??= string.IsNullOrEmpty(PlanJson) ? new() :
        CoasterBlueprintPlan.TryRead(PlanJson, out var parsed, out _) ? parsed : new();
    internal static CoasterBlueprintComponent? Find(Guid id) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Find"); return Editors.GetValueOrDefault(id); }
    internal static void TerrainChanged(WrappedWorldPosition3i cell)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/TerrainChanged");
        var key = new RailCell(cell.X, cell.Y, cell.Z);
        foreach (var editor in Volatile.Read(ref editorSnapshot))
        {
            if (Volatile.Read(ref editor.watchedCells).Contains(key))
                Interlocked.Exchange(ref editor.validationDirty, 1);
        }
    }
    private static BlueprintPieceDefinition Definition(BlueprintNode n) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Definition"); return CoasterBlueprintPlan.Catalog[n.Piece]; }
    private static Type? ContentType(string name) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ContentType"); return ContentTypes.GetOrAdd(name, static key => new(() =>
    {
        var type = typeof(CoasterStationObject).Assembly.GetType("Eco.Mods.TechTree." + key);
        if (type != null && typeof(WorldObject).IsAssignableFrom(type))
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        return type;
    })).Value; }
    internal void RequestReconcile() { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/RequestReconcile", this.Parent); Interlocked.Exchange(ref reconcileRequested, 1); }
    private bool HasBuiltTrack => Plan.Pieces.Any(n => n.Built && n.Id != Plan.AnchorId);
    private void WatchPlan()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/WatchPlan", this.Parent);
        var cells = new HashSet<RailCell>();
        foreach (var n in Plan.Pieces)
        {
            foreach (var p in Footprint(n))
            { cells.Add(new(p.X,p.Y,p.Z)); cells.Add(new(p.X,p.Y-1,p.Z)); }
            var d = Definition(n);
            for (var end = 0; end < 2; end++)
            {
                var p = PositionOf(n) + RotationOf(n).RotateVector(d.PlacementOffset + d.Path.Point(end)) - NVector.UnitY*.5f;
                for (var dx=-2;dx<=2;dx++) for (var dy=-2;dy<=2;dy++) for (var dz=-2;dz<=2;dz++)
                    cells.Add(new((int)MathF.Round(p.X)+dx,(int)MathF.Round(p.Y)+dy,(int)MathF.Round(p.Z)+dz));
            }
        }
        Volatile.Write(ref watchedCells,cells);
    }
    internal NVector PositionOf(BlueprintNode n) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/PositionOf", this.Parent); return Parent.GetComponent<CoasterRailComponent>().Rail.Cell.Origin + NVector.UnitY * .5f +
        BlueprintPieceDefinition.Rotate(n.Position, StationTurns); }
    private int StationTurns => Parent.GetComponent<CoasterRailComponent>().Rail.Profile.QuarterTurns;
    internal EQuaternion RotationOf(BlueprintNode n)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/RotationOf", this.Parent);
        var q = System.Numerics.Quaternion.CreateFromAxisAngle(NVector.UnitY, ((StationTurns + n.Turns) % 4) * MathF.PI / 2);
        return new(q.X, q.Y, q.Z, q.W);
    }
    private bool CanEdit(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/CanEdit", this.Parent); return player != null && !Parent.IsDestroyed &&
        Parent.IsAuthorized(player.User, AccessType.FullAccess) && NVector.Distance(player.User.Position, Parent.Position) <= 512; }
    private bool AtStation(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/AtStation", this.Parent); return CanEdit(player) && NVector.Distance(player.User.Position, Parent.Position) <= 6; }

    // Native UI property retrieval also covers opening the component page via
    // a remote station link. Publish never reads Layout, and the nonblocking
    // gate prevents change notifications from recursively scanning the world.
    internal void SyncOnOpening()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/SyncOnOpening", this.Parent);
        if(Parent==null || Parent.IsDestroyed || !WorldObjectManager.Init.Initialized || !gate.Wait(0))return;
        try
        {
            var now=Environment.TickCount64;
            if(lastOpeningSync!=0 && now-lastOpeningSync<2000)return;
            lastOpeningSync=now; SynchronizePlaced(); Publish();
        }
        catch(Exception e)
        { Eco.Shared.Logging.Log.WriteErrorLineLoc($"Coaster blueprint opening sync failed: {e}"); Status="Track sync could not finish; your plan has been retained."; }
        finally { gate.Release(); }
    }
    private void SynchronizePlaced()
    {
        if(!WorldObjectManager.Init.Initialized || Parent.IsDestroyed)return;
        var original=Plan;
        var selectedId=original.Pieces.ElementAtOrDefault(SelectedIndex)?.Id;
        var station=Parent.GetComponent<CoasterRailComponent>().Rail;
        var turns=station.Profile.QuarterTurns;
        var origin=station.Cell.Origin+NVector.UnitY*.5f;
        var physical=new Dictionary<RailCell,VoxelRail?>();
        var planned=new Dictionary<RailCell,VoxelRail>();
        var plannedNodes=new Dictionary<RailCell,BlueprintNode>();
        var plannedSockets=new Dictionary<RailCell,List<VoxelRail>>();
        VoxelRail? Read(RailCell cell)
        {
            if(!physical.TryGetValue(cell,out var rail))
            {
                rail=cell.X<0 || cell.Z<0 || cell.Y<0 || cell.X>=Eco.World.World.VoxelSize.X ||
                    cell.Y>=Eco.World.World.VoxelSize.Y || cell.Z>=Eco.World.World.VoxelSize.Z ? null : TrackWorld.Read(cell);
                physical[cell]=rail;
            }
            return rail ?? (planned.TryGetValue(cell,out var pending)?pending:null);
        }
        foreach(var n in original.Pieces)
        {
            var d=Definition(n); var centre=PositionOf(n)+RotationOf(n).RotateVector(d.PlacementOffset);
            var cell=new RailCell((int)MathF.Round(centre.X),(int)MathF.Round(centre.Y),(int)MathF.Round(centre.Z));
            if(!IsConstructed(n))
            { planned.TryAdd(cell,new(cell,new(d.Path.Key,(turns+n.Turns)%4,d.Path.Chain,Coaster:true))); plannedNodes.TryAdd(cell,n); }
        }
        foreach(var rail in planned.Values)for(var end=0;end<2;end++)
        {
            var p=rail.Point(end);var key=new RailCell((int)MathF.Round(p.X),(int)MathF.Round(p.Y),(int)MathF.Round(p.Z));
            if(!plannedSockets.TryGetValue(key,out var bucket))plannedSockets[key]=bucket=new();
            bucket.Add(rail);
        }
        var oldByPose=original.Pieces.GroupBy(n=>(n.Piece,n.X,n.Y,n.Z,n.Turns)).ToDictionary(g=>g.Key,g=>g.First());
        var rootId=original.AnchorId!=Guid.Empty ? original.AnchorId : Guid.NewGuid();
        BlueprintNode? Describe(VoxelRail rail,bool reversed)
        {
            _=Read(rail.Cell);
            var obj=physical.GetValueOrDefault(rail.Cell) is {} ? CoasterRailComponent.ObjectAt(rail.Cell) : null;
            var key=rail.Cell==station.Cell?"CoasterStation":obj is CoasterStationObject?"CoasterStation":
                physical.GetValueOrDefault(rail.Cell)==null && plannedNodes.TryGetValue(rail.Cell,out var pending)?pending.Piece:rail.Profile.Shape;
            if(!CoasterBlueprintPlan.Catalog.TryGetValue(key,out var d))return null;
            var relativeTurns=(rail.Profile.QuarterTurns-turns+4)%4;
            var position=BlueprintPieceDefinition.Rotate(rail.Cell.Origin+NVector.UnitY*.5f-origin,-turns)-
                BlueprintPieceDefinition.Rotate(d.PlacementOffset,relativeTurns);
            if(MathF.Abs(position.X)>CoasterBlueprintPlan.MaxExtent || MathF.Abs(position.Y)>CoasterBlueprintPlan.MaxExtent ||
                MathF.Abs(position.Z)>CoasterBlueprintPlan.MaxExtent)return null;
            var pose=(key,(int)MathF.Round(position.X),(int)MathF.Round(position.Y),(int)MathF.Round(position.Z),relativeTurns);
            oldByPose.TryGetValue(pose,out var existing);
            var node=new BlueprintNode{ Id=rail.Cell==station.Cell?rootId:existing?.Id??Guid.NewGuid(),Piece=key,
                X=pose.Item2,Y=pose.Item3,Z=pose.Item4,Turns=relativeTurns,Reversed=reversed };
            return node with{Built=IsConstructed(node)};
        }
        IEnumerable<(VoxelRail Rail,int End)> Connected(VoxelRail rail,int end)
        {
            var point=rail.Point(end);var nearby=new Dictionary<RailCell,VoxelRail>();
            foreach(var other in CoasterRailComponent.Near(point)) if(Read(other.Cell) is {} resolved)nearby[other.Cell]=resolved;
            var x=(int)MathF.Round(point.X);var y=(int)MathF.Round(point.Y);var z=(int)MathF.Round(point.Z);
            for(var dx=-2;dx<=2;dx++)for(var dy=-2;dy<=2;dy++)for(var dz=-2;dz<=2;dz++)
                if(Read(new(x+dx,y+dy,z+dz)) is {} other)nearby[other.Cell]=other;
            // Index virtual sockets too: a large unfinished layout must not
            // rescan every pending piece for every step, or miss long sections.
            for(var dx=-1;dx<=1;dx++)for(var dy=-1;dy<=1;dy++)for(var dz=-1;dz<=1;dz++)
                if(plannedSockets.TryGetValue(new(x+dx,y+dy,z+dz),out var bucket))
                    foreach(var other in bucket)if(physical.GetValueOrDefault(other.Cell)==null)nearby.TryAdd(other.Cell,other);
            foreach(var other in nearby.Values)for(var endpoint=0;endpoint<2;endpoint++)
                if(rail.Connects(end,other,endpoint))yield return(other,endpoint);
        }
        IEnumerable<VoxelRail> LandingCandidates(NVector point,NVector direction)
        {
            var horizontal=new NVector(direction.X,0,direction.Z);
            if(horizontal.LengthSquared()<.01f)yield break;
            horizontal=NVector.Normalize(horizontal);var seen=new HashSet<RailCell>();
            foreach(var rail in planned.Values)if(Read(rail.Cell) is {} current && seen.Add(current.Cell))yield return current;
            for(var distance=1;distance<=BlueprintConnections.MaximumJumpDistance;distance++)
            {
                var p=point+horizontal*distance;
                // Indexed objects cover long landing pieces; terrain queries
                // cover only the narrow flight corridor, never the whole world.
                foreach(var rail in CoasterRailComponent.Near(p))if(Read(rail.Cell) is {} current && seen.Add(current.Cell))yield return current;
                var x=(int)MathF.Round(p.X);var z=(int)MathF.Round(p.Z);var y=(int)MathF.Round(point.Y+.5f);
                for(var dx=-1;dx<=1;dx++)for(var dz=-1;dz<=1;dz++)
                for(var dy=-(int)BlueprintConnections.MaximumJumpHeight-1;dy<=(int)BlueprintConnections.MaximumJumpHeight+1;dy++)
                    if(Read(new(x+dx,y+dy,z+dz)) is {Profile.Coaster:true} rail && seen.Add(rail.Cell))yield return rail;
            }
        }
        var result=CoasterBlueprintScanner.Scan(station,Connected,LandingCandidates,Describe);
        var candidate=result.Document with{Name=original.Name};
        var ids=candidate.Pieces.Select(n=>n.Id).ToHashSet();
        // Do not discard an authored branch or a repair preview just because an
        // ambiguous junction or the bounded jump search cannot reach it.
        if(original.Pieces.Any(n=>!ids.Contains(n.Id)))
        {
            var retained=original.Pieces.Select(n=>n with{Built=IsConstructed(n)}).ToArray();
            candidate=original with{Pieces=retained};
            Status="Some planned pieces could not be reached. Your full plan was retained; clear the active plan to rescan only placed track. "+result.Warning;
        }
        else Status=$"Synced {result.Forward} pieces in front and {result.Behind} behind this station. "+result.Warning;
        if(!CoasterBlueprintPlan.TryRead(CoasterBlueprintPlan.Serialize(candidate),out _,out var error))
        { Status="Placed track needs review; your plan was retained. "+error; return; }
        var gaps=candidate.Pieces.Select((n,i)=>(n,i)).Where(p=>p.n.JumpBefore && p.i>0).ToArray();
        if(gaps.Length>0)
        {
            var needsLaunch=gaps.Count(p=>
            {
                var left=candidate.Pieces[p.i-1];var right=p.n;
                return BlueprintConnections.EstimatedLaunchSpeed(Definition(left).Socket(left,CoasterBlueprintPlan.Exit(left)),
                    CoasterBlueprintPlan.Heading(left,CoasterBlueprintPlan.Exit(left)),Definition(right).Socket(right,CoasterBlueprintPlan.Entry(right)))==null;
            });
            Status+=$" {gaps.Length} jump gaps retained.";
            if(needsLaunch>0)Status+=$" {needsLaunch} need a raised/uphill launch; alignment alone does not guarantee a safe jump.";
        }
        var json=CoasterBlueprintPlan.Serialize(candidate);
        if(json!=PlanJson)
        { SelectedIndex=selectedId is {} id?Array.FindIndex(candidate.Pieces,n=>n.Id==id):-1; Commit(candidate,false); }
        else { Interlocked.Exchange(ref validationDirty,1); RequestReconcile(); WatchPlan(); Reconcile(); }
    }

    public override void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/PostInitialize", this.Parent);
        base.PostInitialize();
        lock(EditorListGate) { Editors[Parent.ObjectID] = this; Volatile.Write(ref editorSnapshot,Editors.Values.ToArray()); }
        if (!string.IsNullOrEmpty(PlanJson) && !CoasterBlueprintPlan.TryRead(PlanJson, out _, out var error))
        { ShowPreview = false; Editing = false; Status = "Saved plan needs review: " + error; }
        Publish();
    }
    public override void Tick()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Tick", this.Parent);
        base.Tick();
        if (Volatile.Read(ref validationDirty)==0 && Volatile.Read(ref reconcileRequested)==0 &&
            (previewLimitEpoch<0 || previewLimitEpoch==CoasterBlueprintGhostComponent.Epoch)) return;
        if (!WorldObjectManager.Init.Initialized || !gate.Wait(0)) return;
        try { Reconcile(); }
        finally { gate.Release(); }
    }
    public override void Destroy()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Destroy", this.Parent);
        lock(EditorListGate) { Editors.TryRemove(Parent.ObjectID, out _); Volatile.Write(ref editorSnapshot,Editors.Values.ToArray()); }
        foreach (var ghost in CoasterBlueprintGhostComponent.ForStation(Parent.ObjectID)) ghost.Destroy();
        base.Destroy();
    }
    private void Action(Player player, Action operation, bool edit = true)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Action", this.Parent);
        if (!CanEdit(player) || (edit && !Editing)) return;
        if (!gate.Wait(0)) { player.InfoBoxLocStr("A blueprint action is already running. Try again in a moment."); return; }
        try { operation(); Publish(); }
        catch (Exception e) { Eco.Shared.Logging.Log.WriteErrorLineLoc($"Coaster blueprint action failed: {e}"); Status = "Blueprint action failed; see the server log."; }
        finally { gate.Release(); }
    }
    private void Menu(Player player,string title,IReadOnlyList<(string Label,Action<Player> Run)> options) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Menu", this.Parent); StartDialog(player,async version =>
    {
        var pick=await player.OptionBox(Localizer.DoStr(title),options.Select(o=>o.Label).ToList());
        if(pick<0 || pick>=options.Count || !CanEdit(player))return;
        if(version!=revision) {player.InfoBoxLocStr("The route changed while this menu was open. Open it again to continue.");return;}
        options[pick].Run(player);
    }); }
    private void PrepareEdit(Player player,bool? behind=null)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/PrepareEdit", this.Parent);
        if(!Editing)BeginBlueprint(player);
        if(!Editing || behind==null)return;
        Action(player,()=> {ExtendBehind=behind.Value;this.Changed(nameof(BuildEnd));Parent.SetDirty();});
    }
    [RPC, Autogen, UITypeName("BigButton")]
    public void BuildTrack(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/BuildTrack", this.Parent); Menu(player,"Where shall we build?",new (string,Action<Player>)[]
    {
        ("Extend the front — choose a rail",p=> {PrepareEdit(p,false);AddTrack(p);}),
        ("Extend the back — choose a rail",p=> {PrepareEdit(p,true);AddTrack(p);}),
        ($"Repeat {NextPiece} ({(ExtendBehind?"back":"front")})",AddNextRail),
        ("Show previews and begin editing",BeginBlueprint),
        ("Finish planning — ready to build",FinishBlueprint)
    }); }
    [RPC, Autogen, UITypeName("BigButton")]
    public void AddNextRail(Player player) {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/AddNextRail", this.Parent);PrepareEdit(player);AddNextPiece(player);}
    [RPC, Autogen, UITypeName("BigButton")]
    public void EditLayout(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/EditLayout", this.Parent); Menu(player,"Shape your coaster",new (string,Action<Player>)[]
    {
        ("Select a rail",SelectPlannedPiece),
        ("Replace the selected preview",p=> {PrepareEdit(p);ReplaceSelectedPiece(p);}),
        ("Insert after the selected rail",p=> {PrepareEdit(p);InsertAfterSelected(p);}),
        ("Remove the selected preview",p=> {PrepareEdit(p);DeleteSelectedPiece(p);}),
        ("Select the previous rail",PreviousPiece),("Select the next rail",NextPlannedPiece),
        ("Undo the last edit",p=> {PrepareEdit(p);Undo(p);}),("Redo",p=> {PrepareEdit(p);Redo(p);})
    }); }
    [RPC, Autogen, UITypeName("BigButton")]
    public void ViewAndInspect(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ViewAndInspect", this.Parent); Menu(player,"Explore your route",new (string,Action<Player>)[]
    {
        ("Refresh from placed track",SyncPlacedTrack),
        ("Jump to the front end",p=>SelectEnd(p,false)),("Jump to the back end",p=>SelectEnd(p,true)),
        ("Find a jump gap",SelectJump),("Waypoint to the selected rail",WaypointSelectedPiece),
        ("Build the selected preview",ConstructSelectedPiece),("Check supports and permissions",ValidateBlueprint),
        (ShowPreview?"Hide previews":"Show previews",ShowPreview?HideBlueprint:FinishBlueprint),
        ("Clear this plan (keep placed rails)",CancelBlueprint)
    }); }
    [RPC, Autogen, UITypeName("BigButton")]
    public void SavedLayoutsMenu(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/SavedLayoutsMenu", this.Parent); Menu(player,"Your coaster collection",new (string,Action<Player>)[]
    {
        ("Save this layout",SaveBlueprint),("Load a saved layout",LoadBlueprint),
        ("Import a shared layout",ImportBlueprint)
    }); }
    private void SelectEnd(Player player,bool behind){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/SelectEnd", this.Parent); Action(player,()=>
    {SelectedIndex=behind?0:Plan.Pieces.Length-1;RefreshSelection();},false); }
    private void SelectJump(Player player){ using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/SelectJump", this.Parent); Action(player,()=>
    {
        var next=Array.FindIndex(Plan.Pieces,Math.Clamp(SelectedIndex+1,0,Plan.Pieces.Length),n=>n.JumpBefore);
        if(next<0)next=Array.FindIndex(Plan.Pieces,n=>n.JumpBefore);
        if(next<0){Status="This route has no detected jump gaps.";return;}
        SelectedIndex=next;RefreshSelection();
    },false); }
    private void Commit(CoasterBlueprintDocument value, bool history = true)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Commit", this.Parent);
        if (history)
        {
            if (undo.Count >= 32) undo.Clear();
            undo.Push(CoasterBlueprintPlan.Serialize(Plan)); redo.Clear();
        }
        document = value; PlanJson = CoasterBlueprintPlan.Serialize(value); revision++;
        SelectedIndex = Math.Clamp(SelectedIndex, -1, Plan.Pieces.Length - 1);
        Interlocked.Exchange(ref validationDirty, 1); RequestReconcile(); WatchPlan(); Parent.SetDirty(); Reconcile();
    }
    private void Edit(Player player, IEnumerable<BlueprintNode> requested, int selected)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Edit", this.Parent);
        if (!CoasterBlueprintPlan.TryLayout(requested, out var fitted, out var error, Plan.AnchorId)) { Status = error; player.InfoBoxLocStr(error); return; }
        foreach (var built in Plan.Pieces.Where(p => p.Built))
            if (!fitted.Any(n => n.Id == built.Id && n.Piece == built.Piece && n.Position == built.Position && n.Turns == built.Turns))
            { Status = "This would move or remove a constructed section. Start a separate plan for that change."; return; }
        SelectedIndex = selected; Commit(Plan with { Pieces = fitted }); Status = "Blueprint updated.";
    }
    [RPC] public void BeginBlueprint(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/BeginBlueprint", this.Parent); Action(player, () =>
    {
        if (Plan.Pieces.Length == 0 && !AtStation(player)) return;
        SynchronizePlaced();
        Editing = true; ShowPreview = true; Status = "Editing blueprint; no materials are consumed until construction.";
        Parent.SetDirty(); Reconcile();
    }, false); }
    [RPC] public void SyncPlacedTrack(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/SyncPlacedTrack", this.Parent); Action(player, SynchronizePlaced, false); }
    [RPC] public void ChooseBuildEnd(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ChooseBuildEnd", this.Parent); StartDialog(player, async version =>
    {
        var pick = await player.OptionBox(Localizer.DoStr("Extend which end of the blueprint?"),new List<string>{"In front of the station","Behind the station"});
        Action(player,()=> { if(version!=revision || pick is <0 or >1)return; ExtendBehind=pick==1; this.Changed(nameof(BuildEnd)); Parent.SetDirty(); Status="Build end: "+BuildEnd; });
    }); }
    [RPC] public void FinishBlueprint(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/FinishBlueprint", this.Parent); Action(player, () =>
    { Editing = false; ShowPreview = true; Reconcile(); Status = "Blueprint ready. Walk to the previews and construct with the required items."; Parent.SetDirty(); }, false); }
    [RPC] public void HideBlueprint(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/HideBlueprint", this.Parent); Action(player, () =>
    { Editing = false; ShowPreview = false; Reconcile(); Status = "Preview hidden. The plan and constructed rails are retained."; Parent.SetDirty(); }, false); }
    [RPC] public void CancelBlueprint(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/CancelBlueprint", this.Parent); StartDialog(player, async version =>
    {
        var answer = await player.InputString(Localizer.DoStr("Type CLEAR to discard the active plan. Constructed rails and saved layouts are retained."));
        Action(player, () => { if (revision != version || answer != "CLEAR") return; Editing = false; ShowPreview = false; SelectedIndex = -1; Commit(new()); Status = "Active plan cleared."; }, false);
    }); }
    [RPC] public void AddNextPiece(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/AddNextPiece", this.Parent); Action(player, () =>
    {
        if (!CoasterBlueprintPlan.Catalog.ContainsKey(nextPiece)) return;
        var node = new BlueprintNode { Piece = nextPiece };
        Edit(player, ExtendBehind ? Plan.Pieces.Prepend(node) : Plan.Pieces.Append(node), ExtendBehind ? 0 : Plan.Pieces.Length);
        if (!ExtendBehind && Plan.Pieces.LastOrDefault()?.Id == node.Id && CoasterTerrainPath.All.FirstOrDefault(p => p.Key == nextPiece)?.NextKey is { } next)
            nextPiece = next;
    }); }
    [RPC] public void AddForward(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/AddForward", this.Parent); Action(player, () =>
        Edit(player, Plan.Pieces.Append(new BlueprintNode { Piece = "CoasterTrackStraightSection01" }), Plan.Pieces.Length)); }
    [RPC] public void AddBehind(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/AddBehind", this.Parent); Action(player, () =>
        Edit(player, Plan.Pieces.Prepend(new BlueprintNode { Piece = "CoasterTrackStraightSection01" }), 0)); }
    [RPC] public void ChooseNextPiece(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ChooseNextPiece", this.Parent); ChoosePiece(player, false, false); }
    [RPC] public void AddTrack(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/AddTrack", this.Parent); ChoosePiece(player, false, false, true); }
    [RPC] public void ReplaceSelectedPiece(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ReplaceSelectedPiece", this.Parent); ChoosePiece(player, true, false); }
    [RPC] public void InsertAfterSelected(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/InsertAfterSelected", this.Parent); ChoosePiece(player, false, true); }
    private void ChoosePiece(Player player, bool replace, bool insert, bool append = false) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ChoosePiece", this.Parent); StartDialog(player, async version =>
    {
        if (!Editing) return;
        BlueprintNode[] basis; int selection; Guid anchor; bool behind;
        if (!gate.Wait(0)) return;
        try { basis = Plan.Pieces.ToArray(); selection = SelectedIndex; anchor=Plan.AnchorId; behind=ExtendBehind; }
        finally { gate.Release(); }
        if ((replace || insert) && (selection < 0 || selection >= basis.Length)) return;
        var options = CoasterBlueprintPlan.Catalog.Values.Where(d =>
        {
            if(!Eco.Mods.TechTree.CoasterTrackNames.IsAvailable(d.Key))return false;
            if(!replace && !insert)return CoasterBlueprintPlan.CanExtend(basis,d.Key,behind,anchor);
            var preview = basis.ToList();
            if (replace) preview[selection] = preview[selection] with { Piece = d.Key };
            else preview.Insert(insert ? selection + 1 : behind ? 0 : preview.Count, new() { Piece = d.Key });
            return CoasterBlueprintPlan.TryLayout(preview, out _, out _, anchor);
        }).OrderBy(d => d.Category).ThenBy(d => d.Name).ToArray();
        if (options.Length == 0) { player.InfoBoxLocStr("No track pieces fit this connection."); return; }
        var groups=options.GroupBy(d=>(d.Category,Choice:CoasterNames.ChoiceFamily(d.Key)??d.Key)).ToArray();
        var pick = await player.OptionBox(Localizer.DoStr(replace ? "Replace the selected track piece" : insert ? "Insert track after the selected piece" : "Choose compatible track"),
            groups.Select(g=>$"{g.Key.Category}: {CoasterNames.ChoiceFamily(g.First().Key)??g.First().Name}").ToList());
        if(pick<0||pick>=groups.Length)return;
        var variants=groups[pick].ToArray();
        var variant=variants.Length==1?0:await player.OptionBox(Localizer.DoStr("Choose direction"),
            variants.Select(d=>CoasterNames.ChoiceDirection(d.Key)).ToList());
        if(variant<0||variant>=variants.Length)return;
        var selectedKey=variants[variant].Key;
        Action(player, () =>
        {
            if (revision != version) { Status = "Another editor changed the plan. Reopen piece selection."; return; }
            var key = selectedKey;
            if (!replace && !insert)
            {
                nextPiece = key;
                if (!append) { Status = "Next piece selected. Use Add Next Piece to extend the path."; return; }
                var node = new BlueprintNode { Piece = key };
                Edit(player, behind ? basis.Prepend(node) : basis.Append(node), behind ? 0 : basis.Length);
                if (!behind && Plan.Pieces.LastOrDefault()?.Id == node.Id && CoasterTerrainPath.All.FirstOrDefault(p => p.Key == key)?.NextKey is { } next)
                    nextPiece = next;
                return;
            }
            var changed = basis.ToList();
            if (replace) changed[selection] = changed[selection] with { Piece = key };
            else changed.Insert(selection + 1, new() { Piece = key });
            Edit(player, changed, replace ? selection : selection + 1);
        });
    }); }
    [RPC] public void PreviousPiece(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/PreviousPiece", this.Parent); Action(player, () => { SelectedIndex = Math.Max(0, SelectedIndex - 1); RefreshSelection(); }, false); }
    [RPC] public void NextPlannedPiece(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/NextPlannedPiece", this.Parent); Action(player, () => { SelectedIndex = Math.Min(Plan.Pieces.Length - 1, SelectedIndex + 1); RefreshSelection(); }, false); }
    [RPC] public void SelectPieceByNumber(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/SelectPieceByNumber", this.Parent); SelectPlannedPiece(player); }
    [RPC] public void SelectPlannedPiece(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/SelectPlannedPiece", this.Parent); StartDialog(player, async version =>
    {
        BlueprintNode[] options; int page;
        if (!gate.Wait(0)) return;
        try { options = Plan.Pieces.ToArray(); page=Math.Max(0,SelectedIndex)/32; }
        finally { gate.Release(); }
        if (options.Length == 0) { player.InfoBoxLocStr("Add track to the blueprint first."); return; }
        while(revision==version)
        {
            page=Math.Clamp(page,0,(options.Length-1)/32);var first=page*32;
            var entries=options.Skip(first).Take(32).Select((n,i)=>(Label:$"{first+i+1}. {Definition(n).Name}{(n.Built?" (built)":" (preview)")}{(n.JumpBefore?" — jump landing":"")}",Index:first+i)).ToList();
            if(page>0)entries.Insert(0,("< Previous page",-1));
            if(first+32<options.Length)entries.Add(("Next page >",-2));
            var pick=await player.OptionBox(Localizer.DoStr($"Choose a rail — page {page+1} of {(options.Length+31)/32}"),entries.Select(e=>e.Label).ToList());
            if(pick<0 || pick>=entries.Count)return;
            var selected=entries[pick].Index;
            if(selected<0){page+=selected==-1?-1:1;continue;}
            Action(player,()=> {if(revision==version){SelectedIndex=selected;RefreshSelection();}},false);return;
        }
        player.InfoBoxLocStr("The route changed while selecting a rail. Open the selector again.");
    }); }
    [RPC] public void DeleteSelectedPiece(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/DeleteSelectedPiece", this.Parent); Action(player, () =>
    {
        if (SelectedIndex < 0 || SelectedIndex >= Plan.Pieces.Length) return;
        Edit(player, Plan.Pieces.Where((_, i) => i != SelectedIndex), SelectedIndex - 1);
    }); }
    [RPC] public void Undo(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Undo", this.Parent); History(player, undo, redo); }
    [RPC] public void Redo(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Redo", this.Parent); History(player, redo, undo); }
    private void History(Player player, Stack<string> from, Stack<string> to) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/History", this.Parent); Action(player, () =>
    {
        if (from.Count == 0 || HasBuiltTrack) { Status = "Undo/redo is available before real construction starts."; return; }
        if (!CoasterBlueprintPlan.TryRead(from.Peek(), out var restored, out var error)) { Status = error; return; }
        to.Push(CoasterBlueprintPlan.Serialize(Plan)); from.Pop(); Commit(restored, false); Status = "Blueprint history restored.";
    }); }
    private void StartDialog(Player player, Func<int, Task> dialog)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/StartDialog", this.Parent);
        if (!CanEdit(player)) return;
        var version = revision;
        _ = Run();
        async Task Run()
        {
            try { await dialog(version); }
            catch (Exception e) { Eco.Shared.Logging.Log.WriteErrorLineLoc($"Coaster blueprint dialog failed: {e}"); }
        }
    }
    private Dictionary<string, string> ReadLibrary()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ReadLibrary", this.Parent);
        if (LibraryJson.Length > CoasterBlueprintPlan.MaxJsonLength*24+4096) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(LibraryJson) ?? new(); }
        catch (JsonException) { return new(); }
    }
    [RPC] public void SaveBlueprint(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/SaveBlueprint", this.Parent); StartDialog(player, async version =>
    {
        var name = await player.InputString(Localizer.DoStr("Name this reusable layout (up to 80 characters)."), Localizer.DoStr(Plan.Name));
        Action(player, () =>
        {
            if (revision != version || string.IsNullOrWhiteSpace(name) || name.Length > 80) return;
            var library = ReadLibrary(); if (library.Count >= 12 && !library.ContainsKey(name)) { Status = "This station already stores 12 layouts."; return; }
            var value = Plan with { Name = name.Trim(), Pieces = Plan.Pieces.Select(n => n with { Built = false }).ToArray() };
            library[value.Name] = CoasterBlueprintPlan.Serialize(value); LibraryJson = JsonSerializer.Serialize(library);
            Parent.SetDirty(); this.Changed(nameof(SavedLayouts)); Status = "Saved layout: " + value.Name;
        }, false);
    }); }
    [RPC] public void LoadBlueprint(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/LoadBlueprint", this.Parent); StartDialog(player, async version =>
    {
        var options = Editors.Values.Where(e => !e.Parent.IsDestroyed && e.Parent.IsAuthorized(player.User, AccessType.FullAccess)).SelectMany(e => e.ReadLibrary().Select(entry => (Station: e.Parent.DisplayName.ToString(), entry.Key, entry.Value))).Take(144).ToArray();
        if (options.Length == 0) { player.InfoBoxLocStr("No saved layouts are available. Save a blueprint first."); return; }
        var choice = await player.OptionBox(Localizer.DoStr("Load a saved layout at this station"), options.Select(o => $"{o.Key} ({o.Station})").ToList());
        Action(player, () =>
        {
            if (revision != version || HasBuiltTrack) { Status = "Start an empty plan to load a layout."; return; }
            if (choice < 0 || choice >= options.Length) return;
            LoadText(player, options[choice].Value);
        }, false);
    }); }
    private string CreateExportJson()
    {
        if (!gate.Wait(0)) throw new InvalidOperationException("A blueprint action is already running. Try exporting again in a moment.");
        try
        {
            var snapshot = Plan;
            if (!snapshot.Pieces.Any(n => n.Piece != "CoasterStation"))
                throw new InvalidOperationException("There is no layout to export. Add or sync track to this station first.");
            var json = CoasterBlueprintPlan.Serialize(snapshot with
                { Pieces = snapshot.Pieces.Select(n => n with { Built = false }).ToArray() });
            if (json.Length > CoasterBlueprintPlan.MaxJsonLength)
                throw new InvalidOperationException("This layout exceeds the Import Blueprint size limit. Export was cancelled instead of truncating the JSON.");
            return json;
        }
        finally { gate.Release(); }
    }
    private static string WriteExportFile(string json, string directory, string name)
    {
        if (!CoasterBlueprintPlan.TryRead(json,out _,out var error,true))
            throw new InvalidOperationException("This layout could not be exported: " + error);
        Directory.CreateDirectory(directory);
        var safe=new string(name.Where(c=>char.IsLetterOrDigit(c)||c==' '||c=='-'||c=='_').Take(48).ToArray()).Trim();
        if(string.IsNullOrEmpty(safe))safe="Coaster";
        var path=Path.Combine(directory,$"Railworks Workshop Blueprint - {safe} - {Guid.NewGuid():N}.json");
        using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        using var writer=new StreamWriter(file,new System.Text.UTF8Encoding(false));
        writer.Write(json);
        return path;
    }
    [RPC, Autogen, UITypeName("BigButton")]
    public void ExportBlueprint(Player player)
    {
        using var scope = RailProfile.Measure("Blueprints/Editing and preview/ExportBlueprint", Parent);
        if (player == null) return;
        if (!CanEdit(player))
        {
            player.InfoBoxLocStr("You need full access to the blueprint station and must be within 512 metres of it to export.");
            return;
        }
        _ = ShowExport();
        async Task ShowExport()
        {
            try
            {
                var json = CreateExportJson();
                if(player.User.IsLocal() && OperatingSystem.IsWindows())
                {
                    var desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    if(!string.IsNullOrWhiteSpace(desktop))
                    {
                        var file=WriteExportFile(json,desktop,Plan.Name);
                        Status="Blueprint JSON saved to desktop: "+Path.GetFileName(file);
                        this.Changed(nameof(Status));
                        player.InfoBoxLocStr("Blueprint exported to your desktop:\n"+file+"\n\nOpen the JSON file to select/copy it, or paste its full contents into Import Blueprint. Your active plan and saved layouts were unchanged.");
                        return;
                    }
                }
                // JSON is DATA, not a dynamic localization key. Native large
                // input provides multiline selection, Ctrl+A and Ctrl+C. Its
                // returned edited/cancelled text is deliberately never committed.
                Status = $"Export ready: {Plan.Pieces.Length} pieces, {json.Length} characters. Copy from the JSON window; closing it keeps this plan.";
                this.Changed(nameof(Status));
                // Match the client RPC's string parameters directly. Close only
                // this player's station window before opening the native modal;
                // neither the snapshot nor any saved layout is edited.
                player.Client.RPC("CloseUI", player.Client, "WorldObjectUI");
                Eco.Shared.Logging.Log.WriteLineLoc($"Blueprint export opening at station {Parent.ObjectID}: {json.Length} characters.");
                await player.Client.RPCAsync<string>("PopupLargeInputString", player.Client,
                    "Export Blueprint: click the JSON text box, then Ctrl+A and Ctrl+C. Closing or editing it does not change your blueprint.",
                    json, CoasterBlueprintPlan.MaxJsonLength);
            }
            catch (InvalidOperationException e) { Status=e.Message; this.Changed(nameof(Status)); player.InfoBoxLocStr(e.Message); }
            catch (Exception e)
            {
                Eco.Shared.Logging.Log.WriteErrorLineLoc($"Blueprint export failed at station {Parent.ObjectID}: {e}");
                player.InfoBoxLocStr("Blueprint export could not open the JSON text box. Your layout and saved blueprints were not changed. See the server log for the error.");
            }
        }
    }
    [RPC] public void ImportBlueprint(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ImportBlueprint", this.Parent); StartDialog(player, async version =>
    {
        var json = await player.InputLargeString(Localizer.DoStr("Paste exported blueprint JSON. This replaces an unconstructed active plan."), maxLength: CoasterBlueprintPlan.MaxJsonLength);
        Action(player, () => { if (revision == version && !HasBuiltTrack) LoadText(player, json); }, false);
    }); }
    private void LoadText(Player player, string text)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/LoadText", this.Parent);
        if (!CoasterBlueprintPlan.TryRead(text, out var loaded, out var error, true)) { Status = error; player.InfoBoxLocStr(error); return; }
        Editing = true; ShowPreview = true; SelectedIndex = loaded.Pieces.Length - 1; Commit(loaded); Status = "Layout loaded relative to this station.";
    }
    [RPC] public void WaypointSelectedPiece(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/WaypointSelectedPiece", this.Parent); Action(player, () =>
    {
        if (SelectedIndex < 0 || SelectedIndex >= Plan.Pieces.Length) return;
        var n = Plan.Pieces[SelectedIndex]; var p = PositionOf(n) + RotationOf(n).RotateVector(Definition(n).PlacementOffset + Definition(n).Path.Point(.5f)) - NVector.UnitY * .5f;
        player.DropExactWaypoint(new((int)MathF.Round(p.X), (int)MathF.Round(p.Y), (int)MathF.Round(p.Z)), "Coaster blueprint: " + Definition(n).Name);
        Status = "Personal waypoint placed at the selected piece; use the normal map/camera to navigate.";
    }, false); }
    [RPC] public void ValidateBlueprint(Player player) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ValidateBlueprint", this.Parent); Action(player, () =>
    { Interlocked.Exchange(ref validationDirty,1); Reconcile(); RefreshValidation(player); RefreshSelection(); Status = "Placement checks refreshed for your permissions. Real construction also runs Eco's native law and inventory checks."; }, false); }
    internal void SelectGhost(Player player, Guid id)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/SelectGhost", this.Parent);
        Action(player, () => { var i = Array.FindIndex(Plan.Pieces, n => n.Id == id); if (i >= 0) { SelectedIndex = i; RefreshSelection(); Parent.OpenUI(player); } }, false);
    }
    private void RefreshSelection()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/RefreshSelection", this.Parent);
        RequestReconcile();Reconcile();
        foreach (var pair in ghosts)
            if (!pair.Value.IsDestroyed) pair.Value.Show(validation.GetValueOrDefault(pair.Key, "Clear"), Plan.Pieces.ElementAtOrDefault(SelectedIndex)?.Id == pair.Key);
    }
    private bool IsConstructed(BlueprintNode n)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/IsConstructed", this.Parent);
        var d = Definition(n); var pos = PositionOf(n); var rot = RotationOf(n);
        if (d.Terrain)
        {
            var block=Eco.World.World.GetBlock(new Vector3i((int)MathF.Round(pos.X),(int)MathF.Round(pos.Y),(int)MathF.Round(pos.Z)));
            // Eco's unshaped origin block is also a real straight rail. Compare
            // its normalized profile so old default placements are recognized.
            return block!=null && CoasterTerrainPath.TryProfile(block.GetType().Name,out var profile) &&
                profile.Shape==d.Path.Key && profile.QuarterTurns==(StationTurns+n.Turns)%4;
        }
        var expected = ContentType(n.Piece + "Object");
        return CoasterRailComponent.Near(pos).Any(rail => NVector.Distance(rail.Cell.Origin + NVector.UnitY * .5f, pos + rot.RotateVector(d.PlacementOffset)) < .002f &&
            rail.Profile.Shape == d.Path.Key && rail.Profile.QuarterTurns == (StationTurns + n.Turns) % 4 && CoasterRailComponent.ObjectAt(rail.Cell)?.GetType() == expected);
    }
    private Type? BlockType(BlueprintNode n) { using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/BlockType", this.Parent); return ContentType(n.Piece + (((StationTurns + n.Turns) % 4) == 0 ? "" : "R" + (((StationTurns + n.Turns) % 4) * 90)) + "Block"); }
    private IEnumerable<Vector3i> Footprint(BlueprintNode n)
    {
        var pos = PositionOf(n); var rot = RotationOf(n); var d = Definition(n);
        if (d.Terrain) { yield return new((int)MathF.Round(pos.X), (int)MathF.Round(pos.Y), (int)MathF.Round(pos.Z)); yield break; }
        var type = ContentType(n.Piece + "Object"); if (type == null) yield break;
        foreach (var occ in WorldObject.GetOccupancy(type))
        { var p = pos + rot.RotateVector((NVector)occ.Offset); yield return new((int)MathF.Round(p.X), (int)MathF.Round(p.Y), (int)MathF.Round(p.Z)); }
        if (n.Piece != "CoasterStation") foreach (var end in CoasterSnapHelpers.Ends(n.Piece, pos, rot)) yield return end.Cell;
    }
    private string Check(BlueprintNode n, Player? player = null)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Check", this.Parent);
        foreach (var cell in Footprint(n).Distinct())
        {
            if (cell.Y < 1 || cell.Y >= Eco.World.World.VoxelSize.Y - 1) return "Blocked: outside world height limits";
            if (cell.X < 0 || cell.Z < 0 || cell.X >= Eco.World.World.VoxelSize.X || cell.Z >= Eco.World.World.VoxelSize.Z)
                return "Blocked: blueprint crosses the world wrap seam; use a starting station away from the seam";
            var block = Eco.World.World.GetBlock(cell);
            if (block is not EmptyBlock && block is not IWaterBlock) return block is WorldObjectBlock ? "Blocked: intersects a structure" : block is CoasterTrackBlock ? "Blocked: another rail occupies this cell" : "Blocked: requires terrain/block removal";
            if (player != null && !(bool)ServiceHolder<IAuthManager>.Obj.IsAuthorized(cell.XZ.ToPlotPos(), player.User, AccessType.FullAccess, null, out _))
                return "Blocked: no building permission here";
        }
        // Exact footprint protection is deliberately conservative: complete
        // sections cannot overlap occupancy even if their centre lines clear.
        var d = Definition(n); var pos = PositionOf(n); var rotation = RotationOf(n);
        if (!d.Terrain && n.Piece != "CoasterStation" && !CoasterSpecialPlacement.IsSupported(n.Piece, pos, rotation))
            return "Warning: needs a real connected rail or grounded support before construction";
        var below = new Vector3i((int)MathF.Round(pos.X), (int)MathF.Round(pos.Y) - 1, (int)MathF.Round(pos.Z));
        if (d.Terrain && Eco.World.World.GetBlock(below) is EmptyBlock) return "Warning: no support directly below";
        return "Clear";
    }
    private void RefreshValidation(Player? player = null)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/RefreshValidation", this.Parent);
        Interlocked.Exchange(ref validationDirty, 0);
        validation.Clear();
        var occupied = new Dictionary<Vector3i, Guid>(); var conflicts = new HashSet<Guid>();
        foreach (var node in Plan.Pieces.Where(n => !n.Built))
        {
            validation[node.Id] = Check(node, player);
            foreach (var cell in Footprint(node).Distinct())
                if (occupied.TryGetValue(cell, out var previous) && previous != node.Id)
                { conflicts.Add(previous); conflicts.Add(node.Id); }
                else occupied[cell] = node.Id;
        }
        foreach (var id in conflicts) validation[id] = "Blocked: planned pieces overlap the same construction cell";
    }
    private void Reconcile()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Reconcile", this.Parent);
        if (!WorldObjectManager.Init.Initialized || Parent.IsDestroyed) return;
        Interlocked.Exchange(ref reconcileRequested,0); previewLimitEpoch=-1;
        if(watchedCells.Count==0 && Plan.Pieces.Length>0) WatchPlan();
        foreach (var g in CoasterBlueprintGhostComponent.ForStation(Parent.ObjectID))
        {
            if (ghosts.TryGetValue(g.StepId, out var duplicate) && duplicate != g && !duplicate.IsDestroyed) { g.Destroy(); continue; }
            ghosts[g.StepId] = g;
        }
        if(Volatile.Read(ref validationDirty)!=0)
        {
            var updated = Plan.Pieces.Select(n => n with { Built=IsConstructed(n) }).ToArray();
            if(!updated.SequenceEqual(Plan.Pieces))
            {
                document=Plan with{Pieces=updated}; PlanJson=CoasterBlueprintPlan.Serialize(document);
                undo.Clear();redo.Clear();revision++;Parent.SetDirty();
            }
        }
        // Show the tips and the selection neighborhood, not thousands of
        // persisted ghosts. Selection moves this window without altering plans.
        var active=(ShowPreview?CoasterBlueprintPlan.PreviewWindow(Plan,SelectedIndex):[]).ToDictionary(n=>n.Id);
        foreach (var pair in ghosts.ToArray())
            if (pair.Value.IsDestroyed || !active.TryGetValue(pair.Key, out var n) || pair.Value.PieceKey != n.Piece)
            { if (!pair.Value.IsDestroyed) pair.Value.Destroy(); ghosts.Remove(pair.Key); }
        if (Volatile.Read(ref validationDirty) != 0) RefreshValidation();
        int created = 0;
        foreach (var n in active.Values)
        {
            if (!validation.ContainsKey(n.Id)) validation[n.Id] = Check(n);
            if (!ghosts.TryGetValue(n.Id, out var ghost))
            {
                if (created++ >= 16) { RequestReconcile(); break; }
                var type = ContentType(Definition(n).GhostName); if (type == null) continue;
                lock (PreviewCreationGate)
                {
                    if (CoasterBlueprintGhostComponent.Live.Count >= 512) { previewLimitEpoch=CoasterBlueprintGhostComponent.Epoch; Status = "Server preview limit reached (512). Hide another blueprint to free preview space."; break; }
                    // Construct first so its static empty-occupancy declaration
                    // is initialized. Never use ForceAdd(validatePlacement:false):
                    // that API clears the footprint before creating the object.
                    ghost = (CoasterBlueprintGhostObject)Activator.CreateInstance(type)!;
                    if (WorldObject.GetOccupancy(type).Count != 0)
                        throw new InvalidOperationException("A planning preview must have empty occupancy: " + type.Name);
                    ghost.StationId = Parent.ObjectID; ghost.StepId = n.Id;
                    ServiceHolder<IWorldObjectManager>.Obj.Add(ghost, null!, PositionOf(n), RotationOf(n));
                }
                if (ghost == null) continue; ghosts[n.Id] = ghost;
            }
            var targetPosition = PositionOf(n); var targetRotation = RotationOf(n);
            if (ghost.Position != targetPosition || ghost.Rotation != targetRotation)
            { ghost.Position = targetPosition; ghost.Rotation = targetRotation; ghost.SyncPositionAndRotation(); }
            ghost.Show(validation[n.Id], n.Id == Plan.Pieces.ElementAtOrDefault(SelectedIndex)?.Id);
        }
        Publish();
    }
    [RPC] public void ConstructSelectedPiece(Player player)
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/ConstructSelectedPiece", this.Parent);
        if (SelectedIndex >= 0 && SelectedIndex < Plan.Pieces.Length) _ = Construct(player, Plan.Pieces[SelectedIndex].Id);
        else player.InfoBoxLocStr("Select a planned piece before constructing it.");
    }
    internal async Task Construct(Player player, Guid id)
    {
        if (!CanEdit(player)) { player.InfoBoxLocStr("You need full access to the blueprint station and must be within 512 metres of it."); return; }
        if (!await gate.WaitAsync(0)) { player.InfoBoxLocStr("A blueprint action is already running. Try again in a moment."); return; }
        try
        {
            var n = Plan.Pieces.FirstOrDefault(n => n.Id == id);
            if (n == null || n.Built) { player.InfoBoxLocStr("This planned piece has already been constructed or removed."); return; }
            var d = Definition(n); var pos = PositionOf(n); var rot = RotationOf(n);
            var nearest = d.Path.Nearest(BlueprintPieceDefinition.Rotate(player.User.Position - pos + NVector.UnitY * .5f, -(StationTurns + n.Turns)) - d.PlacementOffset);
            if (nearest.Distance > 6) { Status = "Walk within six metres of the planned rail to construct it."; player.InfoBoxLocStr(Status); return; }
            RefreshValidation(player);
            var check = validation[n.Id];
            if (check.StartsWith("Blocked")) { Status = check; player.InfoBoxLocStr(check); return; }
            var inv = player.User.Inventory;
            if (d.Terrain)
            {
                if (inv.CarriedItem is not CoasterTrackItem || inv.Toolbar.SelectedItem is not BuildingToolItem)
                { Status = "Carry Roller Coaster Rail and hold a hammer to construct this modular piece."; player.InfoBoxLocStr(Status); return; }
                var block = BlockType(n); if (block == null) { player.InfoBoxLocStr("The block type for this planned piece is unavailable."); return; }
                var context = new MultiblockActionContext { Player = player, AccessNeeded = AccessType.FullAccess, ToolUsed = inv.Toolbar.SelectedItem,
                    CaloriesPerAction = 1, Area = new[] { new Vector3i((int)MathF.Round(pos.X), (int)MathF.Round(pos.Y), (int)MathF.Round(pos.Z)) } };
                AtomicActions.PlaceBlockNow(context, block, createBlockAction: true, removeFrom: inv, removeItem: typeof(CoasterTrackItem));
            }
            else
            {
                var type = ContentType(n.Piece + "Item");
                var item = inv.CarriedItem?.GetType() == type ? inv.CarriedItem : inv.Toolbar.SelectedItem?.GetType() == type ? inv.Toolbar.SelectedItem : null;
                if (item is not IPlaceableItem placeable) { Status = "Hold the crafted item for " + d.Name + " to construct this preview."; player.InfoBoxLocStr(Status); return; }
                var stack = inv.CarriedItem == item ? inv.Carried.SelectedStack : inv.Toolbar.SelectedStack;
                if (stack.Quantity < 1) { player.InfoBoxLocStr("The selected construction item stack is empty."); return; }
                await WorldObjectPlacementUtils.TryPlaceWorldObjectNow(player, placeable, stack, pos, rot, 0);
            }
            Interlocked.Exchange(ref validationDirty,1);
            Reconcile(); Status = IsConstructed(n) ? "Planned rail constructed. Its preview has been removed." : "Placement was rejected by Eco. Check the native message, supports, and building permissions.";
            if (!IsConstructed(n)) player.InfoBoxLocStr(Status);
            Publish();
        }
        catch (Exception e) { Eco.Shared.Logging.Log.WriteErrorLineLoc($"Blueprint construction failed: {e}"); Status = "Construction failed; the plan remains available. See the server log."; player.InfoBoxLocStr(Status); }
        finally { Publish(); RefreshSelection(); gate.Release(); }
    }
    private void Publish()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/Publish", this.Parent);
        var next = CoasterBlueprintPlan.Catalog.GetValueOrDefault(nextPiece)?.Name ?? nextPiece;
        var n = Plan.Pieces.ElementAtOrDefault(SelectedIndex);
        var current = n == null ? "Select a rail in Edit, or press E on a preview." : $"{SelectedIndex + 1:N0} / {Plan.Pieces.Length:N0}: {Definition(n).Name}\n{(n.Built ? "Built" : validation.GetValueOrDefault(n.Id, "Not checked"))}{(n.JumpBefore?" — landing after a jump":"")}";
        var anchor=Array.FindIndex(Plan.Pieces,p=>p.Id==Plan.AnchorId);
        var built=Plan.Pieces.Count(p=>p.Built);var gaps=Plan.Pieces.Count(p=>p.JumpBefore);
        var summary=$"{Plan.Pieces.Length:N0} / {CoasterBlueprintPlan.MaxPieces:N0} pieces  |  {built:N0} built  |  {Plan.Pieces.Length-built:N0} planned  |  {gaps} jumps\n"+
            $"Front: {Plan.Pieces.Length-(anchor<0?0:anchor+1):N0}  |  Back: {Math.Max(0,anchor):N0}  |  Adding: {(ExtendBehind?"back":"front")} — {next}";
        var centre=SelectedIndex>=0?SelectedIndex:Math.Max(0,anchor);var first=Math.Max(0,centre-3);
        var layout=string.Join("\n",Plan.Pieces.Skip(first).Take(7).Select((p,i)=>$"{first+i+1}. {(first+i==SelectedIndex?"> ":"")}{(p.Id==Plan.AnchorId?"Station: ":"")}{Definition(p).Name}{(p.JumpBefore?" [jump]":"")}{(p.Built?" [built]":" [preview]")}"));
        if(Plan.Pieces.Length>7)layout+="\nUse Edit to browse all rails; View jumps to either end or the next gap.";
        if(RouteSummary!=summary){RouteSummary=summary;this.Changed(nameof(RouteSummary));}
        if (CurrentPiece != current) { CurrentPiece = current; this.Changed(nameof(CurrentPiece)); }
        if (NextPiece != next) { NextPiece = next; this.Changed(nameof(NextPiece)); }
        if (layoutText != layout) { layoutText = layout; this.Changed(nameof(Layout)); }
        if (publishedStatus != Status) { publishedStatus = Status; this.Changed(nameof(Status)); }
    }
}

// Invalidation follows Eco's block-change stream instead of running physics
// overlap queries for every preview on every server update.
public sealed class CoasterBlueprintTerrainObserver : IModInit
{
    private static int subscribed;
    public static void PostInitialize()
    {
        using var _railProfileScope = Eco.Minecarts.Runtime.RailProfile.Measure("Blueprints/Editing and preview/PostInitialize");
        if (Interlocked.Exchange(ref subscribed, 1) == 0)
            Eco.World.World.OnBlockChanged.Add(CoasterBlueprintComponent.TerrainChanged);
    }
}
