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
        int hewnLogs = 0, int woodenGears = 0, bool fixedMaterials = false, int fabric = 0, Type? skillType = null, Type? metalType = null,
        int steelGears = 0, int lubricant = 0)
        where TItem : Item, new()
    {
        var skill = skillType ?? typeof(BasicEngineeringSkill);
        var metal = metalType ?? typeof(IronBarItem);
        var recipe = new Recipe();
        var ingredients = new List<IngredientElement>
        {
            fixedMaterials ? new IngredientElement(metal, ironBars, true)
                : new IngredientElement(metal, ironBars, skill),
        };
        if (woodBoards > 0) ingredients.Add(fixedMaterials
            ? new IngredientElement("WoodBoard", woodBoards, true)
            : new IngredientElement("WoodBoard", woodBoards, skill));
        if (hewnLogs > 0) ingredients.Add(fixedMaterials
            ? new IngredientElement("HewnLog", hewnLogs, true)
            : new IngredientElement("HewnLog", hewnLogs, skill));
        if (woodenGears > 0) ingredients.Add(new IngredientElement(typeof(WoodenGearItem), woodenGears, skill));
        if (fabric > 0) ingredients.Add(new IngredientElement("Fabric", fabric, skill));
        if (steelGears > 0) ingredients.Add(new IngredientElement(typeof(SteelGearItem),steelGears,true));
        if (lubricant > 0) ingredients.Add(new IngredientElement(typeof(LubricantItem),lubricant,true));
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
        "Railworks Workbench"=>"Railcraft Workbench",
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
    protected void Configure(Recipe recipe, string displayName, Type recipeType, float labor, float minutes, Type? skillType = null)
    {
        var skill = skillType ?? typeof(BasicEngineeringSkill);
        this.Recipes = [recipe];
        this.ExperienceOnCraft = 2;
        this.LaborInCalories = CreateLaborInCaloriesValue(labor, skill);
        this.CraftMinutes = CreateCraftTimeValue(recipeType, minutes, skill);
        this.Initialize(Localizer.DoStr(displayName), recipeType);
        // Registered recipe families are included in skill-tree client views even
        // when not shown at a table. Eco's CraftingTable getter requires a table
        // registration; leaving it unset can prevent every player from joining.
        CraftingComponent.AddRecipe(RailRecipeWorkshops.For(recipeType), this);
    }
}
