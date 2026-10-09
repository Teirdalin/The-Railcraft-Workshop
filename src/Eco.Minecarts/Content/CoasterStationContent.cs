namespace Eco.Mods.TechTree;
using Eco.Core.Items;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Components;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Players;
using Eco.World.Blocks;
using Eco.Minecarts.Runtime;
using Eco.Minecarts.Track;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

[Serialized,RequireComponent(typeof(TrainStationComponent)),RequireComponent(typeof(RailAutomationComponent)),RequireComponent(typeof(CoasterStationComponent)),RequireComponent(typeof(CoasterBlueprintComponent)),RequireComponent(typeof(CustomTextComponent))]
public sealed class CoasterStationObject:CoasterRailObject
{
    protected override void Initialize(){base.Initialize();GetComponent<CustomTextComponent>().Initialize(120);}
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
