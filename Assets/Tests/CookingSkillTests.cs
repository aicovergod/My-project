using NUnit.Framework;
using Skills.Cooking;
using UnityEngine;

public class CookingSkillTests
{
    private static CookableRecipe CreateRecipe(int required, int noBurn, float burn)
    {
        var recipe = ScriptableObject.CreateInstance<CookableRecipe>();
        recipe.requiredLevel = required;
        recipe.noBurnLevel = noBurn;
        recipe.burnChance = burn;
        return recipe;
    }

    [Test]
    public void BurnChanceEqualsRecipeAtRequiredLevel()
    {
        var recipe = CreateRecipe(5, 10, 0.4f);
        float chance = CookingSkill.CalculateBurnChance(5, recipe);
        Assert.AreEqual(recipe.burnChance, chance);
    }

    [Test]
    public void BurnChanceZeroAtOrAboveNoBurnLevel()
    {
        var recipe = CreateRecipe(5, 10, 0.4f);
        Assert.AreEqual(0f, CookingSkill.CalculateBurnChance(10, recipe));
        Assert.AreEqual(0f, CookingSkill.CalculateBurnChance(11, recipe));
    }

    [Test]
    public void BurnChanceStrictlyDecreasesBetweenLevels()
    {
        var recipe = CreateRecipe(1, 5, 0.6f);
        float previous = CookingSkill.CalculateBurnChance(1, recipe);
        for (int level = 2; level < 5; level++)
        {
            float chance = CookingSkill.CalculateBurnChance(level, recipe);
            Assert.Less(chance, previous);
            previous = chance;
        }
    }
}

