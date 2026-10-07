namespace Eco.Mods.TechTree;

// Recipe construction registers each family once. Infrastructure defaults to
// the rail bench; vehicle and drive families have explicit profession tables.
internal static class RailRecipeWorkshops
{
    internal static Type For(Type recipe) => recipe.Name switch
    {
        nameof(MinecartRecipe) or nameof(WoodenMinecartRecipe) or nameof(MineTrainRecipe)
            or nameof(RailroadHandcarRecipe) or nameof(PassengerCarRecipe) or nameof(CoalTenderRecipe)
            => typeof(WainwrightTableObject),
        nameof(PassengerLocomotiveRecipe) or nameof(FreightLocomotiveRecipe) or nameof(LargeTrainEngineRecipe)
            or nameof(LargeCargoCarRecipe) or nameof(LargePassengerCarRecipe) or nameof(LargeCoalTenderRecipe)
            => typeof(MachinistTableObject),
        nameof(HeritageTramRecipe) or nameof(RollerCoasterCartRecipe)
            or nameof(ElectricalRailChainDriveRecipe) or nameof(ElectricalTramCableDriveRecipe)
            => typeof(ElectricMachinistTableObject),
        _ => typeof(RailcraftWorkbenchObject)
    };
    internal static Type VehicleSkill(Type recipe)
    {
        var table=For(recipe);
        return table==typeof(WainwrightTableObject)?typeof(BasicEngineeringSkill)
            :table==typeof(MachinistTableObject)?typeof(MechanicsSkill)
            :table==typeof(ElectricMachinistTableObject)?typeof(IndustrySkill)
            :throw new InvalidOperationException("Unclassified rail vehicle recipe: "+recipe.Name);
    }
}
