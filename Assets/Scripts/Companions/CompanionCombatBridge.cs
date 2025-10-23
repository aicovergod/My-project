using System;
using Combat;
using Companions.Conversation;
using NPC;
using Pets;
using Skills;
using UnityEngine;

namespace Companions
{
    /// <summary>
    /// Overrides the pet combat controller's stat calculations so the companion uses its own skill levels
    /// and notifies the manager when damage is dealt for XP distribution.
    /// </summary>
    [RequireComponent(typeof(PetCombatController))]
    [DisallowMultipleComponent]
    public sealed class CompanionCombatBridge : MonoBehaviour
    {
        /// <summary>Controller owning this bridge, used to forward XP events.</summary>
        private CompanionController controller;

        /// <summary>Skill data source used to override combat stats.</summary>
        private SkillManager skillManager;

        /// <summary>Initialises the bridge with the owning controller and skill data.</summary>
        public void Initialise(CompanionController owner, SkillManager skills)
        {
            controller = owner;
            skillManager = skills;
        }

        /// <summary>
        /// Attempts to replace the combat stats calculated by <see cref="PetCombatController"/> with the companion's
        /// skill-driven values. Returns true when the override applied successfully.
        /// </summary>
        public bool TryOverrideStats(ref CombatantStats attacker)
        {
            if (skillManager == null)
                return false;

            attacker.AttackLevel = Mathf.Max(1, skillManager.GetLevel(SkillType.Attack));
            attacker.StrengthLevel = Mathf.Max(1, skillManager.GetLevel(SkillType.Strength));
            attacker.DefenceLevel = Mathf.Max(1, skillManager.GetLevel(SkillType.Defence));
            attacker.Style = CombatStyle.Accurate;
            attacker.DamageType = DamageType.Melee;
            attacker.Equip.attack = Mathf.RoundToInt(attacker.AttackLevel * 1.5f);
            attacker.Equip.strength = Mathf.RoundToInt(attacker.StrengthLevel * 1.5f);
            attacker.Equip.rangeStrength = attacker.Equip.strength;
            attacker.Equip.attackSpeedTicks = Mathf.Max(2, attacker.Equip.attackSpeedTicks);
            return true;
        }

        /// <summary>
        /// Forwards damage callbacks so XP can be awarded through the shared manager and conversation feed.
        /// </summary>
        public void NotifyDamageDealt(int damage, CombatStyle style, DamageType type, CombatTarget target)
        {
            controller?.AwardCombatXp(damage, style, type);

            if (damage <= 0)
                return;

            string companionName = CompanionManager.GetCompanionDisplayName();
            string targetName = ResolveTargetName(target);
            var metadata = CompanionEventMetadata.Create(
                primaryActor: companionName,
                secondaryActor: targetName,
                worldPosition: target != null ? (Vector3?)target.transform.position : null,
                additionalContext: $"Used {style} for {damage} damage."
            );

            string summary = $"dealt {damage} {type.ToString().ToLowerInvariant()} damage";
            if (!string.IsNullOrWhiteSpace(targetName))
                summary += $" to {targetName}";
            else
                summary += " to an enemy";

            CompanionConversationService.RegisterEvent(summary, CompanionEventType.Combat, metadata);
        }

        private static string ResolveTargetName(CombatTarget target)
        {
            if (target == null)
                return string.Empty;

            if (target is NpcCombatant npcCombatant)
            {
                var profile = npcCombatant.Profile;
                if (profile != null && !string.IsNullOrWhiteSpace(profile.name))
                    return profile.name;

                if (!string.IsNullOrWhiteSpace(npcCombatant.name))
                    return npcCombatant.name;
            }

            return target.transform != null ? target.transform.name : string.Empty;
        }
    }
}
