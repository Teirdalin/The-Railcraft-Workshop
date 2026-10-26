namespace Eco.Mods.TechTree;

using Eco.Core.Items;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Skills;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

[Serialized, LocDisplayName("Minecart Wheel"),
 LocDescription("A small flanged iron wheel that keeps a minecart running along the rails. Made four at a time from iron plates at the Railworks Workbench using Basic Engineering."),
 Weight(500), MaxStackSize(20), Ecopedia("Items", "Products", createAsSubPage:true)]
public sealed class MinecartWheelItem : Item { }

// A dedicated crafting component; existing vanilla wheels and repair parts
// retain their identities and maintenance behavior.
[RequiresSkill(typeof(BasicEngineeringSkill),2)]
public sealed class MinecartWheelRecipe : MinecartRailRecipeFamily
{
    public MinecartWheelRecipe()
    {
        var recipe=new Recipe();
        recipe.Init("MinecartWheel",Localizer.DoStr("Minecart Wheels"),
            [new IngredientElement(typeof(IronPlateItem),12,typeof(BasicEngineeringSkill)),
             new IngredientElement("WoodBoard",2,typeof(BasicEngineeringSkill)),
             new IngredientElement(typeof(LubricantItem),1,true)],[],
            [new CraftingElement<MinecartWheelItem>(4)]);
        Configure(recipe,"Minecart Wheels",typeof(MinecartWheelRecipe),300,6,typeof(BasicEngineeringSkill));
    }
}
