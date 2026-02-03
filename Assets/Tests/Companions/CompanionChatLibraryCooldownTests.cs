using Companions;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Companions
{
    /// <summary>
    /// Validates the cooldown formatting helpers exposed through <see cref="CompanionChatLibrary"/>.
    /// </summary>
    public class CompanionChatLibraryCooldownTests
    {
        [Test]
        public void WoodcuttingCooldownLines_ReplacePlaceholders()
        {
            Random.InitState(1234);

            string line = CompanionChatLibrary.GetRandomWoodcuttingDeclineCooldownLine("Tester", 1);

            Assert.IsFalse(string.IsNullOrWhiteSpace(line), "Cooldown lines should never be empty.");
            StringAssert.DoesNotContain("{playerName}", line, "Player placeholder should be replaced.");
            StringAssert.DoesNotContain("{minutes}", line, "Minute placeholder should be replaced.");
            StringAssert.Contains("Tester", line, "Player name should be present in the formatted line.");
            StringAssert.Contains("1 minute", line, "Singular minute formatting should be respected.");
        }

        [Test]
        public void FishingCooldownLines_HandlePluralMinutes()
        {
            Random.InitState(5678);

            string line = CompanionChatLibrary.GetRandomFishingDeclineCooldownLine("Adventurer", 3);

            Assert.IsFalse(string.IsNullOrWhiteSpace(line), "Cooldown lines should never be empty.");
            StringAssert.DoesNotContain("{playerName}", line, "Player placeholder should be replaced.");
            StringAssert.DoesNotContain("{minutes}", line, "Minute placeholder should be replaced.");
            StringAssert.Contains("Adventurer", line, "Player name should be injected into the template.");
            StringAssert.Contains("3 minutes", line, "Plural minute formatting should be respected.");
        }
    }
}
