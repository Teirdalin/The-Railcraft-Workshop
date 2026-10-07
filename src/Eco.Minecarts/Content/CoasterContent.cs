namespace Eco.Mods.TechTree;
using Eco.Core.Items;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Players;
using Eco.World.Blocks;
using Eco.Minecarts.Runtime;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

[Serialized,RequireComponent(typeof(PropertyAuthComponent)),RequireComponent(typeof(CoasterRailComponent))]
public abstract class CoasterRailObject : WorldObject,IRepresentsItem
{
    [Serialized] public bool EntrySnapOwned {get;set;}
    [Serialized] public bool ExitSnapOwned {get;set;}
    public System.Numerics.Vector3 RailOffset=>this is CoasterStationObject?-System.Numerics.Vector3.UnitX:CoasterPath.Find(PathKey).PlacementOffset;
    public string PathKey=>this is CoasterStationObject?"CoasterTrackStraightSection01":GetType().Name.Replace("Object","");
    public Type RepresentedItemType=>GetType().Assembly.GetType("Eco.Mods.TechTree."+GetType().Name.Replace("Object","Item"))!;
    public override LocString DisplayName=>Localizer.DoStr(CoasterNames.Name(GetType().Name.Replace("Object","")));
    protected override void OnCreatePostInitialize()
    {
        base.OnCreatePostInitialize();
        if(this is CoasterStationObject)return;
        var ends=CoasterSnapHelpers.Ends(PathKey,Position,Rotation).ToArray();
        for(var i=0;i<ends.Length;i++)
        {
            var (cell,type)=ends[i];
            if(Eco.World.World.GetBlock(cell) is not Eco.World.Blocks.EmptyBlock)continue;
            Eco.World.World.SetBlock(type,cell);
            if(Eco.World.World.GetBlock(cell)?.GetType()==type)
            {
                if(i==0)EntrySnapOwned=true;else ExitSnapOwned=true;
            }
        }
    }
    protected override void OnDestroy()
    {
        var cells=WorldOccupancy?.ToArray();var id=ObjectID;
        var helpers=this is CoasterStationObject?[]:CoasterSnapHelpers.Ends(PathKey,Position,Rotation).ToArray();
        var entryOwned=EntrySnapOwned;var exitOwned=ExitSnapOwned;
        base.OnDestroy();
        for(var i=0;i<helpers.Length;i++)
            if((i==0?entryOwned:exitOwned)&&CoasterSnapHelpers.IsHelper(Eco.World.World.GetBlock(helpers[i].Cell)?.GetType()))
                Eco.World.World.DeleteBlock(helpers[i].Cell);
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
}
public static class CoasterSpecialPlacement
{
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
        Configure(recipe,CoasterNames.Name(key),GetType(),bars*8,bars*.2f, skillType:typeof(IndustrySkill));
    }
}
[Serialized] public sealed class RollerCoasterCartObject:RollingStockObject
{static RollerCoasterCartObject()=>AddOccupancy<RollerCoasterCartObject>([]);}
[Serialized,LocDisplayName("Roller Coaster Cart"),LocDescription("Two passenger seats, captive guide wheels and momentum-driven travel. Requires Roller Coaster Rail; gravity and powered chain lifts provide motion. Crafted at the Electric Machinist Table using Industry."),Weight(12000)]
public sealed class RollerCoasterCartItem:RailModuleItem<RollerCoasterCartObject>
{
    // The station's explicit placement validates track and clearance. Empty
    // occupancy prevents the native floor test rejecting its own rail surface.
    protected override OccupancyContext GetOccupancyContext=>new PositionsRequirementContext([]);
    public override Task<bool> CanPlaceObject(Player player,System.Numerics.Vector3 pos,Eco.Shared.Math.Quaternion rotation)
    {
        if(TrackWorld.Capture(pos,rotation.RotateVector(System.Numerics.Vector3.UnitZ),.5f,1.05f,coaster:true)!=null)
            return Task.FromResult(true);
        return Task.FromResult(new SideAttachedContext(Eco.Shared.Math.DirectionAxisFlags.None,WorldObject.GetOccupancyInfo(WorldObjectType))
            .CanPlaceObject(player,this,pos,rotation));
    }
    [Eco.Gameplay.Interactions.Interactors.Interaction(Eco.Shared.SharedTypes.InteractionTrigger.RightClick,
        "Place cart on station",requiredEnvVars:new[]{"CoasterLoadingStation"},interactionDistance:5,priority:100,
        flags:Eco.Shared.SharedTypes.InteractionFlags.BlocksOtherInteraction)]
    public async Task ApplyToStation(Player player,Eco.Shared.SharedTypes.InteractionTriggerInfo trigger,Eco.Shared.SharedTypes.InteractionTarget target)
    {
        if(target.NetObj is CoasterStationObject station)
            await station.GetComponent<CoasterStationComponent>().PlaceCart(player,this);
    }
}
[RequiresSkill(typeof(IndustrySkill),3)]
public sealed class RollerCoasterCartRecipe:MinecartRailRecipeFamily
{
    public RollerCoasterCartRecipe()
    {
        // Four fabric replace four of the former eighteen bars, one for one.
        var recipe=MinecartRailRecipes.Make<RollerCoasterCartItem>("RollerCoasterCart",14,8,fabric:4,
            skillType:typeof(IndustrySkill),metalType:typeof(SteelBarItem),steelGears:1,lubricant:1);
        Configure(recipe,"Roller Coaster Cart",typeof(RollerCoasterCartRecipe),200,8,skillType:typeof(IndustrySkill));
    }
}






















