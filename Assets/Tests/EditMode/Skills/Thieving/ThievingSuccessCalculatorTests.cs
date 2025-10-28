using NUnit.Framework;
using UnityEngine;
using Skills.Thieving.Data;
using Skills.Thieving.Core;
using NPC;
using Skills.Thieving;

namespace Tests.EditMode.Skills.Thieving
{
    /// <summary>
    ///     Covers success calculations and lockout behaviour for the thieving skill.
    /// </summary>
    public class ThievingSuccessCalculatorTests
    {
        [Test]
        public void GetSuccessThreshold_InterpolatesAcrossLevels()
        {
            var definition = ScriptableObject.CreateInstance<ThievingNpcDefinition>();
            SetField(definition, "lowSuccessThreshold", 100);
            SetField(definition, "highSuccessThreshold", 200);
            SetField(definition, "post99ThresholdGain", 5);

            Assert.AreEqual(100, definition.GetSuccessThreshold(1));
            Assert.AreEqual(150, definition.GetSuccessThreshold(50));
            Assert.AreEqual(200, definition.GetSuccessThreshold(99));
            Assert.AreEqual(205, definition.GetSuccessThreshold(100));
            Assert.AreEqual(255, definition.GetSuccessThreshold(150));
        }

        [Test]
        public void PickpocketRoll_UsesInjectedRandomSource()
        {
            var skill = CreateSkill(out var target, out var definition);
            SetField(definition, "lowSuccessThreshold", 255);
            SetField(definition, "highSuccessThreshold", 255);
            definition.name = "TestDefinition";

            bool successNotified = false;
            skill.PickpocketFinished += (_, success) => successNotified = success;

            skill.PickpocketRoll = () => 0;
            Assert.IsTrue(skill.TryStartPickpocket(target));
            skill.OnTick();
            skill.OnTick();

            Assert.IsTrue(successNotified, "Expected pickpocket to succeed when roll is forced to 0.");
        }

        [Test]
        public void ConsecutiveFailures_TriggerNpcLockout()
        {
            var skill = CreateSkill(out var target, out var definition);
            SetField(definition, "failuresBeforeCooldown", 2);
            SetField(definition, "cooldownTicks", 10);
            SetField(definition, "lowSuccessThreshold", 0);
            SetField(definition, "highSuccessThreshold", 0);

            bool firstAttempt = skill.TryStartPickpocket(target);
            Assert.IsTrue(firstAttempt);
            skill.PickpocketRoll = () => 255;
            skill.OnTick();
            skill.OnTick();
            Assert.IsTrue(target.CanPickpocket, "NPC should still be available after the first failure.");

            bool secondAttempt = skill.TryStartPickpocket(target);
            Assert.IsTrue(secondAttempt);
            skill.OnTick();
            skill.OnTick();

            Assert.IsFalse(target.CanPickpocket, "NPC pickpocket option should be disabled after consecutive failures.");
        }

        private static ThievingSkill CreateSkill(out NpcThievingTarget target, out ThievingNpcDefinition definition)
        {
            var playerGo = new GameObject("ThievingSkill_Player");
            playerGo.AddComponent<Inventory.Inventory>();
            var skill = playerGo.AddComponent<ThievingSkill>();
            SetField(skill, "floatingTextAnchor", playerGo.transform);

            var targetGo = new GameObject("ThievingTarget");
            var options = targetGo.AddComponent<NpcInteractionOptions>();
            options.SetPickpocketEnabled(true);
            target = targetGo.AddComponent<NpcThievingTarget>();
            SetField(target, "interactionOptions", options);

            definition = ScriptableObject.CreateInstance<ThievingNpcDefinition>();
            SetField(definition, "requiredLevel", 1);
            SetField(definition, "baseXp", 1f);
            SetField(definition, "lowSuccessThreshold", 128);
            SetField(definition, "highSuccessThreshold", 128);
            SetField(definition, "cooldownTicks", 10);
            SetField(definition, "failuresBeforeCooldown", 3);
            SetField(definition, "damageOnFail", 0);
            SetField(definition, "stunTicks", 0);
            SetField(target, "definition", definition);
            target.ForceClearLockout();

            return skill;
        }

        private static void SetField(object instance, string fieldName, object value)
        {
            var type = instance.GetType();
            var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, $"Field {fieldName} missing on {type}.");
            field.SetValue(instance, value);
        }
    }
}
