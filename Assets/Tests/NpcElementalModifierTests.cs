using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Combat;
using NPC;

public class NpcElementalModifierTests
{
    private static NpcCombatant CreateCombatant(NpcCombatProfile profile)
    {
        var go = new GameObject();
        go.SetActive(false);
        var combatant = go.AddComponent<NpcCombatant>();
        typeof(NpcCombatant).GetField("profile", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(combatant, profile);
        go.SetActive(true);
        return combatant;
    }

    [TestCase(SpellElement.Air)]
    [TestCase(SpellElement.Water)]
    [TestCase(SpellElement.Earth)]
    [TestCase(SpellElement.Electric)]
    [TestCase(SpellElement.Ice)]
    [TestCase(SpellElement.Fire)]
    [TestCase(SpellElement.None)]
    public void ProtectionBlocksDamage(SpellElement element)
    {
        var profile = ScriptableObject.CreateInstance<NpcCombatProfile>();
        profile.HitpointsLevel = 100;
        profile.elementalModifiers.Add(new ElementalModifier
        {
            element = element,
            protectionPercent = 100,
            bonusPercent = 0
        });
        var npc = CreateCombatant(profile);
        npc.ApplyDamage(50, DamageType.Magic, element, null);
        Assert.AreEqual(100, npc.CurrentHP);
    }

    [TestCase(SpellElement.Air)]
    [TestCase(SpellElement.Water)]
    [TestCase(SpellElement.Earth)]
    [TestCase(SpellElement.Electric)]
    [TestCase(SpellElement.Ice)]
    [TestCase(SpellElement.Fire)]
    [TestCase(SpellElement.None)]
    public void BonusIncreasesDamage(SpellElement element)
    {
        var profile = ScriptableObject.CreateInstance<NpcCombatProfile>();
        profile.HitpointsLevel = 100;
        profile.elementalModifiers.Add(new ElementalModifier
        {
            element = element,
            protectionPercent = 0,
            bonusPercent = 25
        });
        var npc = CreateCombatant(profile);
        npc.ApplyDamage(40, DamageType.Magic, element, null);
        int expected = 100 - Mathf.RoundToInt(40 * 1.25f);
        Assert.AreEqual(expected, npc.CurrentHP);
    }
}