[Serialized,RequireComponent(typeof(TrainStationComponent)),RequireComponent(typeof(RailAutomationComponent)),RequireComponent(typeof(CoasterStationComponent)),RequireComponent(typeof(CoasterBlueprintComponent))]
public sealed class CoasterStationObject:CoasterRailObject
{
    public override void Use(Eco.Gameplay.Players.Player player, Eco.Shared.SharedTypes.InteractionTarget target,
        Eco.Shared.SharedTypes.InteractionTriggerInfo triggerInfo, string ui="WorldObjectUI")
    { GetComponent<CoasterBlueprintComponent>()?.SyncOnOpening(); base.Use(player,target,triggerInfo,ui); }
    static CoasterStationObject()=>Occupy<CoasterStationObject>("CoasterStation");
    [Serialized] public int StationFootprintVersion {get;set;}
    protected override void OnCreatePreInitialize(){base.OnCreatePreInitialize();StationFootprintVersion=1;}
    internal void CompactLegacyFootprint()
    {
        if(StationFootprintVersion>=1)return;
        var forward=Rotation.RotateVector(System.Numerics.Vector3.UnitZ);
        var turn=((int)Math.Round(Math.Atan2(forward.X,forward.Z)/(Math.PI/2))+4)%4;
        var block=turn switch {1=>typeof(CoasterTrackStraightSection01R90Block),2=>typeof(CoasterTrackStraightSection01R180Block),3=>typeof(CoasterTrackStraightSection01R270Block),_=>typeof(CoasterTrackStraightSection01Block)};
        // Old stations included four extra rail cells. Leave ordinary rails in
        // those cells so shrinking a saved station does not drop existing carts
        // or break its route. Never replace a player's later construction.
        foreach(var z in new[]{-2,-1,1,2})
        {
            var rail=Position+Rotation.RotateVector(new System.Numerics.Vector3(-1,0,z));
            var cell=new Eco.Shared.Math.Vector3i((int)MathF.Round(rail.X),(int)MathF.Round(rail.Y),(int)MathF.Round(rail.Z));
            var existing=Eco.World.World.GetBlock(cell);
            if(existing is EmptyBlock||existing is WorldObjectBlock own&&own.WorldObjectHandle.Id==ObjectID)
                Eco.World.World.SetBlock(block,cell);
            var platform=Position+Rotation.RotateVector(new System.Numerics.Vector3(0,0,z));
            var platformCell=new Eco.Shared.Math.Vector3i((int)MathF.Round(platform.X),(int)MathF.Round(platform.Y),(int)MathF.Round(platform.Z));
            if(Eco.World.World.GetBlock(platformCell) is WorldObjectBlock old&&old.WorldObjectHandle.Id==ObjectID)
                Eco.World.World.DeleteBlock(platformCell);
        }
        StationFootprintVersion=1;SetDirty();
    }
}
[Serialized,LocDisplayName("Roller Coaster Station"),Weight(12000),Ecopedia("Blocks","Building Materials",createAsSubPage:true),LocDescription("A loading platform with built-in coaster rail. Holds carts for boarding and dispatches them when its departure conditions are met. Connect Roller Coaster Rail to both ends.")] public sealed class CoasterStationItem:WorldObjectItem<CoasterStationObject>{public override Eco.Shared.Localization.LocString DisplayName=>Localizer.DoStr(CoasterNames.Name("CoasterStation"));}
[RequiresSkill(typeof(IndustrySkill),3)] public sealed class CoasterStationRecipe:CoasterRailRecipe<CoasterStationItem>{}
