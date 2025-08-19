using UnityEngine;
using Skills;

/// <summary>
/// Grants Beastmaster XP to the owning player when their pet deals damage.
/// Awards 4 XP per 1 damage (damage can be int or float; we round down to int damage first).
/// Safe to call even if the owner or skills component cannot be found.
/// </summary>
public static class BeastmasterXp
{
    public static void TryGrantFromPetDamage(GameObject ownerPlayer, float damage)
    {
        if (ownerPlayer == null) return;

        var skills = ownerPlayer.GetComponent<SkillManager>();
        if (skills == null)
        {
            // Try common alternatives; comment out any that don't exist in your project.
            // var skillsAlt = ownerPlayer.GetComponent<PlayerSkills>();
            // if (skillsAlt == null) return; else { /* adapt call below to skillsAlt */ }
            return;
        }

        int dmgInt = Mathf.Max(0, Mathf.FloorToInt(damage));
        if (dmgInt <= 0) return;

        float xp = dmgInt * 4f; // 4 XP per 1 damage
        skills.AddXP(SkillType.Beastmaster, xp);
    }
}
