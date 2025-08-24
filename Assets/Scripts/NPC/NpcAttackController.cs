using System.Collections;
using UnityEngine;
using Combat;
using EquipmentSystem;
using Skills;
using Player;

namespace NPC
{
    /// <summary>
    /// Handles NPC auto-attacks against the player during combat.
    /// </summary>
    [RequireComponent(typeof(NpcCombatant))]
    public class NpcAttackController : MonoBehaviour
    {
        private NpcCombatant combatant;
        private NpcWanderer wanderer;
        private CombatTarget currentTarget;
        private PlayerCombatTarget playerTarget;

        private void Awake()
        {
            combatant = GetComponent<NpcCombatant>();
            wanderer = GetComponent<NpcWanderer>();
            playerTarget = FindObjectOfType<PlayerCombatTarget>();
        }

        public void BeginAttacking(CombatTarget target)
        {
            if (target != null && currentTarget == target)
                return;
            StopAllCoroutines();
            wanderer?.ExitCombat();
            currentTarget = target;
            if (target != null)
            {
                wanderer?.EnterCombat(target.transform);
                StartCoroutine(AttackRoutine(target));
            }
        }

        private void Update()
        {
            var profile = combatant.Profile;
            if (profile == null || !profile.IsAggressive)
                return;

            if (playerTarget == null)
                playerTarget = FindObjectOfType<PlayerCombatTarget>();

            if (playerTarget == null)
                return;

            float dist = Vector2.Distance(playerTarget.transform.position, transform.position);
            if (currentTarget == null)
            {
                if (dist <= profile.AggroRange)
                    BeginAttacking(playerTarget);
            }
            else if (dist > profile.AggroRange)
            {
                BeginAttacking(null);
            }
        }

        private IEnumerator AttackRoutine(CombatTarget target)
        {
            var wait = new WaitForSeconds(4 * CombatMath.TICK_SECONDS);

            // Wait until the player is within melee range before performing the first attack.
            while (target != null && target.IsAlive && combatant.IsAlive &&
                   Vector2.Distance(target.transform.position, transform.position) > CombatMath.MELEE_RANGE)
            {
                // Poll each frame until the target is close enough or combat ends.
                yield return null;
            }

            while (target != null && target.IsAlive && combatant.IsAlive)
            {
                float distance = Vector2.Distance(target.transform.position, transform.position);
                // If the player moves out of aggro or melee range, stop attacking.
                var profile = combatant.Profile;
                if ((profile != null && distance > profile.AggroRange) || distance > CombatMath.MELEE_RANGE)
                    break;

                ResolveAttack(target);
                yield return wait;
            }

            wanderer?.ExitCombat();
            currentTarget = null;
        }

        private void ResolveAttack(CombatTarget target)
        {
            var attacker = combatant.GetCombatantStats();
            CombatantStats defender;
            var playerTarget = target as PlayerCombatTarget;
            if (playerTarget != null)
            {
                var skills = playerTarget.GetComponent<SkillManager>();
                var equipment = playerTarget.GetComponent<EquipmentAggregator>();
                var loadout = playerTarget.GetComponent<PlayerCombatLoadout>();
                defender = CombatantStats.ForPlayer(skills, equipment,
                    loadout != null ? loadout.Style : CombatStyle.Defensive,
                    DamageType.Melee);
            }
            else
            {
                defender = new CombatantStats
                {
                    AttackLevel = 1,
                    StrengthLevel = 1,
                    DefenceLevel = 1,
                    Equip = new EquipmentAggregator.CombinedStats(),
                    Style = CombatStyle.Defensive,
                    DamageType = target.PreferredDefenceType
                };
            }

            int attEff = CombatMath.GetEffectiveAttack(attacker.AttackLevel, attacker.Style);
            int defEff = CombatMath.GetEffectiveDefence(defender.DefenceLevel, defender.Style);
            int atkRoll = CombatMath.GetAttackRoll(attEff, attacker.Equip.attack);
            int defBonus = defender.DamageType switch
            {
                DamageType.Magic => defender.Equip.magicDef,
                DamageType.Ranged => defender.Equip.rangeDef,
                _ => defender.Equip.meleeDef
            };
            int defRoll = CombatMath.GetDefenceRoll(defEff, defBonus);
            bool hit = Random.value < CombatMath.ChanceToHit(atkRoll, defRoll);
            int damage = 0;
            if (hit)
            {
                int strEff = CombatMath.GetEffectiveStrength(attacker.StrengthLevel, attacker.Style);
                int maxHit = CombatMath.GetMaxHit(strEff, attacker.Equip.strength);
                damage = CombatMath.RollDamage(maxHit);
                target.ApplyDamage(damage, attacker.DamageType, this);
                var targetName = (target as MonoBehaviour)?.name ?? "target";
                Debug.Log($"{name} dealt {damage} damage to {targetName}.");
            }
            else
            {
                var targetName = (target as MonoBehaviour)?.name ?? "target";
                Debug.Log($"{name} missed {targetName}.");
            }
        }
    }
}
