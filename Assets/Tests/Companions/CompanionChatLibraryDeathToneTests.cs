using Companions;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Companions
{
    /// <summary>
    /// Validates the tone-aware player death chat selection in <see cref="CompanionChatLibrary"/>.
    /// </summary>
    public class CompanionChatLibraryDeathToneTests
    {
        [Test]
        public void PlayerDeathLines_DefaultToneMatchesSnarkyPool()
        {
            Random.InitState(1337);

            string defaultLine = CompanionChatLibrary.GetRandomPlayerDeathLine();

            Random.InitState(1337);
            string snarkyLine = CompanionChatLibrary.GetRandomPlayerDeathLine(CompanionChatTone.Snarky);

            Assert.IsFalse(string.IsNullOrWhiteSpace(defaultLine), "Default tone should never return an empty string.");
            Assert.AreEqual(snarkyLine, defaultLine, "Parameterless call should reuse the snarky tone pool.");
        }

        [Test]
        public void PlayerDeathLines_SupportiveToneReturnsComfortingLine()
        {
            Random.InitState(2468);

            string supportiveLine = CompanionChatLibrary.GetRandomPlayerDeathLine(CompanionChatTone.Supportive);

            Assert.IsFalse(string.IsNullOrWhiteSpace(supportiveLine), "Supportive tone should never return an empty string.");
        }
    }
}
