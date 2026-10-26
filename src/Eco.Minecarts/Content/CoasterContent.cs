namespace Eco.Mods.TechTree;
using Eco.Core.Items;
using Eco.Core.Utils;
using Eco.Gameplay.Auth;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Placement;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Players;
using Eco.World.Blocks;
using Eco.Minecarts.Runtime;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;
using Eco.Shared.IoC;
using Eco.Shared.Items;
using Eco.Shared.Math;
using Eco.Shared.Voxel;

[Serialized,RequireComponent(typeof(PropertyAuthComponent)),RequireComponent(typeof(CoasterRailComponent))]
public abstract class CoasterRailObject : WorldObject,IRepresentsItem
{
    [Serialized] public bool EntrySnapOwned {get;set;}
    [Serialized] public bool ExitSnapOwned {get;set;}
    internal CoasterSnapOwnership SnapOwnership {get;}
    protected CoasterRailObject(){SnapOwnership=new(this);}
    public System.Numerics.Vector3 RailOffset=>this is CoasterStationObject?-System.Numerics.Vector3.UnitX:CoasterPath.Find(PathKey).PlacementOffset;
    public string PathKey=>this is CoasterStationObject?"CoasterTrackStraightSection01":GetType().Name.Replace("Object","");
    public Type RepresentedItemType=>GetType().Assembly.GetType("Eco.Mods.TechTree."+GetType().Name.Replace("Object","Item"))!;
    public override LocString DisplayName=>Localizer.DoStr(CoasterNames.Name(GetType().Name.Replace("Object","")));
    protected override void OnCreatePostInitialize()
    {
        base.OnCreatePostInitialize();
        SnapOwnership.Attach(create:true);
    }
    protected override void OnDestroy()
    {
        var cells=WorldOccupancy?.ToArray();var id=ObjectID;
        SnapOwnership.Detach(destroying:true);
        base.OnDestroy();
        // Eco occasionally leaves transient occupancy after hammer pickup.
        // Delete only this object's own placeholder, never a replacement rail.
        if(cells!=null)foreach(var cell in cells)
            if(Eco.World.World.GetBlock(cell) is WorldObjectBlock block&&block.WorldObjectHandle.Id==id)
                Eco.World.World.DeleteBlock(cell);
    }
    protected static void Occupy<T>(string key) where T:CoasterRailObject
    {
        var path=CoasterPath.Find(key=="CoasterStation"?"CoasterTrackStraightSection01":key);
        var cells=new HashSet<Eco.Shared.Math.Vector3i>{Eco.Shared.Math.Vector3i.Zero};
        var placementOffset=key=="CoasterStation"?System.Numerics.Vector3.Zero:path.PlacementOffset;
        for(var i=1;i<512;i++)
        {
            var p=path.Point(i/512f)+placementOffset-new System.Numerics.Vector3(0,.5f,0);
            if(key=="CoasterStation")p-=System.Numerics.Vector3.UnitX;
            for(var side=-1;side<=1;side++)
            {
                var right=System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(path.Up(i/512f),path.Tangent(i/512f)));
                var q=p+right*(side*.42f);
                cells.Add(new((int)MathF.Round(q.X),(int)MathF.Round(q.Y),(int)MathF.Round(q.Z)));
            }
        }
        // Native snap helpers occupy the two endpoint cells. The rest of the
        // object keeps a protected footprint without masking those blocks.
        if(key!="CoasterStation")foreach(var (cell,_) in CoasterSnapHelpers.Ends(key,System.Numerics.Vector3.Zero,Eco.Shared.Math.Quaternion.Identity))cells.Remove(cell);
        AddOccupancy<T>(cells.Select(c=>new BlockOccupancy(c,typeof(BuildingWorldObjectBlock))).ToList());
    }
}
public static class CoasterSnapHelpers
{
    public static bool IsHelper(Type? type)=>type==typeof(CoasterTrackSnapEndBlock)||type==typeof(CoasterTrackSnapEndR90Block)
        ||type==typeof(CoasterTrackSnapEndR180Block)||type==typeof(CoasterTrackSnapEndR270Block);
    public static IEnumerable<(Eco.Shared.Math.Vector3i Cell,Type BlockType)> Ends(string key,System.Numerics.Vector3 position,Eco.Shared.Math.Quaternion rotation)
    {
        var path=CoasterPath.Find(key);
        var direction=rotation.RotateVector(System.Numerics.Vector3.UnitZ);
        var turns=((int)Math.Round(Math.Atan2(direction.X,direction.Z)/(Math.PI/2))+4)%4;
        var center=position+rotation.RotateVector(path.PlacementOffset);
        var anchor=new RailCell((int)MathF.Round(center.X),(int)MathF.Round(center.Y),(int)MathF.Round(center.Z));
        var rail=new VoxelRail(anchor,new(key,turns,false,Coaster:true));
        foreach(var end in new[]{0,1})
        {
            var tangent=rail.Profile.Tangent(end);
            var inside=rail.Point(end)+tangent*(end==0?.5f:-.5f);
            var cell=new Eco.Shared.Math.Vector3i((int)MathF.Round(inside.X),(int)MathF.Round(inside.Y+.35f),(int)MathF.Round(inside.Z));
            var heading=((int)Math.Round(Math.Atan2(tangent.X,tangent.Z)/(Math.PI/2))+4)%4;
            var type=heading switch {1=>typeof(CoasterTrackSnapEndR90Block),2=>typeof(CoasterTrackSnapEndR180Block),3=>typeof(CoasterTrackSnapEndR270Block),_=>typeof(CoasterTrackSnapEndBlock)};
            yield return(cell,type);
        }
    }
}
public abstract class CoasterSpecialItem<T>:WorldObjectItem<T> where T:CoasterRailObject
{
    // Complete sections are suspended track, so a floor requirement on the
    // whole world object would reject legitimate rail-supported spans.
    protected override OccupancyContext GetOccupancyContext=>new PositionsRequirementContext([]);
    public override async Task<bool> CanPlaceObject(Player player,System.Numerics.Vector3 pos,Eco.Shared.Math.Quaternion rotation)
    {
        if(!await base.CanPlaceObject(player,pos,rotation))return false;
        var key=typeof(T).Name.Replace("Object","");
        if(!CoasterSnapHelpers.Ends(key,pos,rotation).All(e=>Eco.World.World.GetBlock(e.Cell) is EmptyBlock))return false;
        return CoasterSpecialPlacement.IsSupported(key,pos,rotation);
    }
    public override async Task PlacingObject(GameActionPack pack,Player player,ItemStack containingStack,System.Numerics.Vector3 pos,Eco.Shared.Math.Quaternion rotation)
    {
        await base.PlacingObject(pack,player,containingStack,pos,rotation);
        CoasterSpecialPlacement.Prepare(pack,this,player.User,typeof(T).Name.Replace("Object",""),pos,rotation);
    }
}
public static class CoasterSpecialPlacement
{
    private static readonly object CommitGate=new();
    private static readonly Func<IPlaceableItem,System.Numerics.Vector3,Eco.Shared.Math.Quaternion,Result> NativePlacementCheck=BindPlacementCheck();
    private static Func<IPlaceableItem,System.Numerics.Vector3,Eco.Shared.Math.Quaternion,Result> BindPlacementCheck()
    {
        // Eco 0.14.2 added an optional moving-object argument. Bind the
        // installed native overload once while retaining 0.14.1 support.
        var parameters=new[]{typeof(IPlaceableItem),typeof(System.Numerics.Vector3),typeof(Eco.Shared.Math.Quaternion),typeof(WorldObject)};
        var legacy=typeof(WorldObjectPlacementUtils).GetMethod("IsValidPlacement",parameters);
        if(legacy!=null)
        {
            var check=legacy.CreateDelegate<Func<IPlaceableItem,System.Numerics.Vector3,Eco.Shared.Math.Quaternion,WorldObject?,Result>>();
            return (item,position,rotation)=>check(item,position,rotation,null);
        }
        var current=typeof(WorldObjectPlacementUtils).GetMethod("IsValidPlacement",parameters.Append(typeof(WorldObject)).ToArray())
            ??throw new MissingMethodException("Eco has no supported native object placement validation overload.");
        var currentCheck=current.CreateDelegate<Func<IPlaceableItem,System.Numerics.Vector3,Eco.Shared.Math.Quaternion,WorldObject?,WorldObject?,Result>>();
        return (item,position,rotation)=>currentCheck(item,position,rotation,null,null);
    }
    internal static void Prepare(GameActionPack pack,IPlaceableItem item,User builder,string key,System.Numerics.Vector3 position,Eco.Shared.Math.Quaternion rotation)
    {
        // Native placement validates before the pack commits. Recheck live
        // geometry and helper permissions in that same material transaction.
        // Preserve Eco's user/action locker. The changeset owns only the
        // short synchronous section-placement commit, never preparation/UI.
        pack.GetOrCreateChangeSet(()=>new PlacementChecks()).Requests.Add((item,builder,key,position,rotation));
    }
    private sealed class PlacementChecks : IGameActionPackChangeSet
    {
        internal readonly List<(IPlaceableItem Item,User Builder,string Key,System.Numerics.Vector3 Position,Eco.Shared.Math.Quaternion Rotation)> Requests=[];
        private bool held;
        public bool NeedsDispose=>true;
        public void GameActionPackDispose()
        {
            if(!held)return;
            held=false;Monitor.Exit(CommitGate);
        }
        public LocString GameActionPackPostEffect()=>LocString.Empty;
        public Result GameActionPackPretest()
        {
            // Native packs run pretests and post-effects synchronously. A
            // competing commit can be retried without holding another lock
            // while waiting. Dispose also releases on rejection/exception.
            if(!held)
            {
                if(!Monitor.TryEnter(CommitGate))return Result.FailLocStr("Another coaster section is being placed. Try again in a moment.");
                held=true;
            }
            HashSet<WrappedWorldPosition3i>? occupied=Requests.Count>1?new():null;
            foreach(var request in Requests)
            {
                var valid=NativePlacementCheck(request.Item,request.Position,request.Rotation);
                if(!valid)return valid;
                // Native free placement can allow partial body occupancy. A
                // complete rail needs its whole protected footprint, matching
                // the existing blueprint policy, even after preparation.
                foreach(var cell in WorldObject.GetOccupancyInfo(request.Item.WorldObjectType).GetBlockPositions(request.Position.XYZi(),request.Rotation))
                {
                    var block=Eco.World.World.GetBlock(cell);
                    if(block is not EmptyBlock&&block is not IWaterBlock)
                        return Result.FailLocStr("This rail section needs clear space for its full footprint.");
                    if(occupied!=null&&!occupied.Add(cell))return Result.FailLocStr("Planned rail sections overlap the same construction cell.");
                }
                // Helpers are outside native object occupancy. Their plots
                // still require the same full access as the rest of the rail.
                foreach(var (cell,_) in CoasterSnapHelpers.Ends(request.Key,request.Position,request.Rotation))
                {
                    if(!WrappedWorldPosition3i.TryCreate(cell,out var wrapped))
                        return Result.FailLocStr("The end of this rail section is outside the world height limits.");
                    if(occupied!=null&&!occupied.Add(wrapped))return Result.FailLocStr("Planned rail sections overlap the same construction cell.");
                    if(Eco.World.World.GetBlock(wrapped) is not EmptyBlock)
                        return Result.FailLocStr("The end of this rail section is no longer clear.");
                    var access=ServiceHolder<IAuthManager>.Obj.IsAuthorized(((Eco.Shared.Math.Vector3i)wrapped).XZ.ToPlotPos(),request.Builder,AccessType.FullAccess,null,out _);
                    if(!access)return access.CreateResult();
                }
                if(!IsSupported(request.Key,request.Position,request.Rotation))
                    return Result.FailLocStr("The rail section needs a real connected rail or grounded support before construction.");
            }
            return Result.Succeeded;
        }
    }
    public static bool IsSupported(string key,System.Numerics.Vector3 position,Eco.Shared.Math.Quaternion rotation)
    {
        var path=CoasterPath.Find(key);
        var direction=rotation.RotateVector(System.Numerics.Vector3.UnitZ);
        var turns=((int)Math.Round(Math.Atan2(direction.X,direction.Z)/(Math.PI/2))+4)%4;
        var center=position+rotation.RotateVector(path.PlacementOffset);
        var rail=new VoxelRail(new((int)MathF.Round(center.X),(int)MathF.Round(center.Y),(int)MathF.Round(center.Z)),new(key,turns,false,Coaster:true));
        for(var end=0;end<2;end++)
            if(TrackWorld.Near(rail.Point(end)).Any(other=>other.Profile.Coaster&&
                (rail.Connects(end,other,0)||rail.Connects(end,other,1))))return true;
        foreach(var (cell,_) in CoasterSnapHelpers.Ends(key,position,rotation))
        {
            if(cell.Y<=0)continue;
            var under=new Eco.Shared.Math.Vector3i(cell.X,cell.Y-1,cell.Z);
            var block=Eco.World.World.GetBlock(under);
            if(block==null)continue;
            if(Eco.Minecarts.Runtime.RailSupportColumns.TrySupport(block.GetType(),out _,out _))
            {
                if(Eco.Minecarts.Runtime.RailSupportColumns.GroundedReach(under,
                    p=>Eco.World.World.GetBlock(p)?.GetType())>0)return true;
            }
            else if(block.GetType().IsDefined(typeof(Solid),true))return true;
        }
        return false;
    }
}
public static class CoasterNames
{
    public static string? ChoiceFamily(string key)=>key switch
    {
        "CoasterBendLeft" or "CoasterBendRight"=>"Bend",
        "CoasterBankLeft" or "CoasterBankRight"=>"Bank",
        "CoasterLoop" or "CoasterLoopLeft"=>"Loop",
        _=>CoasterTrackNames.BlueprintShapeFamily(key)
    };
    public static string ChoiceDirection(string key)=>CoasterTrackNames.BlueprintShapeFamily(key)!=null
        ? (key.Contains("Down",StringComparison.Ordinal)?"Downhill":"Uphill")
        :key.EndsWith("Left",StringComparison.Ordinal)?"Left":"Right";
    public static string Name(string key)=>key switch
    {
        "CoasterStraight"=>"Roller Coaster Rail",
        "CoasterChainStraight"=>"Coaster Chain-Lift Rail - Straight",
        "CoasterChainSteepUp"=>"Coaster Chain-Lift Rail - Steep Uphill",
        "CoasterLiftEntry"=>"Coaster Chain-Lift Rail - Entry",
        "CoasterLiftCrest"=>"Coaster Chain-Lift Rail - Crest",
        "CoasterStation"=>"Roller Coaster Station",
        "CoasterLoop"=>"Roller Coaster Rail - Loop Right",
        "CoasterLoopLeft"=>"Roller Coaster Rail - Loop Left",
        _=>"Roller Coaster Rail - "+System.Text.RegularExpressions.Regex.Replace(key.Replace("Coaster",""),"([a-z])([A-Z])","$1 $2")
    };
}
public abstract class CoasterRailRecipe<T>:MinecartRailRecipeFamily where T:Item,new()
{
    protected CoasterRailRecipe()
    {
        var key=typeof(T).Name.Replace("Item","");
        var path=CoasterPath.Find(key=="CoasterStation"?"CoasterTrackStraightSection01":key);
        // Complete geometry is priced by rail length, not by being a bend,
        // bank, hill or loop. The station adds its own boarding structure.
        var station=key=="CoasterStation";
        var bars=Math.Max(1,(int)Math.Ceiling(path.Length*.75))+(station?2:0);
        var boards=(int)Math.Ceiling(path.Length)+(station?4:0);
        var recipe=MinecartRailRecipes.Make<T>(key,bars,boards,fixedMaterials:true, skillType:typeof(IndustrySkill),metalType:typeof(SteelBarItem));
        recipe.DisplayName=Localizer.DoStr(CoasterNames.Name(key));
        Configure(recipe,CoasterNames.Name(key),GetType(),bars*8,bars*.1f, skillType:typeof(IndustrySkill));
    }
}
