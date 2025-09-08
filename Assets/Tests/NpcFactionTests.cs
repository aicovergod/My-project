using NUnit.Framework;
using NPC;

/// <summary>
/// Tests for NPC faction relationships.
/// </summary>
public class NpcFactionTests
{
    [Test]
    public void GoblinClansAreMutualEnemies()
    {
        Assert.IsTrue(FactionUtility.IsEnemy(FactionId.GoblinGreen, FactionId.GoblinRed));
        Assert.IsTrue(FactionUtility.IsEnemy(FactionId.GoblinRed, FactionId.GoblinGreen));
    }

    [Test]
    public void NeutralIsFriendlyWithAll()
    {
        foreach (FactionId id in System.Enum.GetValues(typeof(FactionId)))
        {
            Assert.IsFalse(FactionUtility.IsEnemy(FactionId.Neutral, id));
            Assert.IsFalse(FactionUtility.IsEnemy(id, FactionId.Neutral));
        }
    }
}

