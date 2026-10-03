namespace Eco.Mods.TechTree;

using System;
using System.Collections.Generic;
using Eco.Core.Items;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Items;
using Eco.Gameplay.Items.Recipes;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Occupancy;
using Eco.Gameplay.Skills;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Math;
using Eco.Shared.Serialization;
using Eco.Shared.Utils;

internal static class MinecartRailRecipes
{
    public static Recipe Make<TItem>(string name, int ironBars, int woodBoards, int output = 1,
        int hewnLogs = 0, int woodenGears = 0)
        where TItem : Item, new()
    {
        var recipe = new Recipe();
        var ingredients = new List<IngredientElement>
        {
            new(typeof(IronBarItem), ironBars, typeof(BasicEngineeringSkill)),
        };
        if (woodBoards > 0) ingredients.Add(new IngredientElement("WoodBoard", woodBoards, typeof(BasicEngineeringSkill)));
        if (hewnLogs > 0) ingredients.Add(new IngredientElement("HewnLog", hewnLogs, typeof(BasicEngineeringSkill)));
        if (woodenGears > 0) ingredients.Add(new IngredientElement(typeof(WoodenGearItem), woodenGears, typeof(BasicEngineeringSkill)));
        recipe.Init(
            LegacyRecipeKey(name),
            Localizer.DoStr(name),
            ingredients,
            [],
            [new CraftingElement<TItem>(output)]);
        return recipe;
    }
    private static string LegacyRecipeKey(string name)=>name switch
    {
        "Standard Rail"=>"Minecart Track",
        "Powered Chain Rail"=>"Minecart Chain Track",
        "Rail Chain Drive"=>"Minecart Chain Drive",
        "Standard Rail - Wide Turn (3x3)"=>"Wide Railway Turn (3x3)",
        _ when name.StartsWith("Standard Rail Wide-Turn ")=>name.Replace("Standard Rail Wide-Turn ","Wide Railway "),
        _ when name.StartsWith("Standard Rail ")=>name.Replace("Standard Rail ","Minecart "),
        _=>name
    };
}

public abstract class MinecartRailRecipeFamily : RecipeFamily
{
    protected void Configure(Recipe recipe, string displayName, Type recipeType, float labor, float minutes)
    {
        this.Recipes = [recipe];
        this.ExperienceOnCraft = 2;
        this.LaborInCalories = CreateLaborInCaloriesValue(labor, typeof(BasicEngineeringSkill));
        this.CraftMinutes = CreateCraftTimeValue(recipeType, minutes, typeof(BasicEngineeringSkill));
        this.Initialize(Localizer.DoStr(displayName), recipeType);
        // Registered recipe families are included in skill-tree client views even
        // when not shown at a table. Eco's CraftingTable getter requires a table
        // registration; leaving it unset can prevent every player from joining.
        CraftingComponent.AddRecipe(typeof(RailcraftWorkbenchObject), this);
    }
}
