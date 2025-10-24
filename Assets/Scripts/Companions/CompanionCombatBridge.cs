using System;
using Combat;
using Companions.Conversation;
using Inventory;
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

            // Clamp combat skill levels so calculations never drop below level one even when XP data is missing.
            attacker.AttackLevel = Mathf.Max(1, skillManager.GetLevel(SkillType.Attack));
            attacker.StrengthLevel = Mathf.Max(1, skillManager.GetLevel(SkillType.Strength));
            attacker.DefenceLevel = Mathf.Max(1, skillManager.GetLevel(SkillType.Defence));
            attacker.RangedLevel = Mathf.Max(1, skillManager.GetLevel(SkillType.Ranged));
            attacker.MagicLevel = Mathf.Max(1, skillManager.GetLevel(SkillType.Magic));
            attacker.Style = CombatStyle.Accurate;
            // Reset equipment bonuses so the loop below rebuilds the totals from the currently equipped gear.
            attacker.Equip.attack = 0;
            attacker.Equip.strength = 0;
            attacker.Equip.range = 0;
            attacker.Equip.rangeStrength = 0;
            attacker.Equip.magic = 0;
            attacker.Equip.meleeDef = 0;
            attacker.Equip.rangeDef = 0;
            attacker.Equip.magicDef = 0;
            attacker.Equip.attackSpeedTicks = 4;

            ItemData weapon = null;
            var equipment = controller != null ? controller.Equipment : null;
            if (equipment != null)
            {
                // Mirror the equipment aggregation performed for the player so companion gear grants identical bonuses.
                foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
                {
                    if (slot == EquipmentSlot.None)
                        continue;

                    var entry = equipment.GetEquipped(slot);
                    var item = entry.item;
                    if (item == null)
                        continue;

                    var stats = item.combat;
                    attacker.Equip.attack += stats.Attack;
                    attacker.Equip.strength += stats.Strength;
                    attacker.Equip.range += stats.Range;
                    attacker.Equip.rangeStrength += stats.RangeStrength;
                    attacker.Equip.magic += stats.Magic;
                    attacker.Equip.meleeDef += stats.MeleeDefence;
                    attacker.Equip.rangeDef += stats.RangeDefence;
                    attacker.Equip.magicDef += stats.MagicDefence;

                    if (slot == EquipmentSlot.Weapon)
                    {
                        weapon = item;
                        // Respect weapon-specific attack speed overrides for the companion's current combat style.
                        int speed = item.GetAttackSpeedTicks(attacker.Style);
                        if (speed > 0)
                            attacker.Equip.attackSpeedTicks = speed;
                    }
                }
            }

            attacker.Equip.attackSpeedTicks = Mathf.Max(1, attacker.Equip.attackSpeedTicks);
            // Classify the equipped weapon to pick the most appropriate damage type, falling back to melee when empty.
            attacker.DamageType = WeaponClassificationUtility.ResolveDamageType(weapon);
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
